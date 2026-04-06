// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Services.FileSystem;
using KOTORModSync.Core.Utility;
using SharpCompress.Archives;

namespace KOTORModSync.Core.Services
{
    /// <summary>
    /// Core validation service for component instruction validation, VFS simulation, and path verification.
    /// Handles dry-run validation and file existence checking without GUI dependencies.
    /// </summary>
    public partial class ComponentValidationService
    {
        // Cache validation results to avoid redundant VFS operations
        private static readonly Dictionary<string, (List<string> urls, bool simulationFailed, DateTime timestamp)> s_validationCache
            = new Dictionary<string, (List<string>, bool, DateTime)>(StringComparer.Ordinal);
        private static readonly TimeSpan s_cacheExpiration = TimeSpan.FromMinutes(5);
        private static readonly object s_cacheLock = new object();

        public ComponentValidationService()
        {

        }

        public static Task<bool> IsArchiveValidAsync([NotNull] ModComponent component, CancellationToken cancellationToken = default)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            IEnumerable<Instruction> extractInstructions = component.Instructions.Where(i => i.Action == Instruction.ActionType.Extract);
            foreach (Instruction instruction in extractInstructions)
            {
                IEnumerable<string> sources = instruction.Source ?? Array.Empty<string>();
                foreach (string rawSource in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string resolvedPath = rawSource ?? string.Empty;
                    if (MainConfig.SourcePath != null)
                    {
                        resolvedPath = resolvedPath.Replace("<<modDirectory>>", MainConfig.SourcePath.FullName, StringComparison.OrdinalIgnoreCase);
                    }

                    resolvedPath = PathHelper.FixPathFormatting(resolvedPath);
                    if (!File.Exists(resolvedPath))
                    {
                        return Task.FromResult(false);
                    }

                    try
                    {
                        using (var stream = File.OpenRead(resolvedPath))
                        using (var archive = ArchiveFactory.Open(stream))
                        {
                            _ = archive.Entries.Count();
                        }
                    }
                    catch
                    {
                        return Task.FromResult(false);
                    }
                }
            }

            return Task.FromResult(true);
        }

