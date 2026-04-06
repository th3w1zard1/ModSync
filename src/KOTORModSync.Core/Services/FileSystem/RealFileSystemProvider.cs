// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Utility;

using SharpCompress.Archives;
using SharpCompress.Readers;

namespace KOTORModSync.Core.Services.FileSystem
{


    public class RealFileSystemProvider : IFileSystemProvider
    {
        public bool IsDryRun => false;

        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            string directoryName = Path.GetDirectoryName(destinationPath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            File.Copy(sourcePath, destinationPath, overwrite);
            return Task.CompletedTask;
        }

        public Task MoveFileAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            string directoryName = Path.GetDirectoryName(destinationPath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            if (File.Exists(destinationPath) && overwrite)
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path)
        {
            File.Delete(path);
            return Task.CompletedTask;
        }

        public Task RenameFileAsync(string sourcePath, string newFileName, bool overwrite)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string destinationPath = Path.Combine(directory, newFileName);

            if (overwrite && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);
            return Task.CompletedTask;
        }

        public Task<string> ReadFileAsync(string path)
        {
            string content = File.ReadAllText(path);
            return Task.FromResult(content);
        }

        public Task WriteFileAsync(string path, string contents)
        {
            File.WriteAllText(path, contents);
            return Task.CompletedTask;
        }

        public Task CreateDirectoryAsync(string path)
        {
            _ = Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }

