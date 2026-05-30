// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using JetBrains.Annotations;

namespace KOTORModSync.Core.FileSystemUtils
{
    public static partial class PathHelper
    {
        [NotNull]
        public static List<string> EnumerateFilesWithWildcards([NotNull] string path, [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider)
        {
            return EnumerateFilesWithWildcards(new[] { path }, fileSystemProvider);
        }

        [CanBeNull]
        public static Tuple<FileInfo, DirectoryInfo> TryGetValidFileSystemInfos([CanBeNull] string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return null;
            }

            string formattedPath = FixPathFormatting(folderPath);
            if (!PathValidator.IsValidPath(formattedPath))
            {
                return null;
            }

            OSPlatform thisOS = Utility.UtilityHelper.GetOperatingSystem();
            FileInfo filePathInfo = null;
            DirectoryInfo dirPathInfo = null;

            try
            {
                if (thisOS == OSPlatform.Windows)
                {
                    if (File.Exists(formattedPath))
                    {
                        filePathInfo = new FileInfo(formattedPath);
                    }

                    if (Directory.Exists(formattedPath))
                    {
                        dirPathInfo = new DirectoryInfo(formattedPath);
                    }
                }
                else if (thisOS == OSPlatform.Linux || thisOS == OSPlatform.OSX)
                {
                    if (File.Exists(formattedPath))
                    {
                        filePathInfo = new FileInfo(formattedPath);
                    }
                    else if (Directory.Exists(formattedPath))
                    {
                        dirPathInfo = new DirectoryInfo(formattedPath);
                    }
                }
                else
                {
                    throw new PlatformNotSupportedException("Unsupported platform.");
                }

                return Tuple.Create(filePathInfo, dirPathInfo);
            }
            catch (Exception e)
            {
                Logger.LogException(e);
                return null;
            }
        }

        [CanBeNull]
        public static DirectoryInfo TryGetValidDirectoryInfo([CanBeNull] string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return null;
            }

            string formattedPath = FixPathFormatting(folderPath);
            if (!PathValidator.IsValidPath(formattedPath))
            {
                return null;
            }

            try
            {
                return new DirectoryInfo(formattedPath);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to create DirectoryInfo for path '{formattedPath}'.");
                return null;
            }
        }

        [CanBeNull]
        public static FileInfo TryGetValidFileInfo([CanBeNull] string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            string formattedPath = FixPathFormatting(filePath);
            if (!PathValidator.IsValidPath(formattedPath))
            {
                return null;
            }

            try
            {
                return new FileInfo(formattedPath);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to create FileInfo for path '{formattedPath}'.");
                return null;
            }
        }

