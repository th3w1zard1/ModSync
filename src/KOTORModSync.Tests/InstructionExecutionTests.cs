// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Services.FileSystem;
using KOTORModSync.Core.Utility;
using NUnit.Framework;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class InstructionExecutionTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_InstructionTests_" + Guid.NewGuid());
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

        #region Extract Instruction Tests

        [Test]
        public async Task Extract_ZipArchive_ExtractsAllFiles()
        {
            string zipPath = CreateTestZip("test.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "subdir/file2.txt", "content2" }
            });

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                Destination = "<<modDirectory>>/extracted"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Extract instruction should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.True, "First extracted file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "subdir", "file2.txt")), Is.True, "Second extracted file in subdirectory should exist");
                Assert.That(File.Exists(zipPath), Is.True, "Source archive should still exist after extraction");
                Assert.That(Directory.Exists(Path.Combine(_modDirectory, "extracted")), Is.True, "Extraction destination directory should exist");
                Assert.That(Directory.Exists(Path.Combine(_modDirectory, "extracted", "subdir")), Is.True, "Subdirectory should be created during extraction");
            });
        }

        [Test]
        public async Task Extract_7zArchive_ExtractsAllFiles()
        {
            string archivePath = CreateTest7z("test.7z", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" }
            });

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(archivePath)}" },
                Destination = "<<modDirectory>>/extracted"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Extract instruction for 7z archive should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.True, "First extracted file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.True, "Second extracted file should exist");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should still exist after extraction");
                Assert.That(Directory.Exists(Path.Combine(_modDirectory, "extracted")), Is.True, "Extraction destination directory should exist");
            });
        }

        [Test]
        public async Task Extract_MultipleArchives_ExtractsAll()
        {
            string zip1 = CreateTestZip("test1.zip", new Dictionary<string, string>(StringComparer.Ordinal) { { "file1.txt", "content1" } });
            string zip2 = CreateTestZip("test2.zip", new Dictionary<string, string>(StringComparer.Ordinal) { { "file2.txt", "content2" } });

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string>
                {
                    $"<<modDirectory>>/{Path.GetFileName(zip1)}",
                    $"<<modDirectory>>/{Path.GetFileName(zip2)}"
                },
                Destination = "<<modDirectory>>/extracted"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Extract instruction with multiple archives should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.True, "First extracted file from first archive should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.True, "Second extracted file from second archive should exist");
                Assert.That(File.Exists(zip1), Is.True, "First source archive should still exist");
                Assert.That(File.Exists(zip2), Is.True, "Second source archive should still exist");
                Assert.That(Directory.Exists(Path.Combine(_modDirectory, "extracted")), Is.True, "Extraction destination directory should exist");
            });
        }

        [Test]
        public async Task Extract_WithWildcards_ExtractsMatchingArchives()
        {
            CreateTestZip("mod1.zip", new Dictionary<string, string>(StringComparer.Ordinal) { { "file1.txt", "content1" } });
            CreateTestZip("mod2.zip", new Dictionary<string, string>(StringComparer.Ordinal) { { "file2.txt", "content2" } });
            CreateTestZip("other.rar", new Dictionary<string, string>(StringComparer.Ordinal) { { "file3.txt", "content3" } });

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/*.zip" },
                Destination = "<<modDirectory>>/extracted",
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Extract instruction with wildcards should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.True, "File from first matching archive should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.True, "File from second matching archive should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file3.txt")), Is.False, "File from non-matching archive should not exist");
                Assert.That(Directory.Exists(Path.Combine(_modDirectory, "extracted")), Is.True, "Extraction destination directory should exist");
            });
        }

        [Test]
        public async Task Extract_InvalidArchive_ReturnsError()
        {
            string invalidPath = Path.Combine(_modDirectory, "invalid.zip");
            File.WriteAllText(invalidPath, "not a zip file");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/invalid.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            var mainConfig = new MainConfig();
            mainConfig.sourcePath = _config.sourcePath;
            mainConfig.destinationPath = _config.destinationPath;

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Extract instruction with invalid archive should fail");
                Assert.That(File.Exists(invalidPath), Is.True, "Invalid archive file should still exist");
                Assert.That(Directory.Exists(Path.Combine(_modDirectory, "extracted")), Is.False, "Extraction directory should not be created for invalid archive");
            });
        }

        #endregion

        #region Move Instruction Tests

        [Test]
        public async Task Move_SingleFile_MovesToDestination()
        {
            string sourceFile = Path.Combine(_modDirectory, "source.txt");
            File.WriteAllText(sourceFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/source.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move instruction should succeed");
                Assert.That(File.Exists(sourceFile), Is.False, "Source file should not exist after move");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "source.txt")), Is.True, "File should exist at destination after move");
                Assert.That(Directory.Exists(Path.Combine(_kotorDirectory, "Override")), Is.True, "Destination directory should exist");
            });
        }

        [Test]
        public async Task Move_WithOverwriteTrue_OverwritesExisting()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "new content");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move instruction with overwrite should succeed");
                Assert.That(File.Exists(destFile), Is.True, "Destination file should exist after move with overwrite");
                Assert.That(File.ReadAllText(destFile), Is.EqualTo("new content"), "Destination file should contain new content");
                Assert.That(File.Exists(sourceFile), Is.False, "Source file should not exist after move");
            });
        }

        [Test]
        public async Task Move_WithOverwriteFalse_SkipsExisting()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "new content");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = false
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move instruction without overwrite should succeed");
                Assert.That(File.Exists(destFile), Is.True, "Destination file should still exist");
                Assert.That(File.ReadAllText(destFile), Is.EqualTo("old content"), "Destination file should retain old content when overwrite is false");
                Assert.That(File.Exists(sourceFile), Is.True, "Source file should still exist when overwrite is false");
            });
        }

        [Test]
        public async Task Move_WithWildcards_MovesAllMatching()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "other.dat"), "content3");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "other.dat")), Is.False);
        }

        #endregion

        #region Copy Instruction Tests

        [Test]
        public async Task Copy_SingleFile_CopiesToDestination()
        {
            string sourceFile = Path.Combine(_modDirectory, "source.txt");
            File.WriteAllText(sourceFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/source.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(sourceFile), Is.True);
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "source.txt")), Is.True);
        }

        [Test]
        public async Task Copy_WithOverwriteTrue_OverwritesExisting()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "new content");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.ReadAllText(destFile), Is.EqualTo("new content"));
        }

        [Test]
        public async Task Copy_WithOverwriteFalse_SkipsExisting()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "new content");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = false
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.ReadAllText(destFile), Is.EqualTo("old content"));
        }

        #endregion

        #region Rename Instruction Tests

        [Test]
        public async Task Rename_SingleFile_RenamesFile()
        {
            string sourceFile = Path.Combine(_kotorDirectory, "Override", "old.txt");
            File.WriteAllText(sourceFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" },
                Destination = "new.txt"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(sourceFile), Is.False);
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "new.txt")), Is.True);
        }

        [Test]
        public async Task Rename_WithOverwriteTrue_OverwritesExisting()
        {
            string sourceFile = Path.Combine(_kotorDirectory, "Override", "old.txt");
            File.WriteAllText(sourceFile, "new content");
            string existingFile = Path.Combine(_kotorDirectory, "Override", "new.txt");
            File.WriteAllText(existingFile, "old content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" },
                Destination = "new.txt",
                Overwrite = true
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.ReadAllText(existingFile), Is.EqualTo("new content"));
        }

        [Test]
        public async Task Rename_WithOverwriteFalse_SkipsIfExists()
        {
            string sourceFile = Path.Combine(_kotorDirectory, "Override", "old.txt");
            File.WriteAllText(sourceFile, "new content");
            string existingFile = Path.Combine(_kotorDirectory, "Override", "new.txt");
            File.WriteAllText(existingFile, "old content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" },
                Destination = "new.txt",
                Overwrite = false
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.ReadAllText(existingFile), Is.EqualTo("old content"));
            Assert.That(File.Exists(sourceFile), Is.True);
        }

        #endregion

        #region Delete Instruction Tests

        [Test]
        public async Task Delete_SingleFile_DeletesFile()
        {
            string fileToDelete = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(fileToDelete, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/file.txt" }
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(fileToDelete), Is.False);
        }

        [Test]
        public async Task Delete_WithWildcards_DeletesAllMatching()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "other.dat"), "content3");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/*.txt" }
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "other.dat")), Is.True);
        }

        [Test]
        public async Task Delete_NonExistentFile_WithOverwriteFalse_Succeeds()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/nonexistent.txt" },
                Overwrite = false
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
        }

        [Test]
        public async Task Delete_NonExistentFile_WithOverwriteTrue_ReturnsError()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/nonexistent.txt" },
                Overwrite = true
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success));
        }

        #endregion

        #region DelDuplicate Instruction Tests

        [Test]
        public async Task DelDuplicate_WithTpcAndTga_DeletesTpcWhenTgaExists()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True);
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False);
        }

        [Test]
        public async Task DelDuplicate_NoDuplicates_DoesNothing()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            };

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True);
        }

        #endregion

        #region Choose Instruction Tests

        [Test]
        public async Task Choose_WithSelectedOption_ExecutesOptionInstructions()
        {
            var component = new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid() };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid() };

            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);
            component.IsSelected = true;
            option1.IsSelected = true;
            option2.IsSelected = false;

            var chooseInstruction = new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString() }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            chooseInstruction.SetFileSystemProvider(fileSystemProvider);
            chooseInstruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.True);
        }

        [Test]
        public async Task Choose_WithNoSelectedOptions_ReturnsSuccess()
        {
            var component = new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid() };
            component.Options.Add(option1);
            component.IsSelected = true;
            option1.IsSelected = false;

            var chooseInstruction = new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            chooseInstruction.SetFileSystemProvider(fileSystemProvider);
            chooseInstruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
        }

        #endregion

        #region Dependency and Restriction Tests

        [Test]
        public async Task Instruction_WithDependency_ExecutesWhenDependencySelected()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };

            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { depComponent, component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
        }

        [Test]
        public async Task Instruction_WithDependency_SkipsWhenDependencyNotSelected()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { depComponent, component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.False);
        }

        [Test]
        public async Task Instruction_WithRestriction_SkipsWhenRestrictionSelected()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };

            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { restrictedComponent, component }, fileSystemProvider);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.False);
        }

        [Test]
        public async Task Execute_MissingExecutable_ReturnsFileNotFound()
        {
            var component = new ModComponent { Name = "Exec", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Execute,
                Source = new List<string> { "<<modDirectory>>/missing_exe.exe" },
                Arguments = ""
            };

            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);

            var fs = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fs);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fs);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPost));
        }

        [Test]
        public async Task Move_AutoExtracts_From_ResourceRegistry_WhenMissing()
        {
            // Prepare archive with missing file
            string archivePath = CreateTestZip("resource.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "missing.dat", "payload" }
            });

            // Instruction references missing.dat, should auto-extract from ResourceRegistry
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
            instruction.SetParentComponent(component);

            var fs = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fs);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fs);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "missing.dat")), Is.True);
        }

        #endregion

        #region CleanList Instruction Tests

        [Test]
        public async Task CleanList_MandatoryDeletions_DeleteFiles()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "Mandatory Deletions,old1.tga,old2.tpc");

            string old1 = Path.Combine(_kotorDirectory, "Override", "old1.tga");
            string old2 = Path.Combine(_kotorDirectory, "Override", "old2.tpc");
            File.WriteAllText(old1, "old");
            File.WriteAllText(old2, "old");

            var component = new ModComponent { Name = "Cleaner", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);

            var fs = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fs);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fs);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(old1), Is.False);
            Assert.That(File.Exists(old2), Is.False);
        }

        [Test]
        public async Task CleanList_FuzzyMatch_SelectedMod_DeletesListedFiles()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "HD UI Rewrite,ui_old.tga,ui_old.tpc");

            string uiOldTga = Path.Combine(_kotorDirectory, "Override", "ui_old.tga");
            string uiOldTpc = Path.Combine(_kotorDirectory, "Override", "ui_old.tpc");
            File.WriteAllText(uiOldTga, "old");
            File.WriteAllText(uiOldTpc, "old");

            var component = new ModComponent
            {
                Name = "HD UI Rewrite",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);
            instruction.SetParentComponent(component);

            var fs = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fs);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fs);

            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success));
            Assert.That(File.Exists(uiOldTga), Is.False);
            Assert.That(File.Exists(uiOldTpc), Is.False);
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

        private string CreateTest7z(string fileName, Dictionary<string, string> files)
        {
            string archivePath = Path.Combine(_modDirectory, fileName);
            string tempDir = Path.Combine(Path.GetTempPath(), "temp_7z_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                foreach (var kvp in files)
                {
                    string filePath = Path.Combine(tempDir, kvp.Key);
                    string fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir))
                    {
                        Directory.CreateDirectory(fileDir);
                    }
                    File.WriteAllText(filePath, kvp.Value);
                }

                string sevenZipPath = VirtualFileSystemTests.Find7Zip();
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    Arguments = $"a -t7z \"{archivePath}\" \"{Path.Combine(tempDir, "*")}\" -r",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            throw new InvalidOperationException($"7z CLI failed with exit code {process.ExitCode}");
                        }
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            return archivePath;
        }

        #endregion
    }
}

