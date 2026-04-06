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
    public sealed class RealWorldIntegrationScenariosTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_RealWorldTests_" + Guid.NewGuid());
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

        #region Complete Mod Installation Scenarios

        [Test]
        public async Task CompleteModInstallation_TextureMod_ExecutesAllSteps()
        {
            // Simulates a complete texture mod installation:
            // 1. Extract archive
            // 2. Move textures
            // 3. Delete duplicate TPC files
            // 4. Clean up extracted files

            string archivePath = CreateTestZip("texture_mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "textures/texture1.tga", "tga1" },
                { "textures/texture2.tga", "tga2" },
                { "textures/texture1.tpc", "tpc1" },
                { "textures/texture2.tpc", "tpc2" }
            });

            // Pre-existing TPC files
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture1.tpc"), "old tpc");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture2.tpc"), "old tpc");

            var component = new ModComponent { Name = "Texture Mod", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/texture_mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move TGA files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/textures/*.tga" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            // Delete duplicate TPC files (KOTOR prioritizes TGA)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            // Clean up extracted directory
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/extracted/textures/*.tpc" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complete texture mod should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "TGA files should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "TGA files should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tpc")), Is.False, "TPC duplicates should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tpc")), Is.False, "TPC duplicates should be deleted");
            });
        }

        [Test]
        public async Task CompleteModInstallation_ModelModWithOptions_ExecutesCorrectly()
        {
            // Simulates a model mod with multiple options
            string archivePath = CreateTestZip("model_mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "models/base.mdl", "base model" },
                { "models/hd.mdl", "hd model" },
                { "models/ultra.mdl", "ultra model" }
            });

            var component = new ModComponent { Name = "Model Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var baseOption = new Option { Name = "Base Quality", Guid = Guid.NewGuid(), IsSelected = false };
            var hdOption = new Option { Name = "HD Quality", Guid = Guid.NewGuid(), IsSelected = true };
            var ultraOption = new Option { Name = "Ultra Quality", Guid = Guid.NewGuid(), IsSelected = false };

            baseOption.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/models/base.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            hdOption.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/models/hd.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            ultraOption.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/models/ultra.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(baseOption);
            component.Options.Add(hdOption);
            component.Options.Add(ultraOption);

            // Extract first
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/model_mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Then choose option
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { baseOption.Guid.ToString(), hdOption.Guid.ToString(), ultraOption.Guid.ToString() }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Model mod with options should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "hd.mdl")), Is.True,
                    "Selected option (HD) should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "base.mdl")), Is.False,
                    "Unselected option should not be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ultra.mdl")), Is.False,
                    "Unselected option should not be installed");
            });
        }

        #endregion

        #region Multi-Mod Installation Scenarios

        [Test]
        public async Task MultiModInstallation_ThreeModsWithDependencies_InstallsInOrder()
        {
            var baseMod = new ModComponent { Name = "Base Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var extensionMod = new ModComponent
            {
                Name = "Extension Mod",
                Guid = Guid.NewGuid(),
                Dependencies = new List<Guid> { baseMod.Guid },
                IsSelected = true
            };
            var optionalMod = new ModComponent { Name = "Optional Mod", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "base.txt"), "base");
            File.WriteAllText(Path.Combine(_modDirectory, "extension.txt"), "extension");
            File.WriteAllText(Path.Combine(_modDirectory, "optional.txt"), "optional");

            baseMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/base.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            extensionMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extension.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            optionalMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/optional.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { optionalMod, extensionMod, baseMod }; // Wrong order
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
                Assert.That(executionOrder.IndexOf("Base Mod"), Is.LessThan(executionOrder.IndexOf("Extension Mod")),
                    "Base Mod should install before Extension Mod");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "base.txt")), Is.True,
                    "All mods should install");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "extension.txt")), Is.True,
                    "All mods should install");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "optional.txt")), Is.True,
                    "All mods should install");
            });
        }

        [Test]
        public async Task MultiModInstallation_WithCleanList_DeletesConflictingFiles()
        {
            // Create cleanlist
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath,
                "Mandatory Deletions,old1.tga,old2.tpc\n" +
                "HD Texture Pack,conflict1.tga,conflict2.tga\n");

            // Pre-existing conflicting files
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old1.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old2.tpc"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "conflict1.tga"), "conflict");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "conflict2.tga"), "conflict");

            var hdTextureMod = new ModComponent { Name = "HD Texture Pack", Guid = Guid.NewGuid(), IsSelected = true };
            var cleanupMod = new ModComponent { Name = "Cleanup Mod", Guid = Guid.NewGuid(), IsSelected = true };

            // CleanList instruction
            cleanupMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { hdTextureMod, cleanupMod };
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should succeed");
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old1.tga")), Is.False,
                    "Mandatory deletions should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old2.tpc")), Is.False,
                    "Mandatory deletions should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "conflict1.tga")), Is.False,
                    "HD Texture Pack conflicts should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "conflict2.tga")), Is.False,
                    "HD Texture Pack conflicts should be deleted");
            });
        }

        #endregion

        #region Complex Option Scenarios

        [Test]
        public async Task ComplexOptions_MutuallyExclusiveOptions_OnlyOneExecutes()
        {
            var component = new ModComponent { Name = "Exclusive Options", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { Guid.NewGuid() } // Will be set to option2's GUID
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

