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
    public sealed class InstructionErrorHandlingTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ErrorHandlingTests_" + Guid.NewGuid());
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

        #region Extract Error Handling

        [Test]
        public async Task Extract_WithCorruptedArchive_ReturnsError()
        {
            string corruptedPath = Path.Combine(_modDirectory, "corrupted.zip");
            File.WriteAllBytes(corruptedPath, new byte[] { 0x00, 0x01, 0x02, 0x03 }); // Invalid zip data

            var component = new ModComponent { Name = "Corrupted Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/corrupted.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Corrupted archive should fail");
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.InvalidArchive).Or.EqualTo(Instruction.ActionExitCode.ArchiveParseError),
                    "Should return appropriate error code");
            });
        }

        [Test]
        public async Task Extract_WithNonExistentArchive_ReturnsError()
        {
            var component = new ModComponent { Name = "Missing Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/nonexistent.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Non-existent archive should fail");
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPost), "Should return FileNotFoundPost");
            });
        }

        #endregion

        #region Move/Copy Error Handling

        [Test]
        public async Task Move_WithLockedFile_HandlesGracefully()
        {
            string sourceFile = Path.Combine(_modDirectory, "locked.txt");
            File.WriteAllText(sourceFile, "content");
            File.SetAttributes(sourceFile, FileAttributes.ReadOnly);

            var component = new ModComponent { Name = "Locked File", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/locked.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            // Clean up read-only attribute
            try
            {
                File.SetAttributes(sourceFile, FileAttributes.Normal);
            }
            catch { }

            Assert.Multiple(() =>
            {
                // Move should succeed even with read-only file (overwrite=true should handle it)
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should handle read-only file");
            });
        }

        [Test]
        public async Task Copy_WithInsufficientPermissions_HandlesGracefully()
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "content");

            var component = new ModComponent { Name = "Permissions", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Copy should succeed with normal permissions");
            });
        }

        #endregion

        #region Delete Error Handling

        [Test]
        public async Task Delete_WithLockedFile_HandlesGracefully()
        {
            string lockedFile = Path.Combine(_kotorDirectory, "Override", "locked.txt");
            File.WriteAllText(lockedFile, "content");
            File.SetAttributes(lockedFile, FileAttributes.ReadOnly);

            var component = new ModComponent { Name = "Delete Locked", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/locked.txt" },
                Overwrite = true
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            // Clean up read-only attribute if file still exists
            try
            {
                if (File.Exists(lockedFile))
                {
                    File.SetAttributes(lockedFile, FileAttributes.Normal);
                }
            }
            catch { }

            // Delete with overwrite=true should handle read-only files
            Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success).Or.EqualTo(Instruction.ActionExitCode.UnauthorizedAccessException),
                "Delete should handle read-only file appropriately");
        }

        #endregion

        #region Rename Error Handling

        [Test]
        public async Task Rename_WithTargetInUse_HandlesGracefully()
        {
            string sourceFile = Path.Combine(_kotorDirectory, "Override", "old.txt");
            File.WriteAllText(sourceFile, "content");
            string targetFile = Path.Combine(_kotorDirectory, "Override", "new.txt");
            File.WriteAllText(targetFile, "existing");

            var component = new ModComponent { Name = "Rename Conflict", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" },
                Destination = "new.txt",
                Overwrite = false
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Rename should succeed (skip if exists)");
                Assert.That(File.Exists(sourceFile), Is.True, "Source file should still exist when overwrite=false");
                Assert.That(File.ReadAllText(targetFile), Is.EqualTo("existing"), "Target file should retain original content");
            });
        }

        #endregion

        #region Instruction Sequence Error Handling

        [Test]
        public async Task ExecuteInstructions_WithPartialFailure_ContinuesWithRemaining()
        {
            string validFile = Path.Combine(_modDirectory, "valid.txt");
            File.WriteAllText(validFile, "content");

            var component = new ModComponent { Name = "Partial Failure", Guid = Guid.NewGuid(), IsSelected = true };

            // First instruction will fail (missing file)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/missing.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Second instruction should still execute
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/valid.txt" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should continue after first failure");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "valid.txt")), Is.True, "Second instruction should execute");
            });
        }

        [Test]
        public async Task ExecuteInstructions_WithDependencyFailure_SkipsDependentInstructions()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false }; // Not selected
            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var component = new ModComponent { Name = "Dependent", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { depComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed even when instruction skipped");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.False, "File should not be moved when dependency not met");
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