        public async Task<List<string>> ExtractArchiveAsync(string archivePath, string destinationPath)
        {
            var extractedFiles = new List<string>();
            int maxCount = MainConfig.UseMultiThreadedIO ? 16 : 1;

            using (var semaphore = new SemaphoreSlim(initialCount: 1, maxCount))
            {
                using (var cts = new CancellationTokenSource())
                {
                    try
                    {
                        await InnerExtractFileAsync(archivePath, destinationPath, extractedFiles, semaphore, cts.Token).ConfigureAwait(false);
                    }
                    catch (IndexOutOfRangeException ex)
                    {
                        await Logger.LogWarningAsync("Falling back to 7-Zip and restarting entire archive extraction due to the above error.").ConfigureAwait(false);
                        cts.Cancel();
                        throw new OperationCanceledException("Falling back to 7-Zip extraction", innerException: ex);
                    }
                    catch (OperationCanceledException ex)
                    {
                        await Logger.LogWarningAsync(ex.Message).ConfigureAwait(false);
                        throw;
                    }
                    catch (IOException ex)
                    {
                        await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                        throw;
                    }
                }
            }

            return extractedFiles;

            async Task InnerExtractFileAsync(string sourcePath, string destPath, List<string> extracted, SemaphoreSlim sem, CancellationToken token)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await sem.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    var archive = new FileInfo(sourcePath);
                    string sourceRelDirPath = MainConfig.SourcePath is null ? sourcePath : PathHelper.GetRelativePath(MainConfig.SourcePath.FullName, sourcePath);

                    await Logger.LogAsync($"Extracting archive '{sourcePath}'...").ConfigureAwait(false);

                    // Determine if destination was explicitly provided (different from archive's directory)
                    // When explicitly provided, extract directly to destination without archive name subfolder
                    // When using default (archive directory), add archive name subfolder to avoid conflicts
                    string archiveDirectory = Path.GetDirectoryName(archive.FullName) ?? string.Empty;
                    string normalizedDestPath = Path.GetFullPath(destPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string normalizedArchiveDir = Path.GetFullPath(archiveDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    bool isExplicitDestination = !string.Equals(normalizedDestPath, normalizedArchiveDir, StringComparison.OrdinalIgnoreCase);

                    if (archive.Extension.Equals(value: ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ArchiveHelper.TryExtractSevenZipSfx(archive.FullName, destPath, extracted))
                        {
                            await Logger.LogAsync($"Successfully extracted 7z SFX archive via managed extraction: '{sourceRelDirPath}'").ConfigureAwait(false);
                            return;
                        }

                        if (KOTORModSync.Core.Utility.UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
                        {
                            await Logger.LogAsync($"Managed SFX extraction failed, attempting 7z CLI extraction for '{sourceRelDirPath}'").ConfigureAwait(false);
                            if (await ArchiveHelper.TryExtractWithSevenZipCliAsync(archive.FullName, destPath, extracted).ConfigureAwait(false))
                            {
                                await Logger.LogAsync($"Successfully extracted via 7z CLI: '{sourceRelDirPath}'").ConfigureAwait(false);
                                return;
                            }

                            throw new InvalidOperationException($"Failed to extract '{sourceRelDirPath}': Not a valid 7z SFX or 7z CLI not available. On Linux/macOS, install 7z package (e.g., 'apt install p7zip-full' or 'brew install p7zip').");
                        }

                        await Logger.LogAsync($"Managed SFX extraction failed, attempting to execute SFX for '{sourceRelDirPath}'").ConfigureAwait(false);
                        (int exitCode, string _, string _) = await PlatformAgnosticMethods.ExecuteProcessAsync(archive.FullName, $" -o\"{archive.DirectoryName}\" -y").ConfigureAwait(false);

                        if (exitCode == 0)
                        {
                            return;
                        }

                        throw new InvalidOperationException($"'{sourceRelDirPath}' is not a valid 7z self-extracting executable. Cannot extract.");
                    }

                    string extractRootDirectory = isExplicitDestination
                        ? Path.GetFullPath(destPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        : Path.GetFullPath(Path.Combine(destPath, Path.GetFileNameWithoutExtension(archive.Name)))
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    using (FileStream stream = File.OpenRead(archive.FullName))
                    {
                        IArchive arch = GetArchiveByExtension(archive.Extension, stream);

                        using (arch)
                        using (IReader reader = arch.ExtractAllEntries())
                        {
                            while (reader.MoveToNextEntry())
                            {
                                if (reader.Entry.IsDirectory)
                                {
                                    continue;
                                }

                                if (!PathHelper.TryGetZipSafeArchiveEntryExtractPath(
                                        extractRootDirectory,
                                        reader.Entry.Key,
                                        out string destinationItemPath,
                                        out string destinationDirectory))
                                {
                                    await Logger.LogWarningAsync($"Skipping archive entry with unsafe path: '{reader.Entry.Key}'").ConfigureAwait(false);
                                    continue;
                                }

                                if (MainConfig.CaseInsensitivePathing && !Directory.Exists(destinationDirectory))
                                {
                                    destinationDirectory = PathHelper.GetCaseSensitivePath(destinationDirectory, isFile: false).Item1;
                                }

                                string destinationRelDirPath = MainConfig.SourcePath is null ? destinationDirectory : PathHelper.GetRelativePath(MainConfig.SourcePath.FullName, destinationDirectory);

                                if (!Directory.Exists(destinationDirectory))
                                {
                                    await Logger.LogVerboseAsync($"Create directory '{destinationRelDirPath}'").ConfigureAwait(false);
                                    _ = Directory.CreateDirectory(destinationDirectory);
                                }

                                await Logger.LogVerboseAsync($"Extract '{reader.Entry.Key}' to '{destinationRelDirPath}'").ConfigureAwait(false);

                                try
                                {
                                    IReader localReader = reader;
                                    await Task.Run(() =>
                                    {
                                        if (localReader.Cancelled)
                                        {
                                            return;
                                        }

                                        localReader.WriteEntryToDirectory(destinationDirectory, ArchiveHelper.DefaultExtractionOptions);
                                    }, token).ConfigureAwait(false);

                                    extracted.Add(destinationItemPath);
                                }
                                catch (ObjectDisposedException)
                                {
                                    return;
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    await Logger.LogWarningAsync($"Skipping file '{reader.Entry.Key}' due to lack of permissions.").ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    _ = sem.Release();
                }
            }

            IArchive GetArchiveByExtension(string extension, Stream stream) =>
                ArchiveHelper.OpenArchiveFromStream(extension, stream);
        }

        public List<string> GetFilesInDirectory(string directoryPath, string searchPattern = "*.*", SearchOption searchOption = SearchOption.TopDirectoryOnly) => !Directory.Exists(directoryPath)
                ? new List<string>()
                : Directory.GetFiles(directoryPath, searchPattern, searchOption).ToList();

        public List<string> GetDirectoriesInDirectory(string directoryPath) => !Directory.Exists(directoryPath) ? new List<string>() : Directory.GetDirectories(directoryPath).ToList();

        public string GetFileName(string path) => Path.GetFileName(path);

        public string GetDirectoryName(string path) => Path.GetDirectoryName(path);

        public async Task<(int exitCode, string output, string error)> ExecuteProcessAsync(string programPath, string arguments) => await PlatformAgnosticMethods.ExecuteProcessAsync(programPath, arguments).ConfigureAwait(false);

        public string GetActualPath(string path) => path;
    }
}
