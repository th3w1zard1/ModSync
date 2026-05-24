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
    public sealed class ComplexRealWorldScenariosTests
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

        #region Complex Mod Installation Scenarios

        [Test]
        public async Task Install_TexturePackWithMultipleFormats_HandlesCorrectly()
        {
            // Simulate a texture pack that extracts, moves textures, and cleans up duplicates
            string archivePath = CreateTestZip("texture_pack.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "textures/texture1.tga", "tga1" },
                { "textures/texture1.tpc", "tpc1" },
                { "textures/texture2.tga", "tga2" },
                { "textures/texture2.tpc", "tpc2" },
                { "textures/texture3.dds", "dds3" },
                { "textures/texture3.tga", "tga3" }
            });

            var textureMod = new ModComponent
            {
                Name = "HD Texture Pack",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            textureMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(archivePath)}" },
                Destination = "<<modDirectory>>/extracted"
            });

            textureMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/textures/*.tga", "<<modDirectory>>/extracted/textures/*.dds" },
                Destination = "<<kotorDirectory>>/Override"
            });

            textureMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga", ".dds" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { textureMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Texture pack installation should succeed");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture1.tga")), Is.True, "TGA file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture2.tga")), Is.True, "TGA file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "texture3.dds")), Is.True, "DDS file should exist");
            });
        }

        [Test]
        public async Task Install_ModWithCleanupAndReplacement_HandlesCorrectly()
        {
            // Create old files that need cleanup
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "Override", "old_file1.tga"), "old1");
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "Override", "old_file2.tpc"), "old2");

            // Create cleanup CSV
            string csvPath = Path.Combine(_workingDirectory.FullName, "cleanlist.csv");
            File.WriteAllText(csvPath, "Replacement Mod,old_file1.tga,old_file2.tpc");

            // Create new files
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "new_file1.tga"), "new1");
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "new_file2.tga"), "new2");

            var replacementMod = new ModComponent
            {
                Name = "Replacement Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            replacementMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            replacementMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/new_file1.tga", "<<modDirectory>>/new_file2.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { replacementMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Replacement mod installation should succeed");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "old_file1.tga")), Is.False, "Old file should be deleted");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "old_file2.tpc")), Is.False, "Old file should be deleted");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "new_file1.tga")), Is.True, "New file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "new_file2.tga")), Is.True, "New file should exist");
            });
        }

        [Test]
        public async Task Install_ModChainWithOptions_InstallsCorrectly()
        {
            // Base mod
            var baseMod = TestComponentFactory.CreateComponent("Base Mod", _workingDirectory);

            // Mod with options
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
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Mod chain installation should succeed");
                Assert.That(ordered.IndexOf(baseMod), Is.LessThan(ordered.IndexOf(optionMod)), "Base mod should install before option mod");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "option1_file.txt")), Is.True, "Selected option file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "option2_file.txt")), Is.False, "Unselected option file should not exist");
            });
        }

        [Test]
        public async Task Install_MultipleModsWithConflicts_HandlesCorrectly()
        {
            // Mod 1 installs file1.txt
            var mod1 = new ModComponent
            {
                Name = "Mod 1",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "file1.txt"), "mod1 content");
            mod1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            // Mod 2 also installs file1.txt (conflict)
            var mod2 = new ModComponent
            {
                Name = "Mod 2",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            File.WriteAllText(Path.Combine(_workingDirectory.FullName, "file1_mod2.txt"), "mod2 content");
            mod2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1_mod2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });
            // Rename to same name as mod1's file to create conflict
            mod2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/file1_mod2.txt" },
                Destination = "file1.txt",
                Overwrite = true
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { mod1, mod2 };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                // Last mod to install should win (mod2)
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "file1.txt")), Is.True, "File should exist");
            });
        }

        [Test]
        public async Task Install_ModWithExtractMoveRenameSequence_ExecutesCorrectly()
        {
            string archivePath = CreateTestZip("sequence.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "old_name.txt", "content" }
            });

            var sequenceMod = new ModComponent
            {
                Name = "Sequence Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            sequenceMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(archivePath)}" },
                Destination = "<<modDirectory>>/extracted"
            });

            sequenceMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/old_name.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            sequenceMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old_name.txt" },
                Destination = "new_name.txt"
            });

            _mainConfigInstance.allComponents = new List<ModComponent> { sequenceMod };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sequence installation should succeed");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "new_name.txt")), Is.True, "Renamed file should exist");
                Assert.That(File.Exists(Path.Combine(_workingDirectory.FullName, "Override", "old_name.txt")), Is.False, "Old name should not exist");
            });
        }

        #endregion

        #region Complex Dependency Scenarios

        [Test]
        public async Task Install_ModWithMultipleDependencyTypes_RespectsAll()
        {
            var modA = TestComponentFactory.CreateComponent("Mod A", _workingDirectory);
            var modB = TestComponentFactory.CreateComponent("Mod B", _workingDirectory);
            modB.Dependencies = new List<Guid> { modA.Guid };
            modB.InstallAfter = new List<Guid> { modA.Guid };

            var modC = TestComponentFactory.CreateComponent("Mod C", _workingDirectory);
            modC.Dependencies = new List<Guid> { modB.Guid };
            modC.InstallBefore = new List<Guid> { modA.Guid }; // Conflict - dependency takes precedence

            _mainConfigInstance.allComponents = new List<ModComponent> { modA, modB, modC };

            var coordinator = new InstallCoordinator();
            ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            var ordered = resume.OrderedComponents.Where(c => c.IsSelected).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Has.Count.EqualTo(3), "Should contain all components");
                Assert.That(ordered.IndexOf(modA), Is.LessThan(ordered.IndexOf(modB)), "Mod A should come before Mod B");
                Assert.That(ordered.IndexOf(modB), Is.LessThan(ordered.IndexOf(modC)), "Mod B should come before Mod C (dependency takes precedence)");
            });
        }

        [Test]
        public async Task Install_ModWithRestrictionAndDependency_HandlesBoth()
        {
            var depMod = TestComponentFactory.CreateComponent("Dependency", _workingDirectory);
            depMod.IsSelected = true;

            var restrictedMod = TestComponentFactory.CreateComponent("Restricted", _workingDirectory);
            restrictedMod.IsSelected = true;

            var mod = TestComponentFactory.CreateComponent("Mod", _workingDirectory);
            mod.IsSelected = true;
            mod.Dependencies = new List<Guid> { depMod.Guid };
            mod.Restrictions = new List<Guid> { restrictedMod.Guid };

            _mainConfigInstance.allComponents = new List<ModComponent> { depMod, restrictedMod, mod };

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

