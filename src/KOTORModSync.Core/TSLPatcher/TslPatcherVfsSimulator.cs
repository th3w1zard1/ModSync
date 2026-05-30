// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using JetBrains.Annotations;

using KOTORModSync.Core.Services.FileSystem;

namespace KOTORModSync.Core.TSLPatcher
{
    /// <summary>
    /// Simulates TSLPatcher/HoloPatcher install-list effects in the virtual file system during dry-run.
    /// Parses <c>changes.ini</c> InstallList sections so downstream Move/Rename instructions see expected files.
    /// </summary>
    public static class TslPatcherVfsSimulator
    {
        public static async Task SimulateInstallAsync(
            [NotNull] VirtualFileSystemProvider vfs,
            [NotNull] string tslPatcherDirectoryPath,
            [NotNull] string kotorDirectoryPath)
        {
            if (vfs is null)
            {
                throw new ArgumentNullException(nameof(vfs));
            }

            if (string.IsNullOrWhiteSpace(tslPatcherDirectoryPath))
            {
                throw new ArgumentException("TSL patcher directory path is required.", nameof(tslPatcherDirectoryPath));
            }

            if (string.IsNullOrWhiteSpace(kotorDirectoryPath))
            {
                throw new ArgumentException("KOTOR directory path is required.", nameof(kotorDirectoryPath));
            }

            string changesIniPath = FindChangesIniPath(vfs, tslPatcherDirectoryPath);
            if (changesIniPath is null)
            {
                await Logger.LogVerboseAsync(
                    $"[Simulation] No changes.ini found under '{tslPatcherDirectoryPath}' — skipping TSLPatcher VFS simulation."
                ).ConfigureAwait(false);
                return;
            }

            string modPath = vfs.GetDirectoryName(changesIniPath) ?? tslPatcherDirectoryPath;
            string iniText = await ReadIniTextAsync(vfs, changesIniPath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(iniText))
            {
                return;
            }

            Dictionary<string, Dictionary<string, string>> ini = ParseIni(iniText);
            if (!ini.TryGetValue("InstallList", out Dictionary<string, string> installList))
            {
                return;
            }

            string normalizedKotor = Path.GetFullPath(kotorDirectoryPath);
            foreach (KeyValuePair<string, string> entry in installList)
            {
                if (!entry.Key.StartsWith("install_folder", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string folderSectionName = entry.Key;
                string targetFolder = entry.Value?.Trim();
                if (string.IsNullOrEmpty(targetFolder))
                {
                    continue;
                }

                if (!ini.TryGetValue(folderSectionName, out Dictionary<string, string> folderSection))
                {
                    continue;
                }

                foreach (KeyValuePair<string, string> fileEntry in folderSection)
                {
                    if (fileEntry.Key.StartsWith("!", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string fileName = fileEntry.Value?.Trim();
                    if (string.IsNullOrEmpty(fileName))
                    {
                        continue;
                    }

                    string destinationPath = Path.GetFullPath(Path.Combine(normalizedKotor, targetFolder, fileName));

                    if (fileEntry.Key.StartsWith("Replace", StringComparison.OrdinalIgnoreCase))
                    {
                        await EnsureSimulatedFileAsync(vfs, modPath, fileName, destinationPath).ConfigureAwait(false);
                    }
                    else if (fileEntry.Key.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                    {
                        await EnsureSimulatedFileAsync(vfs, modPath, fileName, destinationPath).ConfigureAwait(false);
                    }
                }
            }
        }

        [CanBeNull]
        private static string FindChangesIniPath([NotNull] IFileSystemProvider vfs, [NotNull] string tslPatcherDirectoryPath)
        {
            string normalized = Path.GetFullPath(tslPatcherDirectoryPath);
            string[] candidates =
            {
                Path.Combine(normalized, "changes.ini"),
                Path.Combine(normalized, "tslpatchdata", "changes.ini"),
            };

            foreach (string candidate in candidates)
            {
                if (vfs.FileExists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static async Task<string> ReadIniTextAsync([NotNull] IFileSystemProvider vfs, [NotNull] string changesIniPath)
        {
            try
            {
                return await vfs.ReadFileAsync(changesIniPath).ConfigureAwait(false);
            }
            catch
            {
                if (File.Exists(changesIniPath))
                {
                    return await File.ReadAllTextAsync(changesIniPath).ConfigureAwait(false);
                }

                return string.Empty;
            }
        }

        private static async Task EnsureSimulatedFileAsync(
            [NotNull] VirtualFileSystemProvider vfs,
            [NotNull] string modPath,
            [NotNull] string fileName,
            [NotNull] string destinationPath)
        {
            string sourcePath = Path.Combine(modPath, fileName);
            if (vfs.FileExists(sourcePath))
            {
                await vfs.CopyFileAsync(sourcePath, destinationPath, overwrite: true).ConfigureAwait(false);
                return;
            }

            if (File.Exists(sourcePath))
            {
                await vfs.CopyFileAsync(sourcePath, destinationPath, overwrite: true).ConfigureAwait(false);
                return;
            }

            await vfs.WriteFileAsync(destinationPath, string.Empty).ConfigureAwait(false);
        }

        [NotNull]
        internal static Dictionary<string, Dictionary<string, string>> ParseIni([NotNull] string content)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string currentSection = null;

            foreach (string rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    if (!result.ContainsKey(currentSection))
                    {
                        result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }

                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0 || currentSection is null)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                result[currentSection][key] = value;
            }

            return result;
        }
    }
}
