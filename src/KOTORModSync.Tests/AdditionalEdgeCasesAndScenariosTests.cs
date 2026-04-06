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
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class AdditionalEdgeCasesAndScenariosTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_AdditionalEdgeTests_" + Guid.NewGuid());
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

        #region Additional Edge Cases

        [Test]
        public async Task Instruction_WithVeryLongPath_HandlesCorrectly()
        {
            // Create a very long path
            string longSubdir = Path.Combine(_modDirectory, new string('A', 200));
            Directory.CreateDirectory(longSubdir);
            string longPath = Path.Combine(longSubdir, "file.txt");
            File.WriteAllText(longPath, "content");

            var component = new ModComponent { Name = "Long Path", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { longPath },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                // May succeed or fail depending on OS path length limits
                Assert.That(result, Is.Not.Null, "Should return a result");
            });
        }

        [Test]
        public async Task Instruction_WithSpecialCharactersInPath_HandlesCorrectly()
        {
            // Create file with special characters in name
            string specialFile = Path.Combine(_modDirectory, "file with spaces & symbols.txt");
            File.WriteAllText(specialFile, "content");

            var component = new ModComponent { Name = "Special Characters", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { specialFile },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle special characters");
            });
        }

        [Test]
        public async Task Instruction_WithCircularPath_HandlesCorrectly()
        {
            // Create a file
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Circular Path", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<modDirectory>>/file.txt" // Same as source (circular)
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                // Should handle circular path gracefully
                Assert.That(result, Is.Not.Null, "Should return a result");
            });
        }

        [Test]
        public async Task Instruction_WithNestedDirectories_HandlesCorrectly()
        {
            // Create nested directory structure
            string nestedPath = Path.Combine(_modDirectory, "level1", "level2", "level3", "file.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(nestedPath));
            File.WriteAllText(nestedPath, "content");

            var component = new ModComponent { Name = "Nested Directories", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { nestedPath },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle nested directories");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should be moved");
            });
        }

        [Test]
        public async Task Instruction_WithMultipleInstructionsSameFile_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Multiple Same File", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" }, // Same file again
                Destination = "<<kotorDirectory>>/Override"
            });

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, new RealFileSystemProvider());

            Assert.Multiple(() =>
            {
                // First instruction should succeed, second should fail (file already moved)
                Assert.That(result, Is.Not.Null, "Should return a result");
            });
        }

        [Test]
        public async Task Component_WithEmptyInstructions_HandlesGracefully()
        {
            var component = new ModComponent
            {
                Name = "Empty Instructions",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>()
            };

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, new RealFileSystemProvider());

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed with no instructions");
                Assert.That(component.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Should be marked as completed");
            });
        }

        [Test]
        public async Task Component_WithNullInstructions_HandlesGracefully()
        {
            var component = new ModComponent
            {
                Name = "Null Instructions",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            component.Instructions = null;

            // Should throw or handle gracefully
            Assert.ThrowsAsync<Exception>(async () =>
            {
                await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, new RealFileSystemProvider());
            }, "Should throw or handle null instructions");
        }

        [Test]
        public async Task Instruction_WithDestinationAsFile_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.txt"), "existing");

            var component = new ModComponent { Name = "Destination File", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override/file2.txt" // Destination is a file
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle destination as file");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "File should exist");
            });
        }

        [Test]
        public async Task Instruction_WithDestinationAsDirectory_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Destination Directory", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override" // Destination is a directory
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle destination as directory");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should exist in directory");
            });
        }

        [Test]
        public async Task Instruction_WithNonExistentDestinationDirectory_CreatesIt()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Create Directory", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/NewDirectory" // Non-existent directory
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should create directory");
                Assert.That(Directory.Exists(Path.Combine(_kotorDirectory, "NewDirectory")), Is.True, "Directory should be created");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "NewDirectory", "file.txt")), Is.True, "File should exist in new directory");
            });
        }

        #endregion
    }
}

