using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using HoloPatcher.UI.Views.Dialogs;
using JetBrains.Annotations;

namespace HoloPatcher.UI.Update
{
    /// <summary>
    /// Handles downloading, extracting, and scheduling the self-update process.
    /// Mirrors holopatcher/app.py::_run_autoupdate.
    /// </summary>
    internal sealed class AutoUpdater
    {
        private readonly RemoteUpdateInfo _info;
        private readonly Window _owner;
        private readonly bool _useBetaChannel;

        public AutoUpdater(RemoteUpdateInfo info, Window owner, bool useBetaChannel)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _useBetaChannel = useBetaChannel;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            var progressWindow = new UpdateProgressWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            progressWindow.Show(_owner);

            try
            {
                string tempRoot = CreateTempDirectory();
                string archivePath = await DownloadUpdateAsync(tempRoot, progressWindow.ViewModel, cancellationToken);
                string payloadRoot = await ExtractArchiveAsync(archivePath, progressWindow.ViewModel, cancellationToken);
                await ApplyUpdateAsync(payloadRoot, progressWindow.ViewModel, cancellationToken);
            }
            finally
            {
                progressWindow.AllowClose();
                progressWindow.Close();
            }
        }

        private async Task<string> DownloadUpdateAsync(string tempRoot, UpdateProgressViewModel progress, CancellationToken token)
        {
            string version = _info.GetChannelVersion(_useBetaChannel);
            progress.ReportStatus($"Downloading HoloPatcher {version}...");

            System.Collections.Generic.IReadOnlyList<string> mirrors = _info.GetPlatformMirrors(_useBetaChannel);
            Exception lastError = null;

            using (HttpClient client = CreateHttpClient())
            {
                foreach (string mirror in mirrors)
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();

                        var uri = new Uri(mirror);
                        string fileName = Path.GetFileName(string.IsNullOrWhiteSpace(uri.AbsolutePath) ? $"holopatcher_{version}.zip" : uri.AbsolutePath);
                        string destination = Path.Combine(tempRoot, fileName);

                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri))
                        using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                        {
                            response.EnsureSuccessStatusCode();

                            long? contentLength = response.Content.Headers.ContentLength;

#if NET8_0_OR_GREATER
                            using (Stream httpStream = await response.Content.ReadAsStreamAsync(token))
#else
                            using (Stream httpStream = await response.Content.ReadAsStreamAsync())
#endif
                            using (FileStream fileStream = File.Create(destination))
                            {
                                byte[] buffer = new byte[81920];
                                long downloaded = 0;
                                var sw = Stopwatch.StartNew();

                                while (true)
                                {
#if NET8_0_OR_GREATER
                                    int read = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
#else
                                    int read = await httpStream.ReadAsync(buffer, 0, buffer.Length, token);
#endif
                                    if (read == 0)
                                    {
                                        break;
                                    }
#if NET8_0_OR_GREATER
                                    await fileStream.WriteAsync(buffer.AsMemory(0, read), token);
#else
                                    await fileStream.WriteAsync(buffer, 0, read, token);
#endif
                                    downloaded += read;

                                    TimeSpan? eta = null;
                                    if (contentLength.HasValue && contentLength.Value > 0 && downloaded > 0)
                                    {
                                        double rate = downloaded / Math.Max(sw.Elapsed.TotalSeconds, 0.1);
                                        if (rate > 1)
                                        {
                                            double remaining = (contentLength.Value - downloaded) / rate;
                                            eta = TimeSpan.FromSeconds(remaining);
                                        }
                                    }

                                    progress.ReportDownload(downloaded, contentLength, eta);
                                }

                                if (contentLength.HasValue && downloaded < contentLength.Value)
                                {
                                    throw new IOException("The download ended prematurely.");
                                }

                                progress.ReportStatus("Download complete.");
                                return destination;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        progress.ReportStatus($"Download failed from {mirror}: {ex.Message}");
                    }
                }
            }

            throw new InvalidOperationException("All update mirrors failed.", lastError);
        }

        private static string CreateTempDirectory()
        {
            string root = Path.Combine(Path.GetTempPath(), "holopatcher_update", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(root);
            return root;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KPatcher AutoUpdater");
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        private async Task<string> ExtractArchiveAsync(string archivePath, UpdateProgressViewModel progress, CancellationToken token)
        {
            progress.ReportStatus("Extracting update package...");
            string extractRoot = Path.Combine(Path.GetDirectoryName(archivePath) ?? Path.GetTempPath(), "extracted");
            Directory.CreateDirectory(extractRoot);

            string lower = archivePath.ToLowerInvariant();
            if (lower.EndsWith(".zip", StringComparison.Ordinal))
            {
#if NET8_0_OR_GREATER
                await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true), token);
#else
                await Task.Run(() => ExtractZipWithOverwrite(archivePath, extractRoot), token);
#endif
            }
            else if (lower.EndsWith(".tar.gz", StringComparison.Ordinal) || lower.EndsWith(".tgz", StringComparison.Ordinal))
            {
                await ExtractTarGzAsync(archivePath, extractRoot, token);
            }
            else
            {
                throw new NotSupportedException($"Unsupported archive format: {Path.GetExtension(archivePath)}");
            }

            progress.ReportStatus("Archive extracted.");
            return LocatePayloadRoot(extractRoot);
        }

#if !NET8_0_OR_GREATER
        private static void ExtractZipWithOverwrite(string archivePath, string extractRoot)
        {
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinationPath = Path.Combine(extractRoot, entry.FullName);
                    string destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }
                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }
            }
        }
