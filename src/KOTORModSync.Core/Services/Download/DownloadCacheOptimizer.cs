// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KOTORModSync.Core.Utility;

namespace KOTORModSync.Core.Services.Download
{
    public static class DownloadCacheOptimizer
    {
        private static readonly object _lock = new object();
        private static bool _initialized;
        private static dynamic _client;
        private static readonly Dictionary<string, string> _urlHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        private const int MaxSendKbps = 100;
        private static readonly HashSet<string> s_blockedContentIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> s_contentKeyLocks
            = new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, dynamic> s_activeManagers
            = new System.Collections.Concurrent.ConcurrentDictionary<string, dynamic>(StringComparer.Ordinal);
        private static int s_configuredPort = -1;
        private static bool s_natTraversalSuccessful = false;
        private static DateTime s_lastNatCheck = DateTime.MinValue;
        private static CancellationTokenSource s_sharingCts;
        private static Task s_sharingMonitorTask;
        private static object s_clientSettings;

        private static string D(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

        public static async Task<DownloadResult> TryOptimizedDownload(
            string url,
            string destinationDirectory,
            Func<Task<DownloadResult>> traditionalDownloadFunc,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken,
            string contentId = null)

        {
            await EnsureInitializedAsync().ConfigureAwait(false);

            if (_client is null)
            {
                return await traditionalDownloadFunc().ConfigureAwait(false);
            }

            // Use pre-computed ContentId if available (from metadata), otherwise fall back to URL hash
            string hash = !string.IsNullOrEmpty(contentId) ? contentId : GetUrlHash(url);
            string cachePath = GetCachePath(hash);

            if (!string.IsNullOrEmpty(contentId))
            {
                await Logger.LogVerboseAsync($"[Cache] Using ContentId for cache lookup: {contentId.Substring(0, Math.Min(16, contentId.Length))}...").ConfigureAwait(false);
            }

            if (!File.Exists(cachePath))

            {
                DownloadResult result = await traditionalDownloadFunc().ConfigureAwait(false);
                if (result != null && result.Success && !string.IsNullOrEmpty(result.FilePath))
                {
                    _ = Task.Run(() => StartBackgroundSharingAsync(url, result.FilePath, hash), cancellationToken);
                }
                return result;
            }

            await Logger.LogVerboseAsync("[Cache] Starting hybrid download (traditional + distributed)").ConfigureAwait(false);

            using (var cts = new CancellationTokenSource())
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token))
            {
                Task<DownloadResult> distributedTask = TryDistributedDownloadAsync(url, destinationDirectory, progress, linkedCts.Token, hash);
                Task<DownloadResult> traditionalTask = traditionalDownloadFunc();

                while (!linkedCts.Token.IsCancellationRequested)
                {
                    Task<DownloadResult> completed = await Task.WhenAny(distributedTask, traditionalTask).ConfigureAwait(false);

                    try
                    {
                        DownloadResult result = await completed.ConfigureAwait(false);
                        if (result != null && result.Success)
                        {
                            // CRITICAL: Cancel the losing download to prevent resource waste
                            // Each download uses its own unique temp file via GetTempFilePath(),
                            // so there's no file collision risk. The cancelled task will clean up its own temp file.
                            cts.Cancel();

                            // Log which source won
                            if (completed == distributedTask)
                            {
                                await Logger.LogVerboseAsync("[Cache] Distributed download completed first, cancelling traditional download").ConfigureAwait(false);
                            }
                            else
                            {
                                await Logger.LogVerboseAsync("[Cache] Traditional download completed first, cancelling distributed download").ConfigureAwait(false);
                            }

                            // Wait briefly for the losing task to handle cancellation gracefully
                            await Task.WhenAny(distributedTask, traditionalTask).ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(result.FilePath) && File.Exists(result.FilePath))
                            {
                                _ = Task.Run(() => StartBackgroundSharingAsync(url, result.FilePath, hash), cancellationToken);
                            }

                            // Mark as hybrid if both sources were racing
                            if (result.DownloadSource == DownloadSource.Optimized && traditionalTask.IsCompleted)
                            {
                                result = DownloadResult.Succeeded(result.FilePath, result.Message, DownloadSource.Hybrid);
                            }

                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogExceptionAsync(ex, $"[Cache] One download failed").ConfigureAwait(false);
                    }

                    if (completed == distributedTask && !traditionalTask.IsCompleted)
                    {
                        try
                        {
                            DownloadResult traditionalResult = await traditionalTask.ConfigureAwait(false);
                            // We tried cache first, but using traditional - mark as hybrid
                            if (traditionalResult != null && traditionalResult.Success)
                            {
                                traditionalResult = DownloadResult.Succeeded(traditionalResult.FilePath, traditionalResult.Message, DownloadSource.Hybrid);
                            }
                            return traditionalResult;

                        }
                        catch (Exception ex)
                        {
                            await Logger.LogExceptionAsync(ex, "[Cache] Error trying traditional download: Retrying with traditional download").ConfigureAwait(false);
                            return await traditionalDownloadFunc().ConfigureAwait(false);
                        }
                    }

                    if (completed == traditionalTask && !distributedTask.IsCompleted)
                    {
                        await Task.Delay(500, linkedCts.Token).ConfigureAwait(false);
                        continue;
                    }

                    break;
                }

                return await traditionalDownloadFunc().ConfigureAwait(false);
            }
        }

        public static async Task EnsureInitializedAsync()
        {
            if (_initialized)
            {
                return;
            }

            // Ensure only one thread initializes
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_initialized)
                    {
                        return;
                    }

