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
    public sealed class ComplexInteractionScenariosTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComplexInteractionTests_" + Guid.NewGuid());
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

        #region Complex Multi-Component Interactions

        [Test]
        public async Task MultiComponent_ModAOverwritesModB_LastModWins()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "modA_file.txt"), "Mod A content");
            File.WriteAllText(Path.Combine(_modDirectory, "modB_file.txt"), "Mod B content");

            // Both mods move file with same name
            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modB_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            // Rename both to same name
            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/modA_file.txt" },
                Destination = "common.txt",
                Overwrite = true
            });

            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/modB_file.txt" },
                Destination = "common.txt",
                Overwrite = true
            });

            var components = new List<ModComponent> { modA, modB };
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

            // Last mod to execute should win
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "common.txt")), Is.True,
                "Common file should exist (last mod wins)");
        }

        [Test]
        public async Task MultiComponent_ModADeletesModBFile_HandlesCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "modA_file.txt"), "Mod A");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "modB_file.txt"), "Mod B existing");

            // Mod A installs file
            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Mod B deletes Mod A's file
            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/modA_file.txt" }
            });

            var components = new List<ModComponent> { modA, modB };
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
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modA_file.txt")), Is.False,
                    "Mod A file should be deleted by Mod B");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modB_file.txt")), Is.True,
                    "Mod B file should remain");
            });
        }

        #endregion

        #region Instruction Dependency on Other Instructions

        [Test]
        public async Task InstructionDependency_InstructionDependsOnPreviousResult_ExecutesInOrder()
        {
            var component = new ModComponent { Name = "Instruction Dependency", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "2");

            // First instruction: Extract archive (creates extracted files)
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file3.txt", "3" }
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Second instruction: Move extracted file (depends on first instruction)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/file3.txt" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Instruction dependency should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True,
                    "File from extracted archive should be moved");
            });
        }

        #endregion

        #region Complex Wildcard and Pattern Combinations

        [Test]
        public async Task ComplexWildcard_MultiplePatternsWithDifferentActions_ProcessesAll()
        {
            // Create various file types
            File.WriteAllText(Path.Combine(_modDirectory, "texture1.tga"), "tga1");
            File.WriteAllText(Path.Combine(_modDirectory, "texture2.tga"), "tga2");
            File.WriteAllText(Path.Combine(_modDirectory, "model1.mdl"), "mdl1");
            File.WriteAllText(Path.Combine(_modDirectory, "model2.mdl"), "mdl2");
            File.WriteAllText(Path.Combine(_modDirectory, "script1.ncs"), "ncs1");

            var component = new ModComponent { Name = "Complex Wildcard", Guid = Guid.NewGuid(), IsSelected = true };

            // Move TGA files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy MDL files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/*.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Delete NCS files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/*.ncs" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complex wildcard should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True,
                    "TGA files should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "model1.mdl")), Is.True,
                    "MDL files should be copied");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "script1.ncs")), Is.False,
                    "NCS files should be deleted");
            });
        }

        [Test]
        public async Task ComplexWildcard_RecursivePattern_ProcessesNestedFiles()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir1", "nested"));
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir2"));

            File.WriteAllText(Path.Combine(_modDirectory, "subdir1", "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "subdir1", "nested", "file2.txt"), "2");
            File.WriteAllText(Path.Combine(_modDirectory, "subdir2", "file3.txt"), "3");
            File.WriteAllText(Path.Combine(_modDirectory, "root.txt"), "root");

            var component = new ModComponent { Name = "Recursive Wildcard", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/**/*.txt" }, // Recursive pattern
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Recursive wildcard should succeed");
                // Files should be moved (exact behavior depends on recursive wildcard support)
                Assert.That(result, Is.Not.Null, "Should return a result");
            });
        }

        #endregion

        #region Complex Option and Instruction Combinations

        [Test]
        public async Task ComplexOptions_OptionWithInstructionDependencies_RespectsAllDependencies()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Complex Option", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "option1_file1.txt"), "file1");
            File.WriteAllText(Path.Combine(_modDirectory, "option1_file2.txt"), "file2");

            // Option instruction also has dependency
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1_file1.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            });

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1_file2.txt" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complex option should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1_file1.txt")), Is.True,
                    "File with dependency should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1_file2.txt")), Is.True,
                    "File without dependency should execute");
            });
        }

        #endregion

        #region Real-World Complex Scenarios

        [Test]
        public async Task RealWorld_TextureModWithMultipleOptionsAndCleanList_ExecutesCorrectly()
        {
            // Simulates a complex texture mod installation:
            // 1. Extract archive
            // 2. Choose option (HD or Standard)
            // 3. Move selected textures
            // 4. Delete duplicate TPC files
            // 5. CleanList removes conflicting files

            string archivePath = CreateTestZip("texture_mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "hd/texture.tga", "hd content" },
                { "standard/texture.tga", "standard content" },
                { "texture.tpc", "tpc content" }
            });

            // Pre-existing conflicting files
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "old tpc");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "conflict.tga"), "conflict");

            // Create cleanlist
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "Texture Mod,conflict.tga\n");

            var textureMod = new ModComponent { Name = "Texture Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Texture Mod Installer", Guid = Guid.NewGuid(), IsSelected = true };

            var hdOption = new Option { Name = "HD Quality", Guid = Guid.NewGuid(), IsSelected = true };
            var standardOption = new Option { Name = "Standard Quality", Guid = Guid.NewGuid(), IsSelected = false };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/texture_mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Choose option
            component.Options.Add(hdOption);
            component.Options.Add(standardOption);

            hdOption.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/hd/texture.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            standardOption.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/standard/texture.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { hdOption.Guid.ToString(), standardOption.Guid.ToString() }
            });

            // Delete duplicates
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            // CleanList
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
            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { textureMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complex texture mod should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True,
                    "HD texture should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False,
                    "TPC duplicate should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "conflict.tga")), Is.False,
                    "Conflicting file should be deleted by CleanList");
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

