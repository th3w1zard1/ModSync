// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Installation;
using KOTORModSync.Core.Services;
using KOTORModSync.Tests.TestHelpers;
using NUnit.Framework;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class RealWorldInstallationScenariosTests
    {
        private DirectoryInfo _workingDirectory;
        private MainConfig _mainConfigInstance;

        [SetUp]
        public void SetUp()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "KOTORModSync_RealWorldTests_" + Guid.NewGuid().ToString("N"));
            _workingDirectory = Directory.CreateDirectory(tempRoot);
            _ = Directory.CreateDirectory(Path.Combine(tempRoot, ModComponent.CheckpointFolderName));

            _mainConfigInstance = new MainConfig
            {
                destinationPath = _workingDirectory,
                sourcePath = _workingDirectory,
                allComponents = new List<ModComponent>(),
            };
            InstallCoordinator.ClearSessionForTests(_workingDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_workingDirectory != null && _workingDirectory.Exists)
            {
                InstallCoordinatorTestsHelper.CleanupTestDirectory(_workingDirectory);
            }
        }

        #region Real-World Mod Installation Scenarios

        [Test]
        public async Task Install_TextureModWithOptions_InstallsCorrectly()
        {
            // Simulate a texture mod with multiple options
            var textureMod = new ModComponent
            {
                Name = "HD Texture Pack",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            // Create archive with textures
            string archivePath = CreateTestZip("textures.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "texture1.tga", "tga content" },
                { "texture2.tga", "tga content" },
                { "texture1.tpc", "tpc content" },
                { "texture2.tpc", "tpc content" }
            });

            textureMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(archivePath)}" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Option 1: TGA only
            var option1 = new Option { Name = "TGA Format", Guid = Guid.NewGuid(), IsSelected = true };
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            // Option 2: TPC only
            var option2 = new Option { Name = "TPC Format", Guid = Guid.NewGuid(), IsSelected = false };
            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/*.tpc" },
                Destination = "<<kotorDirectory>>/Override"
            });

            textureMod.Options.Add(option1);
            textureMod.Options.Add(option2);

            textureMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString() }
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { textureMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Texture mod installation should succeed");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture1.tga")), Is.True, "TGA file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture2.tga")), Is.True, "TGA file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture1.tpc")), Is.False, "TPC file should be deleted (DelDuplicate)");
            });
        }

        [Test]
        public async Task Install_ModWithCleanup_DeletesOldFiles()
        {
            // Create old files that need to be cleaned up
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "Override", "old_texture.tga"), "old");
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "Override", "old_texture.tpc"), "old");

            var cleanupMod = new ModComponent
            {
                Name = "Cleanup Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            string csvPath = Path.Combine(_workingDirectory.FullName, "cleanlist.csv");
            File.WriteAllText(csvPath, "Cleanup Mod,old_texture.tga,old_texture.tpc");

            cleanupMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { cleanupMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Cleanup should succeed");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "old_texture.tga")), Is.False, "Old file should be deleted");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "old_texture.tpc")), Is.False, "Old file should be deleted");
            });
        }

        [Test]
        public async Task Install_ModChainWithDependencies_InstallsInCorrectOrder()
        {
            // Base mod
            var baseMod = TestComponentFactory.CreateComponent("Base Mod", _workingDirectory);

            // Mod that depends on base
            var dependentMod = TestComponentFactory.CreateComponent("Dependent Mod", _workingDirectory);
            dependentMod.Dependencies = new List<Guid> { baseMod.Guid };

            // Mod that depends on dependent
            var finalMod = TestComponentFactory.CreateComponent("Final Mod", _workingDirectory);
            finalMod.Dependencies = new List<Guid> { dependentMod.Guid };

            _mainConfigInstance.allComponents = new List<ModComponent> { baseMod, dependentMod, finalMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Chain installation should succeed");
                Assert.That(ordered.IndexOf(baseMod), Is.LessThan(ordered.IndexOf(dependentMod)), "Base should install before dependent");
                Assert.That(ordered.IndexOf(dependentMod), Is.LessThan(ordered.IndexOf(finalMod)), "Dependent should install before final");
                Assert.That(baseMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Base should be completed");
                Assert.That(dependentMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Dependent should be completed");
                Assert.That(finalMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Final should be completed");
            });
        }

        [Test]
        public async Task Install_ModWithRestriction_BlocksWhenRestrictedSelected()
        {
            var restrictedMod = TestComponentFactory.CreateComponent("Restricted Mod", _workingDirectory);
            restrictedMod.IsSelected = true;

            var mod = TestComponentFactory.CreateComponent("Mod", _workingDirectory);
            mod.IsSelected = true;
            mod.Restrictions = new List<Guid> { restrictedMod.Guid };

            _mainConfigInstance.allComponents = new List<ModComponent> { restrictedMod, mod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            // Filter out components with restrictions
            var selectedComponents = resume.OrderedComponents.Where(c => c.IsSelected &&
                (c.Restrictions == null || !c.Restrictions.Any(r => resume.OrderedComponents.Any(comp => comp.Guid == r && comp.IsSelected)))).ToList();

            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(selectedComponents, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(selectedComponents, Does.Not.Contain(mod), "Mod with restriction should be filtered out");
                Assert.That(restrictedMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Restricted mod should install");
            });
        }

        [Test]
        public async Task Install_MultipleModsWithMixedStates_RespectsStates()
        {
            var completedMod = TestComponentFactory.CreateComponent("Completed", _workingDirectory);
            completedMod.InstallState = ModComponent.ComponentInstallState.Completed;

            var pendingMod = TestComponentFactory.CreateComponent("Pending", _workingDirectory);
            pendingMod.InstallState = ModComponent.ComponentInstallState.Pending;

            var blockedMod = TestComponentFactory.CreateComponent("Blocked", _workingDirectory);
            blockedMod.InstallState = ModComponent.ComponentInstallState.Blocked;

            _mainConfigInstance.allComponents = new List<ModComponent> { completedMod, pendingMod, blockedMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(completedMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Completed should remain completed");
                Assert.That(pendingMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Pending should be completed");
                Assert.That(blockedMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Blocked), "Blocked should remain blocked");
            });
        }

        [Test]
        public async Task Install_ModWithCheckpointResume_ResumesCorrectly()
        {
            var mod1 = TestComponentFactory.CreateComponent("Mod 1", _workingDirectory);
            var mod2 = TestComponentFactory.CreateComponent("Mod 2", _workingDirectory);

            _mainConfigInstance.allComponents = new List<ModComponent> { mod1, mod2 };

            // First installation - complete mod1
            var coordinator1 = new InstallCoordinator();
            ResumeResult resume1 = await coordinator1.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered1 = resume1.OrderedComponents.Where(c => c.IsSelected).ToList();
            mod1.InstallState = ModComponent.ComponentInstallState.Completed;
            coordinator1.CheckpointManager.UpdateComponentState(mod1);
            await coordinator1.CheckpointManager.SaveAsync();

            // Resume installation
            var coordinator2 = new InstallCoordinator();
            ResumeResult resume2 = await coordinator2.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var restored1 = resume2.OrderedComponents.First(c => c.Guid == mod1.Guid);
            var restored2 = resume2.OrderedComponents.First(c => c.Guid == mod2.Guid);

            Assert.Multiple(() =>
            {
                Assert.That(restored1.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Mod 1 should be restored as completed");
                Assert.That(restored2.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Pending), "Mod 2 should be restored as pending");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_workingDirectory.FullName, fileName);
            using (var archive = ZipArchive.CreateArchive())
            {
                foreach (var kvp in files)
                {
                    archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                }
                using (var stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new SharpCompress.Writers.Zip.ZipWriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion
    }
}

