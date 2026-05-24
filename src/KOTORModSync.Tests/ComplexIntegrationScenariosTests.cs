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
    public sealed class ComplexIntegrationScenariosTests
    {
        private DirectoryInfo _workingDirectory;
        private MainConfig _mainConfigInstance;

        [SetUp]
        public void SetUp()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "KOTORModSync_IntegrationTests_" + Guid.NewGuid().ToString("N"));
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

        #region Complex Integration Scenarios

        [Test]
        public async Task Install_FullModInstallationFlow_ExecutesCorrectly()
        {
            // Simulate a complete mod installation: Extract -> CleanList -> Move -> DelDuplicate
            string archivePath = CreateTestZip("full_mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "texture1.tga", "tga1" },
                { "texture1.tpc", "tpc1" },
                { "texture2.tga", "tga2" },
                { "texture2.tpc", "tpc2" }
            });

            // Create old files for cleanup
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "Override", "old_texture.tga"), "old");

            // Create cleanlist
            string csvPath = Path.Combine(_workingDirectory.FullName, "cleanlist.csv");
            File.WriteAllText(csvPath, "Full Mod,old_texture.tga");

            var fullMod = new ModComponent
            {
                Name = "Full Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            fullMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(archivePath)}" },
                Destination = "<<modDirectory>>/extracted"
            });

            fullMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            fullMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            fullMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { fullMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Full installation should succeed");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "old_texture.tga")), Is.False, "Old file should be deleted");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture1.tga")), Is.True, "Texture1 should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture2.tga")), Is.True, "Texture2 should exist");
            });
        }

        [Test]
        public async Task Install_MultipleModsWithInterdependencies_InstallsCorrectly()
        {
            var baseMod = TestComponentFactory.CreateComponent("Base Mod", _workingDirectory);

            var textureMod = new ModComponent
            {
                Name = "Texture Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { baseMod.Guid }
            };
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "texture.tga"), "texture");
            textureMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/texture.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var modelMod = new ModComponent
            {
                Name = "Model Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { baseMod.Guid },
                InstallAfter = new List<Guid> { textureMod.Guid }
            };
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "model.mdl"), "model");
            modelMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/model.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { baseMod, textureMod, modelMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Has.Count.EqualTo(3), "Should contain all components");
                Assert.That(ordered.IndexOf(baseMod), Is.LessThan(ordered.IndexOf(textureMod)), "Base should come before texture");
                Assert.That(ordered.IndexOf(baseMod), Is.LessThan(ordered.IndexOf(modelMod)), "Base should come before model");
                Assert.That(ordered.IndexOf(textureMod), Is.LessThan(ordered.IndexOf(modelMod)), "Texture should come before model (InstallAfter)");
            });
        }

        [Test]
        public async Task Install_ModWithOptionsAndDependencies_InstallsCorrectly()
        {
            var baseMod = TestComponentFactory.CreateComponent("Base Mod", _workingDirectory);

            var optionMod = new ModComponent
            {
                Name = "Option Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { baseMod.Guid }
            };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "option1_file.txt"), "option1");
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "option2_file.txt"), "option2");
            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option2_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            optionMod.Options.Add(option1);
            optionMod.Options.Add(option2);

            optionMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString() }
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { baseMod, optionMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(ordered.IndexOf(baseMod), Is.LessThan(ordered.IndexOf(optionMod)), "Base should come before option mod");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "option1_file.txt")), Is.True, "Selected option file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "option2_file.txt")), Is.False, "Unselected option file should not exist");
            });
        }

        [Test]
        public async Task Install_ModChainWithCheckpointResume_ResumesCorrectly()
        {
            var mod1 = TestComponentFactory.CreateComponent("Mod 1", _workingDirectory);
            var mod2 = TestComponentFactory.CreateComponent("Mod 2", _workingDirectory);
            mod2.Dependencies = new List<Guid> { mod1.Guid };
            var mod3 = TestComponentFactory.CreateComponent("Mod 3", _workingDirectory);
            mod3.Dependencies = new List<Guid> { mod2.Guid };

            _mainConfigInstance.allComponents = new List<ModComponent> { mod1, mod2, mod3 };

            // First session - complete mod1
            var coordinator1 = new InstallCoordinator();
            ResumeResult resume1 = await coordinator1.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered1 = resume1.OrderedComponents.Where(c => c.IsSelected).ToList();
            mod1.InstallState = ModComponent.ComponentInstallState.Completed;
            coordinator1.CheckpointManager.UpdateComponentState(mod1);
            await coordinator1.CheckpointManager.SaveAsync();

            // Resume session
            var coordinator2 = new InstallCoordinator();
            ResumeResult resume2 = await coordinator2.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var restored1 = resume2.OrderedComponents.First(c => c.Guid == mod1.Guid);
            var restored2 = resume2.OrderedComponents.First(c => c.Guid == mod2.Guid);
            var restored3 = resume2.OrderedComponents.First(c => c.Guid == mod3.Guid);

            Assert.Multiple(() =>
            {
                Assert.That(restored1.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Mod 1 should be completed");
                Assert.That(restored2.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Pending), "Mod 2 should be pending");
                Assert.That(restored3.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Pending), "Mod 3 should be pending");
            });
        }

        [Test]
        public async Task Install_ModWithRestrictionsAndDependencies_HandlesBoth()
        {
            var restrictedMod = TestComponentFactory.CreateComponent("Restricted Mod", _workingDirectory);
            restrictedMod.IsSelected = true;

            var depMod = TestComponentFactory.CreateComponent("Dependency Mod", _workingDirectory);
            depMod.IsSelected = true;

            var mod = TestComponentFactory.CreateComponent("Mod", _workingDirectory);
            mod.IsSelected = true;
            mod.Dependencies = new List<Guid> { depMod.Guid };
            mod.Restrictions = new List<Guid> { restrictedMod.Guid };

            _mainConfigInstance.allComponents = new List<ModComponent> { restrictedMod, depMod, mod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            // Filter out components with restrictions
            var selectedComponents = resume.OrderedComponents.Where(c => c.IsSelected &&
                (c.Restrictions == null || !c.Restrictions.Any(r => resume.OrderedComponents.Any(comp => comp.Guid == r && comp.IsSelected)))).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(selectedComponents, Does.Not.Contain(mod), "Mod with restriction should be filtered out");
                Assert.That(selectedComponents, Contains.Item(depMod), "Dependency mod should be selected");
                Assert.That(selectedComponents, Contains.Item(restrictedMod), "Restricted mod should be selected");
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

