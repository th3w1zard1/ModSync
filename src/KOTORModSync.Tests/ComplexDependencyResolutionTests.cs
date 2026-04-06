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
    public sealed class ComplexDependencyResolutionTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComplexDependencyTests_" + Guid.NewGuid());
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

        #region Complex Dependency Chains

        [Test]
        public async Task DependencyChain_FiveLevelChain_ExecutesInCorrectOrder()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid }, IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modB.Guid }, IsSelected = true };
            var modD = new ModComponent { Name = "Mod D", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modC.Guid }, IsSelected = true };
            var modE = new ModComponent { Name = "Mod E", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modD.Guid }, IsSelected = true };

            // Create files for each mod
            for (int i = 0; i < 5; i++)
            {
                File.WriteAllText(Path.Combine(_modDirectory, $"mod{(char)('A' + i)}.txt"), $"mod{(char)('A' + i)}");
            }

            var mods = new[] { modA, modB, modC, modD, modE };
            for (int i = 0; i < mods.Length; i++)
            {
                mods[i].Instructions.Add(new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { $"<<modDirectory>>/mod{(char)('A' + i)}.txt" },
                    Destination = "<<kotorDirectory>>/Override"
                });
            }

            var components = new List<ModComponent> { modE, modD, modC, modB, modA }; // Wrong order
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
                Assert.That(executionOrder[0], Is.EqualTo("Mod A"), "Mod A should be first");
                Assert.That(executionOrder[1], Is.EqualTo("Mod B"), "Mod B should be second");
                Assert.That(executionOrder[2], Is.EqualTo("Mod C"), "Mod C should be third");
                Assert.That(executionOrder[3], Is.EqualTo("Mod D"), "Mod D should be fourth");
                Assert.That(executionOrder[4], Is.EqualTo("Mod E"), "Mod E should be fifth");
            });
        }

        [Test]
        public async Task DependencyChain_DiamondPattern_ExecutesCorrectly()
        {
            // A -> B, C -> D (B and C both depend on A, D depends on both B and C)
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid }, IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid }, IsSelected = true };
            var modD = new ModComponent { Name = "Mod D", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modB.Guid, modC.Guid }, IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "modA.txt"), "A");
            File.WriteAllText(Path.Combine(_modDirectory, "modB.txt"), "B");
            File.WriteAllText(Path.Combine(_modDirectory, "modC.txt"), "C");
            File.WriteAllText(Path.Combine(_modDirectory, "modD.txt"), "D");

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

            modD.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modD.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { modD, modC, modB, modA };
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

            foreach (var mod in ordered)
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should install");
            }

            Assert.Multiple(() =>
            {
                int indexA = ordered.FindIndex(c => c.Guid == modA.Guid);
                int indexB = ordered.FindIndex(c => c.Guid == modB.Guid);
                int indexC = ordered.FindIndex(c => c.Guid == modC.Guid);
                int indexD = ordered.FindIndex(c => c.Guid == modD.Guid);

                Assert.That(indexA, Is.LessThan(indexB), "A should come before B");
                Assert.That(indexA, Is.LessThan(indexC), "A should come before C");
                Assert.That(indexB, Is.LessThan(indexD), "B should come before D");
                Assert.That(indexC, Is.LessThan(indexD), "C should come before D");
            });
        }

        #endregion

        #region InstallAfter/InstallBefore Combinations

        [Test]
        public async Task InstallAfter_ChainOfThree_RespectsOrder()
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
        public async Task InstallBefore_MultipleTargets_RespectsOrder()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), InstallBefore = new List<Guid> { modA.Guid, modB.Guid }, IsSelected = true };

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
                Assert.That(executionOrder[0], Is.EqualTo("Mod C"), "C should be first (InstallBefore A and B)");
                Assert.That(executionOrder.IndexOf("Mod C"), Is.LessThan(executionOrder.IndexOf("Mod A")), "C should come before A");
                Assert.That(executionOrder.IndexOf("Mod C"), Is.LessThan(executionOrder.IndexOf("Mod B")), "C should come before B");
            });
        }

        #endregion

        #region Mixed Dependency Types

        [Test]
        public async Task MixedDependencies_DependenciesAndInstallAfter_RespectsBoth()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid }, IsSelected = true };
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

            foreach (var mod in ordered)
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should install");
            }

            Assert.Multiple(() =>
            {
                int indexA = ordered.FindIndex(c => c.Guid == modA.Guid);
                int indexB = ordered.FindIndex(c => c.Guid == modB.Guid);
                int indexC = ordered.FindIndex(c => c.Guid == modC.Guid);

                Assert.That(indexA, Is.LessThan(indexB), "A should come before B (dependency)");
                Assert.That(indexB, Is.LessThan(indexC), "B should come before C (InstallAfter)");
            });
        }

        [Test]
        public async Task MixedDependencies_DependenciesAndRestrictions_HandlesCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = false }; // Not selected
            var modC = new ModComponent
            {
                Name = "Mod C",
                Guid = Guid.NewGuid(),
                Dependencies = new List<Guid> { modA.Guid },
                Restrictions = new List<Guid> { modB.Guid },
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "modA.txt"), "A");
            File.WriteAllText(Path.Combine(_modDirectory, "modC.txt"), "C");

            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA.txt" },
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

            // Execute only selected mods
            foreach (var mod in ordered.Where(c => c.IsSelected))
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should install");
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modA.txt")), Is.True, "Mod A should install");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modC.txt")), Is.True,
                    "Mod C should install (dependency met, restriction not active)");
            });
        }

        #endregion

        #region Option Dependencies in Complex Scenarios

        [Test]
        public async Task OptionDependencies_NestedOptionDependencies_RespectsChain()
        {
            var component = new ModComponent { Name = "Nested Options", Guid = Guid.NewGuid(), IsSelected = true };
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option
            {
                Name = "Option 2",
                Guid = Guid.NewGuid(),
                Dependencies = new List<Guid> { option1.Guid },
                IsSelected = true
            };
            var option3 = new Option
            {
                Name = "Option 3",
                Guid = Guid.NewGuid(),
                Dependencies = new List<Guid> { option2.Guid, depComponent.Guid },
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");
            File.WriteAllText(Path.Combine(_modDirectory, "option2.txt"), "option2");
            File.WriteAllText(Path.Combine(_modDirectory, "option3.txt"), "option3");

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

            option3.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);
            component.Options.Add(option3);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString(), option3.Guid.ToString() }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Nested options should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "Option 1 should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.True,
                    "Option 2 should execute (depends on option1)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option3.txt")), Is.True,
                    "Option 3 should execute (depends on option2 and component)");
            });
        }

        #endregion
    }
}

