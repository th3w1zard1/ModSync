// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using JetBrains.Annotations;

using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Utility;
using SharpCompress.Archives;

namespace KOTORModSync.Core.Services.FileSystem
{
    public class VirtualFileSystemProvider : IFileSystemProvider
    {
        private readonly HashSet<string> _virtualFiles;
        private readonly HashSet<string> _virtualDirectories;
        private readonly HashSet<string> _removedFiles;
        private readonly List<ValidationIssue> _issues;
        private readonly Dictionary<string, HashSet<string>> _archiveContents;
        private readonly Dictionary<string, string> _archiveOriginalPaths;
        private readonly HashSet<string> _initializedRoots;
        private readonly Dictionary<string, string> _fileContents;
        private readonly object _lockObject = new object();
        public bool IsDryRun => true;

        [NotNull]
        [ItemNotNull]
        public IReadOnlyList<ValidationIssue> ValidationIssues => _issues.AsReadOnly();

        public VirtualFileSystemProvider()
        {
            _virtualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _virtualDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _removedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _issues = new List<ValidationIssue>();
            _archiveContents = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            _archiveOriginalPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _initializedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public void InitializeFromRealFileSystem(string rootPath)
        {
            if (!Directory.Exists(rootPath))
            {
                return;
            }

            try
            {
                string normalizedRoot = Path.GetFullPath(rootPath);
                lock (_lockObject)
                {
                    _initializedRoots.Add(normalizedRoot);
                    _virtualDirectories.Add(normalizedRoot);
                }
            }
            catch (Exception ex)
            {
                AddIssue(ValidationSeverity.Warning, "FileSystemInitialization",
                    $"Could not initialize virtual file system root: {ex.Message}", affectedPath: null);
            }
        }


        public async Task InitializeFromRealFileSystemAsync(string rootPath)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            if (!Directory.Exists(rootPath))
            {
                return;
            }

            try
            {
                string normalizedRoot = Path.GetFullPath(rootPath);
                lock (_lockObject)
                {
                    _initializedRoots.Add(normalizedRoot);
                    _virtualDirectories.Add(normalizedRoot);
                }
            }
            catch (Exception ex)
            {
                AddIssue(ValidationSeverity.Warning, "FileSystemInitialization",
                    $"Could not initialize virtual file system root: {ex.Message}", affectedPath: null);
            }
        }

