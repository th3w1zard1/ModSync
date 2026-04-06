// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System.IO;

using KOTORModSync.Core.FileSystemUtils;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class PathHelperArchiveExtractionTests
    {
        [Test]
        public void TryGetZipSafeArchiveEntryExtractPath_AllowsNormalRelativeEntry()
        {
            string root = Path.Combine(Path.GetTempPath(), "kms_zip_safe_" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(root);
                string rootFull = Path.GetFullPath(root);

                bool ok = PathHelper.TryGetZipSafeArchiveEntryExtractPath(
                    rootFull,
                    "subdir/file.txt",
                    out string fullPath,
                    out string parentDir);

                Assert.That(ok, Is.True);
                Assert.That(fullPath, Is.EqualTo(Path.Combine(rootFull, "subdir", "file.txt")));
                Assert.That(parentDir, Is.EqualTo(Path.Combine(rootFull, "subdir")));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void TryGetZipSafeArchiveEntryExtractPath_RejectsParentTraversal()
        {
            string root = Path.Combine(Path.GetTempPath(), "kms_zip_slip_" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(root);
                string rootFull = Path.GetFullPath(root);

                bool ok = PathHelper.TryGetZipSafeArchiveEntryExtractPath(
                    rootFull,
                    "..\\outside.txt",
                    out _,
                    out _);

                Assert.That(ok, Is.False);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void TryGetZipSafeArchiveEntryExtractPath_RejectsAbsoluteEntryKey()
        {
            string root = Path.Combine(Path.GetTempPath(), "kms_zip_abs_" + Path.GetRandomFileName());
            try
            {
                Directory.CreateDirectory(root);
                string rootFull = Path.GetFullPath(root);

                bool ok = PathHelper.TryGetZipSafeArchiveEntryExtractPath(
                    rootFull,
                    Path.Combine(Path.GetTempPath(), "evil.txt"),
                    out _,
                    out _);

                Assert.That(ok, Is.False);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
        }
    }
}
