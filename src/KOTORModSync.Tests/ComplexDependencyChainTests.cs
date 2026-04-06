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
    public sealed class ComplexDependencyChainTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComplexDeps_" + Guid.NewGuid());
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

        #region Complex Dependency Chains

        [Test]
        public async Task ComplexDependencyChain_FiveLevelDeep_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent
            {
                Name = "Mod B",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid }
            };
            var modC = new ModComponent
            {
                Name = "Mod C",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modB.Guid }
            };
            var modD = new ModComponent
            {
                Name = "Mod D",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modC.Guid }
            };
            var modE = new ModComponent
            {
                Name = "Mod E",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modD.Guid }
            };

            var components = new List<ModComponent> { modE, modD, modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered.Count, Is.EqualTo(5), "Should contain all 5 mods");
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A should be first");
                Assert.That(ordered[1].Guid, Is.EqualTo(modB.Guid), "Mod B should be second");
                Assert.That(ordered[2].Guid, Is.EqualTo(modC.Guid), "Mod C should be third");
                Assert.That(ordered[3].Guid, Is.EqualTo(modD.Guid), "Mod D should be fourth");
                Assert.That(ordered[4].Guid, Is.EqualTo(modE.Guid), "Mod E should be fifth");
            });
        }

        [Test]
        public async Task ComplexDependencyChain_MultipleDependenciesPerMod_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };
            var modC = new ModComponent
            {
                Name = "Mod C",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid, modB.Guid }
            };
            var modD = new ModComponent
            {
                Name = "Mod D",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid }
            };
            var modE = new ModComponent
            {
                Name = "Mod E",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modC.Guid, modD.Guid }
            };

            var components = new List<ModComponent> { modE, modD, modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered.Count, Is.EqualTo(5), "Should contain all 5 mods");
                // A and B should come before C
                Assert.That(ordered.IndexOf(modA), Is.LessThan(ordered.IndexOf(modC)),
                    "Mod A should come before Mod C");
                Assert.That(ordered.IndexOf(modB), Is.LessThan(ordered.IndexOf(modC)),
                    "Mod B should come before Mod C");
                // A should come before D
                Assert.That(ordered.IndexOf(modA), Is.LessThan(ordered.IndexOf(modD)),
                    "Mod A should come before Mod D");
                // C and D should come before E
                Assert.That(ordered.IndexOf(modC), Is.LessThan(ordered.IndexOf(modE)),
                    "Mod C should come before Mod E");
                Assert.That(ordered.IndexOf(modD), Is.LessThan(ordered.IndexOf(modE)),
                    "Mod D should come before Mod E");
            });
        }

        [Test]
        public async Task ComplexDependencyChain_InstallAfterAndBefore_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent
            {
                Name = "Mod B",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                InstallAfter = new List<Guid> { modA.Guid }
            };
            var modC = new ModComponent
            {
                Name = "Mod C",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                InstallBefore = new List<Guid> { modB.Guid }
            };
            var modD = new ModComponent
            {
                Name = "Mod D",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid },
                InstallAfter = new List<Guid> { modB.Guid }
            };

            var components = new List<ModComponent> { modD, modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered.Count, Is.EqualTo(4), "Should contain all 4 mods");
                Assert.That(ordered.IndexOf(modA), Is.LessThan(ordered.IndexOf(modB)),
                    "Mod A should come before Mod B (InstallAfter)");
                Assert.That(ordered.IndexOf(modC), Is.LessThan(ordered.IndexOf(modB)),
                    "Mod C should come before Mod B (InstallBefore)");
                Assert.That(ordered.IndexOf(modB), Is.LessThan(ordered.IndexOf(modD)),
                    "Mod B should come before Mod D (InstallAfter)");
            });
        }

        #endregion

        #region Mixed Dependency Types

        [Test]
        public async Task MixedDependencyTypes_DependenciesAndRestrictions_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = false };
            var modC = new ModComponent
            {
                Name = "Mod C",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid },
                Restrictions = new List<Guid> { modB.Guid }
            };

            var components = new List<ModComponent> { modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered.Count, Is.EqualTo(2), "Should contain only selected mods");
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A should be first");
                Assert.That(ordered[1].Guid, Is.EqualTo(modC.Guid), "Mod C should be second");
            });
        }

        [Test]
        public async Task MixedDependencyTypes_ComponentAndInstructionLevel_ExecutesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent
            {
                Name = "Mod B",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid }
            };
            var component = new ModComponent { Name = "Component", Guid = Guid.NewGuid(), IsSelected = true };

            // Component-level dependency
            component.Dependencies = new List<Guid> { modB.Guid };

            // Instruction-level dependency
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { modA.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { component, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A should be first");
                Assert.That(ordered[1].Guid, Is.EqualTo(modB.Guid), "Mod B should be second");
                Assert.That(ordered[2].Guid, Is.EqualTo(component.Guid), "Component should be third");
            });

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Instruction should execute when both component and instruction dependencies are met");
            });
        }

        #endregion

        #region Diamond Dependency Pattern

        [Test]
        public async Task DiamondDependencyPattern_CommonAncestor_OrdersCorrectly()
        {
            //     A
            //    / \
            //   B   C
            //    \ /
            //     D
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent
            {
                Name = "Mod B",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid }
            };
            var modC = new ModComponent
            {
                Name = "Mod C",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid }
            };
            var modD = new ModComponent
            {
                Name = "Mod D",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modB.Guid, modC.Guid }
            };

            var components = new List<ModComponent> { modD, modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered.Count, Is.EqualTo(4), "Should contain all 4 mods");
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A should be first");
                Assert.That(ordered.IndexOf(modB), Is.LessThan(ordered.IndexOf(modD)),
                    "Mod B should come before Mod D");
                Assert.That(ordered.IndexOf(modC), Is.LessThan(ordered.IndexOf(modD)),
                    "Mod C should come before Mod D");
            });
        }

        #endregion
    }
}

