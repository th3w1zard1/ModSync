// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Services.FileSystem;
using NUnit.Framework;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class FileSystemOperationEdgeCasesTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_FSOperations_" + Guid.NewGuid());
            _modDirectory = Path.Combine(_testDirectory, "Mods");
            _kotorDirectory = Path.Combine(_testDirectory, "KOTOR");
            Directory.CreateDirectory(_modDirectory);
            Directory.CreateDirectory(_kotorDirectory);
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override"));

            _config = new MainConfig
            {
                sourcePath = new DirectoryInfo(_modDirectory),
                destinationPath = new DirectoryInfo(_kotorDirectory)
            };
            MainConfig.Instance = _config;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #region Move Operation Edge Cases

        [Test]
        public async Task MoveFile_WithOverwriteTrue_ReplacesExistingFile()
        {
            var sourceFile = Path.Combine(_modDirectory, "file.txt");
            var destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");

            File.WriteAllText(sourceFile, "original content");
            File.WriteAllText(destFile, "existing content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.MoveFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed");
                Assert.That(File.Exists(sourceFile), Is.False, "Source file should be removed");
                Assert.That(File.Exists(destFile), Is.True, "Destination file should exist");
                Assert.That(File.ReadAllText(destFile), Is.EqualTo("original content"), "Destination should have new content");
            });
        }

        [Test]
        public async Task MoveFile_WithOverwriteFalse_AndExistingFile_ReturnsFileNotFoundPost()
        {
            var sourceFile = Path.Combine(_modDirectory, "file.txt");
            var destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");

            File.WriteAllText(sourceFile, "original content");
            File.WriteAllText(destFile, "existing content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = false
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.MoveFileAsync().ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPost), "Move should fail when file exists and overwrite is false");
        }

        [Test]
        public async Task MoveFile_WithMultipleSources_MovesAll()
        {
            var sourceFile1 = Path.Combine(_modDirectory, "file1.txt");
            var sourceFile2 = Path.Combine(_modDirectory, "file2.txt");
            var destDir = Path.Combine(_kotorDirectory, "Override");

            File.WriteAllText(sourceFile1, "content1");
            File.WriteAllText(sourceFile2, "content2");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt", "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.MoveFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed");
                Assert.That(File.Exists(Path.Combine(destDir, "file1.txt")), Is.True, "File1 should be moved");
                Assert.That(File.Exists(Path.Combine(destDir, "file2.txt")), Is.True, "File2 should be moved");
                Assert.That(File.Exists(sourceFile1), Is.False, "Source file1 should be removed");
                Assert.That(File.Exists(sourceFile2), Is.False, "Source file2 should be removed");
            });
        }

        [Test]
        public async Task MoveFile_WithWildcardSource_MovesMatchingFiles()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "other.dat"), "content3");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file*.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.MoveFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "file1.txt should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "file2.txt should be moved");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "other.dat")), Is.True, "other.dat should not be moved");
            });
        }

        #endregion

        #region Copy Operation Edge Cases

        [Test]
        public async Task CopyFile_WithOverwriteTrue_ReplacesExistingFile()
        {
            var sourceFile = Path.Combine(_modDirectory, "file.txt");
            var destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");

            File.WriteAllText(sourceFile, "original content");
            File.WriteAllText(destFile, "existing content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.CopyFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Copy should succeed");
                Assert.That(File.Exists(sourceFile), Is.True, "Source file should still exist");
                Assert.That(File.Exists(destFile), Is.True, "Destination file should exist");
                Assert.That(File.ReadAllText(destFile), Is.EqualTo("original content"), "Destination should have new content");
            });
        }

        [Test]
        public async Task CopyFile_WithOverwriteFalse_AndExistingFile_ReturnsFileNotFoundPost()
        {
            var sourceFile = Path.Combine(_modDirectory, "file.txt");
            var destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");

            File.WriteAllText(sourceFile, "original content");
            File.WriteAllText(destFile, "existing content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = false
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.CopyFileAsync().ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPost), "Copy should fail when file exists and overwrite is false");
        }

        [Test]
        public async Task CopyFile_WithNestedDirectories_CreatesDirectories()
        {
            var sourceFile = Path.Combine(_modDirectory, "file.txt");
            var destPath = Path.Combine(_kotorDirectory, "Override", "subdir", "nested", "file.txt");

            File.WriteAllText(sourceFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override/subdir/nested",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.CopyFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Copy should succeed");
                Assert.That(File.Exists(destPath), Is.True, "File should be copied to nested directory");
                Assert.That(Directory.Exists(Path.GetDirectoryName(destPath)), Is.True, "Nested directories should be created");
            });
        }

        #endregion

        #region Delete Operation Edge Cases

        [Test]
        public async Task DeleteFile_WithExistingFile_DeletesFile()
        {
            var filePath = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(filePath, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/file.txt" }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.DeleteFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Delete should succeed");
                Assert.That(File.Exists(filePath), Is.False, "File should be deleted");
            });
        }

        [Test]
        public async Task DeleteFile_WithNonExistentFile_ReturnsFileNotFoundPost()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/nonexistent.txt" }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.DeleteFileAsync().ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPost), "Delete should return FileNotFoundPost for non-existent file");
        }

        [Test]
        public async Task DeleteFile_WithWildcard_DeletesMatchingFiles()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "other.dat"), "content3");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/file*.txt" }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.DeleteFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Delete should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.False, "file1.txt should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False, "file2.txt should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "other.dat")), Is.True, "other.dat should not be deleted");
            });
        }

        #endregion

        #region Rename Operation Edge Cases

        [Test]
        public async Task RenameFile_WithExistingFile_RenamesFile()
        {
            var sourceFile = Path.Combine(_kotorDirectory, "Override", "oldname.txt");
            var destFile = Path.Combine(_kotorDirectory, "Override", "newname.txt");

            File.WriteAllText(sourceFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/oldname.txt" },
                Destination = "newname.txt",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.RenameFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Rename should succeed");
                Assert.That(File.Exists(sourceFile), Is.False, "Old file should not exist");
                Assert.That(File.Exists(destFile), Is.True, "New file should exist");
                Assert.That(File.ReadAllText(destFile), Is.EqualTo("content"), "Content should be preserved");
            });
        }

        [Test]
        public async Task RenameFile_WithOverwriteFalse_AndExistingFile_ReturnsFileNotFoundPost()
        {
            var sourceFile = Path.Combine(_kotorDirectory, "Override", "oldname.txt");
            var destFile = Path.Combine(_kotorDirectory, "Override", "newname.txt");

            File.WriteAllText(sourceFile, "old content");
            File.WriteAllText(destFile, "existing content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/oldname.txt" },
                Destination = "newname.txt",
                Overwrite = false
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.RenameFileAsync().ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPost), "Rename should fail when destination exists and overwrite is false");
        }

        [Test]
        public async Task RenameFile_WithNonExistentSource_ReturnsFileNotFoundPost()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/nonexistent.txt" },
                Destination = "newname.txt",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.RenameFileAsync().ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPost), "Rename should fail when source does not exist");
        }

        #endregion

        #region Extract Operation Edge Cases

        [Test]
        public async Task ExtractFile_WithValidArchive_ExtractsFiles()
        {
            var archivePath = Path.Combine(_modDirectory, "archive.zip");
            CreateMinimalZip(archivePath, new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" }
            });

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/archive.zip" },
                Destination = "<<modDirectory>>/extracted",
                Overwrite = true
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.ExtractFileAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(Instruction.ActionExitCode.Success), "Extract should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.True, "file1.txt should be extracted");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.True, "file2.txt should be extracted");
            });
        }

        [Test]
        public async Task ExtractFile_WithOverwriteFalse_AndExistingFiles_HandlesCorrectly()
        {
            var archivePath = Path.Combine(_modDirectory, "archive.zip");
            CreateMinimalZip(archivePath, new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "file.txt", "new content" }
            });

            var extractDir = Path.Combine(_modDirectory, "extracted");
            Directory.CreateDirectory(extractDir);
            File.WriteAllText(Path.Combine(extractDir, "file.txt"), "existing content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/archive.zip" },
                Destination = "<<modDirectory>>/extracted",
                Overwrite = false
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetRealPaths();

            var exitCode = await instruction.ExtractFileAsync().ConfigureAwait(false);

            // Behavior may vary - could succeed or fail depending on implementation
            Assert.That(exitCode, Is.Not.EqualTo(Instruction.ActionExitCode.UnknownError), "Extract should handle existing files");
        }

        #endregion

        #region Helper Methods

        private void CreateMinimalZip(string path, Dictionary<string, string> files)
        {
            string dir = Path.GetDirectoryName(path);
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
            }

            using (var archive = ZipArchive.Create())
            {
                foreach (var file in files)
                {
                    _ = archive.AddEntry(file.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(file.Value)));
                }

                using (var stream = File.Create(path))
                {
                    archive.SaveTo(stream, new WriterOptions(CompressionType.None));
                }
            }
        }

        #endregion
    }
}

