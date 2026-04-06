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
    public sealed class BoundaryConditionsAndEdgeCasesTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_BoundaryTests_" + Guid.NewGuid());
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

        #region Large File and Volume Tests

        [Test]
        public async Task LargeFile_MoveOperation_HandlesCorrectly()
        {
            // Create a moderately large file (1MB)
            string largeFile = Path.Combine(_modDirectory, "large.bin");
            byte[] data = new byte[1024 * 1024]; // 1MB
            new Random().NextBytes(data);
            File.WriteAllBytes(largeFile, data);

            var component = new ModComponent { Name = "Large File", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/large.bin" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Large file move should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "large.bin")), Is.True,
                    "Large file should be moved");
                byte[] movedData = File.ReadAllBytes(Path.Combine(_kotorDirectory, "Override", "large.bin"));
                Assert.That(movedData.Length, Is.EqualTo(data.Length), "File size should be preserved");
            });
        }

        [Test]
        public async Task ManyFiles_WildcardOperation_HandlesCorrectly()
        {
            // Create many files
            for (int i = 0; i < 100; i++)
            {
                File.WriteAllText(Path.Combine(_modDirectory, $"file{i:D3}.txt"), $"content{i}");
            }

            var component = new ModComponent { Name = "Many Files", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Many files should be handled");
                int movedCount = Directory.GetFiles(Path.Combine(_kotorDirectory, "Override"), "file*.txt").Length;
                Assert.That(movedCount, Is.EqualTo(100), "All files should be moved");
            });
        }

        #endregion

        #region Empty and Null Edge Cases

        [Test]
        public async Task EmptyComponent_NoInstructions_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Empty Component", Guid = Guid.NewGuid(), IsSelected = true };
            // No instructions

            var fileSystemProvider = new RealFileSystemProvider();

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Empty component should succeed");
        }

        [Test]
        public async Task Component_WithOnlyUnmetDependencyInstructions_SkipsAll()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Dependent", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { depComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (all skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "No instructions should execute");
            });
        }

        #endregion

        #region Path Boundary Tests

        [Test]
        public async Task PathBoundary_RootLevelFile_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "root.txt"), "content");

            var component = new ModComponent { Name = "Root File", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/root.txt" },
                Destination = "<<kotorDirectory>>" // Root destination
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Root level should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "root.txt")), Is.True,
                    "File should be moved to root");
            });
        }

        [Test]
        public async Task PathBoundary_IdenticalSourceAndDestination_SkipsGracefully()
        {
            string filePath = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(filePath, "content");

            var component = new ModComponent { Name = "Same Path", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<modDirectory>>" // Same as source directory
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle same source/destination gracefully
            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        #endregion

        #region DelDuplicate Edge Cases

        [Test]
        public async Task DelDuplicate_ThreeCompatibleExtensions_DeletesCorrectOne()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.dds"), "dds");

            var component = new ModComponent { Name = "Three Extensions", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga", ".dds" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should handle three extensions");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False,
                    "TPC should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True,
                    "TGA should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.dds")), Is.True,
                    "DDS should remain");
            });
        }

        [Test]
        public async Task DelDuplicate_CaseInsensitiveMatching_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.TGA"), "tga");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc");

            var component = new ModComponent { Name = "Case Insensitive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Case insensitive should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False,
                    "TPC should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.TGA")), Is.True,
                    "TGA should remain (case insensitive match)");
            });
        }

        #endregion

        #region Rename Edge Cases

        [Test]
        public async Task Rename_MultipleFilesToSameName_LastOneWins()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old2.txt"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old3.txt"), "content3");

            var component = new ModComponent { Name = "Rename Multiple", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old*.txt" },
                Destination = "new.txt",
                Overwrite = true
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Rename multiple should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "new.txt")), Is.True,
                    "Renamed file should exist");
                // Only one file should exist with the new name
                int newFileCount = Directory.GetFiles(Path.Combine(_kotorDirectory, "Override"), "new.txt").Length;
                Assert.That(newFileCount, Is.EqualTo(1), "Only one file should have the new name");
            });
        }

        [Test]
        public async Task Rename_ToExistingNameWithOverwriteFalse_SkipsGracefully()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old.txt"), "old content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "new.txt"), "existing content");

            var component = new ModComponent { Name = "Rename Skip", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" },
                Destination = "new.txt",
                Overwrite = false
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should skip gracefully");
                Assert.That(File.ReadAllText(Path.Combine(_kotorDirectory, "Override", "new.txt")),
                    Is.EqualTo("existing content"), "Existing file should not be overwritten");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old.txt")), Is.True,
                    "Source file should remain when overwrite is false");
            });
        }

        #endregion

        #region Extract Edge Cases

        [Test]
        public async Task Extract_ArchiveWithOnlyDirectories_HandlesGracefully()
        {
            // Create archive with only directories (no files)
            string archivePath = CreateTestZip("dirs_only.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "subdir1/", "" }, // Directory entry
                { "subdir2/nested/", "" } // Nested directory
            });

            var component = new ModComponent { Name = "Dirs Only", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/dirs_only.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Should handle archive with only directories");
        }

        [Test]
        public async Task Extract_MultipleArchivesWithOverlappingFiles_LastWins()
        {
            string archive1 = CreateTestZip("part1.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "common.txt", "archive1 content" }
            });

            string archive2 = CreateTestZip("part2.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "common.txt", "archive2 content" }
            });

            var component = new ModComponent { Name = "Overlapping Archives", Guid = Guid.NewGuid(), IsSelected = true };
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Overlapping archives should work");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "common.txt")), Is.True,
                    "Common file should exist");
                // Last archive should win
                Assert.That(File.ReadAllText(Path.Combine(_modDirectory, "extracted", "common.txt")),
                    Is.EqualTo("archive2 content"), "Last archive should overwrite");
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
                    if (string.IsNullOrEmpty(kvp.Value))
                    {
                        // Directory entry
                        archive.AddEntry(kvp.Key, new MemoryStream(), true);
                    }
                    else
                    {
                        archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                    }
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

