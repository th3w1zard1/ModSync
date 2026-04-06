// Copyright (C) 2025
// Licensed under the GPL version 3 license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Services.Download;
using KOTORModSync.Core.Utility;

namespace KOTORModSync.Tests.Services.DistributedCache
{
    /// <summary>
    /// Manages a containerized cache client (Relay, Cascade) for integration testing.
    /// </summary>
    public class DockerCacheClient : IDisposable
    {
        private static readonly string[] s_containerNamePrefixes = { "transmission-test-", "deluge-test-" };
        private static readonly SemaphoreSlim s_environmentLock = new SemaphoreSlim(1, 1);
        private static readonly string[] s_enginePreference = { "docker", "podman" };
        private readonly CacheClientFlavor _clientFlavor;
        private readonly int _webPort;
        private readonly int _distributionPort;
        private string _containerEngine;
        private string _containerId;
        private readonly HttpClient _httpClient;
        private RemoteCacheProtocol.RemoteCacheSession _session;
        private bool _disposed;

        public enum CacheClientFlavor
        {
            Relay,
            Cascade,
        }

        public DockerCacheClient(CacheClientFlavor clientFlavor, int webPort = 0, int distributionPort = 0)
        {
            _clientFlavor = clientFlavor;
            _webPort = webPort == 0 ? GetRandomPort() : webPort;
            _distributionPort = distributionPort == 0 ? GetRandomPort() : distributionPort;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _containerEngine = string.Empty;
        }

        public int WebPort => _webPort;
        public int DistributionPort => _distributionPort;
        public string ContainerId => _containerId;