#endif

        private static async Task ExtractTarGzAsync(string archivePath, string outputDirectory, CancellationToken token)
        {
            string tarPath = Path.Combine(Path.GetDirectoryName(archivePath) ?? Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(archivePath)}.tar");
            using (FileStream sourceStream = File.OpenRead(archivePath))
            using (FileStream tarStream = File.Create(tarPath))
            using (var gzip = new GZipStream(sourceStream, CompressionMode.Decompress))
            {
#if NET8_0_OR_GREATER
                await gzip.CopyToAsync(tarStream, token);
#else
                await gzip.CopyToAsync(tarStream);
#endif
            }

#if NET8_0_OR_GREATER
            System.Formats.Tar.TarFile.ExtractToDirectory(tarPath, outputDirectory, overwriteFiles: true);
#else
            // Fallback for older frameworks - just throw not supported for tar.gz
            throw new NotSupportedException("tar.gz extraction requires .NET 8 or greater");
#endif
        }

        private static string LocatePayloadRoot(string directory)
        {
            string current = directory;
            while (true)
            {
                string[] files = Directory.GetFiles(current);
                string[] dirs = Directory.GetDirectories(current);

                if (dirs.Length == 1 && files.Length == 0)
                {
                    current = dirs[0];
                    continue;
                }

                return current;
            }
        }

        private async Task ApplyUpdateAsync(string payloadRoot, UpdateProgressViewModel progress, CancellationToken token)
        {
#if NET8_0_OR_GREATER
            string currentProcessPath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Unable to locate current process path.");
#else
            string currentProcessPath = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Unable to locate current process path.");
#endif

            string targetDirectory = Path.GetDirectoryName(currentProcessPath)
                ?? throw new InvalidOperationException("Unable to determine application directory.");

            string scriptDirectory = Path.Combine(Path.GetTempPath(), "holopatcher_update_scripts");
            Directory.CreateDirectory(scriptDirectory);
            string scriptPath = Path.Combine(scriptDirectory,
                IsWindows() ? $"update_{Environment.ProcessId}.ps1" : $"update_{Environment.ProcessId}.sh");

            string scriptContent = IsWindows()
                ? BuildWindowsScript(payloadRoot, targetDirectory, currentProcessPath, Environment.ProcessId)
                : BuildUnixScript(payloadRoot, targetDirectory, currentProcessPath, Environment.ProcessId);

#if NET8_0_OR_GREATER
            await File.WriteAllTextAsync(scriptPath, scriptContent, token);
#else
            File.WriteAllText(scriptPath, scriptContent);
            await Task.CompletedTask;
#endif

            if (!IsWindows())
            {
#if NET8_0_OR_GREATER
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);
#endif
            }

            progress.ReportStatus("Finalizing update...");
            LaunchScript(scriptPath);
            progress.ReportStatus("Restarting to complete the update...");

            await Task.Delay(TimeSpan.FromSeconds(2), token);
            Environment.Exit((int)Core.ExitCode.CloseForUpdateProcess);
        }

        private static bool IsWindows()
        {
#if NET8_0_OR_GREATER
            return OperatingSystem.IsWindows();
#else
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
        }

        private static void LaunchScript(string scriptPath)
        {
            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (IsWindows())
            {
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
            }
            else
            {
                psi.FileName = "/bin/bash";
                psi.Arguments = $"\"{scriptPath}\"";
            }

            Process.Start(psi);
        }

        private static string BuildWindowsScript(string sourceDir, string targetDir, string executablePath, int pid)
        {
            string escapedSource = EscapePowerShellPath(sourceDir);
            string escapedTarget = EscapePowerShellPath(targetDir);
            string escapedExecutable = EscapePowerShellPath(executablePath);

            return $@"
$ErrorActionPreference = 'Stop'
$parentPid = {pid}
$sourcePath = '{escapedSource}'
$destPath = '{escapedTarget}'
$launchApp = '{escapedExecutable}'
$scriptPath = $MyInvocation.MyCommand.Path

function Write-Log {{
    param([string]$Message)
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss""
    Write-Host ""[$timestamp] $Message""
}}

Write-Log ""Waiting for process $parentPid to exit...""
while (Get-Process -Id $parentPid -ErrorAction SilentlyContinue) {{
    Start-Sleep -Seconds 1
}}

Write-Log ""Copying update files...""
robocopy ""$sourcePath"" ""$destPath"" /MIR /NFL /NDL /NJH /NJS /R:2 /W:1 | Out-Null

Write-Log ""Launching updated application...""
Start-Process -FilePath ""$launchApp"" | Out-Null

Write-Log ""Cleaning up temporary files...""
Start-Sleep -Seconds 1
if (Test-Path ""$sourcePath"") {{ Remove-Item ""$sourcePath"" -Recurse -Force -ErrorAction SilentlyContinue }}
if (Test-Path ""$scriptPath"") {{ Remove-Item ""$scriptPath"" -Force -ErrorAction SilentlyContinue }}
";
        }

        private static string EscapePowerShellPath(string path)
        {
            return path.Replace("'", "''", StringComparison.Ordinal);
        }

        private static string BuildUnixScript(string sourceDir, string targetDir, string executablePath, int pid)
        {
            return $@"#!/bin/bash
set -euo pipefail

log() {{
    echo ""[$(date '+%Y-%m-%d %H:%M:%S')] $1""
}}

log ""Waiting for process {pid} to exit...""
while kill -0 {pid} 2>/dev/null; do
    sleep 1
done

log ""Copying files...""
rsync -a --delete ""{sourceDir.TrimEnd('/')}/"" ""{targetDir.TrimEnd('/')}/""

log ""Launching updated application...""
chmod +x ""{executablePath}""
""{executablePath}"" &

log ""Cleaning up...""
rm -rf ""{sourceDir}""
rm -f ""$0""
";
        }
    }
}
