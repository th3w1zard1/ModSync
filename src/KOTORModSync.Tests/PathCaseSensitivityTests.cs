// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Utility;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    internal class PathCaseSensitivityTests
    {
#pragma warning disable CS8618
        private static string s_testDirectory;
#pragma warning restore CS8618
        private DirectoryInfo _subDirectory;

        private DirectoryInfo _tempDirectory;

        [SetUp]
        public static void InitializeTestDirectory()
        {
            s_testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _ = Directory.CreateDirectory(s_testDirectory);
        }

        [TearDown]
        public static void CleanUpTestDirectory() => Directory.Delete(s_testDirectory, recursive: true);

        [SetUp]
        public void Setup()
        {
            _tempDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), path2: "UnitTestTempDir"));
            _tempDirectory.Create();
            _subDirectory = new DirectoryInfo(Path.Combine(_tempDirectory.FullName, path2: "SubDir"));
            _subDirectory.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _subDirectory.Delete(recursive: true);
            _tempDirectory.Delete(recursive: true);
        }

        [Test]
        public void FindCaseInsensitiveDuplicates_ThrowsArgumentNullException_WhenDirectoryIsNull()
        {
            DirectoryInfo directory = null;

            var exception = Assert.Throws<ArgumentNullException>(
                () => PathHelper.FindCaseInsensitiveDuplicates(directory)
            );

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Null directory should throw ArgumentNullException");
                Assert.That(exception.ParamName, Is.Not.Null, "Exception should have parameter name");
            });
        }

        [Test]
        public void FindCaseInsensitiveDuplicates_ReturnsEmptyList_WhenDirectoryIsEmpty()
        {
            IEnumerable<FileSystemInfo> result = PathHelper.FindCaseInsensitiveDuplicates(_tempDirectory);

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(_tempDirectory, Is.Not.Null, "Temp directory should not be null");
                Assert.That(_tempDirectory.Exists, Is.True, "Temp directory should exist");
                Assert.That(result, Is.Empty, $"Expected 0 items, but found {result.ToList().Count}. Output: {failureMessage}");
            });
        }

        [Test]
        public void FindCaseInsensitiveDuplicates_ReturnsEmptyList_WhenNoDuplicatesExist()
        {

            var file1 = new FileInfo(Path.Combine(_tempDirectory.FullName, path2: "file1.txt"));
            file1.Create().Close();
            var file2 = new FileInfo(Path.Combine(_tempDirectory.FullName, path2: "file2.txt"));
            file2.Create().Close();

            var result = PathHelper.FindCaseInsensitiveDuplicates(_tempDirectory).ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(file1, Is.Not.Null, "File1 should not be null");
                Assert.That(file2, Is.Not.Null, "File2 should not be null");
                Assert.That(file1.Exists, Is.True, "File1 should exist");
                Assert.That(file2.Exists, Is.True, "File2 should exist");
                Assert.That(result, Is.Empty, $"Expected 0 items, but found {result.Count}. Output: {failureMessage}");
            });
        }

        [Test]

        public void FindCaseInsensitiveDuplicates_FindsFileDuplicates_CaseInsensitive()
        {
            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                TestContext.Progress.WriteLine("FindCaseInsensitiveDuplicates_FindsFileDuplicates_CaseInsensitive: Test is not possible on Windows.");
                return;
            }

            var file1 = new FileInfo(Path.Combine(_tempDirectory.FullName, path2: "file1.txt"));
            file1.Create().Close();
            var file2 = new FileInfo(Path.Combine(_tempDirectory.FullName, path2: "FILE1.txt"));
            file2.Create().Close();

            var result = PathHelper.FindCaseInsensitiveDuplicates(_tempDirectory).ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(file1, Is.Not.Null, "File1 should not be null");
                Assert.That(file2, Is.Not.Null, "File2 should not be null");
                Assert.That(file1.Exists, Is.True, "File1 should exist");
                Assert.That(file2.Exists, Is.True, "File2 should exist");
                Assert.That(result, Has.Count.EqualTo(2),
                    $"Expected 2 items (case-insensitive duplicates), but found {result.Count}. Output: {failureMessage}");
                Assert.That(result.All(r => r != null), Is.True, "All result items should not be null");
            });
        }

        [Test]
        public void FindCaseInsensitiveDuplicates_IgnoresNonDuplicates()
        {

            var file1 = new FileInfo(Path.Combine(_tempDirectory.FullName, path2: "file1.txt"));
            file1.Create().Close();
            var file2 = new FileInfo(Path.Combine(_subDirectory.FullName, path2: "file2.txt"));
            file2.Create().Close();

            var result = PathHelper.FindCaseInsensitiveDuplicates(_tempDirectory).ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(file1, Is.Not.Null, "File1 should not be null");
                Assert.That(file2, Is.Not.Null, "File2 should not be null");
                Assert.That(file1.Exists, Is.True, "File1 should exist");
                Assert.That(file2.Exists, Is.True, "File2 should exist");
                Assert.That(_subDirectory, Is.Not.Null, "Subdirectory should not be null");
                Assert.That(_subDirectory.Exists, Is.True, "Subdirectory should exist");
                Assert.That(result, Is.Empty, $"Expected 0 items (files in different directories), but found {result.Count}. Output: {failureMessage}");
            });
        }

        [Test]
        public void FindCaseInsensitiveDuplicates_IgnoresExtensions()
        {

            var file1 = new FileInfo(Path.Combine(_tempDirectory.FullName, path2: "file1.txt"));
            file1.Create().Close();
            var file2 = new FileInfo(Path.Combine(_subDirectory.FullName, path2: "FILE1.png"));
            file2.Create().Close();

            var result = PathHelper.FindCaseInsensitiveDuplicates(_tempDirectory).ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(file1, Is.Not.Null, "File1 should not be null");
                Assert.That(file2, Is.Not.Null, "File2 should not be null");
                Assert.That(file1.Exists, Is.True, "File1 should exist");
                Assert.That(file2.Exists, Is.True, "File2 should exist");
                Assert.That(file1.Extension, Is.EqualTo(".txt"), "File1 should have .txt extension");
                Assert.That(file2.Extension, Is.EqualTo(".png"), "File2 should have .png extension");
                Assert.That(result, Is.Empty, $"Expected 0 items (different extensions), but found {result.Count}. Output: {failureMessage}");
            });
        }

        [Test]
        public void TestGetClosestMatchingEntry()
        {
            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                TestContext.Progress.WriteLine("FindCaseInsensitiveDuplicates_IgnoresNonDuplicates: Test is not possible on Windows.");
                return;
            }

            string file1 = Path.Combine(s_testDirectory, path2: "file.txt");
            string file2 = Path.Combine(s_testDirectory, path2: "FILE.TXT");
            File.WriteAllText(file1, contents: "Test content");
            File.WriteAllText(file2, contents: "Test content");
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(file1), Is.True, "File1 should exist");
                Assert.That(File.Exists(file2), Is.True, "File2 should exist");
                Assert.That(Path.GetFileName(file1), Is.EqualTo("file.txt"), "File1 should be named file.txt");
                Assert.That(Path.GetFileName(file2), Is.EqualTo("FILE.TXT"), "File2 should be named FILE.TXT");

                var (caseSensitivePath1, _) = PathHelper.GetCaseSensitivePath(
                    Path.Combine(Path.GetDirectoryName(file1), Path.GetFileName(file1).ToUpperInvariant())
                );
                Assert.That(caseSensitivePath1, Is.EqualTo(file2),
                    "GetCaseSensitivePath should return FILE.TXT when given uppercase path");

                var (caseSensitivePath2, _) = PathHelper.GetCaseSensitivePath(file1.ToUpperInvariant());
                Assert.That(caseSensitivePath2, Is.EqualTo(file2),
                    "GetCaseSensitivePath should return FILE.TXT when given uppercase full path");
            });
        }

        [Test]
        public void TestDuplicatesWithFileInfo()
        {
            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                TestContext.Progress.WriteLine("FindCaseInsensitiveDuplicates_IgnoresExtensions: Test is not possible on Windows.");
                return;
            }

            File.WriteAllText(Path.Combine(s_testDirectory, path2: "file.txt"), contents: "Test content");
            File.WriteAllText(Path.Combine(s_testDirectory, path2: "File.txt"), contents: "Test content");

            var fileInfo = new FileInfo(Path.Combine(s_testDirectory, path2: "file.txt"));
            var result = PathHelper.FindCaseInsensitiveDuplicates(fileInfo).ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(fileInfo, Is.Not.Null, "FileInfo should not be null");
                Assert.That(fileInfo.Exists, Is.True, "FileInfo should exist");
                Assert.That(
                    result,
                    Has.Count.EqualTo(2),
                    $"Expected 2 items (case-insensitive duplicates), but found {result.Count}. Output: {failureMessage}");
                Assert.That(result.All(r => r != null), Is.True, "All result items should not be null");
            });
        }

        [Test]
        public void TestDuplicatesWithDirectoryNameString()
        {
            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                TestContext.Progress.WriteLine("TestDuplicatesWithDirectoryNameString: Test is not possible on Windows.");
                return;
            }

            File.WriteAllText(Path.Combine(s_testDirectory, path2: "file.txt"), contents: "Test content");
            File.WriteAllText(Path.Combine(s_testDirectory, path2: "File.txt"), contents: "Test content");

            var result = PathHelper.FindCaseInsensitiveDuplicates(s_testDirectory).ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.That(
                result,
                Has.Count.EqualTo(2),
                $"Expected 2 items, but found {result.Count}. Output: {failureMessage}"
            );
        }

        [Test]
        public void TestDuplicateDirectories()
        {
            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                TestContext.Progress.WriteLine("TestDuplicateDirectories: Test is not possible on Windows.");
                return;
            }

            _ = Directory.CreateDirectory(Path.Combine(s_testDirectory, path2: "subdir"));
            _ = Directory.CreateDirectory(Path.Combine(s_testDirectory, path2: "SubDir"));

            var dirInfo = new DirectoryInfo(s_testDirectory);
            var result = PathHelper.FindCaseInsensitiveDuplicates(dirInfo).ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.That(
                result,
                Has.Count.EqualTo(2),
                $"Expected 2 items, but found {result.Count}. Output: {failureMessage}"
            );
        }

        [Test]
        public void TestDuplicatesWithDifferentCasingFilesInNestedDirectories()
        {
            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                TestContext.Progress.WriteLine("TestDuplicatesWithDifferentCasingFilesInNestedDirectories: Test is not possible on Windows.");
                return;
            }

            string subDirectory = Path.Combine(s_testDirectory, path2: "SubDirectory");
            _ = Directory.CreateDirectory(subDirectory);

            File.WriteAllText(Path.Combine(s_testDirectory, path2: "file.txt"), contents: "Test content");
            File.WriteAllText(Path.Combine(s_testDirectory, path2: "file.TXT"), contents: "Test content");
            File.WriteAllText(Path.Combine(subDirectory, path2: "FILE.txt"), contents: "Test content");
            File.WriteAllText(Path.Combine(subDirectory, path2: "file.tXT"), contents: "Test content");

            var dirInfo = new DirectoryInfo(s_testDirectory);
            var result = PathHelper.FindCaseInsensitiveDuplicates(dirInfo, includeSubFolders: true)
                .ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.That(
                result,
                Has.Count.EqualTo(4),
                $"Expected 4 items, but found {result.Count}. Output: {failureMessage}"
            );
        }

        [Test]
        public void TestDuplicateNestedDirectories()
        {
            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                TestContext.Progress.WriteLine("TestDuplicateNestedDirectories: Test is not possible on Windows.");
                return;
            }

            string subDir1 = Path.Combine(s_testDirectory, path2: "SubDir");
            string subDir2 = Path.Combine(s_testDirectory, path2: "subdir");

            _ = Directory.CreateDirectory(subDir1);
            _ = Directory.CreateDirectory(subDir2);

            File.WriteAllText(Path.Combine(subDir1, path2: "file.txt"), contents: "Test content");
            File.WriteAllText(Path.Combine(subDir2, path2: "file.txt"), contents: "Test content");

            var dirInfo = new DirectoryInfo(s_testDirectory);
            var result = PathHelper.FindCaseInsensitiveDuplicates(dirInfo, includeSubFolders: true)
                .ToList();

            var failureMessage = new StringBuilder();
            _ = StringBuilderExtensions.AppendJoin(failureMessage, Environment.NewLine, result.Select(item => item.FullName)).AppendLine();

            Assert.That(
                result,
                Has.Count.EqualTo(2),
                $"Expected 2 items, but found {result.Count}. Output: {failureMessage}"
            );
        }

        [Test]
        public void TestInvalidPath() =>
            _ = Assert.Throws<ArgumentException>(

                () => PathHelper.FindCaseInsensitiveDuplicates("Invalid>Path")?.ToList()
            );

        [Test]
        public void GetCaseSensitivePath_ValidFile_ReturnsSamePath()
        {

            string testFilePath = Path.Combine(s_testDirectory, path2: "test.txt");
            File.Create(testFilePath).Close();

            string result = PathHelper.GetCaseSensitivePath(testFilePath, isFile: true).Item1;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(File.Exists(testFilePath), Is.True, "Test file should exist");
                Assert.That(result, Is.EqualTo(testFilePath), "GetCaseSensitivePath should return same path for valid file");
            });
        }

        [Test]
        public void GetCaseSensitivePath_ValidDirectory_ReturnsSamePath()
        {

            string testDirPath = Path.Combine(s_testDirectory, path2: "testDir");
            _ = Directory.CreateDirectory(testDirPath);

            DirectoryInfo result = PathHelper.GetCaseSensitivePath(new DirectoryInfo(testDirPath));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(Directory.Exists(testDirPath), Is.True, "Test directory should exist");
                Assert.That(result.FullName, Is.EqualTo(testDirPath), "GetCaseSensitivePath should return same path for valid directory");
                Assert.That(result.Exists, Is.True, "Result directory should exist");
            });
        }

        [Test]
        public void GetCaseSensitivePath_NullOrWhiteSpacePath_ThrowsArgumentException()
        {

            string nullPath = null;
            string emptyPath = string.Empty;
            const string whiteSpacePath = "   ";

            var exception1 = Assert.Throws<ArgumentException>(() => PathHelper.GetCaseSensitivePath(nullPath));
            var exception2 = Assert.Throws<ArgumentException>(() => PathHelper.GetCaseSensitivePath(emptyPath));
            var exception3 = Assert.Throws<ArgumentException>(() => PathHelper.GetCaseSensitivePath(whiteSpacePath));

            Assert.Multiple(() =>
            {
                Assert.That(exception1, Is.Not.Null, "Null path should throw ArgumentException");
                Assert.That(exception2, Is.Not.Null, "Empty path should throw ArgumentException");
                Assert.That(exception3, Is.Not.Null, "Whitespace path should throw ArgumentException");
            });
        }

        [Test]
        public void GetCaseSensitivePath_InvalidCharactersInPath_ReturnsOriginalPath()
        {

            string fileName = "invalid>path";
            string invalidPath = Path.Combine(s_testDirectory, fileName);
            string upperCasePath = invalidPath.ToUpperInvariant();

            (string, bool?) result = PathHelper.GetCaseSensitivePath(upperCasePath);
            Assert.Multiple(() =>
            {
                Assert.That(result.Item1, Is.Not.Null, "Result path should not be null");
                Assert.That(result.Item1, Is.EqualTo(Path.Combine(s_testDirectory, fileName.ToUpperInvariant())),
                    "Invalid path should return original uppercase path");
                Assert.That(result.Item2, Is.Null, "Result should indicate path validity is unknown (null)");
            });
        }

        [Test]
        public void GetCaseSensitivePath_RelativePath_ReturnsAbsolutePath()
        {

            string testFilePath = Path.Combine(s_testDirectory, path2: "test.txt");
            File.Create(testFilePath).Close();
            string relativePath = NetFrameworkCompatibility.GetRelativePath(Directory.GetCurrentDirectory(), testFilePath);
            string upperCasePath = relativePath.ToUpperInvariant();

            string result = PathHelper.GetCaseSensitivePath(upperCasePath).Item1;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(File.Exists(testFilePath), Is.True, "Test file should exist");
                Assert.That(result, Is.EqualTo(testFilePath), "Relative path should resolve to absolute path");
            });
        }

        [Test]
        public void GetCaseSensitivePath_EntirePathCaseIncorrect_ReturnsCorrectPath()
        {

            string testFilePath = Path.Combine(s_testDirectory, path2: "test.txt");
            File.Create(testFilePath).Close();
            string upperCasePath = testFilePath.ToUpperInvariant();

            string result = PathHelper.GetCaseSensitivePath(upperCasePath, isFile: true).Item1;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(File.Exists(testFilePath), Is.True, "Test file should exist");
                Assert.That(result, Is.EqualTo(testFilePath), "Case-incorrect path should return correct case path");
            });
        }

        [Test]
        public void GetCaseSensitivePath_NonExistentFile_ReturnsCaseSensitivePath()
        {

            string nonExistentFileName = "non_existent_file.txt";
            string nonExistentFilePath = Path.Combine(s_testDirectory, nonExistentFileName);
            string upperCasePath = nonExistentFilePath.ToUpperInvariant();

            string result = PathHelper.GetCaseSensitivePath(upperCasePath).Item1;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(File.Exists(nonExistentFilePath), Is.False, "Non-existent file should not exist");
                Assert.That(result, Is.EqualTo(Path.Combine(s_testDirectory, nonExistentFileName.ToUpperInvariant())),
                    "Non-existent file should return uppercase path");
            });
        }

        [Test]
        public void GetCaseSensitivePath_NonExistentDirAndChildFile_ReturnsCaseSensitivePath()
        {

            string nonExistentRelFilePath = Path.Combine(path1: "non_existent_dir", path2: "non_existent_file.txt");
            string nonExistentFilePath = Path.Combine(s_testDirectory, nonExistentRelFilePath);
            string upperCasePath = nonExistentFilePath.ToUpperInvariant();

            string result = PathHelper.GetCaseSensitivePath(upperCasePath, isFile: true).Item1;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(File.Exists(nonExistentFilePath), Is.False, "Non-existent file should not exist");
                Assert.That(Directory.Exists(Path.GetDirectoryName(nonExistentFilePath)), Is.False,
                    "Non-existent directory should not exist");
                Assert.That(result, Is.EqualTo(Path.Combine(s_testDirectory, nonExistentRelFilePath.ToUpperInvariant())),
                    "Non-existent dir and file should return uppercase path");
            });
        }

        [Test]
        public void GetCaseSensitivePath_NonExistentDirectory_ReturnsCaseSensitivePath()
        {

            string nonExistentRelPath = Path.Combine(path1: "non_existent_dir", path2: "non_existent_child_dir");
            string nonExistentDirPath = Path.Combine(s_testDirectory, nonExistentRelPath);
            string upperCasePath = nonExistentDirPath.ToUpperInvariant();

            string result = PathHelper.GetCaseSensitivePath(upperCasePath).Item1;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Result should not be null");
                Assert.That(Directory.Exists(nonExistentDirPath), Is.False, "Non-existent directory should not exist");
                Assert.That(result, Is.EqualTo(Path.Combine(s_testDirectory, nonExistentRelPath.ToUpperInvariant())),
                    "Non-existent directory should return uppercase path");
            });
        }
    }
}
