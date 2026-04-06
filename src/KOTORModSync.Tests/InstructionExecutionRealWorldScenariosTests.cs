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
    public sealed class InstructionExecutionRealWorldScenariosTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_RealWorld_" + Guid.NewGuid());
            _modDirectory = Path.Combine(_testDirectory, "Mods");
            _kotorDirectory = Path.Combine(_testDirectory, "KOTOR");
            Directory.CreateDirectory(_modDirectory);
            Directory.CreateDirectory(_kotorDirectory);
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override"));
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Modules"));

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

        #region Real-World Mod Installation Scenarios

        [Test]
        public async Task RealWorldScenario_CompleteModInstallation_SimulatesFullWorkflow()
        {
            // Simulate a complete mod installation:
            // 1. Extract archive
            // 2. Move files to Override
            // 3. Copy some files to Modules
            // 4. Delete temporary files

            // Create archive
            var archiveFiles = new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "mod/override/file1.2da", "2da content" },
                { "mod/override/file2.tga", "tga content" },
                { "mod/modules/module.mod", "module content" },
                { "mod/readme.txt", "readme" }
            };
            string zipPath = CreateTestZip("mod.zip", archiveFiles);

            var component = new ModComponent { Name = "Complete Mod", Guid = Guid.NewGuid(), IsSelected = true };

            // Step 1: Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Step 2: Move override files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/mod/override/*" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Step 3: Copy module files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/extracted/mod/modules/*" },
                Destination = "<<kotorDirectory>>/Modules"
            });

            // Step 4: Delete readme (optional cleanup)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/extracted/mod/readme.txt" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complete workflow should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.2da")), Is.True,
                    "Override file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.tga")), Is.True,
                    "Override TGA should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Modules", "module.mod")), Is.True,
                    "Module file should be copied");
            });
        }

        [Test]
        public async Task RealWorldScenario_MultipleModsWithDependencies_InstallsInOrder()
        {
            // Mod A: Base mod
            File.WriteAllText(Path.Combine(_modDirectory, "modA.txt"), "Mod A");
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Mod B: Depends on Mod A
            File.WriteAllText(Path.Combine(_modDirectory, "modB.txt"), "Mod B");
            var modB = new ModComponent
            {
                Name = "Mod B",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid }
            };
            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modB.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Mod C: Depends on Mod B
            File.WriteAllText(Path.Combine(_modDirectory, "modC.txt"), "Mod C");
            var modC = new ModComponent
            {
                Name = "Mod C",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modB.Guid }
            };
            modC.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modC.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A should be first");
                Assert.That(ordered[1].Guid, Is.EqualTo(modB.Guid), "Mod B should be second");
                Assert.That(ordered[2].Guid, Is.EqualTo(modC.Guid), "Mod C should be third");
            });

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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    $"{mod.Name} should install successfully");
            }
        }

        [Test]
        public async Task RealWorldScenario_ModWithOptions_InstallsSelectedOptions()
        {
            // Create mod with multiple options
            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "Option 1");
            File.WriteAllText(Path.Combine(_modDirectory, "option2.txt"), "Option 2");
            File.WriteAllText(Path.Combine(_modDirectory, "option3.txt"), "Option 3");

            var component = new ModComponent { Name = "Mod with Options", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };
            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var option3 = new Option { Name = "Option 3", Guid = Guid.NewGuid(), IsSelected = true };
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
                Source = component.Options.Select(o => o.Guid.ToString()).ToList()
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Options mod should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "Selected option1 should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.False,
                    "Unselected option2 should NOT be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option3.txt")), Is.True,
                    "Selected option3 should be installed");
            });
        }

        [Test]
        public async Task RealWorldScenario_ModWithRestrictions_RespectsRestrictions()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "restricted.txt"), "Restricted");

            var baseMod = new ModComponent { Name = "Base Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var restrictedMod = new ModComponent
            {
                Name = "Restricted Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { baseMod.Guid } // Requires base mod
            };

            restrictedMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/restricted.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { restrictedMod, baseMod };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered[0].Guid, Is.EqualTo(baseMod.Guid), "Base mod should be first");
                Assert.That(ordered[1].Guid, Is.EqualTo(restrictedMod.Guid), "Restricted mod should be second");
            });

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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    $"{mod.Name} should install successfully");
            }
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

