// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Installation;
using KOTORModSync.Core.Services.FileSystem;
using NUnit.Framework;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class ComponentValidationComprehensiveTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComponentValidationTests_" + Guid.NewGuid());
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

        #region Component Selection Validation

        [Test]
        public async Task ComponentSelection_SelectedWithUnmetDependency_BlocksInstallation()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent
            {
                Name = "Dependent Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depComponent.Guid }
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
                new List<ModComponent> { depComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (blocked)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Component should be blocked when dependency not met");
            });
        }

        [Test]
        public async Task ComponentSelection_SelectedWithActiveRestriction_BlocksInstallation()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Restricted Component",
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
                new List<ModComponent> { restrictedComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (blocked)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Component should be blocked when restriction is active");
            });
        }

        #endregion

        #region Component Ordering Validation

        [Test]
        public async Task ComponentOrdering_InstallAfterChain_RespectsOrder()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), InstallAfter = new List<Guid> { modA.Guid }, IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), InstallAfter = new List<Guid> { modB.Guid }, IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "modA.txt"), "A");
            File.WriteAllText(Path.Combine(_modDirectory, "modB.txt"), "B");
            File.WriteAllText(Path.Combine(_modDirectory, "modC.txt"), "C");

            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modB.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modC.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modC.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var mod in ordered)
            {
                foreach (var instruction in mod.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(mod);
                }
            }

            var executionOrder = new List<string>();
            foreach (var mod in ordered)
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                executionOrder.Add(mod.Name);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should install");
            }

            Assert.Multiple(() =>
            {
                Assert.That(executionOrder[0], Is.EqualTo("Mod A"), "A should be first");
                Assert.That(executionOrder[1], Is.EqualTo("Mod B"), "B should be second");
                Assert.That(executionOrder[2], Is.EqualTo("Mod C"), "C should be third");
            });
        }

        [Test]
        public async Task ComponentOrdering_InstallBeforeChain_RespectsOrder()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), InstallBefore = new List<Guid> { Guid.NewGuid() }, IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), InstallBefore = new List<Guid> { modA.Guid }, IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), InstallBefore = new List<Guid> { modB.Guid }, IsSelected = true };

            // Fix modA's InstallBefore to reference modB
            modA.InstallBefore[0] = modB.Guid;

            File.WriteAllText(Path.Combine(_modDirectory, "modA.txt"), "A");
            File.WriteAllText(Path.Combine(_modDirectory, "modB.txt"), "B");
            File.WriteAllText(Path.Combine(_modDirectory, "modC.txt"), "C");

            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modB.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modC.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modC.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { modA, modB, modC };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var mod in ordered)
            {
                foreach (var instruction in mod.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(mod);
                }
            }

            var executionOrder = new List<string>();
            foreach (var mod in ordered)
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                executionOrder.Add(mod.Name);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should install");
            }

            Assert.Multiple(() =>
            {
                Assert.That(executionOrder[0], Is.EqualTo("Mod C"), "C should be first (InstallBefore B)");
                Assert.That(executionOrder[1], Is.EqualTo("Mod B"), "B should be second (InstallBefore A)");
                Assert.That(executionOrder[2], Is.EqualTo("Mod A"), "A should be third");
            });
        }

        #endregion

        #region Option Validation

        [Test]
        public async Task OptionValidation_MutuallyExclusiveOptions_OnlyOneCanBeSelected()
        {
            var component = new ModComponent { Name = "Exclusive Options", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { Guid.NewGuid() } // Will be set to option2
            };
            var option2 = new Option
            {
                Name = "Option 2",
                Guid = Guid.NewGuid(),
                IsSelected = false,
                Restrictions = new List<Guid> { option1.Guid }
            };

            // Make them mutually exclusive
            option1.Restrictions[0] = option2.Guid;

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");
            File.WriteAllText(Path.Combine(_modDirectory, "option2.txt"), "option2");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString() }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }
            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Exclusive options should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "Selected option should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.False,
                    "Restricted option should not execute");
            });
        }

        [Test]
        public async Task OptionValidation_OptionWithComponentDependency_RespectsDependency()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Option Dependency", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }
            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { depComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Option dependency should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "Option should execute when dependency is met");
            });
        }

        #endregion

        #region Component State Validation

        [Test]
        public async Task ComponentState_UnselectedComponent_SkipsAllInstructions()
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Unselected should succeed (skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.False,
                    "No instructions should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False,
                    "No instructions should execute");
            });
        }

        #endregion
    }
}

