// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using JetBrains.Annotations;

using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Utility;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using SevenZip;

using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace KOTORModSync.Core.Utility
{
    public static class ArchiveHelper
    {
        public static readonly ExtractionOptions DefaultExtractionOptions = new ExtractionOptions
        {
            ExtractFullPath = false,
            Overwrite = true,
            PreserveFileTime = true,
        };

        public static readonly string[] DefaultArchiveSearchPatterns =
        {
            "*.zip",
            "*.rar",
            "*.7z",
            "*.exe",
        };

        private static readonly object s_sevenZipInitLock = new object();
        private static bool s_sevenZipInitialized;
        private static bool s_sevenZipAvailable;
        private static bool s_missingSevenZipLogged;

        public static bool IsArchive([NotNull] string filePath)
        {
            if (filePath is null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            return HasArchiveExtension(Path.GetExtension(filePath));
        }

        public static bool IsArchive([NotNull] FileInfo thisFile)
        {
            if (thisFile is null)
            {
                throw new ArgumentNullException(nameof(thisFile));
            }

            return HasArchiveExtension(thisFile.Extension);
        }

        public static bool HasArchiveExtension([CanBeNull] string pathOrExtension)
        {
            if (string.IsNullOrWhiteSpace(pathOrExtension))
            {
                return false;
            }

            string extension = pathOrExtension.StartsWith(".", StringComparison.Ordinal)
                ? pathOrExtension
                : Path.GetExtension(pathOrExtension);

            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tar", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".gz", StringComparison.OrdinalIgnoreCase);
        }

        public static (IArchive, FileStream) OpenArchive(string archivePath)
        {
            if (archivePath is null || !File.Exists(archivePath))
            {
                throw new ArgumentException(message: "Path must be a valid file on disk.", nameof(archivePath));
            }

            FileStream stream = null;

            try
            {
                stream = File.OpenRead(archivePath);
                IArchive archive = null;

                if (archivePath.EndsWith(value: ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    archive = ZipArchive.Open(stream);
                }
                else if (archivePath.EndsWith(value: ".rar", StringComparison.OrdinalIgnoreCase))
                {
                    archive = RarArchive.Open(stream);
                }
                else if (archivePath.EndsWith(value: ".7z", StringComparison.OrdinalIgnoreCase))
                {
                    archive = SevenZipArchive.Open(stream);
                }

                return (archive, stream);
            }
            catch (Exception ex)
            {
                stream?.Dispose();

                if (IsExpectedArchiveFormatException(ex))
                {
                    Logger.LogVerbose($"[ArchiveHelper] SharpCompress could not open '{Path.GetFileName(archivePath)}': {ex.Message}");
                }
                else
                {
                    Logger.LogException(ex, $"[ArchiveHelper] Failed to open archive '{Path.GetFileName(archivePath)}'");
                }

                return (null, null);
            }
        }

        public static bool IsPotentialSevenZipSFX([NotNull] string filePath)
        {
            byte[] sfxSignature =
            {
                0x4D, 0x5A,
            };

            byte[] fileHeader = new byte[sfxSignature.Length];

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                _ = fs.Read(fileHeader, offset: 0, sfxSignature.Length);
            }

            return sfxSignature.SequenceEqual(fileHeader);
        }

        public static bool TryExtractSevenZipSfx([NotNull] string sfxPath, [NotNull] string destinationPath, [NotNull] List<string> extractedFiles)
        {
            if (sfxPath is null)
            {
                throw new ArgumentNullException(nameof(sfxPath));
            }

            if (destinationPath is null)
            {
                throw new ArgumentNullException(nameof(destinationPath));
            }

            if (extractedFiles is null)
            {
                throw new ArgumentNullException(nameof(extractedFiles));
            }

            if (!File.Exists(sfxPath))
            {
                return false;
            }

            byte[] sevenZipSignature = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
            long signatureOffset = -1;

            try
            {
                using (var fs = new FileStream(sfxPath, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[8192];
                    long position = 0;
                    int bytesRead;

                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < bytesRead - sevenZipSignature.Length + 1; i++)
                        {
                            bool match = true;
                            for (int j = 0; j < sevenZipSignature.Length; j++)
                            {
                                if (buffer[i + j] != sevenZipSignature[j])
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                signatureOffset = position + i;
                                break;
                            }
                        }

                        if (signatureOffset != -1)
                        {
                            break;
                        }

                        position += bytesRead;
                        if (bytesRead == buffer.Length)
                        {
                            fs.Seek(-sevenZipSignature.Length, SeekOrigin.Current);
                            position -= sevenZipSignature.Length;
                        }
                    }
                }

                if (signatureOffset == -1)
                {
                    Logger.LogVerbose($"No 7z signature found in SFX file: {sfxPath}");
                    return false;
                }

                Logger.LogVerbose($"Found 7z signature at offset {signatureOffset} in {sfxPath}");

                string tempSevenZipPath = Path.Combine(Path.GetTempPath(), $"sfx_extract_{Guid.NewGuid()}.7z");

                try
                {
                    using (var sourceStream = new FileStream(sfxPath, FileMode.Open, FileAccess.Read))
                    using (var destStream = new FileStream(tempSevenZipPath, FileMode.Create, FileAccess.Write))
                    {
                        sourceStream.Seek(signatureOffset, SeekOrigin.Begin);
                        sourceStream.CopyTo(destStream);
                    }

                    string extractFolderName = Path.GetFileNameWithoutExtension(sfxPath);
                    string extractPath = Path.GetFullPath(Path.Combine(destinationPath, extractFolderName));

                    using (FileStream stream = File.OpenRead(tempSevenZipPath))
                    using (var archive = SevenZipArchive.Open(stream))
                    using (IReader reader = archive.ExtractAllEntries())
                    {
                        while (reader.MoveToNextEntry())
                        {
                            if (reader.Entry.IsDirectory)
                            {
                                continue;
                            }

                            if (!PathHelper.TryGetZipSafeArchiveEntryExtractPath(
                                    extractPath,
                                    reader.Entry.Key,
                                    out string destinationItemPath,
                                    out string destinationDirectory))
                            {
                                Logger.LogWarning($"[ArchiveHelper] Skipping 7z SFX entry with unsafe path: '{reader.Entry.Key}'");
                                continue;
                            }

                            if (MainConfig.CaseInsensitivePathing && !Directory.Exists(destinationDirectory))
                            {
                                destinationDirectory = PathHelper.GetCaseSensitivePath(destinationDirectory, isFile: false).Item1;
                            }

                            if (!Directory.Exists(destinationDirectory))
                            {
                                _ = Directory.CreateDirectory(destinationDirectory);
                                Logger.LogVerbose($"Create directory '{destinationDirectory}'");
                            }

                            Logger.LogVerbose($"Extract '{reader.Entry.Key}' to '{destinationDirectory}'");
                            reader.WriteEntryToDirectory(destinationDirectory, DefaultExtractionOptions);
                            extractedFiles.Add(destinationItemPath);
                        }
                    }

                    Logger.Log($"Successfully extracted 7z SFX archive: {sfxPath}");
                    return true;
                }
                finally
                {
                    if (File.Exists(tempSevenZipPath))
                    {
                        try
                        {
                            File.Delete(tempSevenZipPath);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogVerbose($"Failed to delete temporary 7z file: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to extract 7z SFX: {sfxPath}");
                return false;
            }
        }

        public static string AnalyzeArchiveForExe(FileStream fileStream, IArchive archive)
        {
            string exePath = null;
            bool tslPatchDataFolderExists = false;

            try
            {
                using (IReader reader = archive.ExtractAllEntries())
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (reader.Entry.IsDirectory)
                        {
                            continue;
                        }

                        string fileName = Path.GetFileName(reader.Entry.Key);
                        string directory = Path.GetDirectoryName(reader.Entry.Key);

                        if (fileName.EndsWith(value: ".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (exePath != null)
                            {
                                return null;
                            }

                            exePath = reader.Entry.Key;
                        }

                        if (!(directory is null) && NetFrameworkCompatibility.Contains(directory, "tslpatchdata", StringComparison.OrdinalIgnoreCase))
                        {
                            tslPatchDataFolderExists = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"SharpCompress failed to analyze archive: {ex.Message}");
                Logger.LogVerbose("Archive may require 7zip for extraction.");
                return null;
            }

            if (
                exePath != null
                && tslPatchDataFolderExists
                && NetFrameworkCompatibility.Contains(Path.GetDirectoryName(exePath), "tslpatchdata", StringComparison.OrdinalIgnoreCase)
            )
            {
                return exePath;
            }

            return null;
        }

        public static async System.Threading.Tasks.Task<List<string>> TryListArchiveWithSevenZipCliAsync([NotNull] string archivePath)
        {
            if (archivePath is null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            var fileList = new List<string>();
            string sevenZipPath = null;
            string[] possiblePaths = {
				// Command-line accessible (PATH)
				"7z",
                "7za",
                "7zr",

				// Windows - Program Files locations
				@"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
                @"C:\Program Files\7-Zip\7za.exe",
                @"C:\Program Files (x86)\7-Zip\7za.exe",
                @"C:\Program Files\7-Zip\7zr.exe",
                @"C:\Program Files (x86)\7-Zip\7zr.exe",

				// Windows - Chocolatey
				@"C:\ProgramData\chocolatey\bin\7z.exe",
                @"C:\ProgramData\chocolatey\lib\7zip\tools\7z.exe",

				// Windows - Scoop
				@"C:\Users\%USERNAME%\scoop\apps\7zip\current\7z.exe",

				// Windows - Portable installations
				@"C:\7-Zip\7z.exe",
                @"C:\Tools\7-Zip\7z.exe",
                @"C:\Portable\7-Zip\7z.exe",

				// macOS - Homebrew/MacPorts
				"/usr/local/bin/7z",
                "/usr/local/bin/7za",
                "/opt/homebrew/bin/7z",
                "/opt/homebrew/bin/7za",

				// macOS - Manual installations
				"/Applications/7zX.app/Contents/MacOS/7za",
                "/Applications/Keka.app/Contents/Resources/7za",

				// Linux - Common system paths
				"/usr/bin/7z",
                "/usr/bin/7za",
                "/usr/bin/7zr",
                "/usr/local/bin/7z",
                "/usr/local/bin/7za",
                "/usr/local/bin/7zr",

				// Linux - Snap
				"/snap/bin/7z",
                "/snap/p7zip/current/usr/bin/7z",

				// Linux - Flatpak
				"/var/lib/flatpak/exports/bin/7z",

				// Linux - AppImage
				"/opt/7-Zip/7z",

				// Cross-platform - Home directory installations
				"~/bin/7z",
                "~/.local/bin/7z",
            };

            foreach (string path in possiblePaths)
            {
                try
                {
                    (int exitCode, string _, string _) = await PlatformAgnosticMethods.ExecuteProcessAsync(
                        path,
                        "--help",
                        timeout: 2000,
                        hideProcess: true,
                        noLogging: true
                    ).ConfigureAwait(false);

                    if (exitCode == 0)
                    {
                        sevenZipPath = path;


                        await Logger.LogVerboseAsync($"Found 7z CLI at: {sevenZipPath}").ConfigureAwait(false);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogExceptionAsync(ex, $"Failed to find 7z CLI at: {path}").ConfigureAwait(false);
                }
            }

            if (sevenZipPath is null)
            {
                await Logger.LogWarningAsync("7z CLI not found in any standard location. Install 7-Zip to improve archive compatibility.").ConfigureAwait(false);


                await Logger.LogVerboseAsync($"Searched {possiblePaths.Length} possible 7z locations without success.").ConfigureAwait(false);
                return fileList;
            }

            try
            {
                string args = $"l -slt \"{archivePath}\"";
                (int exitCode, string output, string _) = await PlatformAgnosticMethods.ExecuteProcessAsync(
                    sevenZipPath,
                    args,
                    timeout: 30000,
                    hideProcess: true,
                    noLogging: true
                ).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    await Logger.LogVerboseAsync($"7z CLI list failed with exit code {exitCode}").ConfigureAwait(false);
                    return fileList;
                }

                bool inFileSection = false;
                string currentPath = null;
                bool isDirectory = false;

                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmedLine = line.Trim();

                    if (trimmedLine.StartsWith("Path = ", StringComparison.Ordinal))
                    {
                        if (currentPath != null && !isDirectory)
                        {
                            fileList.Add(currentPath);
                        }
                        currentPath = trimmedLine.Substring("Path = ".Length);
                        isDirectory = false;
                        inFileSection = true;
                    }
                    else if (inFileSection && trimmedLine.StartsWith("Folder = ", StringComparison.Ordinal))
                    {
                        string folderValue = trimmedLine.Substring("Folder = ".Length);
                        isDirectory = folderValue.Equals("+", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(trimmedLine, "----------", StringComparison.Ordinal))
                    {
                        if (currentPath != null && !isDirectory)
                        {
                            fileList.Add(currentPath);
                        }
                        currentPath = null;
                        isDirectory = false;
                        inFileSection = false;
                    }
                }

                if (currentPath != null && !isDirectory)
                {
                    fileList.Add(currentPath);


                }

                if (fileList.Count > 0 && string.Equals(fileList



















[0], Path.GetFileName(archivePath), StringComparison.Ordinal))
                {
                    fileList.RemoveAt(0);
                }

                await Logger.LogVerboseAsync($"7z CLI listed {fileList.Count} files in archive").ConfigureAwait(false);
                return fileList;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"Failed to list archive with 7z CLI: {archivePath}").ConfigureAwait(false);
                return fileList;
            }
        }

        public static async System.Threading.Tasks.Task<bool> TryExtractWithSevenZipCliAsync([NotNull] string archivePath, [NotNull] string destinationPath, [NotNull] List<string> extractedFiles)
        {
            if (archivePath is null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            if (destinationPath is null)
            {
                throw new ArgumentNullException(nameof(destinationPath));
            }

            if (extractedFiles is null)
            {
                throw new ArgumentNullException(nameof(extractedFiles));
            }

            string sevenZipPath = null;
            string[] possiblePaths = { "7z", "7za", "/usr/bin/7z", "/usr/local/bin/7z" };

            foreach (string path in possiblePaths)
            {
                try
                {
                    (int exitCode, string _, string _) = await PlatformAgnosticMethods.ExecuteProcessAsync(
                        path,
                        "--help",
                        timeout: 2000,
                        hideProcess: true,
                        noLogging: true
                    ).ConfigureAwait(false);

                    if (exitCode == 0)
                    {
                        sevenZipPath = path;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogExceptionAsync(ex, $"Failed to find 7z CLI at: {path}").ConfigureAwait(false);
                }
            }

            if (sevenZipPath is null)
            {
                await Logger.LogVerboseAsync("7z CLI not found on PATH").ConfigureAwait(false);
                return false;
            }

            await Logger.LogVerboseAsync($"Found 7z CLI at: {sevenZipPath}").ConfigureAwait(false);

            try
            {
                string extractFolderName = Path.GetFileNameWithoutExtension(archivePath);
                string extractPath = Path.Combine(destinationPath, extractFolderName);

                if (!Directory.Exists(extractPath))
                {
                    _ = Directory.CreateDirectory(extractPath);
                }

                string args = $"x \"-o{extractPath}\" -y \"{archivePath}\"";
                (int exitCode, string output, string _) = await PlatformAgnosticMethods.ExecuteProcessAsync(
                    sevenZipPath,
                    args,
                    timeout: 120000,
                    hideProcess: true,
                    noLogging: false
                ).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    await Logger.LogErrorAsync($"7z CLI extraction failed with exit code {exitCode}").ConfigureAwait(false);
                    return false;
                }

                if (Directory.Exists(extractPath))
                {
                    string[] files = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
                    extractedFiles.AddRange(files);
                    await Logger.LogInfoAsync($"Successfully extracted archive using 7z CLI: {archivePath}").ConfigureAwait(false);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"Failed to extract with 7z CLI: {archivePath}").ConfigureAwait(false);
                return false;
            }
        }

        public static void ExtractWith7Zip(FileStream stream, string destinationDirectory)
        {
            if (UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
            {
                throw new NotImplementedException("Non-windows OS's are not currently supported");
            }

            SevenZipBase.SetLibraryPath(Path.Combine(UtilityHelper.GetResourcesDirectory(), "7z.dll"));
            var extractor = new SevenZipExtractor(stream);
            extractor.ExtractArchive(destinationDirectory);
        }

        public static void OutputModTree([NotNull] DirectoryInfo directory, [NotNull] string outputPath)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            if (outputPath is null)
            {
                throw new ArgumentNullException(nameof(outputPath));
            }

            Dictionary<string, object> root = GenerateArchiveTreeJson(directory);
            try
            {
                string json = JsonConvert.SerializeObject(
                    root,
                    Formatting.Indented,
                    new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    }
                );

                File.WriteAllText(outputPath, json);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Error writing output file '{outputPath}': {ex.Message}");
            }
        }

        [CanBeNull]
        public static Dictionary<string, object> GenerateArchiveTreeJson([NotNull] DirectoryInfo directory)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            var root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "Name", directory.Name
                },
                {
                    "Type", "directory"
                },
                {
                    "Contents", new List<object>()
                },
            };

            try
            {
                foreach (FileInfo file in directory.EnumerateFilesSafely(searchPattern: "*.*"))
                {
                    if (file is null || !IsArchive(file.Extension))
                    {
                        continue;
                    }

                    var fileInfo = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "Name", file.Name
                        },
                        {
                            "Type", "file"
                        },
                    };
                    List<ModDirectory.ArchiveEntry> archiveEntries = TraverseArchiveEntries(file.FullName);
                    var archiveRoot = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "Name", file.Name
                        },
                        {
                            "Type", "directory"
                        },
                        {
                            "Contents", archiveEntries
                        },
                    };

                    fileInfo["Contents"] = archiveRoot["Contents"];

                    (root["Contents"] as List<object>)?.Add(fileInfo);
                }

            }
            catch (Exception ex)
            {
                Logger.Log($"Error generating archive tree for '{directory.FullName}': {ex.Message}");
            }

            return root;
        }

        [NotNull]
        private static List<ModDirectory.ArchiveEntry> TraverseArchiveEntries([NotNull] string archivePath)
        {
            if (archivePath is null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            var archiveEntries = new List<ModDirectory.ArchiveEntry>();

            try
            {
                (IArchive archive, FileStream stream) = OpenArchive(archivePath);
                if (archive is null || stream is null)
                {
                    Logger.Log($"Unsupported archive format: '{Path.GetExtension(archivePath)}'");
                    stream?.Dispose();
                    return archiveEntries;
                }

                try
                {
                    archiveEntries.AddRange(
                        from entry in archive.Entries.Where(e => !e.IsDirectory)
                        let pathParts = entry.Key.Split(
                            archivePath.EndsWith(value: ".rar", StringComparison.OrdinalIgnoreCase)
                                ? '\\'
                                : '/'
                        )
                        select new ModDirectory.ArchiveEntry
                        {
                            Name = pathParts[pathParts.Length - 1],
                            Path = entry.Key,
                        }
                    );
                }
                catch (Exception enumEx)
                {
                    Logger.LogWarning($"SharpCompress failed to enumerate archive entries for '{Path.GetFileName(archivePath)}': {enumEx.Message}");
                    Logger.LogVerbose("This archive may require 7zip for extraction.");
                }

                stream.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reading archive '{archivePath}': {ex.Message}");
            }

            return archiveEntries;
        }

        public static void ProcessArchiveEntry(
            [NotNull] IArchiveEntry entry,
            [NotNull] Dictionary<string, object> currentDirectory
        )
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (currentDirectory is null)
            {
                throw new ArgumentNullException(nameof(currentDirectory));
            }

            string[] pathParts = entry.Key.Split('/');
            bool isFile = !entry.IsDirectory;

            foreach (string name in pathParts)
            {
                List<object> existingDirectory = currentDirectory["Contents"] as List<object>
                    ?? throw new InvalidDataException(
                        $"Unexpected data type for directory contents: '{currentDirectory["Contents"]?.GetType()}'"
                    );

                object existingChild = existingDirectory.Find(
                    c => c is Dictionary<string, object> dict
                        && dict.ContainsKey("Name")
                        && dict["Name"] is string directoryName
                        && directoryName.Equals(name, StringComparison.OrdinalIgnoreCase)
                );

                if (existingChild != null)
                {
                    if (isFile)
                    {
                        ((Dictionary<string, object>)existingChild)["Type"] = "file";
                    }

                    currentDirectory = (Dictionary<string, object>)existingChild;
                }
                else
                {
                    var child = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "Name", name
                        },
                        {
                            "Type", isFile
                                ? "file"
                                : "directory"
                        },
                        {
                            "Contents", new List<object>()
                        },
                    };
                    existingDirectory.Add(child);
                    currentDirectory = child;
                }
            }
        }

        public static Dictionary<string, HashSet<string>> GetArchiveContentsByFileName([CanBeNull] IEnumerable<string> archivePaths)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (archivePaths is null)
            {
                return result;
            }

            foreach (string archivePath in archivePaths)
            {
                if (string.IsNullOrWhiteSpace(archivePath))
                {
                    continue;
                }

                if (TryGetArchiveEntries(archivePath, out HashSet<string> entries, out string failureMessage))
                {
                    if (entries.Count > 0)
                    {
                        string archiveFileName = Path.GetFileName(archivePath);
                        result[archiveFileName] = entries;
                    }
                }
                else if (!string.IsNullOrEmpty(failureMessage))
                {
                    Logger.LogVerbose($"[ArchiveHelper] Failed to enumerate archive '{archivePath}': {failureMessage}");
                }
            }

            return result;
        }

        public static bool TryGetArchiveEntries([CanBeNull] string archivePath, out HashSet<string> entries, out string failureMessage)
        {
            entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            failureMessage = null;

            if (string.IsNullOrWhiteSpace(archivePath))
            {
                failureMessage = "Archive path is empty";
                return false;
            }

            if (!HasArchiveExtension(archivePath))
            {
                failureMessage = "Path does not reference a supported archive";
                return false;
            }

            if (archivePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // Treat self-extracting archives as a successful lookup without enumerated contents.
                return true;
            }

            bool shouldTrySevenZipFallback = ShouldAttemptSevenZipFallback(archivePath);
            string localFailureMessage = null;

            try
            {
                (IArchive archive, FileStream stream) = OpenArchive(archivePath);
                if (archive != null && stream != null)
                {
                    using (archive)
                    using (stream)
                    {
                        foreach (IArchiveEntry entry in archive.Entries.Where(e => !e.IsDirectory))
                        {
                            string normalizedKey = entry.Key
                                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                            if (string.IsNullOrEmpty(normalizedKey))
                            {
                                continue;
                            }

                            entries.Add(normalizedKey);
                            string fileName = Path.GetFileName(normalizedKey);
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                entries.Add(fileName);
                            }
                        }
                    }
                    return true;
                }

                stream?.Dispose();

                if (shouldTrySevenZipFallback)
                {
                    if (TryEnumerateEntriesWithSevenZip(archivePath, entries, out string sevenZipFailure))
                    {
                        failureMessage = null;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(sevenZipFailure))
                    {
                        localFailureMessage = sevenZipFailure;
                    }
                }

                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(archivePath))
                    {
                        foreach (System.IO.Compression.ZipArchiveEntry entry in zip.Entries)
                        {
                            if (string.IsNullOrWhiteSpace(entry.FullName))
                            {
                                continue;
                            }

                            string normalizedKey = entry.FullName
                                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                            if (string.IsNullOrEmpty(normalizedKey))
                            {
                                continue;
                            }

                            entries.Add(normalizedKey);
                            string fileName = Path.GetFileName(normalizedKey);
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                entries.Add(fileName);
                            }
                        }
                    }
                    return true;
                }

                failureMessage = localFailureMessage ?? "Unknown archive format";
                return false;
            }
            catch (Exception ex)
            {
                failureMessage = ex.Message;
                Logger.LogVerbose($"[ArchiveHelper] Failed to enumerate archive '{archivePath}': {ex.Message}");

                if (shouldTrySevenZipFallback)
                {
                    if (TryEnumerateEntriesWithSevenZip(archivePath, entries, out string sevenZipFailure))
                    {
                        failureMessage = null;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(sevenZipFailure))
                    {
                        failureMessage = sevenZipFailure;
                    }
                }

                return false;
            }
        }

        public static HashSet<string> GetArchiveEntries([CanBeNull] string archivePath)
        {
            _ = TryGetArchiveEntries(archivePath, out HashSet<string> entries, out _);
            return entries;
        }

        public static HashSet<string> GetNestedArchiveRoots([CanBeNull] IEnumerable<string> trackedFiles)
        {
            var nestedArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (trackedFiles is null)
            {
                return nestedArchives;
            }

            List<string> trackedList = trackedFiles.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();

            foreach (string trackedFile in trackedList)
            {
                if (!HasArchiveExtension(trackedFile))
                {
                    continue;
                }

                string archiveName = Path.GetFileNameWithoutExtension(trackedFile);
                if (string.IsNullOrEmpty(archiveName))
                {
                    continue;
                }

                string nestedPattern = Path.DirectorySeparatorChar + archiveName + Path.DirectorySeparatorChar + archiveName + Path.DirectorySeparatorChar;
                if (trackedList.Any(path => path.IndexOf(nestedPattern, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    nestedArchives.Add(archiveName);
                }
            }

            return nestedArchives;
        }

        public static IArchive OpenArchiveFromStream([CanBeNull] string extension, [CanBeNull] Stream stream)
        {
            if (stream is null)
            {
                return null;
            }

            string normalizedExtension = string.IsNullOrWhiteSpace(extension)
                ? string.Empty
                : extension.ToLowerInvariant();

            try
            {
                if (normalizedExtension.Equals(".zip", StringComparison.Ordinal))
                {
                    return ZipArchive.Open(stream);
                }

                if (normalizedExtension.Equals(".rar", StringComparison.Ordinal))
                {
                    return RarArchive.Open(stream);
                }

                if (normalizedExtension.Equals(".7z", StringComparison.Ordinal) || normalizedExtension.Equals(".exe", StringComparison.Ordinal))
                {
                    return SevenZipArchive.Open(stream);
                }

                return ArchiveFactory.Open(stream);
            }
            catch
            {
                return null;
            }
        }

        public sealed class ArchiveMatchResult
        {
            public bool IsArchiveFile { get; set; }

            public bool CouldOpen { get; set; }

            public bool Matches { get; set; }
        }

        public static ArchiveMatchResult MatchArchivePath([NotNull] string archivePath, [NotNull] string relativePattern)
        {
            if (archivePath is null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            if (relativePattern is null)
            {
                throw new ArgumentNullException(nameof(relativePattern));
            }

            if (!HasArchiveExtension(archivePath))
            {
                return new ArchiveMatchResult
                {
                    IsArchiveFile = false,
                    CouldOpen = false,
                    Matches = false,
                };
            }

            if (archivePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ArchiveMatchResult
                {
                    IsArchiveFile = true,
                    CouldOpen = true,
                    Matches = true,
                };
            }

            try
            {
                (IArchive archive, FileStream stream) = OpenArchive(archivePath);
                if (archive == null || stream == null)
                {
                    return new ArchiveMatchResult
                    {
                        IsArchiveFile = true,
                        CouldOpen = false,
                        Matches = false,
                    };
                }

                using (archive)
                using (stream)
                {
                    string archiveRoot = Path.GetFileNameWithoutExtension(archivePath);

                    if (PathHelper.WildcardPathMatch(archiveRoot, relativePattern))
                    {
                        return new ArchiveMatchResult
                        {
                            IsArchiveFile = true,
                            CouldOpen = true,
                            Matches = true,
                        };
                    }

                    var folderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (IArchiveEntry entry in archive.Entries)
                    {
                        string entryPath = entry.Key.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                        entryPath = entryPath.TrimStart(Path.DirectorySeparatorChar);
                        string pathWithRoot = string.IsNullOrEmpty(entryPath)
                            ? archiveRoot
                            : $"{archiveRoot}{Path.DirectorySeparatorChar}{entryPath}";

                        if (PathHelper.WildcardPathMatch(pathWithRoot, relativePattern))
                        {
                            return new ArchiveMatchResult
                            {
                                IsArchiveFile = true,
                                CouldOpen = true,
                                Matches = true,
                            };
                        }

                        string folder = entry.IsDirectory ? pathWithRoot : Path.GetDirectoryName(pathWithRoot);
                        if (!string.IsNullOrEmpty(folder))
                        {
                            folderPaths.Add(folder.TrimEnd(Path.DirectorySeparatorChar));
                        }
                    }

                    bool folderMatch = folderPaths.Any(folder => PathHelper.WildcardPathMatch(folder, relativePattern));
                    return new ArchiveMatchResult
                    {
                        IsArchiveFile = true,
                        CouldOpen = true,
                        Matches = folderMatch,
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ArchiveHelper] Failed to match archive path '{archivePath}': {ex.Message}");
                return new ArchiveMatchResult
                {
                    IsArchiveFile = true,
                    CouldOpen = false,
                    Matches = false,
                };
            }
        }

        private static bool ShouldAttemptSevenZipFallback([NotNull] string archivePath)
        {
            if (archivePath is null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            string extension = Path.GetExtension(archivePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            return extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xz", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".gz", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tar", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryEnumerateEntriesWithSevenZip(
            [NotNull] string archivePath,
            [NotNull] HashSet<string> entries,
            out string failureMessage
        )
        {
            if (archivePath is null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            failureMessage = null;

            if (!EnsureSevenZipLibraryLoaded())
            {
                failureMessage = "7z.dll is not available for archive enumeration.";
                return false;
            }

            try
            {
                using (var extractor = new SevenZipExtractor(archivePath))
                {
                    foreach (ArchiveFileInfo fileInfo in extractor.ArchiveFileData.Where(data => !data.IsDirectory))
                    {
                        string normalizedKey = fileInfo.FileName
                            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                            .Replace('\\', Path.DirectorySeparatorChar)
                            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                        if (string.IsNullOrEmpty(normalizedKey))
                        {
                            continue;
                        }

                        entries.Add(normalizedKey);

                        string fileName = Path.GetFileName(normalizedKey);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            entries.Add(fileName);
                        }
                    }
                }

                Logger.LogVerbose($"[ArchiveHelper] Enumerated archive '{Path.GetFileName(archivePath)}' using 7z.dll fallback.");
                return true;
            }
            catch (Exception ex)
            {
                failureMessage = ex.Message;
                Logger.LogVerbose($"[ArchiveHelper] 7z.dll fallback failed for '{archivePath}': {ex.Message}");
                return false;
            }
        }

        private static bool EnsureSevenZipLibraryLoaded()
        {
            if (s_sevenZipInitialized)
            {
                return s_sevenZipAvailable;
            }

            lock (s_sevenZipInitLock)
            {
                if (s_sevenZipInitialized)
                {
                    return s_sevenZipAvailable;
                }

                s_sevenZipInitialized = true;

                try
                {
                    if (UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
                    {
                        s_sevenZipAvailable = false;
                        Logger.LogVerbose("[ArchiveHelper] 7z.dll fallback is only available on Windows.");
                        return false;
                    }

                    string libraryPath = Path.Combine(UtilityHelper.GetResourcesDirectory(), "7z.dll");
                    if (!File.Exists(libraryPath))
                    {
                        s_sevenZipAvailable = false;
                        if (!s_missingSevenZipLogged)
                        {
                            Logger.LogVerbose($"[ArchiveHelper] 7z.dll not found at '{libraryPath}'. SevenZip fallback will be skipped.");
                            s_missingSevenZipLogged = true;
                        }
                        return false;
                    }

                    SevenZipBase.SetLibraryPath(libraryPath);
                    s_sevenZipAvailable = true;
                    Logger.LogVerbose($"[ArchiveHelper] Loaded 7z.dll from '{libraryPath}'");
                }
                catch (Exception ex)
                {
                    s_sevenZipAvailable = false;
                    Logger.LogVerbose($"[ArchiveHelper] Failed to load 7z.dll: {ex.Message}");
                }

                return s_sevenZipAvailable;
            }
        }

        private static bool IsExpectedArchiveFormatException([NotNull] Exception ex)
        {
            if (ex is null)
            {
                throw new ArgumentNullException(nameof(ex));
            }

            if (ex is InvalidOperationException || ex is EndOfStreamException)
            {
                return true;
            }

            if (ex is ArchiveException)
            {
                return true;
            }

            string message = ex.Message?.ToLowerInvariant() ?? string.Empty;
            return NetFrameworkCompatibility.Contains(message, "nextheaderoffset", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "header offset", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "unknown archive format", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "failed to locate", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "zip header", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "corrupt", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "invalid archive", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "unexpected end", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "invalid header", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "bad archive", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "crc mismatch", StringComparison.OrdinalIgnoreCase)
                || NetFrameworkCompatibility.Contains(message, "data error", StringComparison.OrdinalIgnoreCase);
        }
    }
}