        private static int GetRandomPort()
        {
            var socket = new TcpListener(System.Net.IPAddress.Loopback, 0);
            try
            {
                socket.Start();
                int port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
                return port;
            }
            finally
            {
                socket.Stop();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            List<string> availableEngines = await GetAvailableEnginesAsync(cancellationToken).ConfigureAwait(false);
            if (availableEngines.Count == 0)
            {
                throw new InvalidOperationException("Neither docker nor podman is available for distributed cache tests.");
            }

            Exception lastError = null;

            foreach (string engine in availableEngines)
            {
                _containerEngine = engine;
                var (imageName, runArgs) = BuildRunArguments();

                try
                {
                    await RunEngineCommandAsync(_containerEngine, $"pull {imageName}", cancellationToken).ConfigureAwait(false);
                    string output = await RunEngineCommandAsync(_containerEngine, string.Join(" ", runArgs), cancellationToken).ConfigureAwait(false);
                    _containerId = output.Trim();

                    await WaitForWebUIAsync(cancellationToken).ConfigureAwait(false);

                    _session = await RemoteCacheProtocol.AuthenticateAsync(
                        MapFlavor(_clientFlavor),
                        _httpClient,
                        BuildBaseUri(),
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    lastError = ex;
                    await Logger.LogVerboseAsync($"[{engine}] Failed to start cache container: {ex.Message}").ConfigureAwait(false);
                    _containerId = null;
                    await CleanupResidualContainersForEngineAsync(engine, cancellationToken).ConfigureAwait(false);
                }
            }

            throw lastError ?? new InvalidOperationException("Unable to start distributed cache container with any available engine.");
        }

        private static async Task<List<string>> GetAvailableEnginesAsync(CancellationToken cancellationToken)
        {
            var engines = new List<string>();

            foreach (string engine in s_enginePreference)
            {
                if (await IsEngineAvailableAsync(engine, cancellationToken).ConfigureAwait(false))
                {
                    engines.Add(engine);
                }
            }

            return engines;
        }

        private static async Task<bool> IsEngineAvailableAsync(string engine, CancellationToken cancellationToken)
        {
            // First check if the command exists
            var versionPsi = new ProcessStartInfo
            {
                FileName = engine,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using var versionProcess = Process.Start(versionPsi);
                if (versionProcess == null)
                {
                    return false;
                }

                await NetFrameworkCompatibility.WaitForExitAsync(versionProcess, cancellationToken).ConfigureAwait(false);
                if (versionProcess.ExitCode != 0)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            // Now verify the daemon/socket is actually accessible and functional
            if (engine.Equals("docker", StringComparison.OrdinalIgnoreCase))
            {
                return await IsDockerDaemonAccessibleAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (engine.Equals("podman", StringComparison.OrdinalIgnoreCase))
            {
                return await IsPodmanDaemonAccessibleAsync(cancellationToken).ConfigureAwait(false);
            }

            return false;
        }

        private static async Task<bool> IsDockerDaemonAccessibleAsync(CancellationToken cancellationToken)
        {
            // Use 'docker info' which requires daemon to be running and accessible
            // This is more reliable than 'docker ps' as it provides detailed daemon info
            var infoPsi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using var infoProcess = Process.Start(infoPsi);
                if (infoProcess == null)
                {
                    await Logger.LogVerboseAsync("[Docker] Failed to start 'docker info' process").ConfigureAwait(false);
                    return false;
                }

                Task<string> outputTask = infoProcess.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = infoProcess.StandardError.ReadToEndAsync();

                await NetFrameworkCompatibility.WaitForExitAsync(infoProcess, cancellationToken).ConfigureAwait(false);

                string errorOutput = await errorTask.ConfigureAwait(false);
                string standardOutput = await outputTask.ConfigureAwait(false);

                await Logger.LogVerboseAsync($"[Docker] 'docker info' exit code: {infoProcess.ExitCode}, output length: {standardOutput?.Length ?? 0}, error: {errorOutput}").ConfigureAwait(false);

                // Exit code 0 means daemon is accessible
                if (infoProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(standardOutput))
                {
                    return true;
                }

                // Check error output for daemon-not-running indicators
                string errorLower = errorOutput.ToLowerInvariant();
                bool isDaemonError = errorLower.Contains("cannot connect") ||
                                    errorLower.Contains("daemon") ||
                                    errorLower.Contains("socket") ||
                                    errorLower.Contains("connection refused") ||
                                    errorLower.Contains("pipe") ||
                                    errorLower.Contains("system cannot find the file") ||
                                    errorLower.Contains("unable to connect") ||
                                    errorLower.Contains("dial tcp") ||
                                    errorLower.Contains("docker_engine");

                await Logger.LogVerboseAsync($"[Docker] Daemon error detected: {isDaemonError}").ConfigureAwait(false);
                return !isDaemonError && infoProcess.ExitCode == 0;
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[Docker] Exception checking daemon: {ex.Message}").ConfigureAwait(false);
                return false;
            }
        }

        private static async Task<bool> IsPodmanDaemonAccessibleAsync(CancellationToken cancellationToken)
        {
            // Verify actual connectivity with 'podman ps' - this is the definitive test
            // If this works, Podman is accessible regardless of machine configuration
            var psPsi = new ProcessStartInfo
            {
                FileName = "podman",
                Arguments = "ps",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using var psProcess = Process.Start(psPsi);
                if (psProcess == null)
                {
                    await Logger.LogVerboseAsync("[Podman] Failed to start 'podman ps' process").ConfigureAwait(false);
                    return false;
                }

                Task<string> psOutputTask = psProcess.StandardOutput.ReadToEndAsync();
                Task<string> psErrorTask = psProcess.StandardError.ReadToEndAsync();

                await NetFrameworkCompatibility.WaitForExitAsync(psProcess, cancellationToken).ConfigureAwait(false);

                string psErrorOutput = await psErrorTask.ConfigureAwait(false);
                string psStandardOutput = await psOutputTask.ConfigureAwait(false);

                await Logger.LogVerboseAsync($"[Podman] 'podman ps' exit code: {psProcess.ExitCode}, output length: {psStandardOutput?.Length ?? 0}, error: {psErrorOutput}").ConfigureAwait(false);

                // Exit code 0 means connection is working (even if no containers)
                if (psProcess.ExitCode == 0)
                {
                    // Verify no connection errors in error output
                    string psErrorLower = psErrorOutput.ToLowerInvariant();
                    bool isPsConnectionError = psErrorLower.Contains("cannot connect to podman") ||
                                             psErrorLower.Contains("unable to connect to podman") ||
                                             psErrorLower.Contains("connection list") ||
                                             (psErrorLower.Contains("dial tcp") && psErrorLower.Contains("127.0.0.1"));

                    await Logger.LogVerboseAsync($"[Podman] Connection error detected: {isPsConnectionError}").ConfigureAwait(false);
                    return !isPsConnectionError;
                }

                // If ps failed, check if it's a connection error
                string psErrorLower2 = psErrorOutput.ToLowerInvariant();
                bool isPsConnectionError2 = psErrorLower2.Contains("cannot connect to podman") ||
                                          psErrorLower2.Contains("unable to connect to podman") ||
                                          psErrorLower2.Contains("connection list") ||
                                          (psErrorLower2.Contains("dial tcp") && psErrorLower2.Contains("127.0.0.1"));

                await Logger.LogVerboseAsync($"[Podman] Exit code {psProcess.ExitCode}, connection error detected: {isPsConnectionError2}").ConfigureAwait(false);
                // If it's a clear connection error, Podman is not available
                return !isPsConnectionError2;
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[Podman] Exception checking daemon: {ex.Message}").ConfigureAwait(false);
                return false;
            }
        }

        private (string imageName, List<string> runArgs) BuildRunArguments()
        {
            switch (_clientFlavor)
            {
                case CacheClientFlavor.Relay:
                    {
                        string imageName = "linuxserver/transmission:latest";
                        string containerName = $"transmission-test-{Guid.NewGuid():N}";
                        var args = new List<string>
                    {
                        "run", "-d",
                        "-p", $"{_webPort}:9091",
                        "-p", $"{_distributionPort}:51413/tcp",
                        "-p", $"{_distributionPort}:51413/udp",
                        "-e", "PUID=1000",
                        "-e", "PGID=1000",
                        "-e", "USER=admin",
                        "-e", "PASS=adminadmin",
                        "--name", containerName,
                        imageName,
                    };
                        return (imageName, args);
                    }

                case CacheClientFlavor.Cascade:
                    {
                        string imageName = "linuxserver/deluge:latest";
                        string containerName = $"deluge-test-{Guid.NewGuid():N}";
                        var args = new List<string>
                    {
                        "run", "-d",
                        "-p", $"{_webPort}:8112",
                        "-p", $"{_distributionPort}:6881/tcp",
                        "-p", $"{_distributionPort}:6881/udp",
                        "-e", "PUID=1000",
                        "-e", "PGID=1000",
                        "--name", containerName,
                        imageName,
                    };
                        return (imageName, args);
                    }

                default:
                    throw new InvalidOperationException($"Unsupported client flavor: {_clientFlavor}");
            }
        }

        public static async Task CleanupResidualContainersAsync(CancellationToken cancellationToken = default)
        {
            await s_environmentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (string engine in s_enginePreference.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    await CleanupResidualContainersForEngineAsync(engine, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                s_environmentLock.Release();
            }
        }

        private static async Task CleanupResidualContainersForEngineAsync(string engine, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(engine))
            {
                return;
            }

            foreach (string prefix in s_containerNamePrefixes)
            {
                string listOutput = await RunEngineCommandAsync(engine, $"ps -aq --filter name={prefix}", cancellationToken, throwOnError: false).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(listOutput))
                {
                    continue;
                }

                string[] ids = listOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string id in ids)
                {
                    await RunEngineCommandAsync(engine, $"rm -f {id}", cancellationToken, throwOnError: false).ConfigureAwait(false);
                }
            }
        }

        private static async Task<string> RunEngineCommandAsync(string engine, string arguments, CancellationToken cancellationToken, bool throwOnError = true)
        {
            var psi = new ProcessStartInfo
            {
                FileName = engine,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                {
                    if (throwOnError)
                    {
                        throw new InvalidOperationException($"Failed to start process: {engine} {arguments}");
                    }

                    await Logger.LogVerboseAsync($"[{engine}] Failed to start process for '{arguments}'.").ConfigureAwait(false);
                    return string.Empty;
                }

                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

                await NetFrameworkCompatibility.WaitForExitAsync(process, cancellationToken).ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    if (throwOnError)
                    {
                        throw new InvalidOperationException($"Command failed: {engine} {arguments}\nExit Code: {process.ExitCode}\nError: {error}");
                    }

                    await Logger.LogVerboseAsync($"[{engine}] Command '{arguments}' failed with exit code {process.ExitCode}: {error}").ConfigureAwait(false);
                    return string.Empty;
                }

                return output;
            }
            catch (Exception ex) when (!throwOnError)
            {
                await Logger.LogVerboseAsync($"[{engine}] Command '{arguments}' raised an exception: {ex.Message}").ConfigureAwait(false);
                return string.Empty;
            }
        }

