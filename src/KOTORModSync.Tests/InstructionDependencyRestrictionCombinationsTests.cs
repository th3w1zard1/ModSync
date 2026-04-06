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
    public sealed class InstructionDependencyRestrictionCombinationsTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_InstDepRest_" + Guid.NewGuid());
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

        #region Instruction-Level Dependencies

        [Test]
        public async Task InstructionDependency_RequiredModSelected_ExecutesInstruction()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent { Name = "Required Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Dependent Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { requiredMod.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Instruction should execute when dependency is selected");
            });
        }

        [Test]
        public async Task InstructionDependency_RequiredModNotSelected_SkipsInstruction()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent { Name = "Required Mod", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Dependent Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { requiredMod.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (skip is not failure)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Instruction should be skipped when dependency is not selected");
            });
        }

        [Test]
        public async Task InstructionDependency_MultipleDependencies_AllMustBeSelected()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var mod1 = new ModComponent { Name = "Mod 1", Guid = Guid.NewGuid(), IsSelected = true };
            var mod2 = new ModComponent { Name = "Mod 2", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Dependent Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { mod1.Guid, mod2.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { mod1, mod2, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Instruction should execute when all dependencies are selected");
            });
        }

        [Test]
        public async Task InstructionDependency_OneDependencyMissing_SkipsInstruction()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var mod1 = new ModComponent { Name = "Mod 1", Guid = Guid.NewGuid(), IsSelected = true };
            var mod2 = new ModComponent { Name = "Mod 2", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Dependent Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { mod1.Guid, mod2.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { mod1, mod2, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Instruction should be skipped when one dependency is missing");
            });
        }

        #endregion

        #region Instruction-Level Restrictions

        [Test]
        public async Task InstructionRestriction_RestrictedModNotSelected_ExecutesInstruction()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var restrictedMod = new ModComponent { Name = "Restricted Mod", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Restricting Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { restrictedMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Instruction should execute when restriction is not selected");
            });
        }

        [Test]
        public async Task InstructionRestriction_RestrictedModSelected_SkipsInstruction()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var restrictedMod = new ModComponent { Name = "Restricted Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Restricting Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { restrictedMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Instruction should be skipped when restriction is selected");
            });
        }

        [Test]
        public async Task InstructionRestriction_MultipleRestrictions_AnySelectedSkips()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var mod1 = new ModComponent { Name = "Mod 1", Guid = Guid.NewGuid(), IsSelected = false };
            var mod2 = new ModComponent { Name = "Mod 2", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Restricting Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { mod1.Guid, mod2.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { mod1, mod2, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Instruction should be skipped when any restriction is selected");
            });
        }

        #endregion

        #region Combined Dependencies and Restrictions

        [Test]
        public async Task InstructionCombined_DependencyAndRestriction_BothMustBeSatisfied()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent { Name = "Required", Guid = Guid.NewGuid(), IsSelected = true };
            var restrictedMod = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Combined Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { requiredMod.Guid },
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, restrictedMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Instruction should execute when dependency is met and restriction is not");
            });
        }

        [Test]
        public async Task InstructionCombined_DependencyMetButRestrictionSelected_Skips()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent { Name = "Required", Guid = Guid.NewGuid(), IsSelected = true };
            var restrictedMod = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Combined Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { requiredMod.Guid },
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, restrictedMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Instruction should be skipped when restriction is selected (even if dependency is met)");
            });
        }

        #endregion

        #region Option Dependencies and Restrictions

        [Test]
        public async Task InstructionOptionDependency_RequiredOptionSelected_Executes()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Option Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Option", Guid = Guid.NewGuid(), IsSelected = true };
            component.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { option.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Instruction should execute when option dependency is selected");
            });
        }

        [Test]
        public async Task InstructionOptionRestriction_RestrictedOptionNotSelected_Executes()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Option Restriction", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Option", Guid = Guid.NewGuid(), IsSelected = false };
            component.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { option.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Instruction should execute when option restriction is not selected");
            });
        }

        #endregion

        #region Multiple Instructions with Different Dependencies

        [Test]
        public async Task MultipleInstructions_DifferentDependencies_ExecutesSelectively()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            var mod1 = new ModComponent { Name = "Mod 1", Guid = Guid.NewGuid(), IsSelected = true };
            var mod2 = new ModComponent { Name = "Mod 2", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Multiple Instructions", Guid = Guid.NewGuid(), IsSelected = true };

            // Instruction 1: Requires mod1 (selected)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { mod1.Guid }
            });

            // Instruction 2: Requires mod2 (not selected)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { mod2.Guid }
            });

            // Instruction 3: No dependencies
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { mod1, mod2, component };

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "Instruction 1 should execute (dependency met)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False,
                    "Instruction 2 should be skipped (dependency not met)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True,
                    "Instruction 3 should execute (no dependencies)");
            });
        }

        #endregion
    }
}

