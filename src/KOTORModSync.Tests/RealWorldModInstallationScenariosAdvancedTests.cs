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
    public sealed class RealWorldModInstallationScenariosAdvancedTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_RealWorldAdv_" + Guid.NewGuid());
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

        #region Complex Real-World Scenarios

        [Test]
        public async Task RealWorldScenario_MultiModBuildWithDependencies_InstallsCorrectly()
        {
            // Simulate a complex mod build with multiple mods, dependencies, and options

            // Base mods
            var baseMod = new ModComponent { Name = "Base Mod", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "base.txt"), "base");
            baseMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/base.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Texture mod with options
            var textureMod = new ModComponent
            {
                Name = "Texture Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { baseMod.Guid }
            };

            var hdOption = new Option { Name = "HD Textures", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "hd_texture.tga"), "hd");
            hdOption.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/hd_texture.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var sdOption = new Option { Name = "SD Textures", Guid = Guid.NewGuid(), IsSelected = false };
            File.WriteAllText(Path.Combine(_modDirectory, "sd_texture.tga"), "sd");
            sdOption.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/sd_texture.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            textureMod.Options.Add(hdOption);
            textureMod.Options.Add(sdOption);

            textureMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { hdOption.Guid.ToString(), sdOption.Guid.ToString() }
            });

            // Compatibility cleanup mod
            var cleanupMod = new ModComponent
            {
                Name = "Cleanup Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                InstallAfter = new List<Guid> { textureMod.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "cleanlist.csv"),
                "Texture Mod,hd_texture.tga\nMandatory Deletions,old_file.tga");

            cleanupMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { cleanupMod, textureMod, baseMod };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered[0].Guid, Is.EqualTo(baseMod.Guid), "Base mod should be first");
                Assert.That(ordered.IndexOf(textureMod), Is.LessThan(ordered.IndexOf(cleanupMod)),
                    "Texture mod should come before cleanup mod");
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var mod in ordered)
            {
                foreach (var instruction in mod.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(mod);
                }
                foreach (var option in mod.Options)
                {
                    foreach (var instruction in option.Instructions)
                    {
                        instruction.SetFileSystemProvider(fileSystemProvider);
                        instruction.SetParentComponent(mod);
                    }
                }
            }

            foreach (var mod in ordered)
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    $"{mod.Name} should install successfully");
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "base.txt")), Is.True,
                    "Base mod should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "hd_texture.tga")), Is.True,
                    "HD texture option should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "sd_texture.tga")), Is.False,
                    "SD texture option should NOT be installed");
            });
        }

        [Test]
        public async Task RealWorldScenario_ModWithArchiveAndExtraction_CompleteWorkflow()
        {
            // Create archive
            string zipPath = CreateTestZip("mod.zip", new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "override/texture.tga", "texture content" },
                { "override/model.mdl", "model content" },
                { "modules/custom.mod", "module content" },
                { "readme.txt", "readme" }
            });

            var component = new ModComponent { Name = "Archive Mod", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract archive
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move override files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/override/*" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy module files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/extracted/modules/*" },
                Destination = "<<kotorDirectory>>/Modules"
            });

            // Delete readme
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/extracted/readme.txt" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True,
                    "Texture should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "model.mdl")), Is.True,
                    "Model should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Modules", "custom.mod")), Is.True,
                    "Module should be copied");
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

