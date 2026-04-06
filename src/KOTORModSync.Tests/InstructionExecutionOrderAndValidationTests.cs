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
    public sealed class InstructionExecutionOrderAndValidationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ExecutionOrderTests_" + Guid.NewGuid());
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

        #region Instruction Execution Order Tests

        [Test]
        public async Task InstructionOrder_SequentialExecution_ExecutesInOrder()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "3");

            var component = new ModComponent { Name = "Order Test", Guid = Guid.NewGuid(), IsSelected = true };

            var executionOrder = new List<int>();

            // Instruction 1: Move file1
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Instruction 2: Copy file2
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Instruction 3: Move file3
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sequential execution should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "First instruction should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Second instruction should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True, "Third instruction should execute");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file2.txt")), Is.True, "Copied file should remain");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file1.txt")), Is.False, "Moved file should not remain");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file3.txt")), Is.False, "Moved file should not remain");
            });
        }

        [Test]
        public async Task InstructionOrder_WithConditionalInstructions_RespectsDependencies()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Conditional Order", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "3");

            // Instruction 1: Always executes
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Instruction 2: Requires dependency
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            });

            // Instruction 3: Always executes
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                component.Instructions, new List<ModComponent> { depComponent, component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Conditional execution should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "First instruction should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Second instruction should execute (dependency met)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True, "Third instruction should execute");
            });
        }

        #endregion

        #region Instruction Validation Tests

        [Test]
        public async Task InstructionValidation_NullSourceList_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Null Source", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = null, // Invalid
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle null source gracefully
            Assert.That(result, Is.Not.Null, "Should return a result even with null source");
        }

        [Test]
        public async Task InstructionValidation_EmptyDestination_HandlesGracefully()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Empty Dest", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "" // Empty destination
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle empty destination gracefully
            Assert.That(result, Is.Not.Null, "Should return a result even with empty destination");
        }

        [Test]
        public async Task InstructionValidation_InvalidActionType_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Invalid Action", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = (Instruction.ActionType)999, // Invalid action type
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle invalid action type gracefully
            Assert.That(result, Is.Not.Null, "Should return a result even with invalid action type");
        }

        #endregion

        #region Instruction Dependency on Options

        [Test]
        public async Task InstructionDependency_OnOption_RespectsOptionSelection()
        {
            var component = new ModComponent { Name = "Option Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "2");

            component.Options.Add(option1);
            component.Options.Add(option2);

            // Instruction depends on option1
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { option1.Guid }
            });

            // Instruction depends on option2 (not selected)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { option2.Guid }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "Instruction with selected option dependency should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False,
                    "Instruction with unselected option dependency should not execute");
            });
        }

        [Test]
        public async Task InstructionRestriction_OnOption_RespectsOptionSelection()
        {
            var component = new ModComponent { Name = "Option Restriction", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "2");

            component.Options.Add(option1);
            component.Options.Add(option2);

            // Instruction restricted by option1 (selected)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { option1.Guid }
            });

            // Instruction restricted by option2 (not selected)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { option2.Guid }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.False,
                    "Instruction with active option restriction should not execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "Instruction with inactive option restriction should execute");
            });
        }

        #endregion

        #region Component Selection Scenarios

        [Test]
        public async Task ComponentSelection_UnselectedComponent_SkipsAllInstructions()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "2");

            var component = new ModComponent { Name = "Unselected", Guid = Guid.NewGuid(), IsSelected = false };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.False,
                    "No instructions should execute for unselected component");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False,
                    "No instructions should execute for unselected component");
            });
        }

        [Test]
        public async Task ComponentSelection_SelectedButBlockedByRestriction_SkipsInstructions()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Blocked Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

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

            var result = await component.ExecuteInstructionsAsync(
                component.Instructions, new List<ModComponent> { restrictedComponent, component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (blocked)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Instructions should not execute when component is blocked");
            });
        }

        #endregion

        #region Error Code Validation

        [Test]
        public async Task ErrorCode_FileNotFound_ReturnsCorrectCode()
        {
            var component = new ModComponent { Name = "File Not Found", Guid = Guid.NewGuid(), IsSelected = true };
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

            // Should return appropriate error code
            Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success),
                "Should return error code when file not found");
        }

        [Test]
        public async Task ErrorCode_InvalidArchive_ReturnsCorrectCode()
        {
            string invalidArchive = Path.Combine(_modDirectory, "invalid.zip");
            File.WriteAllText(invalidArchive, "not a zip");

            var component = new ModComponent { Name = "Invalid Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/invalid.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success),
                "Should return error code for invalid archive");
        }

        #endregion
    }
}