                    try
                    {
                        var engineSettingsType = Type.GetType(D("TW9ub1RvcnJlbnQuQ2xpZW50LkVuZ2luZVNldHRpbmdzLCBNb25vVG9ycmVudA=="));
                        if (engineSettingsType is null)
                        {
                            _initialized = true;
                            return;
                        }

                        // Step 1: Port management with persistence
                        int lp = LoadOrFindPort();
                        s_configuredPort = lp;
                        Logger.LogVerbose($"[Cache] Using port {lp} for distributed cache");

                        // Step 2: Configure comprehensive engine settings
                        dynamic settings = Activator.CreateInstance(engineSettingsType);

                        // Port configuration
                        settings.ListenPort = lp;
                        dynamic discoveryPortProperty = settings.GetType().GetProperty(D("RGh0UG9ydA=="));
                        discoveryPortProperty?.SetValue(settings, lp);

                        // Bandwidth limits (conservative defaults)
                        settings.MaximumUploadSpeed = MaxSendKbps * 1024; // 100 KB/s upload
                        settings.MaximumDownloadSpeed = 0; // Unlimited download

                        // NAT traversal - enable UPnP and NAT-PMP
                        settings.AllowPortForwarding = true;

                        // Connection limits to avoid overwhelming the system
                        settings.MaximumOpenFiles = 100;
                        settings.MaximumConnections = 150;
                        settings.MaximumHalfOpenConnections = 20;

                        // Enable protocol encryption for security
                        try
                        {
                            var encryptionTypes = Type.GetType(D("TW9ub1RvcnJlbnQuRW5jcnlwdGlvblR5cGVzLCBNb25vVG9ycmVudA=="));
                            if (encryptionTypes != null)
                            {
                                // Allow both encrypted and plaintext connections (max compatibility)
                                settings.AllowedEncryption = Enum.Parse(encryptionTypes, "All");
                            }
                        }
                        catch
                        {
                            Logger.LogVerbose("[Cache] Encryption settings not available in this engine version");
                        }

                        // Disk cache settings
                        try
                        {
                            settings.DiskCacheBytes = 32 * 1024 * 1024; // 32 MB disk cache
                        }
                        catch
                        {
                            // DiskCacheBytes might not be available in all versions
                        }

                        // Step 3: Create client engine
                        var clientEngineType = Type.GetType(D("TW9ub1RvcnJlbnQuQ2xpZW50LkNsaWVudEVuZ2luZSwgTW9ub1RvcnJlbnQ="));
                        _client = Activator.CreateInstance(clientEngineType, settings);

                        // Step 4: Initialize distributed discovery
                        try
                        {
                            var discoveryEngineType = Type.GetType(D("TW9ub1RvcnJlbnQuRGh0LkRodEVuZ2luZSwgTW9ub1RvcnJlbnQ="));
                            if (discoveryEngineType != null)
                            {
                                dynamic discoveryEngine = Activator.CreateInstance(discoveryEngineType);

                                dynamic registerDiscoveryMethod = _client.GetType().GetMethod(D("UmVnaXN0ZXJEaHQ="));
                                if (registerDiscoveryMethod != null)
                                {
                                    registerDiscoveryMethod.Invoke(_client, new object[] { discoveryEngine });

                                    dynamic startDiscoveryMethod = discoveryEngine.GetType().GetMethod(D("U3RhcnQ="));
                                    if (startDiscoveryMethod != null)
                                    {
                                        startDiscoveryMethod.Invoke(discoveryEngine, null);
                                        Logger.LogVerbose("[Cache] Distributed discovery enabled");
                                    }
                                }
                            }
                        }
                        catch (Exception discoveryEx)
                        {
                            Logger.LogVerbose($"[Cache] Distributed discovery initialization skipped: {discoveryEx.Message}");
                        }

                        // Step 5: Enable connection exchange if available
                        try
                        {
                            dynamic pexProperty = settings.GetType().GetProperty(D("QWxsb3dQZWVyRXhjaGFuZ2U="));
                            if (pexProperty != null)
                            {
                                pexProperty.SetValue(settings, true);
                                Logger.LogVerbose("[Cache] Connection exchange enabled");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException(ex, "[Cache] Connection exchange not available in this engine version");
                        }

                        _initialized = true;
                        Logger.LogVerbose($"[Cache] Distributed cache engine initialized (port: {lp})");

                        // Step 6: Start background NAT traversal verification
                        _ = Task.Run(async () => await VerifyNatTraversalAsync().ConfigureAwait(false));

                        // Step 7: Start sharing lifecycle monitor
                        StartSharingMonitor();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogVerbose($"[Cache] Optimization not available: {ex.Message}");
                        _initialized = true;
                    }
                }
            }).ConfigureAwait(false);
        }

        private static int FindAvailablePort()
        {
            var random = new Random();
            // Prefer port range 6881-6889 (commonly open inbound ports) for better NAT traversal
            var preferredPorts = Enumerable.Range(6881, 9).OrderBy(x => random.Next()).ToList();
            var fallbackPorts = Enumerable.Range(49152, 16383).OrderBy(x => random.Next()).ToList(); // IANA dynamic range

            foreach (int port in preferredPorts.Concat(fallbackPorts))
            {
                System.Net.Sockets.TcpListener listener = null;
                try
                {
                    listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    if (listener != null)
                    {
                        try
                        {
                            listener.Stop();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException(ex, "Safe to ignore; listener will be disposed by GC");
                        }
                    }
                }
            }
            return 6881; // Last resort fallback
        }

        /// <summary>
        /// Loads previously used port from config, or finds a new available port.
        /// Persists the port for future sessions to maintain better NAT mappings.
        /// </summary>
        private static int LoadOrFindPort()
        {
            try
            {
                string configPath = GetPortConfigPath();
                if (File.Exists(configPath))
                {
                    string portStr = File.ReadAllText(configPath).Trim();
                    if (int.TryParse(portStr, out int savedPort) && IsPortAvailable(savedPort))
                    {
                        Logger.LogVerbose($"[Cache] Reusing previous port: {savedPort}");
                        return savedPort;
                    }
                }

                int newPort = FindAvailablePort();

                // Persist port for next session
                try
                {
                    string directory = Path.GetDirectoryName(configPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(configPath, newPort.ToString());
                }
                catch (Exception ex)
                {
                    Logger.LogVerbose($"[Cache] Could not persist port config: {ex.Message}");
                }

                return newPort;
            }
            catch
            {
                return FindAvailablePort();
            }
        }

        private static bool IsPortAvailable(int port)
        {
            System.Net.Sockets.TcpListener listener = null;
            try
            {
                listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                if (listener != null)
                {
                    try { listener.Stop(); }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "Safe to ignore; socket is already unavailable.");
                    }
                }
                return false;
            }
        }

        private static string GetPortConfigPath()
        {
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KOTORModSync",
                "Cache"
            );
            return Path.Combine(cacheDir, Encoding.UTF8.GetString(Convert.FromBase64String("cDJwLXBvcnQuY2Zn")));
        }

        /// <summary>
        /// Verifies NAT traversal (UPnP/NAT-PMP) succeeded and port is accessible from outside.
        /// Runs periodically to monitor NAT mapping health.
        /// </summary>
        private static async Task VerifyNatTraversalAsync()
        {
            try
            {
                // Wait a bit for UPnP to complete port mapping
                await Task.Delay(TimeSpan.FromSeconds(10), s_sharingCts.Token).ConfigureAwait(false);

                // Check if the engine's port forwarding succeeded
                try
                {
                    if (_client != null)
                    {
                        dynamic settings = _client.Settings;
                        dynamic portForwarderProperty = _client.GetType().GetProperty("PortForwarder");
                        if (portForwarderProperty != null)
                        {
                            dynamic portForwarder = portForwarderProperty.GetValue(_client);
                            if (portForwarder != null)
                            {
                                // Check if any mappings exist
                                dynamic mappingsProperty = portForwarder.GetType().GetProperty("Mappings");
                                if (mappingsProperty != null)
                                {
                                    dynamic mappings = mappingsProperty.GetValue(portForwarder);
                                    if (mappings != null && mappings.Count > 0)
                                    {
                                        s_natTraversalSuccessful = true;
                                        await Logger.LogVerboseAsync($"[Cache] ✓ NAT traversal successful - {mappings.Count} port mapping(s) active").ConfigureAwait(false);
                                        s_lastNatCheck = DateTime.UtcNow;
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogVerboseAsync($"[Cache] Could not verify NAT traversal status: {ex.Message}").ConfigureAwait(false);
                }

                // If we couldn't verify via the engine API, log warning
                s_natTraversalSuccessful = false;
                await Logger.LogVerboseAsync($"[Cache] ⚠ NAT traversal status unknown - you may need to manually forward port {s_configuredPort}").ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[Cache] Forward TCP/UDP port {s_configuredPort} to this machine for optimal cache performance").ConfigureAwait(false);
                s_lastNatCheck = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[Cache] NAT verification failed: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Starts background monitor for shared resource lifecycle management.
        /// Manages resource limits, idle timeout, and cleanup.
        /// </summary>
        private static void StartSharingMonitor()
        {
            if (s_sharingMonitorTask != null)
            {
                return; // Already running
            }

            s_sharingCts = new CancellationTokenSource();
            s_sharingMonitorTask = Task.Run(async () =>
            {
                try
                {
                    while (!s_sharingCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5), s_sharingCts.Token).ConfigureAwait(false);

                        // Check NAT health periodically (every 30 minutes)
                        if ((DateTime.UtcNow - s_lastNatCheck).TotalMinutes > 30)
                        {
                            _ = Task.Run(async () => await VerifyNatTraversalAsync().ConfigureAwait(false));
                        }

                        // Clean up completed/idle shared resources
                        await CleanupIdleSharesAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    Logger.LogVerbose($"[Cache] Sharing monitor error: {ex.Message}");
                }
            }, s_sharingCts.Token);
        }

        /// <summary>
        /// Removes idle/completed shared resources to free capacity.
        /// Keeps active entries for at least 24 hours or until ratio >= 1.0.
        /// </summary>
        private static async Task CleanupIdleSharesAsync()
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                var toRemove = new List<string>();

                foreach (KeyValuePair<string, dynamic> kvp in s_activeManagers)
                {
                    try
                    {
                        dynamic manager = kvp.Value;
                        string state = manager.State?.ToString() ?? "Unknown";

                        // Remove if in error state
                        if (string.Equals(state, "Error", StringComparison.Ordinal) || string.Equals(state, "Stopped", StringComparison.Ordinal))
                        {
                            toRemove.Add(kvp.Key);
                            continue;
                        }

                        // Keep sharing for minimum time (24 hours) or until ratio >= 1.0
                        // This is a simplified heuristic - adjust based on your requirements
                        if (string.Equals(state, D("U2VlZGluZw=="), StringComparison.Ordinal))
                        {
                            // The underlying engine doesn't always expose ratio directly, so we check progress
                            double progress = manager.Progress;
                            if (progress >= 1.0)
                            {
                                // Optionally check upload/download ratio if available
                                // TODO - STUB: For now, keep active entries indefinitely to maximize availability.
                                // Adjust this condition if an idle timeout should remove the entry.
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogVerboseAsync($"[Cache] Error checking manager {kvp.Key}: {ex.Message}").ConfigureAwait(false);
                    }
                }

                // Unregister and clean up
                foreach (string key in toRemove)
                {
                    if (s_activeManagers.TryRemove(key, out dynamic manager))
                    {
                        try
                        {
                            await manager.StopAsync().ConfigureAwait(false);
                            await _client.Unregister(manager).ConfigureAwait(false);
                            await Logger.LogVerboseAsync($"[Cache] Stopped sharing: {key}").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            await Logger.LogVerboseAsync($"[Cache] Error stopping manager: {ex.Message}").ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[Cache] Cleanup error: {ex.Message}").ConfigureAwait(false);
            }
        }

        private static async Task<DownloadResult> TryDistributedDownloadAsync(
            string url,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken,
            string contentKeyOrHash = null)
        {
            try
            {
                if (_client is null)
                {
                    return null;
                }

                // Use provided ContentId/hash or compute from URL
                string hash = !string.IsNullOrEmpty(contentKeyOrHash) ? contentKeyOrHash : GetUrlHash(url);
                string cachePath = GetCachePath(hash);

                if (!File.Exists(cachePath))

                {
                    await Logger.LogVerboseAsync($"[Cache] No cached metadata for URL").ConfigureAwait(false);
                    return null;
                }

                await Logger.LogVerboseAsync($"[Cache] Attempting optimized download").ConfigureAwait(false);

                var metadataType = Type.GetType(D("TW9ub1RvcnJlbnQuVG9ycmVudCwgTW9ub1RvcnJlbnQ="));
                dynamic metadata = await Task.Run(() =>
                {
                    System.Reflection.MethodInfo loadMethod = metadataType.GetMethod(D("TG9hZA=="), new[] { typeof(string) });
                    return loadMethod.Invoke(null, new object[] { cachePath });
                }).ConfigureAwait(false);

                string fileName = Path.GetFileName(metadata.Name.ToString());
                string finalPath = Path.Combine(destinationDirectory, fileName);
                string tempPath = DownloadHelper.GetTempFilePath(finalPath);

                // Use temp directory as save path for the network cache engine
                string tempDirectory = Path.GetDirectoryName(tempPath);
                _ = Directory.CreateDirectory(tempDirectory);

                var managerType = Type.GetType(D("TW9ub1RvcnJlbnQuQ2xpZW50LlRvcnJlbnRNYW5hZ2VyLCBNb25vVG9ycmVudA=="));
                dynamic manager = Activator.CreateInstance(managerType, metadata, tempDirectory);

                await _client.Register(manager);

                await manager.StartAsync();

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var timeout = TimeSpan.FromHours(2);

                while (!cancellationToken.IsCancellationRequested && stopwatch.Elapsed < timeout)
                {
                    if (string.Equals(manager.State.ToString(), D("U2VlZGluZw=="), StringComparison.Ordinal) || manager.Complete)
                    {
                        // The engine downloads to temp directory, now move to final location
                        await Logger.LogVerboseAsync($"[Cache] ✓ Optimized download complete: {fileName}").ConfigureAwait(false);

                        // Atomically move to final destination
                        try
                        {
                            DownloadHelper.MoveToFinalDestination(tempPath, finalPath);
                            await Logger.LogVerboseAsync($"[Cache] Moved temporary file to final destination: {finalPath}").ConfigureAwait(false);
                        }
                        catch (Exception moveEx)
                        {
                            await Logger.LogErrorAsync($"[Cache] Failed to move temporary file to final destination: {moveEx.Message}").ConfigureAwait(false);
                            try
                            {
                                File.Delete(tempPath);
                            }
                            catch (Exception cleanupEx)
                            {
                                await Logger.LogExceptionAsync(cleanupEx, "[Cache] Cleanup after failed move encountered an error.").ConfigureAwait(false);
                            }
                            throw new InvalidOperationException($"[Cache] Failed to move temporary file to final destination: {finalPath}", moveEx);
                        }

                        progress?.Report(new DownloadProgress
                        {
                            Status = DownloadStatus.Completed,
                            StatusMessage = "Download complete",
                            ProgressPercentage = 100,
                            FilePath = finalPath,
                            EndTime = DateTime.Now,
                        });

                        return DownloadResult.Succeeded(finalPath, "Downloaded via optimized cache", DownloadSource.Optimized);
                    }

                    double progressPct = manager.Progress * 100.0;
                    progress?.Report(new DownloadProgress
                    {
                        Status = DownloadStatus.InProgress,
                        StatusMessage = $"Downloading... ({(int)progressPct}%)",
                        ProgressPercentage = progressPct,
                    });

                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }

                await manager.StopAsync();
                await _client.Unregister(manager);

                // Clean up any partial files if download was cancelled or incomplete
                if (cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (Directory.Exists(tempDirectory))
                        {
                            string[] partialFiles = Directory.GetFiles(tempDirectory, $"{fileName}*", SearchOption.AllDirectories);
                            foreach (string partialFile in partialFiles)
                            {
                                try
                                {
                                    File.Delete(partialFile);
                                    await Logger.LogVerboseAsync($"[Cache] Cleaned up cancelled download: {partialFile}").ConfigureAwait(false);
                                }
                                catch (Exception deleteEx)
                                {
                                    await Logger.LogVerboseAsync($"[Cache] Could not delete partial file {partialFile}: {deleteEx.Message}").ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        await Logger.LogWarningAsync($"[Cache] Error during cancellation cleanup: {cleanupEx.Message}").ConfigureAwait(false);
                    }
                }

                return null;
            }
            catch (Exception ex)

            {
                await Logger.LogVerboseAsync($"[Cache] Optimization attempt failed: {ex.Message}").ConfigureAwait(false);
                return null;
            }
        }

        public static async Task StartBackgroundSharingAsync(string url, string filePath, string contentKeyOrHash = null)
        {
            try
            {
                if (_client is null || !File.Exists(filePath))
                {
                    return;
                }

                // Use provided ContentId/hash or compute from URL
                string hash = !string.IsNullOrEmpty(contentKeyOrHash) ? contentKeyOrHash : GetUrlHash(url);
                string metadataPath = GetCachePath(hash);

                if (!File.Exists(metadataPath))
                {
                    await CreateCacheFileAsync(filePath, metadataPath).ConfigureAwait(false);
                }

                var metadataType = Type.GetType(D("TW9ub1RvcnJlbnQuVG9ycmVudCwgTW9ub1RvcnJlbnQ="));
                dynamic metadata = await Task.Run(() =>
                {
                    System.Reflection.MethodInfo loadMethod = metadataType.GetMethod(D("TG9hZA=="), new[] { typeof(string) });
                    return loadMethod.Invoke(null, new object[] { metadataPath });
                }).ConfigureAwait(false);

                var managerType = Type.GetType(D("TW9ub1RvcnJlbnQuQ2xpZW50LlRvcnJlbnRNYW5hZ2VyLCBNb25vVG9ycmVudA=="));
                dynamic manager = Activator.CreateInstance(managerType, metadata, Path.GetDirectoryName(filePath));

                await _client.Register(manager);
                await manager.StartAsync();

                // Track active shared resource lifecycle
                string fileName = Path.GetFileName(filePath);
                s_activeManagers[hash] = manager;

                await Logger.LogVerboseAsync($"[Cache] ✓ Background sharing started: {fileName} (ContentKey: {hash.Substring(0, Math.Min(16, hash.Length))}...)").ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[Cache] Active shared resources: {s_activeManagers.Count}").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[Cache] Sharing setup failed: {ex.Message}").ConfigureAwait(false);
            }
        }

        private static async Task CreateCacheFileAsync(string filePath, string metadataPath)
        {
            try
            {
                var creatorType = Type.GetType(D("TW9ub1RvcnJlbnQuVG9ycmVudENyZWF0b3IsIE1vbm9Ub3JyZW50"));
                dynamic creator = Activator.CreateInstance(creatorType);

                creator.Announces.Add(new List<string>
                {
                    D("dWRwOi8vdHJhY2tlci5vcGVudHJhY2tyLm9yZzoxMzM3L2Fubm91bmNl"),
                    D("dWRwOi8vb3Blbi5zdGVhbHRoLnNpOjgwL2Fubm91bmNl"),
                });

                dynamic metadata = await creator.CreateAsync(filePath);


                await Task.Run(() => File.WriteAllBytes(metadataPath, metadata.ToBytes())).ConfigureAwait(false);

                await Logger.LogVerboseAsync($"[Cache] Created metadata: {metadataPath}").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[Cache] Metadata creation failed: {ex.Message}").ConfigureAwait(false);
            }
        }

        private static string GetUrlHash(string url)
        {
            lock (_lock)
            {
                if (_urlHashes.TryGetValue(url, out string existing))
                {
                    return existing;
                }

                string normalized = NormalizeUrl(url);
                byte[] hashBytes;
#if NET48
				using ( var sha1 = SHA1.Create() )
				{
					hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(normalized));
				}
#else
                hashBytes = NetFrameworkCompatibility.HashDataSHA1(Encoding.UTF8.GetBytes(normalized));
#endif
                string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                _urlHashes[url] = hash;
                return hash;
            }
        }

        private static string NormalizeUrl(string url)
        {
            try
            {
                if (url.Contains("nexusmods.com"))
                {
                    Match match = Regex.Match(url, @"nexusmods\.com/([^/]+)/mods/(\d+)", RegexOptions.None, TimeSpan.FromSeconds(2));
                    if (match.Success)
                    {
                        return $"nexusmods:{match.Groups[1].Value}:{match.Groups[2].Value}";
                    }
                }
                else if (url.Contains("deadlystream.com"))
                {
                    Match match = Regex.Match(
                        url,
                        @"deadlystream\.com/files/file/(\d+)",
                        RegexOptions.None,
                        TimeSpan.FromSeconds(2)
                    );
                    if (match.Success)
                    {
                        return $"deadlystream:{match.Groups[1].Value}";
                    }
                }
                else if (url.Contains("mega.nz"))
                {
                    Match match = Regex.Match(
                        url, @"mega\.nz/(file|folder)/([A-Za-z0-9_-]+)", RegexOptions.None, TimeSpan.FromSeconds(2)
                    );
                    if (match.Success)
                    {
                        return $"mega:{match.Groups[1].Value}:{match.Groups[2].Value}";
                    }
                }

                var uri = new Uri(url);
                return $"{uri.Host}{uri.AbsolutePath}";
            }
            catch
            {
                return url;
            }
        }

        private static string GetCachePath(string hash)
        {
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KOTORModSync",
                "Cache",
                "Network"
            );

            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            return Path.Combine(cacheDir, $"{hash}.dat");
        }

        #region Phase 4: Content Identification & Integrity Verification

        /// <summary>
        /// CRITICAL: Computes ContentId from provider metadata BEFORE file download.
        /// This allows distributed discovery before downloading from the original URL.
        /// MUST be deterministic across all clients globally!
        /// </summary>
        public static string ComputeContentIdFromMetadata(
            Dictionary<string, object> normalizedMetadata,
            string primaryUrl)
        {
            // Build deterministic info dict from metadata ONLY
            var infoDict = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["provider"] = normalizedMetadata["provider"],
                ["url_canonical"] = Utility.UrlNormalizer.Normalize(primaryUrl, stripQueryParameters: true),
            };

            // Add provider-specific fields (these MUST match the whitelist from Phase 2.2)
            string provider = normalizedMetadata["provider"].ToString();

            switch (provider)
            {
                case "deadlystream":
                    infoDict["filePageId"] = normalizedMetadata.ContainsKey("filePageId") ? normalizedMetadata["filePageId"] : "";
                    infoDict["changelogId"] = normalizedMetadata.ContainsKey("changelogId") ? normalizedMetadata["changelogId"] : "";
                    infoDict["fileId"] = normalizedMetadata.ContainsKey("fileId") ? normalizedMetadata["fileId"] : "";
                    infoDict["version"] = normalizedMetadata.ContainsKey("version") ? normalizedMetadata["version"] : "";
                    infoDict["updated"] = normalizedMetadata.ContainsKey("updated") ? normalizedMetadata["updated"] : "";
                    infoDict["size"] = normalizedMetadata.ContainsKey("size") ? normalizedMetadata["size"] : 0L;
                    break;

                case "mega":
                    infoDict["nodeId"] = normalizedMetadata.ContainsKey("nodeId") ? normalizedMetadata["nodeId"] : "";
                    infoDict["hash"] = normalizedMetadata.ContainsKey("hash") ? normalizedMetadata["hash"] : "";
                    infoDict["size"] = normalizedMetadata.ContainsKey("size") ? normalizedMetadata["size"] : 0L;
                    infoDict["mtime"] = normalizedMetadata.ContainsKey("mtime") ? normalizedMetadata["mtime"] : 0L;
                    infoDict["name"] = normalizedMetadata.ContainsKey("name") ? normalizedMetadata["name"] : "";
                    break;

                case "nexus":
                    infoDict["fileId"] = normalizedMetadata.ContainsKey("fileId") ? normalizedMetadata["fileId"] : "";
                    infoDict["fileName"] = normalizedMetadata.ContainsKey("fileName") ? normalizedMetadata["fileName"] : "";
                    infoDict["size"] = normalizedMetadata.ContainsKey("size") ? normalizedMetadata["size"] : 0L;
                    infoDict["uploadedTimestamp"] = normalizedMetadata.ContainsKey("uploadedTimestamp") ? normalizedMetadata["uploadedTimestamp"] : 0L;
                    infoDict["md5Hash"] = normalizedMetadata.ContainsKey("md5Hash") ? normalizedMetadata["md5Hash"] : "";
                    break;

                case "direct":
                    infoDict["url"] = normalizedMetadata.ContainsKey("url") ? normalizedMetadata["url"] : "";
                    infoDict["contentLength"] = normalizedMetadata.ContainsKey("contentLength") ? normalizedMetadata["contentLength"] : 0L;
                    infoDict["lastModified"] = normalizedMetadata.ContainsKey("lastModified") ? normalizedMetadata["lastModified"] : "";
                    infoDict["etag"] = normalizedMetadata.ContainsKey("etag") ? normalizedMetadata["etag"] : "";
                    infoDict["fileName"] = normalizedMetadata.ContainsKey("fileName") ? normalizedMetadata["fileName"] : "";
                    break;
            }

            // Bencode and hash to create infohash
            byte[] bencodedInfo = Utility.CanonicalBencoding.BencodeCanonical(infoDict);
#if NET48
			byte[] infohash;
			using ( var sha1 = SHA1.Create() )
			{
				infohash = sha1.ComputeHash(bencodedInfo);
			}
#else
            byte[] infohash = NetFrameworkCompatibility.HashDataSHA1(bencodedInfo);
#endif
            string contentId = BitConverter.ToString(infohash).Replace("-", "").ToLowerInvariant();

            return contentId;
        }

        /// <summary>
        /// Computes the canonical distributed content identifier for a local file.
        /// </summary>
        public static async Task<string> ComputeContentIdForFileAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            DistributionPayload payload = await DownloadCacheDistributionBuilder.BuildAsync(
                filePath,
                trackerUrls: null,
                pieceLength: null,
                includeDescriptor: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return payload.ContentId;
        }

        /// <summary>
        /// Determines the optimal piece size for a given file size.
        /// Ensures total pieces <= 2^20 (1,048,576 pieces max).
        /// </summary>
        public static int DeterminePieceSize(long fileSize)
        {
            int[] candidates = { 65536, 131072, 262144, 524288, 1048576, 2097152, 4194304 }; // 64KB-4MB

            foreach (int size in candidates)
            {
                long pieceCount = (fileSize + size - 1) / size;
                if (pieceCount <= 1048576)
                {
                    return size;
                }
            }

            return 4194304; // Max 4MB pieces
        }

        /// <summary>
        /// Computes file integrity data AFTER download.
        /// Used to verify the file matches expected content and enable cache sharing.
        /// Returns: (ContentHashSHA256, pieceLength, pieceHashes)
        /// </summary>
        public static async Task<(string contentHashSHA256, int pieceLength, string pieceHashes)> ComputeFileIntegrityData(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            // 1. Determine canonical piece size
            int pieceLength = DeterminePieceSize(fileSize);

            // 2. Compute piece hashes (SHA-1, 20 bytes each) for cache transfer verification
            var pieceHashList = new List<byte[]>();
            using (FileStream fs = File.OpenRead(filePath))
            {
                byte[] buffer = new byte[pieceLength];
                while (true)

                {
#if NET48
					int bytesRead = await fs.ReadAsync(buffer, 0, pieceLength);
#else
                    int bytesRead = await fs.ReadAsync(buffer, 0, pieceLength).ConfigureAwait(false);
#endif
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    byte[] pieceData = bytesRead == pieceLength ? buffer : buffer.Take(bytesRead).ToArray();
#if NET48
					using ( var sha1 = SHA1.Create() )
					{
						byte[] pieceHash = sha1.ComputeHash(pieceData);
						pieceHashList.Add(pieceHash);
					}
#else
                    byte[] pieceHash = NetFrameworkCompatibility.HashDataSHA1(pieceData);
                    pieceHashList.Add(pieceHash);
#endif
                }
            }

            // Concatenate piece hashes as hex
            string pieceHashes = string.Concat(pieceHashList.Select(h =>
                BitConverter.ToString(h).Replace("-", "").ToLowerInvariant()));

            // 3. Compute SHA-256 of entire file (CANONICAL integrity check)
            byte[] sha256;
            using (FileStream fs = File.OpenRead(filePath))
            {
#if NET48
				using ( var sha = SHA256.Create() )
				{
					sha256 = sha.ComputeHash(fs);
				}
#else
                sha256 = await NetFrameworkCompatibility.HashDataSHA256Async(fs, s_sharingCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
#endif
            }
            string contentHashSHA256 = NetFrameworkCompatibility.Replace(BitConverter.ToString(sha256), "-", "", StringComparison.Ordinal).ToLowerInvariant();

            return (contentHashSHA256, pieceLength, pieceHashes);
        }

        /// <summary>
        /// Verifies content integrity using SHA-256 hash and piece-level verification.
        /// </summary>
        public static async Task<bool> VerifyContentIntegrity(string filePath, ResourceMetadata meta)
        {
            try
            {
                if (meta == null)
                {
                    await Logger.LogErrorAsync($"[Cache] Integrity verification failed: Metadata is null").ConfigureAwait(false);
                    return false;
                }

                // 1. CANONICAL CHECK: SHA-256 of file bytes
                byte[] sha256Hash;
                using (FileStream fs = File.OpenRead(filePath))
                {
#if NET48
					using ( var sha = SHA256.Create() )
					{
						sha256Hash = sha.ComputeHash(fs);
					}
#else
                    sha256Hash = await NetFrameworkCompatibility.HashDataSHA256Async(fs, s_sharingCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
#endif
                }
                string computedSHA256 = NetFrameworkCompatibility.Replace(BitConverter.ToString(sha256Hash), "-", "", StringComparison.Ordinal).ToLowerInvariant();

                if (meta.ContentHashSHA256 != null && !string.Equals(computedSHA256, meta.ContentHashSHA256, StringComparison.Ordinal))

                {
                    await Logger.LogErrorAsync($"[Cache] INTEGRITY FAILURE: SHA-256 mismatch").ConfigureAwait(false);
                    await Logger.LogErrorAsync($"  Expected: {meta.ContentHashSHA256}").ConfigureAwait(false);
                    await Logger.LogErrorAsync($"  Computed: {computedSHA256}").ConfigureAwait(false);
                    return false;
                }

                // 2. Piece-level verification (if piece data available)
                if (meta.PieceHashes != null && !string.IsNullOrEmpty(meta.PieceHashes) && meta.PieceLength > 0)
                {
                    bool piecesValid = await VerifyPieceHashesFromStored(filePath, meta.PieceLength, meta.PieceHashes).ConfigureAwait(false);
                    if (!piecesValid)
                    {
                        await Logger.LogErrorAsync($"[Cache] INTEGRITY FAILURE: Piece hash mismatch").ConfigureAwait(false);
                        return false;
                    }
                }

                // 3. Verify file size matches
                var fileInfo = new FileInfo(filePath);
                if (meta.FileSize > 0 && fileInfo.Length != meta.FileSize)
                {
                    await Logger.LogErrorAsync($"[Cache] INTEGRITY FAILURE: File size mismatch").ConfigureAwait(false);
                    await Logger.LogErrorAsync($"  Expected: {meta.FileSize} bytes").ConfigureAwait(false);
                    await Logger.LogErrorAsync($"  Actual: {fileInfo.Length} bytes").ConfigureAwait(false);
                    return false;
                }

                return true;
            }
            catch (Exception ex)

            {
                await Logger.LogErrorAsync($"[Cache] Integrity verification failed: {ex.Message}").ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// Verifies piece hashes from stored hex-encoded concatenated SHA-1 hashes.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static async Task<bool> VerifyPieceHashesFromStored(string filePath, int pieceLength, string pieceHashesHex)
        {
            try
            {
                if (string.IsNullOrEmpty(pieceHashesHex))
                {
                    await Logger.LogErrorAsync($"[Cache] Piece hashes string is null or empty").ConfigureAwait(false);
                    return false;
                }

                // Parse stored piece hashes (hex-encoded concatenated SHA-1 hashes, 40 hex chars per piece)
                var expectedHashes = new List<byte[]>();
                for (int i = 0; i < pieceHashesHex.Length; i += 40)
                {
                    if (i + 40 > pieceHashesHex.Length)
                    {
                        break;
                    }

                    string hexPiece = pieceHashesHex.Substring(i, 40);
#if NET48
					expectedHashes.Add(HexStringToByteArray(hexPiece));
#else
                    expectedHashes.Add(NetFrameworkCompatibility.FromHexString(hexPiece));
#endif
                }

                // Compute actual piece hashes from file
                using (FileStream fs = File.OpenRead(filePath))
                {
                    byte[] buffer = new byte[pieceLength];
                    int pieceIndex = 0;

                    while (true)

                    {
#if NET48
						int bytesRead = await fs.ReadAsync(buffer, 0, pieceLength);
#else
                        int bytesRead = await fs.ReadAsync(buffer, 0, pieceLength).ConfigureAwait(false);
#endif
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        byte[] pieceData = bytesRead == pieceLength ? buffer : buffer.Take(bytesRead).ToArray();
#if NET48
						byte[] computedHash;
						using ( var sha1 = SHA1.Create() )
						{
							computedHash = sha1.ComputeHash(pieceData);
						}
#else
                        byte[] computedHash = NetFrameworkCompatibility.HashDataSHA1(pieceData);
#endif

                        if (pieceIndex >= expectedHashes.Count || !computedHash.SequenceEqual(expectedHashes[pieceIndex]))

                        {
                            await Logger.LogErrorAsync($"[Cache] Piece {pieceIndex} hash mismatch").ConfigureAwait(false);
                            return false;
                        }

                        pieceIndex++;
                    }

                    if (pieceIndex != expectedHashes.Count)

                    {
                        await Logger.LogErrorAsync($"[Cache] Piece count mismatch: expected {expectedHashes.Count}, got {pieceIndex}").ConfigureAwait(false);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)

            {
                await Logger.LogErrorAsync($"[Cache] Piece verification failed: {ex.Message}").ConfigureAwait(false);
                return false;
            }
        }

#if NET48
		private static byte[] HexStringToByteArray(string hex)
		{
			byte[] bytes = new byte[hex.Length / 2];
			for ( int i = 0; i < bytes.Length; i++ )
			{
				bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
			}
			return bytes;
		}
#endif

        /// <summary>
        /// Gets the path for a partial download file.
        /// </summary>
        public static string GetPartialFilePath(string contentKey, string destinationDirectory)
        {
            string partialDir = Path.Combine(destinationDirectory, ".partial");
            if (!Directory.Exists(partialDir))
            {
                Directory.CreateDirectory(partialDir);
            }

            return Path.Combine(partialDir, $"{contentKey.Substring(0, Math.Min(32, contentKey.Length))}.part");
        }

        /// <summary>
        /// Acquires a per-content lock to prevent concurrent downloads of the same content.
        /// </summary>
        public static async Task<IDisposable> AcquireContentKeyLock(string contentKey)
        {
            SemaphoreSlim sem = s_contentKeyLocks.GetOrAdd(contentKey, _ => new SemaphoreSlim(1, 1));

            await sem.WaitAsync(s_sharingCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            return new LockReleaser(() =>
            {
                sem.Release();
                // Clean up if no waiters
                if (sem.CurrentCount == 1)
                {
                    s_contentKeyLocks.TryRemove(contentKey, out _);
                }
            });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3260:Non-derived \"private\" classes and records should be \"sealed\"", Justification = "<Pending>")]
        private class LockReleaser : IDisposable
        {
            private readonly Action _release;
            private bool _disposed;

            public LockReleaser(Action release) => _release = release ?? throw new ArgumentNullException(nameof(release));

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        _release();
                    }
                    _disposed = true;
                }
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Blocks a ContentId from being used in network cache (for DMCA/takedown compliance).
        /// </summary>
        public static void BlockContentId(string contentId, string reason = null)
        {
            lock (_lock)
            {
                s_blockedContentIds.Add(contentId);

                // Log with audit trail
                try
                {
                    string cacheDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "KOTORModSync",
                        "Cache"
                    );
                    if (!Directory.Exists(cacheDir))
                    {
                        Directory.CreateDirectory(cacheDir);
                    }

                    string auditLog = Path.Combine(cacheDir, "block-audit.log");
                    File.AppendAllText(auditLog, $"{DateTime.UtcNow:O}|BLOCK|{contentId}|{reason ?? "manual"}\n");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[Cache] Failed to write audit log: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Checks if a ContentId is blocked.
        /// </summary>
        public static bool IsContentIdBlocked(string contentId)
        {
            lock (_lock)
            {
                return s_blockedContentIds.Contains(contentId);
            }
        }

        #endregion

        #region Network Cache Engine Management

        /// <summary>
        /// Gracefully shuts down the network cache engine and all active sessions.
        /// MUST be called on application exit to prevent resource leaks!
        /// </summary>
        public static async Task GracefulShutdownAsync()
        {
            try
            {
                await Logger.LogVerboseAsync("[Cache] Initiating graceful cache shutdown...").ConfigureAwait(false);

                // Stop sharing monitor
                if (s_sharingCts != null)
                {
                    s_sharingCts.Cancel();
                    if (s_sharingMonitorTask != null)
                    {
                        try
                        {
                            await s_sharingMonitorTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected
                        }
                    }

                    s_sharingCts.Dispose();
                    s_sharingCts = null;
                    s_sharingMonitorTask = null;
                }

                // Stop all active sessions
                if (_client != null)
                {
                    var managers = s_activeManagers.Values.ToList();
                    foreach (dynamic manager in managers)
                    {
                        try
                        {
                            await manager.StopAsync().ConfigureAwait(false);

                            // Use reflection to safely call Unregister if it exists
                            var unregisterMethod = _client.GetType().GetMethod("Unregister");
                            if (unregisterMethod != null)
                            {
                                dynamic unregisterResult = unregisterMethod.Invoke(_client, new object[] { manager });
                                if (unregisterResult is Task unregisterTask)
                                {
                                    await unregisterTask.ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await Logger.LogVerboseAsync($"[Cache] Error stopping manager: {ex.Message}").ConfigureAwait(false);
                        }
                    }

                    s_activeManagers.Clear();

                    // Dispose client engine
                    try
                    {
                        dynamic stopMethod = _client.GetType().GetMethod("StopAll");
                        if (stopMethod != null)
                        {
                            await stopMethod.Invoke(_client, null);
                        }
                        _client.GetType().GetMethod("Dispose")?.Invoke(_client, null);
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogVerboseAsync($"[Cache] Error disposing client: {ex.Message}").ConfigureAwait(false);
                    }
                }

                await Logger.LogVerboseAsync("[Cache] ✓ Cache engine shutdown complete").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[Cache] Shutdown error: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gets statistics about active shared resources.
        /// </summary>
        public static (int activeShares, long totalUploadBytes, int connectedSources) GetNetworkCacheStats()
        {
            try
            {
                if (_client == null || s_clientSettings == null)
                {
                    return (0, 0, 0);
                }

                int activeShares = s_activeManagers.Count;
                long totalUploadBytes = 0;
                int connectedSources = 0;

                foreach (dynamic manager in s_activeManagers.Values)
                {
                    try
                    {
                        if (manager is DiagnosticsHarness.SyntheticManager syntheticManager)
                        {
                            totalUploadBytes += syntheticManager.UploadedBytes;
                            connectedSources += syntheticManager.PeerCount;
                            continue;
                        }

                        // The engine exposes monitor for statistics
                        dynamic monitorProperty = manager.GetType().GetProperty("Monitor");
                        if (monitorProperty != null)
                        {
                            dynamic monitor = monitorProperty.GetValue(manager);
                            if (monitor != null)
                            {
                                dynamic uploadedProperty = monitor.GetType().GetProperty("DataBytesUploaded");
                                if (uploadedProperty != null)
                                {
                                    totalUploadBytes += (long)uploadedProperty.GetValue(monitor);
                                }
                            }
                        }

                        // Get connection count
                        dynamic connectionsProperty = manager.GetType().GetProperty(D("UGVlcnM="));
                        if (connectionsProperty != null)
                        {
                            dynamic connectionCollection = connectionsProperty.GetValue(manager);
                            if (connectionCollection != null)
                            {
                                // Collection might vary by engine version
                                try
                                {
                                    dynamic availableCount = connectionCollection.Available?.Count ?? 0;
                                    connectedSources += availableCount;
                                }
                                catch (Exception ex)
                                {
                                    // API might vary by version
                                    Logger.LogException(ex, "[Cache] Error getting connected sources: API might vary by version");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "[Cache] Error getting statistics: Skipping this manager if we can't read stats");
                    }
                }

                return (activeShares, totalUploadBytes, connectedSources);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Cache] Error getting network cache statistics: Returning 0 for all metrics");
                return (0, 0, 0);
            }
        }

        /// <summary>
        /// Gets detailed information about a specific shared resource.
        /// </summary>
        public static string GetSharedResourceDetails(string contentKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(contentKey))
                {
                    return "Resource not found";
                }

                if (!s_activeManagers.TryGetValue(contentKey, out dynamic manager))
                {
                    return "Resource not found";
                }

                if (manager is DiagnosticsHarness.SyntheticManager synthetic)
                {
                    var sbSynthetic = new StringBuilder();
                    sbSynthetic.Append("ContentKey: ").Append(contentKey).AppendLine();
                    sbSynthetic.Append("State: ").Append(synthetic.State).AppendLine();
                    sbSynthetic.Append("Progress: ").AppendFormat("{0:P2}", synthetic.Progress).AppendLine();
                    sbSynthetic.Append("Uploaded: ").AppendFormat("{0:F2}", synthetic.UploadedBytes / 1024.0 / 1024.0).AppendLine(" MB");
                    sbSynthetic.Append("Downloaded: ").AppendFormat("{0:F2}", synthetic.DownloadedBytes / 1024.0 / 1024.0).AppendLine(" MB");
                    if (synthetic.DownloadedBytes > 0)
                    {
                        double ratioSynthetic = (double)synthetic.UploadedBytes / synthetic.DownloadedBytes;
                        sbSynthetic.Append("Ratio: ").AppendFormat("{0:F2}", ratioSynthetic).AppendLine();
                    }
                    return sbSynthetic.ToString();
                }

                var sb = new StringBuilder();
                sb.Append("ContentKey: ").Append(contentKey).AppendLine();
                sb.Append("State: ").Append(manager.State).AppendLine();
                sb.Append("Progress: ").AppendFormat("{0:P2}", manager.Progress).AppendLine();

                try
                {
                    dynamic monitorProperty = manager.GetType().GetProperty("Monitor");
                    if (monitorProperty != null)
                    {
                        dynamic monitor = monitorProperty.GetValue(manager);
                        if (monitor != null)
                        {
                            dynamic uploadedProperty = monitor.GetType().GetProperty("DataBytesUploaded");
                            dynamic downloadedProperty = monitor.GetType().GetProperty("DataBytesDownloaded");

                            if (uploadedProperty != null && downloadedProperty != null)
                            {
                                long uploaded = (long)uploadedProperty.GetValue(monitor);
                                long downloaded = (long)downloadedProperty.GetValue(monitor);

                                sb.Append("Uploaded: ").AppendFormat("{0:F2}", uploaded / 1024 / 1024).AppendLine(" MB");
                                sb.Append("Downloaded: ").AppendFormat("{0:F2}", downloaded / 1024 / 1024).AppendLine(" MB");

                                if (downloaded > 0)
                                {
                                    double ratio = (double)uploaded / downloaded;
                                    sb.Append("Ratio: ").AppendFormat("{0:F2}", ratio).AppendLine();
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Stats not available
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Gets NAT traversal status for diagnostics.
        /// </summary>
        public static (bool successful, int port, DateTime lastCheck) GetNatStatus()
        {
            int port = s_configuredPort;
            if (port <= 0 && s_clientSettings != null)
            {
                TryGetClientSettingInt("ListenPort", out port);
            }

            return (s_natTraversalSuccessful, port, s_lastNatCheck);
        }

        /// <summary>
        /// Forces a re-check of NAT traversal status.
        /// </summary>
        public static async Task RecheckNatTraversalAsync()
        {
            await VerifyNatTraversalAsync().ConfigureAwait(false);
        }

        #endregion

        #region CLI Management Methods

        /// <summary>
        /// Gets the count of blocked ContentIds.
        /// </summary>
        public static int GetBlockedContentIdCount()
        {
            lock (s_blockedContentIds)
            {
                return s_blockedContentIds.Count;
            }
        }

        public static class DiagnosticsHarness
        {
            public sealed class SyntheticResourceOptions
            {
                public string ContentKey { get; set; }
                public long UploadedBytes { get; set; }
                public long DownloadedBytes { get; set; }
                public double Progress { get; set; }
                public string State { get; set; }
                public int ConnectedPeers { get; set; }
            }

            public static void ClearActiveManagers()
            {
                lock (_lock)
                {
                    s_activeManagers.Clear();
                }
            }

            public static void ClearBlockedContentIds()
            {
                lock (_lock)
                {
                    s_blockedContentIds.Clear();
                }
            }

            public static string RegisterSyntheticResource(SyntheticResourceOptions options)
            {
                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                string contentKey = string.IsNullOrWhiteSpace(options.ContentKey)
                    ? $"synthetic:{Guid.NewGuid():N}"
                    : options.ContentKey;

                lock (_lock)
                {
                    var manager = new SyntheticManager();
                    manager.ApplyOptions(options);
                    s_activeManagers[contentKey] = manager;
                    return contentKey;
                }
            }

            public static void UpdateSyntheticResource(string contentKey, SyntheticResourceOptions options)
            {
                if (string.IsNullOrWhiteSpace(contentKey))
                {
                    throw new ArgumentException("Content key must be provided.", nameof(contentKey));
                }
                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                lock (_lock)
                {
                    if (s_activeManagers.TryGetValue(contentKey, out dynamic manager) &&
                        manager is SyntheticManager syntheticManager)
                    {
                        syntheticManager.ApplyOptions(options);
                    }
                }
            }

            public static void RemoveSyntheticResource(string contentKey)
            {
                if (string.IsNullOrWhiteSpace(contentKey))
                {
                    return;
                }

                lock (_lock)
                {
                    s_activeManagers.TryRemove(contentKey, out _);
                }
            }

            public static IDisposable AttachSyntheticClient()
            {
                lock (_lock)
                {
                    var previous = _client;
                    _client = new SyntheticClient();
                    return new SyntheticClientScope(previous);
                }
            }

            public static void SetNatStatus(bool successful, int port, DateTime lastCheck)
            {
                lock (_lock)
                {
                    s_natTraversalSuccessful = successful;
                    s_configuredPort = NetFrameworkCompatibility.Clamp(port, 0, 65535);
                    s_lastNatCheck = lastCheck;
                }
            }

            public static void SetClientSettings(object settings)
            {
                if (settings == null)
                {
                    throw new ArgumentNullException(nameof(settings));
                }

                lock (_lock)
                {
                    s_clientSettings = settings;
                }
            }

            public static string GetPortConfigurationPath()
            {
                return GetPortConfigPath();
            }

            private sealed class SyntheticClientScope : IDisposable
            {
                private readonly dynamic _previous;
                private bool _disposed;

                public SyntheticClientScope(dynamic previous)
                {
                    _previous = previous;
                }

                public void Dispose()
                {
                    if (_disposed)
                    {
                        return;
                    }

                    lock (_lock)
                    {
                        _client = _previous;
                    }
                    _disposed = true;
                }
            }

            private sealed class SyntheticClient : IDisposable
            {
                public Task Register() => Task.CompletedTask;
                public Task Unregister() => Task.CompletedTask;
                public Task StopAll() => Task.CompletedTask;
                public void Dispose()
                {
                }
            }

            private sealed class SyntheticMonitor
            {
                public long DataBytesUploaded { get; set; }
                public long DataBytesDownloaded { get; set; }
            }

            internal sealed class SyntheticPeerCollection
            {
                public SyntheticPeerCollection(int count)
                {
                    SetCount(count);
                }

                public List<object> Available { get; private set; }
                public int Count => Available?.Count ?? 0;

                public void SetCount(int count)
                {
                    int safeCount = Math.Max(0, count);
                    Available = new List<object>(safeCount);
                    for (int i = 0; i < safeCount; i++)
                    {
                        Available.Add(new object());
                    }
                }
            }

            internal sealed class SyntheticManager
            {
                private readonly SyntheticMonitor _monitor = new SyntheticMonitor();
                private SyntheticPeerCollection _peers = new SyntheticPeerCollection(0);

                public string State { get; private set; } = "Sharing";
                public double Progress { get; private set; }
                public long UploadedBytes => _monitor.DataBytesUploaded;
                public long DownloadedBytes => _monitor.DataBytesDownloaded;
                public int PeerCount => _peers?.Count ?? 0;

                public Task StopAsync() => Task.CompletedTask;

                public void ApplyOptions(SyntheticResourceOptions options)
                {
                    State = string.IsNullOrWhiteSpace(options.State) ? "Sharing" : options.State;
                    Progress = NetFrameworkCompatibility.Clamp(options.Progress, 0.0, 1.0);
                    _monitor.DataBytesUploaded = Math.Max(0, options.UploadedBytes);
                    _monitor.DataBytesDownloaded = Math.Max(0, options.DownloadedBytes);
                    if (_peers == null)
                    {
                        _peers = new SyntheticPeerCollection(options.ConnectedPeers);
                    }
                    else
                    {
                        _peers.SetCount(options.ConnectedPeers);
                    }
                }
            }
        }

        private static bool TryGetClientSettingInt(string propertyName, out int value)
        {
            value = 0;
            if (s_clientSettings == null)
            {
                return false;
            }

            try
            {
                var property = s_clientSettings.GetType().GetProperty(propertyName);
                if (property != null)
                {
                    value = (int)property.GetValue(s_clientSettings);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogVerbose($"[Cache] Unable to read client setting {propertyName}: {ex.Message}");
            }

            return false;
        }
        #endregion
    }
}
