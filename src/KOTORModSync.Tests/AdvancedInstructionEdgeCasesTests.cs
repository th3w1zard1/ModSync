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
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class AdvancedInstructionEdgeCasesTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_AdvancedEdgeCases_" + Guid.NewGuid());
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

        #region CleanList Complex Scenarios

        [Test]
        public async Task CleanList_MultipleModsWithFuzzyMatching_DeletesCorrectFiles()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath,
                "Mandatory Deletions,old1.tga,old2.tpc\n" +
                "HD UI Rewrite,ui_old.tga,ui_old.tpc\n" +
                "Weapon Model Overhaul,w_blaster_01.mdl,w_blaster_01.mdx\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old1.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old2.tpc"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdl"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdx"), "old");

            var hdUIMod = new ModComponent { Name = "HD UI Rewrite", Guid = Guid.NewGuid(), IsSelected = true };
            var weaponMod = new ModComponent { Name = "Weapon Model Overhaul", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { hdUIMod, weaponMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "CleanList should succeed");
                // Mandatory deletions always execute
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old1.tga")), Is.False, "Mandatory deletion should remove old1.tga");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old2.tpc")), Is.False, "Mandatory deletion should remove old2.tpc");
                // HD UI Rewrite is selected, so its files should be deleted
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tga")), Is.False, "HD UI files should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc")), Is.False, "HD UI files should be deleted");
                // Weapon Model Overhaul is selected, so its files should be deleted
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdl")), Is.False, "Weapon mod files should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdx")), Is.False, "Weapon mod files should be deleted");
            });
        }

        [Test]
        public async Task CleanList_UnselectedMod_DoesNotDeleteFiles()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "HD UI Rewrite,ui_old.tga,ui_old.tpc\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc"), "old");

            var hdUIMod = new ModComponent { Name = "HD UI Rewrite", Guid = Guid.NewGuid(), IsSelected = false }; // Not selected
            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { hdUIMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "CleanList should succeed");
                // HD UI Rewrite is not selected, so files should NOT be deleted
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tga")), Is.True, "Files should remain when mod is not selected");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc")), Is.True, "Files should remain when mod is not selected");
            });
        }

        #endregion

        #region Complex Dependency Chains

        [Test]
        public async Task ComplexDependencyChain_ThreeLevelChain_ExecutesInCorrectOrder()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid }, IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modB.Guid }, IsSelected = true };

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

            var components = new List<ModComponent> { modC, modB, modA }; // Wrong order
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var comp in ordered)
            {
                foreach (var instruction in comp.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(comp);
                }
            }

            var executionOrder = new List<string>();
            foreach (var comp in ordered)
            {
                var result = await comp.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                executionOrder.Add(comp.Name);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {comp.Name} should install successfully");
            }

            Assert.Multiple(() =>
            {
                Assert.That(executionOrder[0], Is.EqualTo("Mod A"), "Mod A should install first");
                Assert.That(executionOrder[1], Is.EqualTo("Mod B"), "Mod B should install second");
                Assert.That(executionOrder[2], Is.EqualTo("Mod C"), "Mod C should install third");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modA.txt")), Is.True);
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modB.txt")), Is.True);
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modC.txt")), Is.True);
            });
        }

        [Test]
        public async Task ComplexDependencyChain_MultipleDependencies_AllMustBeMet()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid, modB.Guid }, IsSelected = true };

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
            foreach (var comp in ordered)
            {
                foreach (var instruction in comp.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(comp);
                }
            }

            foreach (var comp in ordered)
            {
                var result = await comp.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {comp.Name} should install successfully");
            }

            Assert.Multiple(() =>
            {
                int indexA = ordered.FindIndex(c => c.Guid == modA.Guid);
                int indexB = ordered.FindIndex(c => c.Guid == modB.Guid);
                int indexC = ordered.FindIndex(c => c.Guid == modC.Guid);

                Assert.That(indexA, Is.LessThan(indexC), "Mod A should install before Mod C");
                Assert.That(indexB, Is.LessThan(indexC), "Mod B should install before Mod C");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modC.txt")), Is.True, "Mod C should install when all dependencies are met");
            });
        }

        #endregion

        #region Instruction Dependency Combinations

        [Test]
        public async Task InstructionDependencyCombination_MultipleDependencies_AllMustBeSelected()
        {
            var dep1 = new ModComponent { Name = "Dependency 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dependency 2", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Dependent Mod", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { dep1, dep2, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when all dependencies are met");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should be moved when all dependencies are selected");
            });
        }

        [Test]
        public async Task InstructionDependencyCombination_OneDependencyMissing_SkipsInstruction()
        {
            var dep1 = new ModComponent { Name = "Dependency 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dependency 2", Guid = Guid.NewGuid(), IsSelected = false }; // Not selected
            var component = new ModComponent { Name = "Dependent Mod", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { dep1, dep2, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (instruction skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False, "File should NOT be moved when dependency is missing");
            });
        }

        [Test]
        public async Task InstructionRestrictionCombination_MultipleRestrictions_AnyBlocksInstruction()
        {
            var restricted1 = new ModComponent { Name = "Restricted 1", Guid = Guid.NewGuid(), IsSelected = false };
            var restricted2 = new ModComponent { Name = "Restricted 2", Guid = Guid.NewGuid(), IsSelected = true }; // Selected - should block
            var component = new ModComponent { Name = "Restricted Mod", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restricted1.Guid, restricted2.Guid }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { restricted1, restricted2, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (instruction skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False, "File should NOT be moved when restriction is active");
            });
        }

        #endregion

        #region Extract Edge Cases

        [Test]
        public async Task Extract_NoDestination_ExtractsToArchiveDirectory()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "content" }
            });

            var component = new ModComponent { Name = "Extract No Dest", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" }
                // No Destination specified
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Extract without destination should succeed");
                // File should be extracted to the same directory as the archive
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file.txt")), Is.True, "File should be extracted to archive directory");
            });
        }

        [Test]
        public async Task Extract_MultipleArchivesToSameDestination_MergesCorrectly()
        {
            string archive1 = CreateTestZip("part1.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "common.txt", "common1" }
            });
            string archive2 = CreateTestZip("part2.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file2.txt", "content2" },
                { "common.txt", "common2" } // Overwrites common.txt
            });

            var component = new ModComponent { Name = "Multi Extract", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/part1.zip", "<<modDirectory>>/part2.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multi-extract should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.True, "First archive file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.True, "Second archive file should exist");
                Assert.That(File.ReadAllText(Path.Combine(_modDirectory, "extracted", "common.txt")), Is.EqualTo("common2"), "Second archive should overwrite common file");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            using (var archive = ZipArchive.Create())
            {
                foreach (var kvp in files)
                {
                    archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                }
                using (var stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new WriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion
    }
}

