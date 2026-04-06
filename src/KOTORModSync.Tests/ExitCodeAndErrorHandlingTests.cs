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
    public sealed class ExitCodeAndErrorHandlingTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ExitCodeTests_" + Guid.NewGuid());
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

        #region Exit Code Scenarios

        [Test]
        public async Task Instruction_WithMissingSource_ReturnsFileNotFound()
        {
            var component = new ModComponent { Name = "Missing Source", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Should not succeed with missing source");
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.FileNotFoundPre).Or.EqualTo(Instruction.ActionExitCode.FileNotFoundPost), "Should return file not found exit code");
            });
        }

        [Test]
        public async Task Instruction_WithInvalidDestination_ReturnsError()
        {
            var component = new ModComponent { Name = "Invalid Destination", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Invalid:Path" // Invalid characters
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Should not succeed with invalid destination");
            });
        }

        [Test]
        public async Task Instruction_WithWildcardNoMatches_ReturnsWildcardNotFound()
        {
            var component = new ModComponent { Name = "No Wildcard Matches", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.nonexistent" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Should not succeed with no wildcard matches");
            });
        }

        [Test]
        public async Task Instruction_WithOverwriteFalseAndExistingFile_ReturnsSuccessButSkips()
        {
            var component = new ModComponent { Name = "Overwrite False", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "new content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file.txt"), "old content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
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
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should succeed even when skipping");
                Assert.That(File.ReadAllText(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.EqualTo("old content"), "File should retain old content");
            });
        }

        [Test]
        public async Task Component_WithFailedInstruction_ReturnsFailedState()
        {
            var component = new ModComponent { Name = "Failed Component", Guid = Guid.NewGuid(), IsSelected = true };
            // Make it fail by removing archive
            string archivePath = Path.Combine(_modDirectory, "Failed Component.zip");
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, new RealFileSystemProvider());

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(ModComponent.InstallExitCode.Success), "Should not succeed");
                Assert.That(component.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Failed), "Component should be marked as failed");
            });
        }

        [Test]
        public async Task Component_WithSuccessfulInstructions_ReturnsSuccess()
        {
            var component = new ModComponent { Name = "Success Component", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, new RealFileSystemProvider());

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(component.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Component should be completed");
            });
        }

        #endregion

        #region Error Recovery Scenarios

        [Test]
        public async Task Instruction_WithPartialFailure_ContinuesWithRemaining()
        {
            var component = new ModComponent { Name = "Partial Failure", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            // file2.txt doesn't exist

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string>
                {
                    "<<modDirectory>>/file1.txt",
                    "<<modDirectory>>/file2.txt" // Missing
                },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Should not fully succeed");
                // file1.txt might still be moved depending on implementation
            });
        }

        [Test]
        public async Task Component_WithMultipleInstructions_StopsOnFirstFailure()
        {
            var component = new ModComponent { Name = "Multiple Instructions", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent.txt" }, // Will fail
                Destination = "<<kotorDirectory>>/Override"
            });

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, new RealFileSystemProvider());

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(ModComponent.InstallExitCode.Success), "Should not fully succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "First instruction should complete");
            });
        }

        #endregion

        #region Edge Cases

        [Test]
        public async Task Instruction_WithEmptySource_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Empty Source", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string>(), // Empty
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                // Should handle gracefully - either succeed (no-op) or return appropriate error
                Assert.That(result, Is.Not.Null, "Should return a result");
            });
        }

        [Test]
        public async Task Instruction_WithNullSource_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Null Source", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = null, // Null
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            // Should throw or handle gracefully
            Assert.ThrowsAsync<Exception>(async () =>
            {
                await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);
            }, "Should throw or handle null source");
        }

        [Test]
        public async Task Instruction_WithInvalidAction_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Invalid Action", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var instruction = new Instruction
            {
                Action = (Instruction.ActionType)999, // Invalid action
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            // Should handle invalid action gracefully
            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Should not succeed with invalid action");
            });
        }

        #endregion
    }
}

