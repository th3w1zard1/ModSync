// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
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
    public sealed class AutoExtractionAndCleanListTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_AutoExtractTests_" + Guid.NewGuid());
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

        #region Auto-Extraction Scenarios

        [Test]
        public async Task Move_AutoExtracts_FromMultipleArchives_WhenFilesMissing()
        {
            // Create archives with different files
            string archive1 = CreateTestZip("archive1.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" }
            });

            string archive2 = CreateTestZip("archive2.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file2.txt", "content2" }
            });

            var component = new ModComponent
            {
                Name = "Multi Archive",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archive1),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file1.txt", true }
                            }
                        }
                    },
                    {
                        Path.GetFileName(archive2),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file2.txt", true }
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string>
                {
                    "<<modDirectory>>/file1.txt",
                    "<<modDirectory>>/file2.txt"
                },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed after auto-extraction");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "File1 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "File2 should be moved");
            });
        }

        [Test]
        public async Task Move_AutoExtracts_WithWildcardSource_WhenFilesMissing()
        {
            string archive = CreateTestZip("archive.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "texture1.tga", "tga1" },
                { "texture2.tga", "tga2" },
                { "texture3.tga", "tga3" }
            });

            var component = new ModComponent
            {
                Name = "Wildcard AutoExtract",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archive),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "texture1.tga", true },
                                { "texture2.tga", true },
                                { "texture3.tga", true }
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed after auto-extraction");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "Texture1 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "Texture2 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture3.tga")), Is.True, "Texture3 should be moved");
            });
        }

        [Test]
        public async Task Copy_AutoExtracts_WhenFilesMissing()
        {
            string archive = CreateTestZip("archive.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "content" }
            });

            var component = new ModComponent
            {
                Name = "Copy AutoExtract",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archive),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file.txt", true }
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Copy should succeed after auto-extraction");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should be copied");
            });
        }

        [Test]
        public async Task AutoExtract_WithMissingFileInRegistry_DoesNotExtract()
        {
            string archive = CreateTestZip("archive.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" }
            });

            var component = new ModComponent
            {
                Name = "Missing Registry",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archive),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file1.txt", true }
                                // file2.txt not in registry
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string>
                {
                    "<<modDirectory>>/file1.txt",
                    "<<modDirectory>>/file2.txt" // Not in registry
                },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            // Should fail because file2.txt is not in registry
            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.EqualTo(Instruction.ActionExitCode.Success), "Should fail when file not in registry");
            });
        }

        #endregion

        #region CleanList Scenarios

        [Test]
        public async Task CleanList_WithExactMatch_DeletesFiles()
        {
            // Create files to delete
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old_file1.tga"), "old1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old_file2.tpc"), "old2");

            // Create cleanlist CSV
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "Test Mod,old_file1.tga,old_file2.tpc");

            var selectedMod = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var component = new ModComponent
            {
                Name = "CleanList Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { selectedMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "CleanList should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old_file1.tga")), Is.False, "Old file1 should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old_file2.tpc")), Is.False, "Old file2 should be deleted");
            });
        }

        [Test]
        public async Task CleanList_WithFuzzyMatch_DeletesFiles()
        {
            // Create files to delete
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old_file.tga"), "old");

            // Create cleanlist CSV with partial mod name
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "HD Texture Pack,old_file.tga");

            var selectedMod = new ModComponent
            {
                Name = "HD Texture Pack by Author",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var component = new ModComponent
            {
                Name = "CleanList Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { selectedMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "CleanList should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old_file.tga")), Is.False, "Old file should be deleted (fuzzy match)");
            });
        }

        [Test]
        public async Task CleanList_WithMandatoryDeletions_AlwaysDeletes()
        {
            // Create files to delete
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "mandatory1.tga"), "mandatory1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "mandatory2.tpc"), "mandatory2");

            // Create cleanlist CSV with mandatory deletions
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "Mandatory Deletions,mandatory1.tga,mandatory2.tpc");

            var component = new ModComponent
            {
                Name = "CleanList Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "CleanList should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mandatory1.tga")), Is.False, "Mandatory file1 should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mandatory2.tpc")), Is.False, "Mandatory file2 should be deleted");
            });
        }

        [Test]
        public async Task CleanList_WithUnselectedMod_DoesNotDelete()
        {
            // Create files to delete
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old_file.tga"), "old");

            // Create cleanlist CSV
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "Unselected Mod,old_file.tga");

            var unselectedMod = new ModComponent
            {
                Name = "Unselected Mod",
                Guid = Guid.NewGuid(),
                IsSelected = false // Not selected
            };

            var component = new ModComponent
            {
                Name = "CleanList Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { unselectedMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "CleanList should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old_file.tga")), Is.True, "File should not be deleted (mod not selected)");
            });
        }

        [Test]
        public async Task CleanList_WithMultipleMods_DeletesForSelectedOnly()
        {
            // Create files to delete
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "mod1_file.tga"), "mod1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "mod2_file.tga"), "mod2");

            // Create cleanlist CSV with multiple mods
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "Mod 1,mod1_file.tga\nMod 2,mod2_file.tga");

            var mod1 = new ModComponent
            {
                Name = "Mod 1",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var mod2 = new ModComponent
            {
                Name = "Mod 2",
                Guid = Guid.NewGuid(),
                IsSelected = false // Not selected
            };

            var component = new ModComponent
            {
                Name = "CleanList Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { mod1, mod2, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "CleanList should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mod1_file.tga")), Is.False, "Mod1 file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mod2_file.tga")), Is.True, "Mod2 file should not be deleted");
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