        private static string NormalizeFileNameForComparison(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            string trimmed = NetFrameworkCompatibility.Replace(
                NetFrameworkCompatibility.Replace(fileName, "<<modDirectory>>\\", "", StringComparison.OrdinalIgnoreCase),
                "<<modDirectory>>/", "", StringComparison.OrdinalIgnoreCase)
                .Replace('/', Path.DirectorySeparatorChar)
                .Trim(Path.DirectorySeparatorChar);

            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            // Remove wildcards and quotes that may be present in patterns
            trimmed = NetFrameworkCompatibility.Replace(
                NetFrameworkCompatibility.Replace(trimmed, "*", string.Empty, StringComparison.Ordinal),
                "?", string.Empty, StringComparison.Ordinal)
                             .Trim('"');

            string nameOnly = Path.GetFileNameWithoutExtension(trimmed);
            if (string.IsNullOrEmpty(nameOnly))
            {
                nameOnly = trimmed;
            }

            var sb = new StringBuilder(nameOnly.Length);
            foreach (char c in nameOnly)
            {
                if (char.IsLetterOrDigit(c))
                {
                    _ = sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Clears the validation cache. Call this after file operations that might affect validation results.
        /// </summary>
        public static void ClearValidationCache()
        {
            lock (s_cacheLock)
            {
                s_validationCache.Clear();
            }
        }

        /// <summary>
        /// Clears the validation cache for a specific component. Call this after modifying that component's instructions.
        /// </summary>
        public static void ClearValidationCacheForComponent(string componentGuid)
        {
            lock (s_cacheLock)

            {
                var keysToRemove = s_validationCache.Keys.Where(k => k.StartsWith(componentGuid + "_", StringComparison.Ordinal)).ToList();
                foreach (string key in keysToRemove)
                {
                    _ = s_validationCache.Remove(key);
                }
            }
        }

        /// <summary>
        /// Analyzes whether a component needs files downloaded by matching instruction patterns against ResourceRegistry filenames.
        /// Uses simple wildcard pattern matching instead of VFS simulation for performance and architecture compliance.
        /// </summary>
        /// <returns>List of URLs that need downloading, and whether matching failed</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S6966:Awaitable method should be used", Justification = "<Pending>")]
        public async Task<(
            List<string> urlsNeedingDownload,
            bool simulationFailed)>
        AnalyzeDownloadNecessityAsync(
            [NotNull] ModComponent component,
            [NotNull] string modArchiveDirectory,
            CancellationToken cancellationToken = default)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (modArchiveDirectory is null)
            {
                throw new ArgumentNullException(nameof(modArchiveDirectory));
            }

            // Check cache first to avoid redundant operations
            string cacheKey = $"{component.Guid}_{modArchiveDirectory}_{component.Instructions.Count}";
            lock (s_cacheLock)
            {
                if (s_validationCache.TryGetValue(cacheKey, out (List<string> urls, bool simulationFailed, DateTime timestamp) cachedResult))
                {
                    if (DateTime.UtcNow - cachedResult.timestamp < s_cacheExpiration)
                    {
                        Logger.LogVerbose($"[ComponentValidationService] Using cached validation result for component: {component.Name}");
                        return (cachedResult.urls, cachedResult.simulationFailed);
                    }

                    // Cache expired, remove it
                    _ = s_validationCache.Remove(cacheKey);
                }
            }

            await Logger.LogVerboseAsync($"[ComponentValidationService] Analyzing download necessity for component: {component.Name}").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[ComponentValidationService] Mod archive directory: {modArchiveDirectory}").ConfigureAwait(false);

            var urlsNeedingDownload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await Logger.LogVerboseAsync("[ComponentValidationService] Matching instruction patterns against ResourceRegistry filenames and archives...").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[ComponentValidationService] Component has {component.Instructions.Count} instructions").ConfigureAwait(false);

            try
            {
                // Collect filenames that actually exist on disk or in archives (not just in ResourceRegistry)
                var availableFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var archiveFiles = new List<string>(); // List of archives in modArchiveDirectory
                var resourceRegistryFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Files available for download from ResourceRegistry

                // Collect ResourceRegistry filenames separately - these are available for download but may not exist on disk
                foreach (ResourceMetadata meta in component.ResourceRegistry.Values)
                {
                    if (meta.Files != null)
                    {
                        foreach (string filename in meta.Files.Keys)
                        {
                            if (!string.IsNullOrWhiteSpace(filename))
                            {
                                resourceRegistryFilenames.Add(filename);
                            }
                        }
                    }
                }

                // Check files that actually exist on disk in modArchiveDirectory
                if (Directory.Exists(modArchiveDirectory))
                {
                    foreach (string file in Directory.GetFiles(modArchiveDirectory, "*", SearchOption.TopDirectoryOnly))
                    {
                        string fname = Path.GetFileName(file);
                        if (!string.IsNullOrWhiteSpace(fname))
                        {
                            availableFilenames.Add(fname);
                            // Identify ZIP/RAR/7z files as mod archives
                            if (ArchiveHelper.HasArchiveExtension(fname))
                            {
                                archiveFiles.Add(file);
                            }
                        }
                    }
                }

                // Scan inside all discovered mod archives for contained files
                Dictionary<string, HashSet<string>> archiveContents = ArchiveHelper.GetArchiveContentsByFileName(archiveFiles);
                foreach (string archivePath in archiveFiles)
                {
                    string archiveFileName = Path.GetFileName(archivePath);
                    if (!string.IsNullOrEmpty(archiveFileName))
                    {
                        availableFilenames.Add(archiveFileName);
                    }
                }

                await Logger.LogVerboseAsync($"[ComponentValidationService] Found {availableFilenames.Count} available filename(s) on disk/archives, {resourceRegistryFilenames.Count} in ResourceRegistry").ConfigureAwait(false);

                // Collect all Extract, Move, and Copy instruction source patterns
                // These instructions reference files that may need to be downloaded
                var filePatterns = new List<string>();
                foreach (Instruction instruction in component.Instructions)
                {
                    if ((instruction.Action == Instruction.ActionType.Extract ||
                         instruction.Action == Instruction.ActionType.Move ||
                         instruction.Action == Instruction.ActionType.Copy) &&
                        instruction.Source != null)
                    {
                        foreach (string sourcePath in instruction.Source)
                        {
                            if (!string.IsNullOrWhiteSpace(sourcePath))
                            {
                                filePatterns.Add(sourcePath);
                            }
                        }
                    }
                }
                foreach (Option option in component.Options)
                {
                    foreach (Instruction instruction in option.Instructions)
                    {
                        if ((instruction.Action == Instruction.ActionType.Extract ||
                             instruction.Action == Instruction.ActionType.Move ||
                             instruction.Action == Instruction.ActionType.Copy) &&
                            instruction.Source != null)
                        {
                            foreach (string sourcePath in instruction.Source)
                            {
                                if (!string.IsNullOrWhiteSpace(sourcePath))
                                {
                                    filePatterns.Add(sourcePath);
                                }
                            }
                        }
                    }
                }

                // Try to match patterns against available filenames and archive contents
                var normalizedAvailableNames = availableFilenames
                    .Select(name => (Original: name, Normalized: NormalizeFileNameForComparison(name)))
                    .Where(tuple => !string.IsNullOrEmpty(tuple.Normalized))
                    .ToList();

                var unmatchedPatterns = new List<string>();
                foreach (string pattern in filePatterns)
                {
                    bool matched = false;

                    // Remove <<modDirectory>> prefix and standardize slash style
                    string patternFileName = pattern
                        .Replace("<<modDirectory>>\\", "")
                        .Replace("<<modDirectory>>/", "")
                        .TrimStart('\\', '/');

                    string normalizedPatternName = NormalizeFileNameForComparison(patternFileName);

                    // 1. Try matching against available filenames on disk/registry
                    foreach (string filename in availableFilenames)
                    {
                        string testPath = $@"<<modDirectory>>\{filename}";
                        if (PathHelper.WildcardPathMatch(testPath, pattern) ||
                            PathHelper.WildcardPathMatch(filename, Path.GetFileName(patternFileName)))
                        {
                            matched = true;
                            break;
                        }
                    }

                    // 2. Try matching inside archive file listings if not matched already
                    if (!matched)
                    {
                        foreach (KeyValuePair<string, HashSet<string>> kvp in archiveContents)
                        {
                            string archiveName = kvp.Key;
                            HashSet<string> insideArchive = kvp.Value;
                            // For patterns like <<modDirectory>>\ArchiveName*\Subfolder\*
                            if (NetFrameworkCompatibility.Contains(pattern, archiveName, StringComparison.OrdinalIgnoreCase))
                            {
                                // Remove path/prefix to get just the *relative pattern* inside the archive
                                string afterArchive = patternFileName;
                                int idx = afterArchive.IndexOf(archiveName, StringComparison.OrdinalIgnoreCase);
                                if (idx >= 0)
                                {
                                    afterArchive = afterArchive.Substring(idx + archiveName.Length).TrimStart('\\', '/');
                                }

                                // Try matching against files inside archive
                                foreach (string entry in insideArchive)
                                {
                                    if (PathHelper.WildcardPathMatch(entry, afterArchive) ||
                                        PathHelper.WildcardPathMatch(Path.GetFileName(entry), Path.GetFileName(afterArchive)))
                                    {
                                        matched = true;
                                        break;
                                    }
                                }
                            }

                            if (matched)
                            {
                                break;
                            }
                        }
                    }

                    if (!matched)
                    {
                        bool satisfiedBySimilarFile = false;

                        if (!string.IsNullOrEmpty(normalizedPatternName))
                        {
                            satisfiedBySimilarFile = normalizedAvailableNames.Any(tuple =>
                                string.Equals(tuple.Normalized, normalizedPatternName, StringComparison.Ordinal));

                            if (!satisfiedBySimilarFile)
                            {
                                // Allow lenient comparison where available file name contains the pattern token entirely
                                satisfiedBySimilarFile = normalizedAvailableNames.Any(tuple =>
                                    NetFrameworkCompatibility.Contains(tuple.Normalized, normalizedPatternName, StringComparison.Ordinal));

                                if (!satisfiedBySimilarFile)
                                {
                                    satisfiedBySimilarFile = normalizedPatternName.Length > 3 &&
                                        normalizedAvailableNames.Any(tuple =>
                                        NetFrameworkCompatibility.Contains(normalizedPatternName, tuple.Normalized, StringComparison.Ordinal));
                                }
                            }
                        }

                        if (satisfiedBySimilarFile)
                        {
                            await Logger.LogVerboseAsync($"[ComponentValidationService] Pattern '{pattern}' satisfied by existing file with different extension or naming.").ConfigureAwait(false);
                        }
                        else
                        {
                            // Pattern doesn't match files on disk/archives - check if it's in ResourceRegistry (needs download)
                            string patternFileNameOnly = Path.GetFileName(patternFileName);
                            string normalizedPatternFileOnly = NormalizeFileNameForComparison(patternFileNameOnly);
                            bool inResourceRegistry = !string.IsNullOrEmpty(normalizedPatternFileOnly) &&
                                resourceRegistryFilenames.Any(f => string.Equals(NormalizeFileNameForComparison(f), normalizedPatternFileOnly, StringComparison.Ordinal));

                            if (inResourceRegistry || !string.IsNullOrEmpty(normalizedPatternName))
                            {
                                // File is in ResourceRegistry or we have a pattern - needs download
                                unmatchedPatterns.Add(pattern);
                                await Logger.LogVerboseAsync($"[ComponentValidationService] Unmatched file pattern (needs download): {pattern}").ConfigureAwait(false);
                            }
                            else
                            {
                                await Logger.LogVerboseAsync($"[ComponentValidationService] Pattern '{pattern}' not found on disk and not in ResourceRegistry - skipping").ConfigureAwait(false);
                            }
                        }
                    }
                }

                if (unmatchedPatterns.Count > 0)
                {
                    await Logger.LogWarningAsync($"[ComponentValidationService] Found {unmatchedPatterns.Count} unmatched file pattern(s)").ConfigureAwait(false);
                    var targetedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (string unmatched in unmatchedPatterns)
                    {
                        string normalized = NormalizeFileNameForComparison(Path.GetFileName(unmatched));
                        if (string.IsNullOrEmpty(normalized))
                        {
                            continue;
                        }

                        foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry)
                        {
                            if (string.IsNullOrWhiteSpace(kvp.Key))
                            {
                                continue;
                            }

                            if (kvp.Value?.Files is null || kvp.Value.Files.Count == 0)
                            {
                                continue;
                            }

                            bool matchesResource = kvp.Value.Files.Keys.Any(fileName =>
                                string.Equals(
                                    NormalizeFileNameForComparison(fileName),
                                    normalized,
                                    StringComparison.Ordinal));

                            if (matchesResource)
                            {
                                targetedUrls.Add(kvp.Key);
                            }
                        }
                    }

                    if (targetedUrls.Count > 0)
                    {
                        urlsNeedingDownload.UnionWith(targetedUrls);
                    }
                    else
                    {
                        urlsNeedingDownload.UnionWith(component.ResourceRegistry.Keys.Where(url => !string.IsNullOrWhiteSpace(url)));
                    }
                }
                else
                {
                    await Logger.LogVerboseAsync($"[ComponentValidationService] ✓ All Extract patterns matched available files or archive contents").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[ComponentValidationService] Pattern matching failed").ConfigureAwait(false);
                urlsNeedingDownload.UnionWith(component.ResourceRegistry.Keys.Where(url => !string.IsNullOrWhiteSpace(url)));
            }

            bool simulationFailed = urlsNeedingDownload.Count > 0;

            // Cache the result before returning
            (List<string>, bool simulationFailed) result = (urlsNeedingDownload.ToList(), simulationFailed);
            lock (s_cacheLock)
            {
                s_validationCache[cacheKey] = (result.Item1, result.simulationFailed, DateTime.UtcNow);
            }

            return result;
        }

        /// <summary>
        /// Fixes instruction Source patterns to account for nested archive folders.
        /// When an archive contains a root folder matching the archive name, the extracted structure is:
        /// workspace\ArchiveName\ArchiveName\files (double nesting)
        /// This fixes patterns like: modDirectory\ArchiveName*\Override\*
        /// To: modDirectory\ArchiveName*\ArchiveName*\Override\*
        /// </summary>
        public static int FixNestedArchiveFolderInstructions(ModComponent component, VirtualFileSystemProvider virtualFileSystem)
        {
            int fixCount = 0;

            if (component is null || virtualFileSystem is null)
            {
                return 0;
            }

            // Get all extracted archives and check which have nested folders
            HashSet<string> nestedArchives = ArchiveHelper.GetNestedArchiveRoots(virtualFileSystem.GetTrackedFiles());
            foreach (string archiveName in nestedArchives)
            {
                Logger.LogVerbose($"[ComponentValidationService] Detected nested archive structure for: {archiveName}");
            }

            if (nestedArchives.Count == 0)
            {
                return 0;
            }

            // Fix instructions that reference these archives
            foreach (Instruction instruction in component.Instructions)
            {
                if (instruction.Source.Count == 0)
                {
                    continue;
                }

                bool instructionModified = false;
                var newSources = new List<string>();

                foreach (string source in instruction.Source)
                {
                    string modifiedSource = source;
                    bool wasModified = false;

                    foreach (string archiveName in nestedArchives)
                    {
                        // Pattern: <<modDirectory>>\ArchiveName*\something\*
                        // Should be: <<modDirectory>>\ArchiveName*\ArchiveName*\something\*
                        string searchPattern = $"<<modDirectory>>\\{archiveName}";

                        if (modifiedSource.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Find where the archive name pattern is
                            int archivePatternIndex = modifiedSource.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase);
                            int afterArchiveNameIndex = archivePatternIndex + searchPattern.Length;

                            // Look for the pattern after archive name: could be "*\" or just "\"
                            // Find the first path separator after the archive name
                            int firstSeparatorIndex = modifiedSource.IndexOf(Path.DirectorySeparatorChar, afterArchiveNameIndex);

                            if (firstSeparatorIndex > afterArchiveNameIndex)
                            {
                                // If it's just "*" or empty, we need to look at the next segment
                                // Find the second separator to see what folder comes after
                                int secondSeparatorIndex = modifiedSource.IndexOf(Path.DirectorySeparatorChar, firstSeparatorIndex + 1);

                                if (secondSeparatorIndex > firstSeparatorIndex + 1)
                                {
                                    // Get the folder name between first and second separator
                                    string nextFolderSegment = modifiedSource.Substring(firstSeparatorIndex + 1, secondSeparatorIndex - firstSeparatorIndex - 1);

                                    // Check if this folder segment is NOT the archive name (meaning we need to add it)
                                    if (!nextFolderSegment.Equals(archiveName, StringComparison.OrdinalIgnoreCase) &&
                                         !(archiveName is null) &&
                                         !nextFolderSegment.StartsWith(archiveName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Insert the archive name pattern after the first separator
                                        string beforeInsert = modifiedSource.Substring(0, firstSeparatorIndex + 1);
                                        string afterInsert = modifiedSource.Substring(firstSeparatorIndex + 1);
                                        modifiedSource = beforeInsert + archiveName + "*" + Path.DirectorySeparatorChar + afterInsert;
                                        wasModified = true;

                                        Logger.LogVerbose("[ComponentValidationService] Fixed instruction source:");
                                        Logger.LogVerbose($"  From: {source}");
                                        Logger.LogVerbose($"  To:   {modifiedSource}");
                                    }
                                }
                            }
                        }
                    }

                    newSources.Add(modifiedSource);
                    if (wasModified)
                    {
                        instructionModified = true;
                        fixCount++;
                    }
                }

                if (instructionModified)
                {
                    instruction.Source = newSources;
                }
            }

            return fixCount;
        }

        /// <summary>
        /// Validates that all files required by a component's instructions exist.
        /// </summary>
        public static async Task<bool> ValidateComponentFilesExistAsync(ModComponent component)
        {
            try
            {
                if (component?.Instructions is null || component.Instructions.Count == 0)
                {
                    return true;
                }

                await Logger.LogVerboseAsync($"[ComponentValidationService] Validating component '{component.Name}' (GUID: {component.Guid})").ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[ComponentValidationService] ModComponent has {component.Instructions.Count} instructions").ConfigureAwait(false);

                var validationProvider = new VirtualFileSystemProvider();
                await validationProvider.InitializeFromRealFileSystemAsync(MainConfig.SourcePath?.FullName ?? "").ConfigureAwait(false);

                foreach (Instruction instruction in component.Instructions)
                {
                    if (instruction.Source.Count == 0)
                    {
                        continue;
                    }

                    var sourcePaths = instruction.Source
                        .Where(sourcePath => !string.IsNullOrWhiteSpace(sourcePath))
                        .ToList();

                    if (sourcePaths.Count == 0)
                    {
                        continue;
                    }

                    await Logger.LogVerboseAsync($"[ComponentValidationService] Checking {sourcePaths.Count} source paths for instruction").ConfigureAwait(false);

                    List<string> foundFiles = PathHelper.EnumerateFilesWithWildcards(
                        sourcePaths,
                        validationProvider,
                        includeSubFolders: true
                    );

                    if (foundFiles is null || foundFiles.Count == 0)
                    {
                        await Logger.LogVerboseAsync($"[ComponentValidationService] No files found for paths: {string.Join(", ", sourcePaths)}").ConfigureAwait(false);
                        return false;
                    }

                    await Logger.LogVerboseAsync($"[ComponentValidationService] Found {foundFiles.Count} files for instruction").ConfigureAwait(false);
                }

                await Logger.LogVerboseAsync($"[ComponentValidationService] ModComponent '{component.Name}' validation passed - all files exist").ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"Error validating files for component '{component.Name}'").ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// Gets a list of missing files for a component.
        /// </summary>
        public static async Task<List<string>> GetMissingFilesForComponentAsync(ModComponent component)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            var missingFiles = new List<string>();

            try
            {
                if (component.Instructions is null || component.Instructions.Count == 0)
                {
                    return missingFiles;
                }

                await Logger.LogVerboseAsync($"[ComponentValidationService] Getting missing files for component '{component.Name}' (GUID: {component.Guid})").ConfigureAwait(false);

                var validationProvider = new VirtualFileSystemProvider();
                await validationProvider.InitializeFromRealFileSystemAsync(MainConfig.SourcePath?.FullName ?? "").ConfigureAwait(false);

                foreach (Instruction instruction in component.Instructions)
                {
                    if (instruction.Action == Instruction.ActionType.Choose)
                    {
                        if (instruction.Source != null
                            && instruction.Source.Count > 0)
                        {
                            foreach (string optionGuidStr in instruction.Source)
                            {
                                if (Guid.TryParse(optionGuidStr, out Guid optionGuid))
                                {
                                    Option selectedOption = component.Options?.FirstOrDefault(o => o.Guid == optionGuid);
                                    if (selectedOption != null
                                        && selectedOption.IsSelected
                                        && selectedOption.Instructions != null)
                                    {
                                        foreach (Instruction optionInstruction in selectedOption.Instructions)
                                        {
                                            if (optionInstruction.Source is null || optionInstruction.Source.Count == 0)
                                            {
                                                continue;
                                            }

                                            var optionSourcePaths = optionInstruction.Source
                                                .Where(sourcePath => !string.IsNullOrWhiteSpace(sourcePath))
                                                .ToList();

                                            if (optionSourcePaths.Count == 0)
                                            {
                                                continue;
                                            }

                                            // Replace placeholders before checking files
                                            var resolvedOptionSourcePaths = optionSourcePaths
                                                .Select(path => UtilityHelper.ReplaceCustomVariables(path))
                                                .Where(path => !string.IsNullOrWhiteSpace(path))
                                                .ToList();

                                            List<string> foundOptionFiles = PathHelper.EnumerateFilesWithWildcards(
                                                resolvedOptionSourcePaths,
                                                validationProvider,
                                                includeSubFolders: true
                                            );

                                            if (foundOptionFiles is null || foundOptionFiles.Count == 0)
                                            {
                                                foreach (string sourcePath in optionSourcePaths)
                                                {
                                                    string fileName = Path.GetFileName(sourcePath);
                                                    if (!string.IsNullOrEmpty(fileName) && !missingFiles.Contains(fileName, StringComparer.Ordinal))
                                                    {
                                                        missingFiles.Add(fileName);
                                                        await Logger.LogVerboseAsync($"[ComponentValidationService] Missing file in option '{selectedOption.Name}': {fileName}").ConfigureAwait(false);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        continue;
                    }

                    if (instruction.Source is null || instruction.Source.Count == 0)
                    {
                        continue;
                    }

                    var sourcePaths = instruction.Source
                        .Where(sourcePath => !string.IsNullOrWhiteSpace(sourcePath))
                        .ToList();

                    if (sourcePaths.Count == 0)
                    {
                        continue;
                    }

                    await Logger.LogVerboseAsync($"[ComponentValidationService] Checking {sourcePaths.Count} source paths for instruction").ConfigureAwait(false);

                    // Replace placeholders before checking files
                    var resolvedSourcePaths = sourcePaths
                        .Select(path => UtilityHelper.ReplaceCustomVariables(path))
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .ToList();

                    List<string> foundFiles = PathHelper.EnumerateFilesWithWildcards(
                        resolvedSourcePaths,
                        validationProvider,
                        includeSubFolders: true
                    );

                    // For Extract instructions, always validate archives even if files are found
                    if (instruction.Action == Instruction.ActionType.Extract)
                    {
                        if (foundFiles is null || foundFiles.Count == 0)
                        {
                            // Files not found - add to missing
                            foreach (string sourcePath in sourcePaths)
                            {
                                string fileName = Path.GetFileName(sourcePath);
                                if (!string.IsNullOrEmpty(fileName) && !missingFiles.Contains(fileName, StringComparer.Ordinal))
                                {
                                    missingFiles.Add(fileName);
                                    await Logger.LogVerboseAsync($"[ComponentValidationService] Missing file: {fileName}").ConfigureAwait(false);
                                }
                            }
                        }
                        else
                        {
                            // Files found - validate they are actually valid archives
                            var invalidArchives = new List<string>();
                            var validArchives = new List<string>();

                            foreach (string foundFile in foundFiles)
                            {
                                if (ArchiveHelper.HasArchiveExtension(foundFile))
                                {
                                    try
                                    {
                                        // Try to open the archive to validate it's actually valid
                                        (IArchive archive, FileStream stream) = ArchiveHelper.OpenArchive(foundFile);
                                        if (archive == null || stream == null)
                                        {
                                            // Invalid archive - treat as missing
                                            invalidArchives.Add(foundFile);
                                        }
                                        else
                                        {
                                            try
                                            {
                                                // Try to enumerate entries to ensure archive is actually valid
                                                // Some invalid archives might open but fail when enumerating
                                                // Force enumeration by trying to access the entries collection
                                                int entryCount = 0;
                                                foreach (var entry in archive.Entries)
                                                {
                                                    entryCount++;
                                                    // Just accessing the entry is enough to validate it
                                                    _ = entry.Key;
                                                    break; // Only need to check one entry
                                                }
                                                // If we got here without exception, archive is valid
                                                validArchives.Add(foundFile);
                                            }
                                            catch (Exception enumEx)
                                            {
                                                // Archive opened but cannot enumerate entries - invalid
                                                await Logger.LogVerboseAsync($"[ComponentValidationService] Archive '{foundFile}' opened but cannot enumerate entries: {enumEx.Message}").ConfigureAwait(false);
                                                invalidArchives.Add(foundFile);
                                            }
                                            finally
                                            {
                                                archive.Dispose();
                                                stream.Dispose();
                                            }
                                        }
                                    }
                                    catch (ArgumentException)
                                    {
                                        // File doesn't exist - should have been caught earlier, but treat as missing
                                        invalidArchives.Add(foundFile);
                                    }
                                    catch (Exception ex)
                                    {
                                        // Archive cannot be opened - treat as missing
                                        await Logger.LogVerboseAsync($"[ComponentValidationService] Exception validating archive '{foundFile}': {ex.Message}").ConfigureAwait(false);
                                        invalidArchives.Add(foundFile);
                                    }
                                }
                                else
                                {
                                    // File found but doesn't have archive extension - for Extract, this is invalid
                                    await Logger.LogVerboseAsync($"[ComponentValidationService] Found file '{foundFile}' for Extract instruction but it doesn't have archive extension - treating as invalid").ConfigureAwait(false);
                                    invalidArchives.Add(foundFile);
                                }
                            }

                            // Add invalid archives to missing files
                            if (invalidArchives.Count > 0)
                            {
                                foreach (string invalidArchive in invalidArchives)
                                {
                                    string fileName = Path.GetFileName(invalidArchive);
                                    if (!string.IsNullOrEmpty(fileName) && !missingFiles.Contains(fileName, StringComparer.Ordinal))
                                    {
                                        missingFiles.Add(fileName);
                                        await Logger.LogVerboseAsync($"[ComponentValidationService] Invalid archive (cannot be opened): {fileName}").ConfigureAwait(false);
                                    }
                                }
                            }

                            // If all archives are invalid, ensure we report them as missing
                            if (invalidArchives.Count > 0 && validArchives.Count == 0)
                            {
                                await Logger.LogVerboseAsync($"[ComponentValidationService] All {invalidArchives.Count} archive(s) for Extract instruction are invalid").ConfigureAwait(false);
                            }
                            else if (validArchives.Count > 0)
                            {
                                await Logger.LogVerboseAsync($"[ComponentValidationService] Found {validArchives.Count} valid archive(s) for Extract instruction").ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        // For non-Extract instructions, just check if files exist
                        if (foundFiles is null || foundFiles.Count == 0)
                        {
                            foreach (string sourcePath in sourcePaths)
                            {
                                string fileName = Path.GetFileName(sourcePath);
                                if (!string.IsNullOrEmpty(fileName) && !missingFiles.Contains(fileName, StringComparer.Ordinal))
                                {
                                    missingFiles.Add(fileName);
                                    await Logger.LogVerboseAsync($"[ComponentValidationService] Missing file: {fileName}").ConfigureAwait(false);
                                }
                            }
                        }
                        else
                        {
                            await Logger.LogVerboseAsync($"[ComponentValidationService] Found {foundFiles.Count} files for instruction").ConfigureAwait(false);
                        }
                    }
                }

                await Logger.LogVerboseAsync($"[ComponentValidationService] Found {missingFiles.Count} missing files for component '{component.Name}'").ConfigureAwait(false);
                return missingFiles;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"Error getting missing files for component '{component?.Name}'").ConfigureAwait(false);
                return missingFiles;
            }
        }

        /// <summary>
        /// Simulates previous Extract instructions to populate VFS for path validation.
        /// </summary>
        public static void SimulatePreviousInstructions(VirtualFileSystemProvider virtualProvider, ModComponent component, Instruction targetInstruction)
        {
            try
            {
                int targetIndex = component.Instructions.IndexOf(targetInstruction);
                if (targetIndex < 0)
                {
                    return;
                }

                for (int i = 0; i < targetIndex; i++)
                {
                    Instruction prevInstruction = component.Instructions[i];
                    if (prevInstruction.Action == Instruction.ActionType.Extract)
                    {
                        foreach (string sourcePath in prevInstruction.Source)
                        {
                            List<string> archiveFiles = PathHelper.EnumerateFilesWithWildcards(
                                new List<string> { sourcePath },
                                virtualProvider,
                                includeSubFolders: true
                            );

                            foreach (string archiveFile in archiveFiles)
                            {
                                string destination = !string.IsNullOrWhiteSpace(prevInstruction.Destination)
                                    ? ResolvePath(prevInstruction.Destination)
                                    : MainConfig.SourcePath?.FullName;

                                _ = virtualProvider.ExtractArchiveAsync(archiveFile, destination).GetAwaiter().GetResult();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error simulating previous instructions");
            }
        }

        /// <summary>
        /// Resolves placeholder paths (<<modDirectory>>, <<kotorDirectory>>) to actual paths.
        /// </summary>
        [NotNull]
        public static string ResolvePath([CanBeNull] string path)
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

        /// <summary>
        /// Validates URL format.
        /// </summary>
        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates all mod links in a list.
        /// </summary>
        public static bool AreModLinksValid(List<string> modLinks)
        {
            if (modLinks is null || modLinks.Count == 0)
            {
                return true;
            }

            foreach (string link in modLinks)
            {
                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (!IsValidUrl(link))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets validation reason for a URL.
        /// </summary>
        public static string GetUrlValidationReason(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "Empty URL";
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return "Invalid URL format";
            }

            if (!string.Equals(uri.Scheme, "http", StringComparison.Ordinal) && !string.Equals(uri.Scheme, "https", StringComparison.Ordinal))
            {
                return $"Unsupported protocol: {uri.Scheme}";
            }

            return "Valid URL";
        }
        /// <summary>
        /// Gets the current VirtualFileSystemProvider instance.
        /// </summary>
        [NotNull]
        public VirtualFileSystemProvider GetVirtualFileSystem() => MainConfig.VirtualFileSystemProvider;

        /// <summary>
        /// Filters a list of filenames to only those that satisfy currently unsatisfied Extract instruction patterns.
        /// Uses simple wildcard pattern matching against ResourceRegistry filenames instead of VFS.
        /// </summary>
        public List<string> FilterFilenamesByUnsatisfiedPatterns(
            [NotNull] ModComponent component,
            [NotNull] List<string> filenames,
            [NotNull] string modArchiveDirectory)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (filenames is null || filenames.Count == 0)
            {
                return new List<string>();
            }

            if (string.IsNullOrEmpty(modArchiveDirectory))
            {
                return filenames; // Fallback: download all
            }

            // Collect all Extract instructions (component + options)
            var extractInstructions = new List<Instruction>();
            foreach (Instruction instruction in component.Instructions)
            {
                if (instruction.Action == Instruction.ActionType.Extract)
                {
                    extractInstructions.Add(instruction);
                }
            }
            foreach (Option option in component.Options)
            {
                foreach (Instruction instruction in option.Instructions)
                {
                    if (instruction.Action == Instruction.ActionType.Extract)
                    {
                        extractInstructions.Add(instruction);
                    }
                }
            }

            if (extractInstructions.Count == 0)
            {
                Logger.LogVerbose($"[ComponentValidationService] No Extract instructions found, all files allowed");
                return filenames;
            }

            // Collect available filenames from ResourceRegistry and disk
            var availableFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ResourceMetadata meta in component.ResourceRegistry.Values)
            {
                if (meta.Files != null)
                {
                    foreach (string filename in meta.Files.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(filename))
                        {
                            availableFilenames.Add(filename);
                            availableFilenames.Add(Path.GetFileName(filename)); // Also add just the filename
                        }
                    }
                }
            }

            // Also check files that exist on disk
            if (Directory.Exists(modArchiveDirectory))
            {
                foreach (string file in Directory.GetFiles(modArchiveDirectory, "*", SearchOption.AllDirectories))
                {
                    string relativeFile = file.Substring(modArchiveDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.IsNullOrWhiteSpace(relativeFile))
                    {
                        availableFilenames.Add(relativeFile);
                        availableFilenames.Add(Path.GetFileName(relativeFile));
                    }
                }
            }

            // Find which Extract patterns are currently unsatisfied
            var unsatisfiedPatterns = new List<string>();

            foreach (Instruction instruction in extractInstructions)
            {
                if (instruction.Source is null || instruction.Source.Count == 0)
                {
                    continue;
                }

                foreach (string sourcePath in instruction.Source)
                {
                    if (string.IsNullOrWhiteSpace(sourcePath))
                    {
                        continue;
                    }

                    // Check if pattern matches any available filename
                    bool satisfied = false;
                    string patternFileName = sourcePath.Replace("<<modDirectory>>\\", "").Replace("<<modDirectory>>/", "").TrimStart('\\', '/');

                    foreach (string availableFile in availableFilenames)
                    {
                        string testPath = $@"<<modDirectory>>\{availableFile}";
                        if (PathHelper.WildcardPathMatch(testPath, sourcePath) ||
                            PathHelper.WildcardPathMatch(availableFile, Path.GetFileName(patternFileName)))
                        {
                            satisfied = true;
                            break;
                        }
                    }

                    if (!satisfied)
                    {
                        unsatisfiedPatterns.Add(sourcePath);
                        Logger.LogVerbose($"[ComponentValidationService] Unsatisfied Extract pattern: {sourcePath}");
                    }
                }
            }

            if (unsatisfiedPatterns.Count == 0)
            {
                Logger.LogVerbose($"[ComponentValidationService] All Extract patterns already satisfied, no files needed");
                return new List<string>();
            }

            Logger.LogVerbose($"[ComponentValidationService] Found {unsatisfiedPatterns.Count} unsatisfied Extract pattern(s), testing {filenames.Count} filenames...");

            // Test each filename to see if it satisfies any unsatisfied patterns using wildcard matching
            var neededFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string filename in filenames)
            {
                if (string.IsNullOrWhiteSpace(filename))
                {
                    continue;
                }

                // Test if this filename matches any unsatisfied pattern
                foreach (string sourcePath in unsatisfiedPatterns)
                {
                    string patternFileName = sourcePath.Replace("<<modDirectory>>\\", "").Replace("<<modDirectory>>/", "").TrimStart('\\', '/');
                    string testPath = $@"<<modDirectory>>\{filename}";

                    if (PathHelper.WildcardPathMatch(testPath, sourcePath) ||
                        PathHelper.WildcardPathMatch(filename, Path.GetFileName(patternFileName)))
                    {
                        // This file satisfies a previously unsatisfied pattern!
                        neededFiles.Add(filename);
                        Logger.LogVerbose($"[ComponentValidationService] ✓ File '{filename}' satisfies Extract pattern '{sourcePath}'");
                        break; // No need to check other patterns for this file
                    }
                }
            }

            if (neededFiles.Count == 0)
            {
                Logger.LogVerbose($"[ComponentValidationService] No filenames matched unsatisfied Extract patterns. Available files:");
                foreach (string fn in filenames)
                {
                    Logger.LogVerbose($"  • {fn}");
                }
            }

            return neededFiles.ToList();
        }

        /// <summary>
        /// Tests if a filename is needed by any instruction pattern.
        /// </summary>
        public Task<bool> TestFilenameNeededByInstructionsAsync(ModComponent component, string filename, string modArchiveDirectory)
        {
            if (component is null || string.IsNullOrWhiteSpace(filename) || string.IsNullOrEmpty(modArchiveDirectory))
            {
                return Task.FromResult(false);
            }

            List<string> matchedFiles = FilterFilenamesByUnsatisfiedPatterns(component, new List<string> { filename }, modArchiveDirectory);
            return Task.FromResult(matchedFiles.Count > 0);
        }

        /// <summary>
        /// Finds the best matching filename from a list based on instruction patterns.
        /// Uses VFS to test which files would satisfy unsatisfied Extract patterns.
        /// </summary>
        public async Task<string> FindBestMatchingFilenameAsync(ModComponent component, string url, List<string> filenames, string modArchiveDirectory = null)
        {
            if (component is null || filenames is null || filenames.Count == 0)
            {
                return null;
            }

            if (filenames.Count == 1)
            {
                return filenames[0];
            }

            await Logger.LogVerboseAsync($"[ComponentValidationService] Testing {filenames.Count} filenames for URL '{url}' against instruction patterns...").ConfigureAwait(false);

            // If modArchiveDirectory not provided, use simple pattern matching as fallback
            if (string.IsNullOrEmpty(modArchiveDirectory))
            {
                modArchiveDirectory = MainConfig.SourcePath?.FullName;
            }

            if (!string.IsNullOrEmpty(modArchiveDirectory))
            {
                // Use VFS-based filtering to find files that satisfy unsatisfied patterns
                List<string> neededFiles = FilterFilenamesByUnsatisfiedPatterns(component, filenames, modArchiveDirectory);
                if (neededFiles.Count > 0)
                {
                    await Logger.LogAsync($"[ComponentValidationService] ✓ Found {neededFiles.Count} file(s) that satisfy unsatisfied patterns, returning first: {neededFiles[0]}'").ConfigureAwait(false);
                    return neededFiles[0];
                }
            }

            // Fallback: simple pattern matching
            var allInstructions = new List<Instruction>(component.Instructions);
            foreach (Option option in component.Options)
            {
                allInstructions.AddRange(option.Instructions);
            }

            foreach (Instruction instruction in allInstructions)
            {
                if (instruction.Source is null || instruction.Source.Count == 0)
                {
                    continue;
                }

                foreach (string sourcePath in instruction.Source)
                {
                    if (string.IsNullOrWhiteSpace(sourcePath))
                    {
                        continue;
                    }

                    string pattern = sourcePath.Replace("<<modDirectory>>\\", "").Replace("<<modDirectory>>/", "");

                    foreach (string filename in filenames)
                    {
                        if (FileMatchesPattern(filename, pattern))
                        {
                            await Logger.LogAsync($"[ComponentValidationService] ✓ Matched '{filename}' to pattern '{pattern}' (fallback pattern matching)").ConfigureAwait(false);
                            return filename;
                        }
                    }
                }
            }

            await Logger.LogWarningAsync($"[ComponentValidationService] No filename matched instruction patterns for URL '{url}'. Available files:").ConfigureAwait(false);
            foreach (string fn in filenames)
            {
                await Logger.LogWarningAsync($"  • {fn}").ConfigureAwait(false);
            }
            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task<bool> TryFixSingleMismatchAsync(
            ModComponent component,
            Instruction extractInstruction,
            string newArchiveName,
            VirtualFileSystemProvider vfs,
            string modArchiveDirectory,
            CancellationToken cancellationToken = default)
        {
            string oldArchiveName = ExtractFilenameFromSource(extractInstruction.Source[0]);

            string oldExtractedFolder = Path.GetFileNameWithoutExtension(oldArchiveName);
            string newExtractedFolder = Path.GetFileNameWithoutExtension(newArchiveName);

            // Update extract source
            int sourceIndex = -1;
            for (int i = 0; i < extractInstruction.Source.Count; i++)
            {
                if (string.Equals(ExtractFilenameFromSource(extractInstruction.Source[i]), oldArchiveName, StringComparison.Ordinal))
                {
                    sourceIndex = i;
                    break;
                }
            }
            if (sourceIndex >= 0)
            {
                // Create a mutable list, update, then re-assign to source as a new list.
                var sources = extractInstruction.Source.ToList();
                sources[sourceIndex] = $@"<<modDirectory>>\{newArchiveName}";
                extractInstruction.Source = sources;
            }

            void UpdateSources(IEnumerable<Instruction> instructions)
            {
                foreach (Instruction instr in instructions)
                {
                    var sources = instr.Source.ToList();
                    for (int i = 0; i < sources.Count; i++)
                    {
                        string src = sources[i];
                        if (src.IndexOf(oldExtractedFolder, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Manual case-insensitive replace
                            int index = src.IndexOf(oldExtractedFolder, StringComparison.OrdinalIgnoreCase);
                            while (index >= 0)
                            {
                                src = src.Substring(0, index) + newExtractedFolder + src.Substring(index + oldExtractedFolder.Length);
                                index = src.IndexOf(oldExtractedFolder, index + newExtractedFolder.Length, StringComparison.OrdinalIgnoreCase);
                            }
                            sources[i] = src;
                        }
                    }
                    instr.Source = sources;
                }
            }

            UpdateSources(component.Instructions);
            foreach (Option opt in component.Options)
            {
                UpdateSources(opt.Instructions);
            }

            // Validate
            var tempVfs = new VirtualFileSystemProvider();
            await tempVfs.InitializeFromRealFileSystemAsync(modArchiveDirectory).ConfigureAwait(false);

            try
            {
                ModComponent.InstallExitCode exitCode = await component.ExecuteInstructionsAsync(
                    component.Instructions,
                    new List<ModComponent>(),
                    cancellationToken,
                    tempVfs,
                    skipDependencyCheck: true
                ).ConfigureAwait(false);

                if (exitCode == ModComponent.InstallExitCode.Success)
                {
                    return true;
                }

                // Rollback if fails
                return false;
            }
            catch
            {
                return false;
            }
        }
        private static string ExtractFilenameFromSource(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return string.Empty;
            }

            string cleanedPath = sourcePath.Replace("<<modDirectory>>\\", "").Replace("<<modDirectory>>/", "");

            string filename = Path.GetFileName(cleanedPath);

            return filename;
        }

        private static bool FileMatchesPattern(string filename, string pattern)
        {
            if (string.IsNullOrWhiteSpace(filename) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            try
            {
                return Regex.IsMatch(filename, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(1000));
            }
            catch
            {
                return filename.IndexOf(pattern.Replace("*", ""), StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static readonly Regex s_underscoreDashSpaceRegex = new Regex(@"[_\-\s]+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));
        private static readonly Regex s_versionRegex = new Regex(@"v?\d+(\.\d+)*", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));
        private static readonly Regex s_startsWithWordOrSpaceRegex = new Regex(@"[^\w\s]", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));
    }
}
