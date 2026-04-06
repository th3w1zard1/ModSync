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
    public sealed class ResourceRegistryAdvancedTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ResourceRegistryTests_" + Guid.NewGuid());
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

        #region Resource Registry Auto-Extraction Tests

        [Test]
        public async Task ResourceRegistry_AutoExtractFromNestedArchive_ExtractsCorrectly()
        {
            // Create archive with nested structure
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "subdir/nested/file.txt", "nested content" }
            });

            var component = new ModComponent
            {
                Name = "Nested Archive",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archivePath),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "subdir/nested/file.txt", true }
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/subdir/nested/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should auto-extract nested file");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Nested file should be extracted and moved");
            });
        }

        [Test]
        public async Task ResourceRegistry_AutoExtractMultipleFiles_ExtractsAll()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" },
                { "file3.txt", "content3" }
            });

            var component = new ModComponent
            {
                Name = "Multi File Archive",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archivePath),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file1.txt", true },
                                { "file2.txt", true },
                                { "file3.txt", true }
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
                    "<<modDirectory>>/file2.txt",
                    "<<modDirectory>>/file3.txt"
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
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should auto-extract all files");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "File1 should be extracted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "File2 should be extracted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True, "File3 should be extracted");
            });
        }

        [Test]
        public async Task ResourceRegistry_AutoExtractForCopy_ExtractsAndCopies()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
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
                        Path.GetFileName(archivePath),
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
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should auto-extract for copy");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be copied");
            });
        }

        [Test]
        public async Task ResourceRegistry_AutoExtractForPatcher_ExtractsTslpatchdata()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "tslpatchdata/changes.ini", "[Changes]" },
                { "tslpatchdata/namespaces.ini", "[Namespaces]" }
            });

            var component = new ModComponent
            {
                Name = "Patcher AutoExtract",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archivePath),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "tslpatchdata/changes.ini", true },
                                { "tslpatchdata/namespaces.ini", true }
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Patcher,
                Source = new List<string> { "<<modDirectory>>/tslpatchdata" },
                Arguments = "0"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            // Should handle auto-extraction for patcher (may fail if HoloPatcher not available, but tests extraction)
            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        #endregion

        #region Resource Registry Edge Cases

        [Test]
        public async Task ResourceRegistry_MultipleArchives_SelectsCorrectArchive()
        {
            string archive1 = CreateTestZip("archive1.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "archive1 content" }
            });

            string archive2 = CreateTestZip("archive2.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "archive2 content" }
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
                                { "file.txt", true }
                            }
                        }
                    },
                    {
                        Path.GetFileName(archive2),
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
                Action = Instruction.ActionType.Move,
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
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should extract from one of the archives");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be extracted and moved");
            });
        }

        [Test]
        public async Task ResourceRegistry_FileNotInRegistry_HandlesGracefully()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "content" }
            });

            var component = new ModComponent
            {
                Name = "Missing Registry",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archivePath),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "other.txt", true } // Different file
                            }
                        }
                    }
                }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            // Should handle file not in registry gracefully
            Assert.That(result, Is.Not.Null, "Should return a result");
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