        private async Task WaitForWebUIAsync(CancellationToken cancellationToken)
        {
            string url = $"http://localhost:{_webPort}";
            DateTime timeout = DateTime.UtcNow.AddMinutes(2);

            while (DateTime.UtcNow < timeout)
            {
                try
                {
                    HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogInfoAsync($" Web UI is not ready yet: {ex.Message}", ex, fileOnly: false).ConfigureAwait(false);
                }

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException("Web UI did not become ready within timeout period");
        }

        public async Task<string> AddResourceAsync(
            string descriptorPath,
            string downloadPath,
            string expectedContentKey,
            CancellationToken cancellationToken = default)
        {
            if (_session == null)
            {
                throw new InvalidOperationException("Client has not been started or authenticated.");
            }

            if (string.IsNullOrEmpty(expectedContentKey))
            {
                throw new ArgumentNullException(nameof(expectedContentKey), "Expected content identifier must be provided.");
            }

            string resultKey = await RemoteCacheProtocol.SubmitDescriptorAsync(
                _session,
                _httpClient,
                descriptorPath,
                downloadPath,
                expectedContentKey,
                cancellationToken).ConfigureAwait(false);

            await RemoteCacheProtocol.WaitForRegistrationAsync(
                _session,
                _httpClient,
                expectedContentKey,
                timeout: TimeSpan.FromSeconds(45),
                cancellationToken).ConfigureAwait(false);

            return resultKey;
        }

        public async Task<ResourceStats> GetResourceStatsAsync(string contentKey, CancellationToken cancellationToken = default)
        {
            if (_session == null)
            {
                return new ResourceStats();
            }

            RemoteCacheProtocol.ResourceSnapshot snapshot = await RemoteCacheProtocol.QueryResourceAsync(
                _session,
                _httpClient,
                contentKey,
                cancellationToken).ConfigureAwait(false);

            return new ResourceStats
            {
                Progress = snapshot.Progress,
                Downloaded = snapshot.DownloadedBytes,
                Uploaded = snapshot.UploadedBytes,
                Peers = snapshot.ConnectedPeers,
                Seeds = snapshot.ConnectedSeeds,
                State = snapshot.State ?? string.Empty,
            };
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_containerId))
            {
                return;
            }

