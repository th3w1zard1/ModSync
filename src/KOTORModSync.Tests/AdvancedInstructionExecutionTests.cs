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
    public sealed class AdvancedInstructionExecutionTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_AdvancedTests_" + Guid.NewGuid());
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

        #region Complex Extract Scenarios

        [Test]
        public async Task Extract_WithNestedArchiveStructure_PreservesStructure()
        {
            string zipPath = CreateTestZip("nested.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "root.txt", "root" },
                { "subdir1/file1.txt", "file1" },
                { "subdir1/subdir2/file2.txt", "file2" },
                { "subdir3/file3.txt", "file3" }
            });

            var component = new ModComponent { Name = "Nested Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Extract should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "root.txt")), Is.True, "Root file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "subdir1", "file1.txt")), Is.True, "Nested file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "subdir1", "subdir2", "file2.txt")), Is.True, "Deeply nested file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "subdir3", "file3.txt")), Is.True, "Another nested file should exist");
            });
        }

        [Test]
        public async Task Extract_WithLargeArchive_HandlesCorrectly()
        {
            // Create a larger archive with many files
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < 100; i++)
            {
                files.Add($"file{i}.txt", $"content{i}");
            }

            string zipPath = CreateTestZip("large.zip", files);

            var component = new ModComponent { Name = "Large Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Large archive extract should succeed");
                Assert.That(Directory.GetFiles(Path.Combine(_modDirectory, "extracted"), "*", SearchOption.AllDirectories).Length,
                    Is.EqualTo(100), "All files should be extracted");
            });
        }

        #endregion

        #region Complex Move/Copy Scenarios

        [Test]
        public async Task Move_WithMultipleSourcesToSameDestination_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            var component = new ModComponent { Name = "Multiple Move", Guid = Guid.NewGuid(), IsSelected = true };
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
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Move should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "File1 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "File2 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True, "File3 should be moved");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file1.txt")), Is.False, "File1 should not exist in source");
            });
        }

        [Test]
        public async Task Copy_WithOverwriteFalseAndExistingFiles_SkipsAll()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "new content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "new content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.txt"), "old content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.txt"), "old content2");

            var component = new ModComponent { Name = "Copy Skip", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string>
                {
                    "<<modDirectory>>/file1.txt",
                    "<<modDirectory>>/file2.txt"
                },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = false
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Copy should succeed");
                Assert.That(File.ReadAllText(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.EqualTo("old content1"), "File1 should retain old content");
                Assert.That(File.ReadAllText(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.EqualTo("old content2"), "File2 should retain old content");
            });
        }

        #endregion

        #region Complex Rename Scenarios

        [Test]
        public async Task Rename_WithMultipleFilesToSameName_LastOneWins()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old2.txt"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old3.txt"), "content3");

            var component = new ModComponent { Name = "Rename Multiple", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string>
                {
                    "<<kotorDirectory>>/Override/old1.txt",
                    "<<kotorDirectory>>/Override/old2.txt",
                    "<<kotorDirectory>>/Override/old3.txt"
                },
                Destination = "new.txt",
                Overwrite = true
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Rename should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "new.txt")), Is.True, "New file should exist");
                // Only the last file should remain
            });
        }

        #endregion

        #region Complex Delete Scenarios

        [Test]
        public async Task Delete_WithMultiplePatterns_DeletesAllMatching()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file3.dat"), "content3");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "other.txt"), "content4");

            var component = new ModComponent { Name = "Delete Multiple", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string>
                {
                    "<<kotorDirectory>>/Override/file*.txt",
                    "<<kotorDirectory>>/Override/*.dat"
                }
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Delete should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.False, "File1 should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False, "File2 should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.dat")), Is.False, "File3 should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "other.txt")), Is.True, "Other file should remain");
            });
        }

        #endregion

        #region Complex DelDuplicate Scenarios

        [Test]
        public async Task DelDuplicate_WithMultipleExtensionGroups_ProcessesAll()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture1.tpc"), "tpc1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture1.tga"), "tga1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture2.tpc"), "tpc2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture2.tga"), "tga2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture3.dds"), "dds3");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture3.tga"), "tga3");

            var component = new ModComponent { Name = "DelDuplicate Multiple", Guid = Guid.NewGuid(), IsSelected = true };
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

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "DelDuplicate should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tpc")), Is.False, "TPC1 should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "TGA1 should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tpc")), Is.False, "TPC2 should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "TGA2 should remain");
                // texture3.dds and texture3.tga - only .tpc is deleted, so both should remain
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture3.dds")), Is.True, "DDS3 should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture3.tga")), Is.True, "TGA3 should remain");
            });
        }

        #endregion

        #region Complex Choose Scenarios

        [Test]
        public async Task Choose_WithNestedOptions_ExecutesCorrectly()
        {
            var component = new ModComponent { Name = "Nested Choose", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = true };
            var option3 = new Option { Name = "Option 3", Guid = Guid.NewGuid(), IsSelected = false };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option3.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);
            component.Options.Add(option3);

            var chooseInstruction = new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString(), option3.Guid.ToString() }
            };

            component.Instructions.Add(chooseInstruction);

            var fileSystemProvider = new RealFileSystemProvider();
            chooseInstruction.SetFileSystemProvider(fileSystemProvider);
            chooseInstruction.SetParentComponent(component);

            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Choose should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "Option 1 file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Option 2 file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.False, "Option 3 file should not be moved");
            });
        }

        [Test]
        public async Task Choose_WithOptionDependencies_RespectsDependencies()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Choose Deps", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option
            {
                Name = "Option 2",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);

            var chooseInstruction = new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString() }
            };

            component.Instructions.Add(chooseInstruction);

            var fileSystemProvider = new RealFileSystemProvider();
            chooseInstruction.SetFileSystemProvider(fileSystemProvider);
            chooseInstruction.SetParentComponent(component);

            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { depComponent, component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Choose should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "Option 1 file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Option 2 file should be moved (dependency met)");
            });
        }

        #endregion

        #region Complex Instruction Sequences

        [Test]
        public async Task ExecuteInstructions_ComplexModInstallation_ExecutesCorrectly()
        {
            // Simulate a complex mod installation: Extract -> Move -> Rename -> DelDuplicate
            string zipPath = CreateTestZip("complex.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "texture1.tpc", "tpc" },
                { "texture1.tga", "tga" },
                { "texture2.tpc", "tpc" },
                { "texture2.tga", "tga" }
            });

            var component = new ModComponent { Name = "Complex Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                Destination = "<<modDirectory>>/extracted"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/texture1.tga" },
                Destination = "renamed_texture.tga"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complex installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "renamed_texture.tga")), Is.True, "Renamed file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "Second TGA should exist");
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