        [NotNull]
        public static string ConvertWindowsPathToCaseSensitive([NotNull] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"'{nameof(path)}' cannot be null or whitespace.", nameof(path));
            }

            if (Utility.UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
            {
                return path;
            }

            if (!PathValidator.IsValidPath(path))
            {
                throw new ArgumentException($"{path} is not a valid path!", nameof(path));
            }

            const uint FILE_SHARE_READ = 1;
            const uint OPEN_EXISTING = 3;
            const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
            const uint VOLUME_NAME_DOS = 0;

            IntPtr handle = CreateFile(
                path,
                dwDesiredAccess: 0,
                FILE_SHARE_READ,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero
            );

            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var buffer = new StringBuilder(4096);
                uint result = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, VOLUME_NAME_DOS);

                if (result == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                string finalPath = buffer.ToString();
                const string prefix = @"\\?\";
                if (finalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    finalPath = finalPath.Substring(prefix.Length);
                }

                return finalPath;
            }
            finally
            {
                _ = CloseHandle(handle);
            }
        }

        [NotNull]
        public static string GetRelativePath([NotNull] string relativeTo, [NotNull] string path)
        {
            return GetRelativePath(relativeTo, path, StringComparison.OrdinalIgnoreCase);
        }

        [NotNull]
        private static string GetRelativePath(
            [NotNull] string relativeTo,
            [NotNull] string path,
            StringComparison comparisonType
        )
        {
            Logger.LogVerbose($"[PathHelper.GetRelativePath] Called with relativeTo='{relativeTo}', path='{path}'");

            if (string.IsNullOrWhiteSpace(relativeTo))
            {
                Logger.LogVerbose($"[PathHelper.GetRelativePath] ERROR: relativeTo is null or whitespace");
                throw new ArgumentException(message: "Value cannot be null or whitespace.", nameof(relativeTo));
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                Logger.LogVerbose($"[PathHelper.GetRelativePath] ERROR: path is null or whitespace");
                throw new ArgumentException(message: "Value cannot be null or whitespace.", nameof(path));
            }

            if (!Enum.IsDefined(typeof(StringComparison), comparisonType))
            {
                throw new InvalidEnumArgumentException(
                    nameof(comparisonType),
                    (int)comparisonType,
                    typeof(StringComparison)
                );
            }

            string fixedRelativeTo = FixPathFormatting(relativeTo);
            string fixedPath = FixPathFormatting(path);
            Logger.LogVerbose($"[PathHelper.GetRelativePath] After FixPathFormatting: relativeTo='{fixedRelativeTo}', path='{fixedPath}'");

            relativeTo = Path.GetFullPath(fixedRelativeTo);
            path = Path.GetFullPath(fixedPath);
            Logger.LogVerbose($"[PathHelper.GetRelativePath] After GetFullPath: relativeTo='{relativeTo}', path='{path}'");

            if (!AreRootsEqual(relativeTo, path, comparisonType))
            {
                Logger.LogVerbose($"[PathHelper.GetRelativePath] Roots are not equal, returning full path: '{path}'");
                return path;
            }

            int commonLength = GetCommonPathLength(
                relativeTo,
                path,
                comparisonType == StringComparison.OrdinalIgnoreCase
            );
            Logger.LogVerbose($"[PathHelper.GetRelativePath] Common path length: {commonLength}");

            if (commonLength == 0)
            {
                Logger.LogVerbose($"[PathHelper.GetRelativePath] No common path, returning full path: '{path}'");
                return path;

            }

            bool pathEndsInSeparator = path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
            int pathLength = path.Length;
            if (pathEndsInSeparator)
            {
                pathLength--;
            }

            if (relativeTo.Length == pathLength && commonLength >= relativeTo.Length)
            {
                Logger.LogVerbose($"[PathHelper.GetRelativePath] Paths are identical, returning '.'");
                return ".";
            }

            var sb = new StringBuilder(Math.Max(relativeTo.Length, path.Length));

            if (commonLength >= relativeTo.Length && path[commonLength] == Path.DirectorySeparatorChar)
            {
                commonLength++;
            }

            int differenceLength = pathLength - commonLength;
            if (pathEndsInSeparator)
            {
                differenceLength++;
            }

            if (differenceLength > 0)
            {
                if (sb.Length > 0)
                {
                    _ = sb.Append(Path.DirectorySeparatorChar);
                }

                _ = sb.Append(path, commonLength, differenceLength);
            }

            string result = sb.ToString();
            Logger.LogVerbose($"[PathHelper.GetRelativePath] Returning relative path: '{result}'");
            return result;
        }

        private static bool AreRootsEqual(string first, string second, StringComparison comparisonType)
        {
            int firstRootLength = Path.GetPathRoot(first)?.Length ?? 0;
            int secondRootLength = Path.GetPathRoot(second)?.Length ?? 0;

            return firstRootLength == secondRootLength
                   && 0 == string.Compare(first, indexA: 0, second, indexB: 0, firstRootLength, comparisonType);
        }

        private static int GetCommonPathLength(string first, string second, bool ignoreCase)
        {
            int commonChars = Math.Min(first.Length, second.Length);

            int commonLength = 0;
            for (int i = 0; i < commonChars; i++)
            {
                if (first[i] != Path.DirectorySeparatorChar && second[i] != Path.DirectorySeparatorChar)
                {
                    continue;
                }

                if (0 != string.Compare(
                        first,
                        indexA: 0,
                        second,
                        indexB: 0,
                        i + 1,
                        ignoreCase
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal
                    ))
                {
                    return commonLength;
                }

                commonLength = i + 1;
            }

            return commonLength;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW")]
        private static extern uint GetFinalPathNameByHandleNative(
            IntPtr hFile,
            StringBuilder lpszFilePath,
            uint cchFilePath,
            uint dwFlags
        );

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "CloseHandle")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandleNative(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        private static extern IntPtr CreateFileNative(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        private static uint GetFinalPathNameByHandle(
            IntPtr hFile,
            StringBuilder lpszFilePath,
            uint cchFilePath,
            uint dwFlags
        )
        {
            if (Utility.UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
            {
                Logger.LogWarning("Attempted to resolve a Windows handle path on a non-Windows platform.");
                return 0;
            }

            return GetFinalPathNameByHandleNative(hFile, lpszFilePath, cchFilePath, dwFlags);
        }

        private static bool CloseHandle(IntPtr hObject)
        {
            if (Utility.UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
            {
                Logger.LogWarning("Attempted to close a Windows handle on a non-Windows platform.");
                return false;
            }

            return CloseHandleNative(hObject);
        }

        private static IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        )
        {
            if (Utility.UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
            {
                Logger.LogWarning("Attempted to create a Win32 file handle on a non-Windows platform.");
                return IntPtr.Zero;
            }

            return CreateFileNative(
                lpFileName,
                dwDesiredAccess,
                dwShareMode,
                lpSecurityAttributes,
                dwCreationDisposition,
                dwFlagsAndAttributes,
                hTemplateFile
            );
        }

        public static FileSystemInfo GetCaseSensitivePath(FileSystemInfo fileSystemInfoItem)
        {
            switch (fileSystemInfoItem)
            {
                case DirectoryInfo dirInfo: return GetCaseSensitivePath(dirInfo);
                case FileInfo fileInfo: return GetCaseSensitivePath(fileInfo);
                default: return null;
            }
        }

        public static FileInfo GetCaseSensitivePath(FileInfo file)
        {
            (string thisFilePath, _) = GetCaseSensitivePath(file?.FullName, isFile: true);
            return new FileInfo(thisFilePath);
        }

        public static DirectoryInfo GetCaseSensitivePath(DirectoryInfo folder)
        {
            (string thisFilePath, _) = GetCaseSensitivePath(folder?.FullName, isFile: true);
            return new DirectoryInfo(thisFilePath);
        }

        public static (string, bool?) GetCaseSensitivePath(string path, bool? isFile = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"'{nameof(path)}' cannot be null or whitespace.", nameof(path));
            }

            string formattedPath = Path.GetFullPath(FixPathFormatting(path));

            bool fileExists = File.Exists(formattedPath);
            bool folderExists = Directory.Exists(formattedPath);
            if (fileExists && (isFile == true || !folderExists))
            {
                return (ConvertWindowsPathToCaseSensitive(formattedPath), true);
            }

            if (folderExists && (isFile == false || !fileExists))
            {
                return (ConvertWindowsPathToCaseSensitive(formattedPath), false);
            }

            string[] parts = formattedPath.Split(
                new[] { Path.DirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries
            );

            if (parts.Length == 0)
            {
                parts = new[] { formattedPath };
            }

            string currentPath = Path.GetPathRoot(formattedPath);
            if (!string.IsNullOrEmpty(currentPath) && !Path.IsPathRooted(parts[0]))
            {
                parts = new[] { currentPath }.Concat(parts).ToArray();
            }

            if (parts[0].EndsWith(":", StringComparison.Ordinal))
            {
                parts[0] += Path.DirectorySeparatorChar;
            }

            int largestExistingPathPartsIndex = -1;
            string caseSensitiveCurrentPath = null;
            for (int i = 1; i < parts.Length; i++)
            {

                string previousCurrentPath = Path.Combine(parts.Take(i).ToArray());
                currentPath = Path.Combine(previousCurrentPath, parts[i]);
                if (Utility.UtilityHelper.GetOperatingSystem() != OSPlatform.Windows
                    && !Directory.Exists(currentPath)
                    && Directory.Exists(previousCurrentPath))
                {
                    int maxMatchingCharacters = -1;
                    string closestMatch = parts[i];

                    foreach (FileSystemInfo folderOrFileInfo in new DirectoryInfo(previousCurrentPath)
                        .EnumerateFileSystemInfosSafely(searchPattern: "*"))
                    {
                        if (folderOrFileInfo is null || !folderOrFileInfo.Exists)
                        {
                            continue;
                        }

                        if (folderOrFileInfo is FileInfo && i < parts.Length - 1)
                        {
                            continue;
                        }

                        int matchingCharacters = GetMatchingCharactersCount(folderOrFileInfo.Name, parts[i]);
                        if (matchingCharacters > maxMatchingCharacters)
                        {
                            maxMatchingCharacters = matchingCharacters;
                            closestMatch = folderOrFileInfo.Name;
                            if (i == parts.Length - 1)
                            {
                                isFile = folderOrFileInfo is FileInfo;
                            }
                        }
                    }

                    parts[i] = closestMatch;
                }
                else if (string.IsNullOrEmpty(caseSensitiveCurrentPath)
                      && !File.Exists(currentPath)
                      && !Directory.Exists(currentPath))
                {

                    largestExistingPathPartsIndex = i;
                    caseSensitiveCurrentPath = ConvertWindowsPathToCaseSensitive(previousCurrentPath);
                }
            }

            if (caseSensitiveCurrentPath is null)
            {
                return (Path.Combine(parts), isFile);
            }

            string combinedPath = largestExistingPathPartsIndex > -1
                ? Path.Combine(
                    caseSensitiveCurrentPath,
                    Path.Combine(parts.Skip(largestExistingPathPartsIndex).ToArray())
                )
                : Path.Combine(parts);

            return (combinedPath, isFile);
        }

        private static int GetMatchingCharactersCount(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1))
            {
                throw new ArgumentException(message: "Value cannot be null or empty.", nameof(str1));
            }

            if (string.IsNullOrEmpty(str2))
            {
                throw new ArgumentException(message: "Value cannot be null or empty.", nameof(str2));
            }

            if (!str1.Equals(str2, StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }

            int matchingCount = 0;
            for (int i = 0; i < str1.Length && i < str2.Length; i++)
            {

                if (str1[i] == str2[i])
                {
                    matchingCount++;
                }
            }

            return matchingCount;
        }

        public static async Task MoveFileAsync(string sourcePath, string destinationPath)
        {
            if (sourcePath is null)
            {
                throw new ArgumentNullException(nameof(sourcePath));
            }

            if (destinationPath is null)
            {
                throw new ArgumentNullException(nameof(destinationPath));
            }

            using (var sourceStream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true
                ))
            {
                using (var destinationStream = new FileStream(
                        destinationPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 4096,
                        useAsync: true
                    ))

                {
                    await sourceStream.CopyToAsync(destinationStream).ConfigureAwait(false);
                }
            }

            File.Delete(sourcePath);
        }

        public static List<string> EnumerateFilesWithWildcards(
            IEnumerable<string> filesAndFolders,
            Services.FileSystem.IFileSystemProvider fileSystemProvider,
            bool includeSubFolders = true
        )
        {
            if (filesAndFolders is null)
            {
                throw new ArgumentNullException(nameof(filesAndFolders));
            }

            if (fileSystemProvider is null)
            {
                throw new ArgumentNullException(nameof(fileSystemProvider));
            }

            var result = new List<string>();

            var uniquePaths = new HashSet<string>(filesAndFolders, StringComparer.Ordinal);

            foreach (string path in uniquePaths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                try
                {
                    string formattedPath = FixPathFormatting(path);
                    try
                    {
                        formattedPath = Path.GetFullPath(formattedPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogVerbose($"[PathHelper] Could not get full path for '{formattedPath}': {ex.Message}");
                    }

                    Logger.LogVerbose($"[PathHelper] EnumerateFilesWithWildcards: path={path}, formatted={formattedPath}");

                    if (!ContainsWildcards(formattedPath))
                    {
                        Logger.LogVerbose("[PathHelper] No wildcards, checking FileExists...");

                        if (!fileSystemProvider.FileExists(formattedPath))
                        {
                            Logger.LogVerbose("[PathHelper] Not found, trying case-sensitive...");
                            (string, bool?) returnTuple = GetCaseSensitivePath(formattedPath);
                            formattedPath = returnTuple.Item1;
                            Logger.LogVerbose($"[PathHelper] Case-sensitive: {formattedPath}");
                        }

                        if (fileSystemProvider.FileExists(formattedPath))
                        {
                            Logger.LogVerbose($"[PathHelper] EXISTS! Adding: {formattedPath}");
                            result.Add(formattedPath);
                        }
                        else
                        {
                            Logger.LogVerbose($"[PathHelper] NOT FOUND: {formattedPath}");
                        }

                        continue;
                    }

                    string currentDir = formattedPath;
                    while (ContainsWildcards(currentDir))
                    {
                        string parentDirectory = Path.GetDirectoryName(currentDir);


                        if (string.IsNullOrEmpty(parentDirectory) || string.Equals(parentDirectory, currentDir, StringComparison.Ordinal))
                        {
                            break;
                        }

                        currentDir = parentDirectory;
                    }

                    if (!fileSystemProvider.DirectoryExists(currentDir))
                    {
                        continue;
                    }

                    List<string> checkFiles = fileSystemProvider.GetFilesInDirectory(
                        currentDir,
                        "*",
                        includeSubFolders
                            ? SearchOption.AllDirectories
                            : SearchOption.TopDirectoryOnly
                    );

                    result.AddRange(
                        from filePath in checkFiles
                        where !string.IsNullOrEmpty(filePath) && WildcardPathMatch(filePath, formattedPath)
                        select filePath
                    );

                    if (MainConfig.CaseInsensitivePathing && !fileSystemProvider.IsDryRun)
                    {
                        IEnumerable<FileSystemInfo> duplicates = FindCaseInsensitiveDuplicates(
                            currentDir,
                            includeSubFolders: true,
                            isFile: false
                        );

                        foreach (FileSystemInfo thisDuplicateFolder in duplicates)
                        {

                            if (!(thisDuplicateFolder is DirectoryInfo dirInfo))
                            {
                                throw new InvalidOperationException(nameof(dirInfo));
                            }

                            List<string> duplicateFiles = fileSystemProvider.GetFilesInDirectory(
                                dirInfo.FullName,
                                "*",
                                includeSubFolders
                                    ? SearchOption.AllDirectories
                                    : SearchOption.TopDirectoryOnly
                            );

                            result.AddRange(
                                from filePath in duplicateFiles
                                where !string.IsNullOrEmpty(filePath) && WildcardPathMatch(filePath, formattedPath)
                                select filePath
                            );
                        }
                    }
                }
                catch (Exception ex)
                {

                    Logger.LogVerbose($"An error occurred while processing path '{path}': {ex.Message}");
                }
            }

            return result;
        }

        private static bool ContainsWildcards([NotNull] string path) => path.Contains('*') || path.Contains('?');

        public static bool WildcardPathMatch(string input, string patternInput)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (patternInput is null)
            {
                throw new ArgumentNullException(nameof(patternInput));
            }

            input = FixPathFormatting(input);
            patternInput = FixPathFormatting(patternInput);

            string[] inputLevels = input.Split(Path.DirectorySeparatorChar);
            string[] patternLevels = patternInput.Split(Path.DirectorySeparatorChar);

            if (inputLevels.Length != patternLevels.Length)
            {
                return false;
            }

            for (int i = 0; i < inputLevels.Length; i++)
            {
                string inputLevel = inputLevels[i];
                string patternLevel = patternLevels[i];

                if (patternLevel is "*")
                {
                    continue;
                }

                if (!WildcardMatch(inputLevel, patternLevel))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool WildcardMatch(string input, string patternInput)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (patternInput is null)
            {
                throw new ArgumentNullException(nameof(patternInput));
            }

            patternInput = Regex.Escape(patternInput);

            patternInput = patternInput.Replace(oldValue: @"\*", newValue: ".*")
                .Replace(oldValue: @"\?", newValue: ".");

            return Regex.IsMatch(input, $"^{patternInput}$", RegexOptions.None, TimeSpan.FromSeconds(10));
        }

        [NotNull]
        public static string FixPathFormatting([NotNull] string path)
        {
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string formattedPath = path.TrimStart(new[] { '\\' })
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace(oldChar: '\\', Path.DirectorySeparatorChar).Replace(oldChar: '/', Path.DirectorySeparatorChar);

            formattedPath = Regex.Replace(
                formattedPath,
                $"(?<!:){Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}+",
                Path.DirectorySeparatorChar.ToString()
            , RegexOptions.None, TimeSpan.FromSeconds(10));

            if (formattedPath.Length > 1)
            {
                formattedPath = formattedPath.TrimEnd(Path.DirectorySeparatorChar);
            }

            return formattedPath;
        }

        [NotNull]
        public static IEnumerable<FileSystemInfo> FindCaseInsensitiveDuplicates(
            DirectoryInfo dirInfo,
            bool includeSubFolders = true
        ) =>

            FindCaseInsensitiveDuplicates(dirInfo?.FullName, includeSubFolders, isFile: false);

        [NotNull]
        public static IEnumerable<FileSystemInfo> FindCaseInsensitiveDuplicates(FileInfo fileInfo) =>

            FindCaseInsensitiveDuplicates(fileInfo?.FullName, isFile: true);

        public static IEnumerable<FileSystemInfo> FindCaseInsensitiveDuplicates(
            [NotNull] string path,
            bool includeSubFolders = true,
            bool? isFile = null
        )
        {
            FindCaseInsensitiveDuplicatesContext context = ValidateFindCaseInsensitiveDuplicatesParameters(path, isFile);

            if (context is null)
            {
                return Array.Empty<FileSystemInfo>();
            }

            return FindCaseInsensitiveDuplicatesIterator(
                context.Directory,
                context.FileName,
                includeSubFolders,
                context.IsFile
            );
        }

        [CanBeNull]
        private static FindCaseInsensitiveDuplicatesContext ValidateFindCaseInsensitiveDuplicatesParameters(
            [NotNull] string path,
            bool? isFile
        )
        {
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string formattedPath = FixPathFormatting(path);
            if (!PathValidator.IsValidPath(formattedPath))
            {
                throw new ArgumentException($"'{path}' is not a valid path string", nameof(path));
            }

            if (Utility.UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                return null;
            }

            DirectoryInfo dirInfo = null;
            string fileName = Path.GetFileName(formattedPath);
            bool? resolvedIsFile = isFile;

            switch (resolvedIsFile)
            {
                case false:
                    {
                        dirInfo = new DirectoryInfo(formattedPath);
                        if (!dirInfo.Exists)
                        {
                            dirInfo = new DirectoryInfo(GetCaseSensitivePath(formattedPath).Item1);
                        }

                        break;
                    }
                case true:
                    {
                        string parentDir = Path.GetDirectoryName(formattedPath);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            dirInfo = new DirectoryInfo(parentDir);
                            if (!dirInfo.Exists)
                            {
                                dirInfo = new DirectoryInfo(GetCaseSensitivePath(parentDir).Item1);
                            }
                        }

                        break;
                    }
                default:
                    {
                        dirInfo = new DirectoryInfo(formattedPath);
                        string caseSensitivePath = formattedPath;
                        if (!dirInfo.Exists)
                        {
                            caseSensitivePath = GetCaseSensitivePath(formattedPath).Item1;
                            dirInfo = new DirectoryInfo(caseSensitivePath);
                        }

                        if (!dirInfo.Exists)
                        {
                            string folderPath = Path.GetDirectoryName(caseSensitivePath);
                            resolvedIsFile = true;
                            if (!(folderPath is null))
                            {
                                dirInfo = new DirectoryInfo(folderPath);
                            }
                        }

                        break;
                    }
            }

            if (!dirInfo?.Exists ?? false)
            {
                throw new ArgumentException($"Path item doesn't exist on disk: '{formattedPath}'", nameof(path));
            }

            return new FindCaseInsensitiveDuplicatesContext(dirInfo, fileName, resolvedIsFile);
        }

        private static IEnumerable<FileSystemInfo> FindCaseInsensitiveDuplicatesIterator(
            DirectoryInfo dirInfo,
            string fileName,
            bool includeSubFolders,
            bool? isFile
        )
        {
            var fileList = new Dictionary<string, List<FileSystemInfo>>(StringComparer.OrdinalIgnoreCase);
            var folderList = new Dictionary<string, List<FileSystemInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (FileInfo file in dirInfo.EnumerateFilesSafely())
            {
                if (!file.Exists)
                {
                    continue;
                }

                if (isFile == true && !file.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string filePath = file.FullName;
                if (!fileList.TryGetValue(filePath, out List<FileSystemInfo> files))
                {
                    files = new List<FileSystemInfo>();
                    fileList.Add(filePath, files);
                }

                files.Add(file);
            }

            foreach (List<FileSystemInfo> files in fileList.Values)
            {
                if (files.Count <= 1)
                {
                    continue;
                }

                foreach (FileSystemInfo duplicate in files)
                {
                    yield return duplicate;
                }
            }

            if (isFile == true)
            {
                yield break;
            }

            foreach (DirectoryInfo subDirectory in dirInfo.EnumerateDirectoriesSafely())
            {
                if (!subDirectory.Exists)
                {
                    continue;
                }

                if (!folderList.TryGetValue(subDirectory.FullName, out List<FileSystemInfo> folders))
                {
                    folders = new List<FileSystemInfo>();
                    folderList.Add(subDirectory.FullName, folders);
                }

                folders.Add(subDirectory);

                if (includeSubFolders)
                {
                    foreach (FileSystemInfo duplicate in FindCaseInsensitiveDuplicates(subDirectory))
                    {
                        yield return duplicate;
                    }
                }
            }

            foreach (List<FileSystemInfo> foldersInCurrentDir in folderList.Values)
            {
                if (foldersInCurrentDir.Count <= 1)
                {
                    continue;
                }

                foreach (FileSystemInfo duplicate in foldersInCurrentDir)
                {
                    yield return duplicate;
                }
            }
        }

        private sealed class FindCaseInsensitiveDuplicatesContext
        {
            public FindCaseInsensitiveDuplicatesContext(DirectoryInfo directory, string fileName, bool? isFile)
            {
                Directory = directory;
                FileName = fileName;
                IsFile = isFile;
            }

            public DirectoryInfo Directory { get; }

            public string FileName { get; }

            public bool? IsFile { get; }
        }
    }
}