        /// <summary>
        /// Initializes VFS with root path for components. Files are loaded lazily as needed.
        /// </summary>
        public async Task InitializeFromRealFileSystemForComponentAsync(string rootPath, ModComponent component)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            if (component != null)
            {
                await InitializeFromRealFileSystemForComponentsAsync(rootPath, new List<ModComponent> { component }).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Initializes VFS with root path and optionally pre-loads files referenced by components.
        /// Most files are still loaded lazily as needed during execution.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public async Task InitializeFromRealFileSystemForComponentsAsync(string rootPath, List<ModComponent> components)
        {
            if (!Directory.Exists(rootPath) || components is null || components.Count == 0)
            {
                return;
            }

            try
            {
                string normalizedRoot = NormalizePath(rootPath);
                lock (_lockObject)
                {
                    _initializedRoots.Add(normalizedRoot);
                    _virtualDirectories.Add(normalizedRoot);
                }

                string rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? normalizedRoot
                    : normalizedRoot + Path.DirectorySeparatorChar;

                var relevantFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var relevantDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    normalizedRoot,
                };

                var realFileSystem = new RealFileSystemProvider();
                char[] wildcardChars = { '*', '?' };

                bool IsWithinRoot(string path)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        return false;
                    }

                    if (path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
                }

                bool ContainsWildcard(string path) => path.IndexOfAny(wildcardChars) >= 0;

                IEnumerable<string> ExpandResolvedPaths(string rawPath)
                {
                    if (string.IsNullOrWhiteSpace(rawPath))
                    {
                        return Array.Empty<string>();
                    }

                    string resolved = ResolvePath(rawPath);
                    if (string.IsNullOrWhiteSpace(resolved))
                    {
                        return Array.Empty<string>();
                    }

                    string normalized = NormalizePath(resolved);
                    if (!IsWithinRoot(normalized))
                    {
                        return Array.Empty<string>();
                    }

                    if (!ContainsWildcard(normalized))
                    {
                        return new[] { normalized };
                    }

                    try
                    {
                        List<string> matches = PathHelper.EnumerateFilesWithWildcards(
                            new[] { normalized },
                            realFileSystem,
                            includeSubFolders: true);

                        if (matches != null && matches.Count > 0)
                        {
                            return matches
                                .Select(NormalizePath)
                                .Where(IsWithinRoot)
                                .ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        AddIssue(ValidationSeverity.Warning, "FileSystemInitialization",
                            $"Failed expanding wildcard path '{rawPath}': {ex.Message}", affectedPath: null);
                    }

                    return new[] { normalized };
                }

                void AddDirectoryIfExists(string directoryPath)
                {
                    if (string.IsNullOrWhiteSpace(directoryPath))
                    {
                        return;
                    }

                    string normalizedDirectory = NormalizePath(directoryPath);
                    if (!IsWithinRoot(normalizedDirectory))
                    {
                        return;
                    }

                    if (Directory.Exists(normalizedDirectory))
                    {
                        relevantDirectories.Add(normalizedDirectory);
                    }
                }

                void AddFileOrDirectory(string rawPath, bool treatAsDirectory = false)
                {
                    foreach (string candidate in ExpandResolvedPaths(rawPath))
                    {
                        if (string.IsNullOrWhiteSpace(candidate))
                        {
                            continue;
                        }

                        if (File.Exists(candidate))
                        {
                            relevantFiles.Add(candidate);

                            string directory = Path.GetDirectoryName(candidate);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                AddDirectoryIfExists(directory);
                            }

                            continue;
                        }

                        if (Directory.Exists(candidate))
                        {
                            AddDirectoryIfExists(candidate);
                            continue;
                        }

                        if (treatAsDirectory)
                        {
                            AddDirectoryIfExists(candidate);
                        }
                        else
                        {
                            string parent = Path.GetDirectoryName(candidate);
                            if (!string.IsNullOrEmpty(parent))
                            {
                                AddDirectoryIfExists(parent);
                            }
                        }
                    }
                }

                void CollectInstructionPaths(IEnumerable<Instruction> instructions)
                {
                    if (instructions is null)
                    {
                        return;
                    }

                    foreach (Instruction instruction in instructions)
                    {
                        if (instruction is null)
                        {
                            continue;
                        }

                        if (instruction.Source != null)
                        {
                            foreach (string sourcePath in instruction.Source)
                            {
                                if (!string.IsNullOrWhiteSpace(sourcePath))
                                {
                                    AddFileOrDirectory(sourcePath);
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(instruction.Destination))
                        {
                            bool destinationIsDirectory =
                                instruction.Action == Instruction.ActionType.Extract ||
                                instruction.Action == Instruction.ActionType.Copy ||
                                instruction.Action == Instruction.ActionType.Move ||
                                instruction.Action == Instruction.ActionType.DelDuplicate;

                            AddFileOrDirectory(instruction.Destination, destinationIsDirectory);
                        }
                    }
                }

                foreach (ModComponent component in components)
                {
                    if (component is null)
                    {
                        continue;
                    }

                    CollectInstructionPaths(component.Instructions);

                    foreach (Option option in component.Options)
                    {
                        CollectInstructionPaths(option.Instructions);
                    }
                }

                if (relevantFiles.Count > 0)
                {
                    foreach (string filePath in relevantFiles)
                    {
                        lock (_lockObject)
                        {
                            _virtualFiles.Add(filePath);

                            if (IsArchiveFile(filePath) && !_archiveOriginalPaths.ContainsKey(filePath))
                            {
                                _archiveOriginalPaths[filePath] = filePath;
                            }
                        }

                        if (IsArchiveFile(filePath))
                        {
                            await ScanArchiveContentsAsync(filePath).ConfigureAwait(false);
                        }
                    }
                }

#pragma warning disable CA1508 // determined to be false positive due to HashSet semantics
                foreach (string directory in relevantDirectories)
                {
                    lock (_lockObject)
                    {
                        _virtualDirectories.Add(directory);
                    }
                }
            }
            catch (Exception ex)
            {
                AddIssue(ValidationSeverity.Warning, "FileSystemInitialization",
                    $"Could not fully initialize virtual file system for components: {ex.Message}", affectedPath: null);
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            string fixedPath = PathHelper.FixPathFormatting(path);
            try
            {
                return Path.GetFullPath(fixedPath);
            }
            catch
            {
                return fixedPath;
            }
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            if (path.Contains("<<modDirectory>>"))
            {
                string modDir = MainConfig.SourcePath?.FullName ?? "";
                path = path.Replace("<<modDirectory>>", modDir);
            }

            if (path.Contains("<<kotorDirectory>>"))
            {
                string kotorDir = MainConfig.DestinationPath?.FullName ?? "";
                path = path.Replace("<<kotorDirectory>>", kotorDir);
            }

            return path;
        }

        private async Task ScanArchiveContentsAsync([NotNull] string archivePath)
        {
            lock (_lockObject)
            {
                if (_archiveContents.ContainsKey(archivePath))
                {
                    return;
                }
            }

            if (!ArchiveHelper.HasArchiveExtension(archivePath))
            {
                lock (_lockObject)
                {
                    _archiveContents[archivePath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            }

            string pathToScan = archivePath;
            lock (_lockObject)
            {
                if (_archiveOriginalPaths.TryGetValue(archivePath, out string originalPath))
                {
                    pathToScan = originalPath;
                }
            }

            if (!File.Exists(pathToScan))
            {
                lock (_lockObject)
                {
                    if (!_archiveContents.ContainsKey(archivePath))
                    {
                        AddIssue(
                            ValidationSeverity.Warning,
                            "ArchiveValidation",
                            $"Archive file does not exist on disk: {archivePath}",
                            archivePath);
                        _archiveContents[archivePath] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            }

            if (ArchiveHelper.TryGetArchiveEntries(pathToScan, out HashSet<string> entries, out string failureReason))
            {
                lock (_lockObject)
                {
                    _archiveContents[archivePath] = entries;
                }
            }
            else
            {
                string message = string.IsNullOrEmpty(failureReason)
                    ? $"Unable to scan archive '{Path.GetFileName(archivePath)}' - may be corrupted or not an archive."
                    : $"Unable to scan archive '{Path.GetFileName(archivePath)}': {failureReason}";

                AddIssue(
                    ValidationSeverity.Warning,
                    "ArchiveValidation",
                    message,
                    archivePath);

                lock (_lockObject)
                {
                    _archiveContents[archivePath] = entries ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static bool IsArchiveFile([NotNull] string path) => ArchiveHelper.HasArchiveExtension(path);

        public bool FileExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            lock (_lockObject)
            {
                if (_removedFiles.Contains(path))
                {
                    return false;
                }

                if (_virtualFiles.Contains(path))
                {
                    return true;
                }
            }

            bool onDisk = File.Exists(path);
            if (onDisk)
            {
                lock (_lockObject)
                {
                    if (!_removedFiles.Contains(path))
                    {
                        _virtualFiles.Add(path);

                        string directory = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            _virtualDirectories.Add(directory);
                        }

                        if (IsArchiveFile(path) && !_archiveContents.ContainsKey(path) && !_archiveOriginalPaths.ContainsKey(path))
                        {
                            _archiveOriginalPaths[path] = path;
                        }
                    }
                }
            }

            return onDisk;
        }

        public bool DirectoryExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            lock (_lockObject)
            {
                if (_virtualDirectories.Contains(path))
                {
                    return true;
                }
            }

            bool onDisk = Directory.Exists(path);
            if (onDisk)
            {
                lock (_lockObject)
                {
                    _virtualDirectories.Add(path);
                }
            }

            return onDisk;
        }

        public Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            if (!FileExists(sourcePath))
            {
                AddIssue(ValidationSeverity.Error, "CopyFile",
                    $"Source file does not exist: {sourcePath}", sourcePath);
                return Task.CompletedTask;
            }

            if (FileExists(destinationPath) && !overwrite)
            {
                AddIssue(ValidationSeverity.Warning, "CopyFile",
                    $"Destination file already exists and overwrite is false: {destinationPath}", destinationPath);
                return Task.CompletedTask;
            }

            lock (_lockObject)
            {
                _ = _virtualFiles.Add(destinationPath);
                _ = _removedFiles.Remove(destinationPath);

                // Copy file content if it exists
                if (_fileContents.TryGetValue(sourcePath, out string fileContent))
                {
                    _fileContents[destinationPath] = fileContent;
                }

                if (IsArchiveFile(sourcePath))
                {
                    if (_archiveContents.TryGetValue(sourcePath, out HashSet<string> archiveContents))
                    {
                        _archiveContents[destinationPath] = new HashSet<string>(archiveContents, StringComparer.OrdinalIgnoreCase);
                    }

                    if (_archiveOriginalPaths.TryGetValue(sourcePath, out string originalPath))
                    {
                        _archiveOriginalPaths[destinationPath] = originalPath;
                    }
                    else
                    {
                        _archiveOriginalPaths[destinationPath] = sourcePath;
                    }
                }

                string parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !DirectoryExists(parentDir))
                {
                    _ = _virtualDirectories.Add(parentDir);
                }
            }

            return Task.CompletedTask;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public Task MoveFileAsync(string sourcePath, string destinationPath, bool overwrite)
        {
            // Only log VFS operations in debug mode to avoid performance issues
            if (MainConfig.DebugLogging)
            {
                Logger.LogVerbose($"[VFS] MoveFileAsync: source={sourcePath}");
                Logger.LogVerbose($"[VFS] MoveFileAsync: dest={destinationPath}");
                Logger.LogVerbose($"[VFS] MoveFileAsync: overwrite={overwrite}");
            }

            bool sourceExistsInVirtualFiles = false;
            bool sourceExistsOnDisk = false;
            bool sourceInRemovedFiles = false;
            lock (_lockObject)
            {
                sourceExistsInVirtualFiles = _virtualFiles.Contains(sourcePath);
                sourceInRemovedFiles = _removedFiles.Contains(sourcePath);
            }
            sourceExistsOnDisk = File.Exists(sourcePath);

            if (MainConfig.DebugLogging)
            {
                Logger.LogVerbose($"[VFS] MoveFileAsync: sourceExistsInVirtualFiles={sourceExistsInVirtualFiles}");
                Logger.LogVerbose($"[VFS] MoveFileAsync: sourceExistsOnDisk={sourceExistsOnDisk}");
                Logger.LogVerbose($"[VFS] MoveFileAsync: sourceInRemovedFiles={sourceInRemovedFiles}");
            }

            if (!FileExists(sourcePath))
            {
                AddIssue(ValidationSeverity.Error, "MoveFile",
                    $"Source file does not exist: {sourcePath}", sourcePath);
                if (MainConfig.DebugLogging)
                {
                    Logger.LogVerbose($"[VFS] MoveFileAsync: ERROR - source does not exist!");
                    Logger.LogVerbose($"[VFS] MoveFileAsync: _virtualFiles count={_virtualFiles.Count}");
                    Logger.LogVerbose($"[VFS] MoveFileAsync: _removedFiles count={_removedFiles.Count}");
                }
                return Task.CompletedTask;
            }

            if (FileExists(destinationPath) && !overwrite)
            {
                AddIssue(ValidationSeverity.Warning, "MoveFile",
                    $"Destination file already exists and overwrite is false: {destinationPath}", destinationPath);
                return Task.CompletedTask;
            }

            lock (_lockObject)
            {
                bool removed = _virtualFiles.Remove(sourcePath);
                _ = _virtualFiles.Add(destinationPath);
                _ = _removedFiles.Add(sourcePath);
                _ = _removedFiles.Remove(destinationPath);

                if (MainConfig.DebugLogging)
                {
                    Logger.LogVerbose($"[VFS] MoveFileAsync: Removed={removed}, total files now={_virtualFiles.Count}");
                    Logger.LogVerbose($"[VFS] MoveFileAsync: Added destination to _virtualFiles, removed source from _virtualFiles");
                    Logger.LogVerbose($"[VFS] MoveFileAsync: Added source to _removedFiles, removed destination from _removedFiles");
                }

                // Move file content if it exists
                if (_fileContents.TryGetValue(sourcePath, out string fileContent))
                {
                    _fileContents[destinationPath] = fileContent;
                    _ = _fileContents.Remove(sourcePath);
                }

                if (IsArchiveFile(sourcePath) && _archiveContents.TryGetValue(sourcePath, out HashSet<string> archiveContents))
                {
                    _ = _archiveContents.Remove(sourcePath);
                    _archiveContents[destinationPath] = archiveContents;

                    if (_archiveOriginalPaths.TryGetValue(sourcePath, out string originalPath))
                    {
                        _archiveOriginalPaths[destinationPath] = originalPath;
                        _ = _archiveOriginalPaths.Remove(sourcePath);
                    }
                    else
                    {
                        _archiveOriginalPaths[destinationPath] = sourcePath;
                    }

                    if (MainConfig.DebugLogging)
                    {
                        Logger.LogVerbose($"[VFS] MoveFileAsync: Moved archive contents mapping from source to destination");
                    }
                }
                else if (IsArchiveFile(sourcePath))
                {
                    string originalPath = sourcePath;
                    if (_archiveOriginalPaths.TryGetValue(sourcePath, out string existingOriginal))
                    {
                        originalPath = existingOriginal;
                    }
                    _archiveOriginalPaths[destinationPath] = originalPath;
                }

                string parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !DirectoryExists(parentDir))
                {
                    _ = _virtualDirectories.Add(parentDir);
                    if (MainConfig.DebugLogging)
                    {
                        Logger.LogVerbose($"[VFS] MoveFileAsync: Added parent directory to _virtualDirectories: {parentDir}");
                    }
                }
            }

            if (MainConfig.DebugLogging)
            {
                Logger.LogVerbose($"[VFS] MoveFileAsync: Operation completed successfully");
            }

            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path)
        {
            if (!FileExists(path))
            {
                AddIssue(ValidationSeverity.Warning, "DeleteFile",
                    $"Attempting to delete non-existent file: {path}", path);
                return Task.CompletedTask;
            }

            lock (_lockObject)
            {
                _ = _virtualFiles.Remove(path);
                _ = _removedFiles.Add(path);
                _ = _fileContents.Remove(path);

                if (IsArchiveFile(path))
                {
                    _ = _archiveContents.Remove(path);
                    _ = _archiveOriginalPaths.Remove(path);
                }
            }

            return Task.CompletedTask;
        }

        public Task RenameFileAsync(string sourcePath, string newFileName, bool overwrite)
        {
            if (!FileExists(sourcePath))
            {
                AddIssue(ValidationSeverity.Error, "RenameFile",
                    $"Source file does not exist: {sourcePath}", sourcePath);
                return Task.CompletedTask;
            }

            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string destinationPath = Path.Combine(directory, newFileName);

            if (FileExists(destinationPath) && !overwrite)
            {
                AddIssue(ValidationSeverity.Warning, "RenameFile",
                    $"Destination file already exists and overwrite is false: {destinationPath}", destinationPath);
                return Task.CompletedTask;
            }

            lock (_lockObject)
            {
                _ = _virtualFiles.Remove(sourcePath);
                _virtualFiles.Add(destinationPath);
                _ = _removedFiles.Add(sourcePath);
                _ = _removedFiles.Remove(destinationPath);

                // Move file content if it exists
                if (_fileContents.TryGetValue(sourcePath, out string fileContent))
                {
                    _fileContents[destinationPath] = fileContent;
                    _ = _fileContents.Remove(sourcePath);
                }

                if (IsArchiveFile(sourcePath) && _archiveContents.TryGetValue(sourcePath, out HashSet<string> archiveContents))
                {
                    _ = _archiveContents.Remove(sourcePath);
                    _archiveContents[destinationPath] = archiveContents;

                    if (_archiveOriginalPaths.TryGetValue(sourcePath, out string originalPath))
                    {
                        _archiveOriginalPaths[destinationPath] = originalPath;
                        _ = _archiveOriginalPaths.Remove(sourcePath);
                    }
                    else
                    {
                        _archiveOriginalPaths[destinationPath] = sourcePath;
                    }
                }
                else if (IsArchiveFile(sourcePath))
                {
                    string originalPath = sourcePath;
                    if (_archiveOriginalPaths.TryGetValue(sourcePath, out string existingOriginal))
                    {
                        originalPath = existingOriginal;
                    }
                    _archiveOriginalPaths[destinationPath] = originalPath;
                }
            }

            return Task.CompletedTask;
        }

        public Task<string> ReadFileAsync(string path)
        {
            if (!FileExists(path))
            {
                AddIssue(ValidationSeverity.Error, "ReadFile",
                    $"Cannot read non-existent file: {path}", path);
                return Task.FromResult(string.Empty);
            }

            lock (_lockObject)
            {
                if (_fileContents.TryGetValue(path, out string content))
                {
                    return Task.FromResult(content);
                }
            }

            // Fallback: if file exists but content not in VFS, read from disk
            // This handles cases where InitializeFromRealFileSystemAsync didn't load file contents
            if (System.IO.File.Exists(path))
            {
                try
                {
                    string diskContent = System.IO.File.ReadAllText(path);
                    lock (_lockObject)
                    {
                        _fileContents[path] = diskContent;
                    }
                    return Task.FromResult(diskContent);
                }
                catch (Exception ex)
                {
                    AddIssue(ValidationSeverity.Warning, "ReadFile",
                        $"Failed to read file from disk: {ex.Message}", path);
                }
            }

            return Task.FromResult(string.Empty);
        }

        public Task WriteFileAsync(string path, string contents)
        {
            lock (_lockObject)
            {
                _virtualFiles.Add(path);
                _fileContents[path] = contents ?? string.Empty;

                string directory = GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    _ = _virtualDirectories.Add(directory);
                }
            }

            return Task.CompletedTask;
        }

        public Task CreateDirectoryAsync(string path)
        {
            if (!DirectoryExists(path))
            {
                lock (_lockObject)
                {
                    _ = _virtualDirectories.Add(path);
                }
            }

            return Task.CompletedTask;
        }

        public async Task<List<string>> ExtractArchiveAsync(string archivePath, string destinationPath)
        {
            var extractedFiles = new List<string>();

            if (!FileExists(archivePath))
            {
                AddIssue(ValidationSeverity.Error, "ExtractArchive",
                    $"Archive file does not exist: {archivePath}", archivePath);
                return extractedFiles;
            }

            bool hasContents;
            lock (_lockObject)
            {
                hasContents = _archiveContents.ContainsKey(archivePath);
            }

            if (!hasContents)
            {
                await ScanArchiveContentsAsync(archivePath).ConfigureAwait(false);
            }

            HashSet<string> contents;
            lock (_lockObject)
            {
                if (!_archiveContents.TryGetValue(archivePath, out contents))
                {
                    AddIssue(ValidationSeverity.Error, "ExtractArchive",
                        $"Could not determine archive contents: {archivePath}", archivePath);
                    return extractedFiles;
                }
            }

            string extractRootDirectory = Path.GetFullPath(Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(archivePath)))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            lock (_lockObject)
            {
                foreach (string entryPath in contents)
                {
                    if (!PathHelper.TryGetZipSafeArchiveEntryExtractPath(
                            extractRootDirectory,
                            entryPath,
                            out string fullPath,
                            out string parentDir))
                    {
                        AddIssue(
                            ValidationSeverity.Warning,
                            "ExtractArchive",
                            $"Skipping virtual archive entry with unsafe path: {entryPath}",
                            archivePath);
                        continue;
                    }

                    fullPath = fullPath.Replace('/', Path.DirectorySeparatorChar).Replace("\\\\", "\\");

                    _ = _virtualFiles.Add(fullPath);
                    _ = _removedFiles.Remove(fullPath);
                    extractedFiles.Add(fullPath);

                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        _ = _virtualDirectories.Add(parentDir);
                    }
                }
            }

            return extractedFiles;
        }

        public List<string> GetFilesInDirectory(
            string directoryPath,
            string searchPattern = "*.*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            if (!DirectoryExists(directoryPath))
            {
                return new List<string>();
            }

            string normalizedDir = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<string> virtualFilesSnapshot;
            HashSet<string> removedFilesSnapshot;
            lock (_lockObject)
            {
                virtualFilesSnapshot = new List<string>(_virtualFiles);
                removedFilesSnapshot = new HashSet<string>(_removedFiles, StringComparer.OrdinalIgnoreCase);
            }

            foreach (string f in virtualFilesSnapshot)
            {
                string fileDir = Path.GetDirectoryName(f);
                if (string.IsNullOrEmpty(fileDir))
                {
                    continue;
                }

                bool matches = searchOption == SearchOption.TopDirectoryOnly
                    ? string.Equals(fileDir, normalizedDir, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(fileDir, normalizedDir, StringComparison.OrdinalIgnoreCase) ||
                      fileDir.StartsWith(normalizedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

                if (matches)
                {
                    _ = files.Add(f);
                }
            }

            if (Directory.Exists(directoryPath))
            {
                try
                {
                    foreach (string f in Directory.GetFiles(directoryPath, "*", searchOption))
                    {
                        if (!removedFilesSnapshot.Contains(f))
                        {
                            files.Add(f);

                            lock (_lockObject)
                            {
                                if (!_removedFiles.Contains(f))
                                {
                                    _virtualFiles.Add(f);
                                }
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    AddIssue(ValidationSeverity.Warning, "GetFilesInDirectory",
                        $"Unauthorized access to directory: {directoryPath}", directoryPath);
                    Logger.LogException(ex, $"[VFS] Unauthorized access while enumerating files in '{directoryPath}'.");
                }
                catch (DirectoryNotFoundException ex)
                {
                    AddIssue(ValidationSeverity.Warning, "GetFilesInDirectory",
                        $"Directory not found: {directoryPath}", directoryPath);
                    Logger.LogException(ex, $"[VFS] Directory not found while enumerating files in '{directoryPath}'.");
                }
            }

            return files.ToList();
        }

        public List<string> GetDirectoriesInDirectory(string directoryPath)
        {
            if (!DirectoryExists(directoryPath))
            {
                return new List<string>();
            }

            lock (_lockObject)
            {
                return _virtualDirectories
                    .Where(d =>
                    {
                        string parentDir = Path.GetDirectoryName(d);
                        return string.Equals(parentDir, directoryPath, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }
        }

        public string GetFileName(string path) => Path.GetFileName(path);

        public string GetDirectoryName(string path) => Path.GetDirectoryName(path);

        public Task<(int exitCode, string output, string error)> ExecuteProcessAsync(string programPath, string arguments)
        {

            if (FileExists(programPath))
            {
                return Task.FromResult((0, "[Dry-run: Program execution simulated]", string.Empty));
            }

            AddIssue(ValidationSeverity.Error, "ExecuteProcess",
                $"Program file does not exist: {programPath}", programPath);
            return Task.FromResult((1, string.Empty, $"Program not found: {programPath}"));
        }

        public string GetActualPath(string path) => path;

        private void AddIssue(
            ValidationSeverity severity,
            [NotNull] string category,
            [NotNull] string message,
            [CanBeNull] string affectedPath
                                            )
        {
            lock (_lockObject)
            {
                _issues.Add(new ValidationIssue
                {
                    Severity = severity,
                    Category = category,
                    Message = message,
                    AffectedPath = affectedPath,
                    Timestamp = DateTimeOffset.UtcNow,
                });
            }
        }

        [NotNull]
        public List<string> GetTrackedFiles()
        {
            lock (_lockObject)
            {
                return new List<string>(_virtualFiles);
            }
        }

        [NotNull]
        public List<ValidationIssue> GetValidationIssues()
        {
            lock (_lockObject)
            {
                return new List<ValidationIssue>(_issues);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0048:File name must match type name", Justification = "<Pending>")]
    public class ValidationIssue
    {
        public ValidationSeverity Severity { get; set; }
        public string Category { get; set; }
        public string Message { get; set; }
        public string AffectedPath { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public ModComponent AffectedComponent { get; set; }
        public Instruction AffectedInstruction { get; set; }
        public int InstructionIndex { get; set; }
        public string Icon { get; set; }
        public string IssueType { get; set; }
        public string Solution { get; set; }
        public bool HasSolution => !string.IsNullOrEmpty(Solution);
    }


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0048:File name must match type name", Justification = "<Pending>")]
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error,
        Critical,
    }
}