            try
            {
                // Use throwOnError: false for cleanup - container might already be gone
                await RunEngineCommandAsync(_containerEngine, $"stop {_containerId}", cancellationToken, throwOnError: false).ConfigureAwait(false);
                await RunEngineCommandAsync(_containerEngine, $"rm {_containerId}", cancellationToken, throwOnError: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"Container cleanup completed with benign issue: {ex.Message}").ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                try
                {
                    StopAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Swallow exceptions during Dispose; cleanup is best-effort.
                }

                _httpClient.Dispose();
            }

            _disposed = true;
        }

        private Uri BuildBaseUri()
        {
            int port = _webPort;
            return new Uri($"http://localhost:{port}/");
        }

        private static RemoteCacheProtocol.GatewayFlavor MapFlavor(CacheClientFlavor flavor)
        {
            return flavor switch
            {
                CacheClientFlavor.Relay => RemoteCacheProtocol.GatewayFlavor.Relay,
                CacheClientFlavor.Cascade => RemoteCacheProtocol.GatewayFlavor.Cascade,
                _ => throw new ArgumentOutOfRangeException(nameof(flavor), flavor, "Unsupported flavor"),
            };
        }

        public class ResourceStats
        {
            public double Progress { get; set; }
            public long Downloaded { get; set; }
            public long Uploaded { get; set; }
            public int Peers { get; set; }
            public int Seeds { get; set; }
            public string State { get; set; }
        }
    }
}

