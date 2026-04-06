// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.InteropServices;

using JetBrains.Annotations;

namespace KOTORModSync.Core.FileSystemUtils
{
    public static partial class PathHelper
    {
        /// <summary>
        /// Resolves a safe on-disk path for an archive entry under <paramref name="extractRootDirectory"/>,
        /// rejecting zip-slip / path traversal in <paramref name="archiveEntryKey"/>.
        /// </summary>
        public static bool TryGetZipSafeArchiveEntryExtractPath(
            [NotNull] string extractRootDirectory,
            [CanBeNull] string archiveEntryKey,
            [NotNull] out string fullFilePath,
            [NotNull] out string parentDirectoryForWrite)
        {
            fullFilePath = null;
            parentDirectoryForWrite = null;

            if (string.IsNullOrWhiteSpace(extractRootDirectory) || string.IsNullOrWhiteSpace(archiveEntryKey))
            {
                return false;
            }

            string key = archiveEntryKey.Trim();
            string normalized = key.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
            {
                return false;
            }

            foreach (string segment in normalized.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            string rootFull = Path.GetFullPath(extractRootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string combined = Path.GetFullPath(Path.Combine(rootFull, normalized));

            if (!IsStrictPathUnderRoot(rootFull, combined))
            {
                return false;
            }

            parentDirectoryForWrite = Path.GetDirectoryName(combined);
            if (string.IsNullOrEmpty(parentDirectoryForWrite))
            {
                return false;
            }

            fullFilePath = combined;
            return true;
        }

        private static bool IsStrictPathUnderRoot([NotNull] string rootFullPath, [NotNull] string candidateFullPath)
        {
            string root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringComparison cmp = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return candidateFullPath.StartsWith(root + Path.DirectorySeparatorChar, cmp)
                   || candidateFullPath.StartsWith(root + Path.AltDirectorySeparatorChar, cmp);
        }
    }
}
