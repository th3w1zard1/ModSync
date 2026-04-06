// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Services;
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
    public sealed class InstructionEdgeCaseTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_EdgeCaseTests_" + Guid.NewGuid());
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

        #region Path Edge Cases

        [Test]
        public async Task Move_WithSpecialCharactersInFilename_HandlesCorrectly()
        {
            string specialFileName = "file with spaces & special chars!@#.txt";
            string sourceFile = Path.Combine(_modDirectory, specialFileName);
            File.WriteAllText(sourceFile, "content");

            var component = new ModComponent { Name = "Special Chars", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{specialFileName}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed with special characters");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", specialFileName)), Is.True, "File with special characters should exist");
            });
        }

        [Test]
        public async Task Move_WithUnicodeCharactersInFilename_HandlesCorrectly()
        {
            string unicodeFileName = "测试文件_тест_テスト.txt";
            string sourceFile = Path.Combine(_modDirectory, unicodeFileName);
            File.WriteAllText(sourceFile, "content");

            var component = new ModComponent { Name = "Unicode", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{unicodeFileName}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed with Unicode characters");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", unicodeFileName)), Is.True, "File with Unicode characters should exist");
            });
        }

        [Test]
        public async Task Move_WithNestedDirectories_CreatesDirectories()
        {
            string nestedPath = Path.Combine(_modDirectory, "subdir1", "subdir2", "file.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(nestedPath));
            File.WriteAllText(nestedPath, "content");

            var component = new ModComponent { Name = "Nested", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/subdir1/subdir2/file.txt" },
                Destination = "<<kotorDirectory>>/Override/nested"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed with nested directories");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "nested", "file.txt")), Is.True, "File should exist in nested destination");
            });
        }

        [Test]
        public async Task Rename_WithWildcards_MultipleFilesRenamed()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old2.txt"), "content2");

            var component = new ModComponent { Name = "Rename Wildcard", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old*.txt" },
                Destination = "new.txt"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Rename should succeed");
                // Both files should be renamed to "new.txt" (last one wins with overwrite)
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "new.txt")), Is.True, "Renamed file should exist");
            });
        }

        #endregion

        #region Overwrite Edge Cases

        [Test]
        public async Task Copy_WithOverwriteFalse_AndReadOnlyFile_HandlesCorrectly()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "new content");

            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");
            File.SetAttributes(destFile, FileAttributes.ReadOnly);

            var component = new ModComponent { Name = "ReadOnly", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = false
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Copy should succeed even with read-only file");
                Assert.That(File.ReadAllText(destFile), Is.EqualTo("old content"), "Read-only file should not be overwritten");
            });

            // Clean up read-only attribute
            File.SetAttributes(destFile, FileAttributes.Normal);
        }

        [Test]
        public async Task Move_WithOverwriteTrue_AndReadOnlyFile_OverwritesCorrectly()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "new content");

            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");
            File.SetAttributes(destFile, FileAttributes.ReadOnly);

            var component = new ModComponent { Name = "ReadOnly Overwrite", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed");
                Assert.That(File.ReadAllText(destFile), Is.EqualTo("new content"), "Read-only file should be overwritten");
            });
        }

        #endregion

        #region Wildcard Edge Cases

        [Test]
        public async Task Move_WithQuestionMarkWildcard_MatchesSingleCharacter()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file10.txt"), "content10");

            var component = new ModComponent { Name = "Wildcard", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file?.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "file1.txt should match");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "file2.txt should match");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file10.txt")), Is.False, "file10.txt should not match (too many chars)");
            });
        }

        [Test]
        public async Task Delete_WithRecursiveWildcard_DeletesAllMatching()
        {
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override", "subdir1"));
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override", "subdir2"));

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "subdir1", "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "subdir2", "file3.txt"), "content3");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "other.dat"), "content4");

            var component = new ModComponent { Name = "Recursive Delete", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/**/*.txt" }
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Delete should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.False, "Root file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "subdir1", "file2.txt")), Is.False, "Nested file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "subdir2", "file3.txt")), Is.False, "Nested file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "other.dat")), Is.True, "Non-matching file should remain");
            });
        }

        #endregion

        #region Auto-Extraction Edge Cases

        [Test]
        public async Task Move_WithAutoExtraction_ExtractsFromArchive()
        {
            // Create archive with file
            string archivePath = CreateTestZip("resource.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "missing.dat", "payload" }
            });

            var component = new ModComponent
            {
                Name = "AutoExtract",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archivePath),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "missing.dat", true }
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/missing.dat" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move with auto-extraction should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "missing.dat")), Is.True, "File should be extracted and moved");
            });
        }

        [Test]
        public async Task Copy_WithAutoExtraction_ExtractsFromArchive()
        {
            // Create archive with file
            string archivePath = CreateTestZip("resource.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "missing.dat", "payload" }
            });

            var component = new ModComponent
            {
                Name = "AutoExtract Copy",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archivePath),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "missing.dat", true }
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/missing.dat" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Copy with auto-extraction should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "missing.dat")), Is.True, "File should be extracted and copied");
            });
        }

        #endregion

        #region Empty and Null Edge Cases

        [Test]
        public async Task Extract_WithEmptyArchive_HandlesGracefully()
        {
            string zipPath = CreateTestZip("empty.zip", new Dictionary<string, string>(StringComparer.Ordinal));

            var component = new ModComponent { Name = "Empty Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Extract should handle empty archive");
                Assert.That(Directory.Exists(Path.Combine(_modDirectory, "extracted")), Is.True, "Destination directory should exist");
            });
        }

        [Test]
        public async Task Delete_WithEmptyWildcardPattern_HandlesGracefully()
        {
            // Create files that don't match pattern
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.dat"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.dat"), "content2");

            var component = new ModComponent { Name = "Empty Pattern", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/*.txt" },
                Overwrite = false
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Delete should handle empty pattern gracefully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.dat")), Is.True, "Non-matching files should remain");
            });
        }

        #endregion

        #region Concurrent Operations Edge Cases

        [Test]
        public async Task Move_ThenCopy_SameFile_HandlesCorrectly()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "content");

            var component = new ModComponent { Name = "Move Copy", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Try to copy the file that was just moved (should fail gracefully)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should handle missing file gracefully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should exist after move");
            });
        }

        [Test]
        public async Task Delete_ThenMove_SameFile_HandlesCorrectly()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "content");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");

            var component = new ModComponent { Name = "Delete Move", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/file.txt" }
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sequence should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should exist after move");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            using (var archive = ZipArchive.Create())
            {
                foreach (var kvp in files)
                {
                    archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                }
                using (var stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new WriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion
    }
}

