// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using KOTORModSync.Core.Utility;

namespace KOTORModSync.Services
{
    public sealed class AutoUpdatePackage
    {
        public string ArchivePath { get; set; } = string.Empty;
    }

    public interface IAutoUpdateCoordinator
    {
        Task<AutoUpdateExecutionResult> ApplyUpdateAsync(
            AutoUpdatePackage package,
            string currentProcessPath,
            CancellationToken cancellationToken = default);
    }

    public sealed class AutoUpdateCoordinator : IAutoUpdateCoordinator
    {
        public async Task<AutoUpdateExecutionResult> ApplyUpdateAsync(
            AutoUpdatePackage package,
            string currentProcessPath,
            CancellationToken cancellationToken = default)
        {
            if (package is null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (string.IsNullOrWhiteSpace(package.ArchivePath))
            {
                throw new ArgumentException("ArchivePath must be provided.", nameof(package));
            }

            if (string.IsNullOrWhiteSpace(currentProcessPath))
            {
                throw new ArgumentException("Current process path must be provided.", nameof(currentProcessPath));
            }

            string payloadRoot = await ExtractArchiveAsync(package.ArchivePath, cancellationToken).ConfigureAwait(false);
            string targetDirectory = Path.GetDirectoryName(currentProcessPath)
                ?? throw new InvalidOperationException("Unable to determine current application directory.");

            string scriptDirectory = Path.Combine(Path.GetTempPath(), "kotormodsync_update_scripts");
            Directory.CreateDirectory(scriptDirectory);

            int processId = CurrentProcessId;
            string scriptPath = Path.Combine(
                scriptDirectory,
                IsWindows()
                    ? $"update_{processId}.ps1"
                    : $"update_{processId}.sh");

            string scriptContent = IsWindows()
                ? BuildWindowsScript(payloadRoot, targetDirectory, currentProcessPath, processId)
                : BuildUnixScript(payloadRoot, targetDirectory, currentProcessPath, processId);

            await NetFrameworkCompatibility.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!IsWindows())
            {
#if NET8_0_OR_GREATER
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
            }

            ProcessStartInfo launchInfo = CreateLaunchInfo(scriptPath);

            return new AutoUpdateExecutionResult
            {
                PayloadRoot = payloadRoot,
                ScriptPath = scriptPath,
                LaunchInfo = launchInfo,
                ExitCode = 10,
            };
        }

        private static async Task<string> ExtractArchiveAsync(string archivePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"Update archive not found: {archivePath}", archivePath);
            }

            string extractionRoot = Path.Combine(
                Path.GetTempPath(),
                "kotormodsync_update",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

            Directory.CreateDirectory(extractionRoot);

            string lowerPath = archivePath.ToLowerInvariant();
            if (lowerPath.EndsWith(".zip", StringComparison.Ordinal))
            {
                await Task.Run(
                    () =>
                    {
#if NET5_0_OR_GREATER
                        ZipFile.ExtractToDirectory(archivePath, extractionRoot, overwriteFiles: true);
#else
                        ZipFile.ExtractToDirectory(archivePath, extractionRoot);
#endif
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else if (lowerPath.EndsWith(".tar.gz", StringComparison.Ordinal) || lowerPath.EndsWith(".tgz", StringComparison.Ordinal))
            {
                await ExtractTarGzAsync(archivePath, extractionRoot, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException($"Unsupported update archive format: {Path.GetExtension(archivePath)}");
            }

            return LocatePayloadRoot(extractionRoot);
        }

        private static async Task ExtractTarGzAsync(string archivePath, string extractionRoot, CancellationToken cancellationToken)
        {
            string tarPath = Path.Combine(
                Path.GetDirectoryName(archivePath) ?? Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(archivePath)}.tar");

            using (FileStream sourceStream = File.OpenRead(archivePath))
            using (FileStream tarStream = File.Create(tarPath))
            using (var gzip = new GZipStream(sourceStream, CompressionMode.Decompress))
            {
#if NET8_0_OR_GREATER
                await gzip.CopyToAsync(tarStream, cancellationToken).ConfigureAwait(false);
                System.Formats.Tar.TarFile.ExtractToDirectory(tarPath, extractionRoot, overwriteFiles: true);
#else
                await gzip.CopyToAsync(tarStream).ConfigureAwait(false);
                throw new NotSupportedException("tar.gz extraction requires .NET 8 or greater.");
#endif
            }
        }

        private static string LocatePayloadRoot(string extractionRoot)
        {
            string current = extractionRoot;
            while (true)
            {
                string[] files = Directory.GetFiles(current);
                string[] directories = Directory.GetDirectories(current);

                if (directories.Length == 1 && files.Length == 0)
                {
                    current = directories[0];
                    continue;
                }

                return current;
            }
        }

        private static ProcessStartInfo CreateLaunchInfo(string scriptPath)
        {
            var launchInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (IsWindows())
            {
                launchInfo.FileName = "powershell.exe";
                launchInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
            }
            else
            {
                launchInfo.FileName = "/bin/bash";
                launchInfo.Arguments = $"\"{scriptPath}\"";
            }

            return launchInfo;
        }

        internal static string BuildWindowsScript(string sourceDir, string targetDir, string executablePath, int parentProcessId)
        {
            string escapedSource = EscapePowerShellPath(sourceDir);
            string escapedTarget = EscapePowerShellPath(targetDir);
            string escapedExecutable = EscapePowerShellPath(executablePath);

            return $@"
$ErrorActionPreference = 'Stop'
$parentPid = {parentProcessId}
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

        internal static string BuildUnixScript(string sourceDir, string targetDir, string executablePath, int parentProcessId)
        {
            return $@"#!/bin/bash
set -euo pipefail

log() {{
    echo ""[$(date '+%Y-%m-%d %H:%M:%S')] $1""
}}

log ""Waiting for process {parentProcessId} to exit...""
while kill -0 {parentProcessId} 2>/dev/null; do
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

        private static string EscapePowerShellPath(string value)
        {
#if NETCOREAPP
            return value.Replace("'", "''", StringComparison.Ordinal);
#else
            return value.Replace("'", "''");
#endif
        }

        private static int CurrentProcessId
        {
            get
            {
#if NET5_0_OR_GREATER
                return Environment.ProcessId;
#else
                return Process.GetCurrentProcess().Id;
#endif
            }
        }

        private static bool IsWindows()
        {
#if NET8_0_OR_GREATER
            return OperatingSystem.IsWindows();
#else
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
        }
    }

    public sealed class AutoUpdateExecutionResult
    {
        public string PayloadRoot { get; set; }

        public string ScriptPath { get; set; }

        public ProcessStartInfo LaunchInfo { get; set; }

        public int ExitCode { get; set; }
    }
}
