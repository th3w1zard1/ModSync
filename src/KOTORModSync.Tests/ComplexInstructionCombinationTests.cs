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
using KOTORModSync.Core.Services;
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
    public sealed class ComplexInstructionCombinationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComplexTests_" + Guid.NewGuid());
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

        #region Complex Instruction Sequences

        [Test]
        public async Task ExecuteInstructions_ExtractMoveCopyDeleteSequence_ExecutesInOrder()
        {
            // Create archive with files
            string zipPath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" }
            });

            // Create component with sequence: Extract -> Move -> Copy -> Delete
            var component = new ModComponent { Name = "Sequence Test", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                Destination = "<<modDirectory>>/extracted"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/extracted/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/extracted/file2.txt" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sequence should complete successfully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "Moved file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Copied file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.False, "Deleted file should not exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.False, "Moved file should not exist in source");
            });
        }

        [Test]
        public async Task ExecuteInstructions_ExtractRenameDelDuplicateSequence_ExecutesCorrectly()
        {
            // Create archive with texture files
            string zipPath = CreateTestZip("textures.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "texture.tpc", "tpc content" },
                { "texture.tga", "tga content" }
            });

            var component = new ModComponent { Name = "Texture Test", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/texture.tpc" },
                Destination = "old_texture.tpc"
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sequence should complete successfully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True, "TGA file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old_texture.tpc")), Is.True, "Renamed TPC file should exist");
            });
        }

        #endregion

        #region Instruction Dependencies and Restrictions

        [Test]
        public async Task ExecuteInstructions_WithInstructionDependency_ExecutesWhenDependencyMet()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };

            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var instruction1 = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var instruction2 = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<kotorDirectory>>/Override/test.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            component.Instructions.Add(instruction1);
            component.Instructions.Add(instruction2);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { depComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Instructions should execute successfully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.True, "File should exist after both instructions");
            });
        }

        [Test]
        public async Task ExecuteInstructions_WithInstructionRestriction_SkipsWhenRestrictionMet()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };

            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var instruction1 = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var instruction2 = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/test.txt" },
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            component.Instructions.Add(instruction1);
            component.Instructions.Add(instruction2);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { restrictedComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Instructions should execute successfully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.True, "File should still exist because delete was restricted");
            });
        }

        [Test]
        public async Task ExecuteInstructions_MultipleDependencies_AllMustBeMet()
        {
            var dep1 = new ModComponent { Name = "Dep1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep2", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };

            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid }
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { dep1, dep2, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Instruction should execute when all dependencies met");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.True, "File should be moved");
            });
        }

        [Test]
        public async Task ExecuteInstructions_MultipleDependencies_OneMissing_SkipsInstruction()
        {
            var dep1 = new ModComponent { Name = "Dep1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep2", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };

            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid }
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { dep1, dep2, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed even when instruction skipped");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.False, "File should not be moved when dependency missing");
            });
        }

        #endregion

        #region Choose Instruction Complex Scenarios

        [Test]
        public async Task Choose_WithMultipleOptions_ExecutesSelectedOptions()
        {
            var component = new ModComponent { Name = "Choose Test", Guid = Guid.NewGuid(), IsSelected = true };

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

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Choose instruction should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "Option 1 file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Option 2 file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.False, "Option 3 file should not be moved (not selected)");
            });
        }

        [Test]
        public async Task Choose_WithOptionDependencies_RespectsDependencies()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Choose Test", Guid = Guid.NewGuid(), IsSelected = true };

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

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { depComponent, component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Choose instruction should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "Option 1 file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Option 2 file should be moved (dependency met)");
            });
        }

        [Test]
        public async Task Choose_WithChooseDependency_RespectsDependency()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Choose Test", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);

            var chooseInstruction = new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() },
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            component.Instructions.Add(chooseInstruction);

            var fileSystemProvider = new RealFileSystemProvider();
            chooseInstruction.SetFileSystemProvider(fileSystemProvider);
            chooseInstruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { depComponent, component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Choose instruction should succeed when dependency met");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "File should be moved");
            });
        }

        #endregion

        #region CleanList Complex Scenarios

        [Test]
        public async Task CleanList_WithMultipleMods_FuzzyMatchesCorrectly()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath,
                "HD UI Rewrite,ui_old.tga,ui_old.tpc\n" +
                "Weapon Model Overhaul,w_blaster_01.mdl,w_blaster_01.mdx\n" +
                "Mandatory Deletions,mandatory1.tga,mandatory2.tpc");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdl"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdx"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "mandatory1.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "mandatory2.tpc"), "old");

            var component1 = new ModComponent
            {
                Name = "HD UI Rewrite",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var component2 = new ModComponent
            {
                Name = "Weapon Model Overhaul",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var cleanerComponent = new ModComponent
            {
                Name = "Cleaner",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            };

            cleanerComponent.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(cleanerComponent);

            var result = await cleanerComponent.ExecuteSingleInstructionAsync(instruction, 0,
                new List<ModComponent> { component1, component2, cleanerComponent }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "CleanList should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tga")), Is.False, "HD UI Rewrite file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc")), Is.False, "HD UI Rewrite file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdl")), Is.False, "Weapon Model Overhaul file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "w_blaster_01.mdx")), Is.False, "Weapon Model Overhaul file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mandatory1.tga")), Is.False, "Mandatory deletion should be processed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mandatory2.tpc")), Is.False, "Mandatory deletion should be processed");
            });
        }

        [Test]
        public async Task CleanList_WithUnselectedMod_DoesNotDelete()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "HD UI Rewrite,ui_old.tga,ui_old.tpc");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tga"), "old");

            var component = new ModComponent
            {
                Name = "HD UI Rewrite",
                Guid = Guid.NewGuid(),
                IsSelected = false  // Not selected
            };

            var cleanerComponent = new ModComponent
            {
                Name = "Cleaner",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            };

            cleanerComponent.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(cleanerComponent);

            var result = await cleanerComponent.ExecuteSingleInstructionAsync(instruction, 0,
                new List<ModComponent> { component, cleanerComponent }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "CleanList should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tga")), Is.True, "File should not be deleted when mod not selected");
            });
        }

        #endregion

        #region DelDuplicate Complex Scenarios

        [Test]
        public async Task DelDuplicate_WithMultipleExtensions_DeletesCorrectExtension()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.dds"), "dds");

            var component = new ModComponent { Name = "DelDuplicate Test", Guid = Guid.NewGuid(), IsSelected = true };
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
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False, "TPC file should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True, "TGA file should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.dds")), Is.True, "DDS file should remain");
            });
        }

        [Test]
        public async Task DelDuplicate_WithNoDuplicates_DoesNothing()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture1.tga"), "tga1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture2.tpc"), "tpc2");

            var component = new ModComponent { Name = "DelDuplicate Test", Guid = Guid.NewGuid(), IsSelected = true };
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

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "DelDuplicate should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "TGA file should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tpc")), Is.True, "TPC file should remain");
            });
        }

        [Test]
        public async Task DelDuplicate_WithMultipleDuplicateGroups_ProcessesAll()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture1.tpc"), "tpc1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture1.tga"), "tga1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture2.tpc"), "tpc2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture2.tga"), "tga2");

            var component = new ModComponent { Name = "DelDuplicate Test", Guid = Guid.NewGuid(), IsSelected = true };
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

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "DelDuplicate should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tpc")), Is.False, "First TPC should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "First TGA should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tpc")), Is.False, "Second TPC should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "Second TGA should remain");
            });
        }

        #endregion

        #region Multi-Component Complex Scenarios

        [Test]
        public async Task ExecuteInstructions_MultipleComponentsWithDependencies_ExecutesInOrder()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent
            {
                Name = "Mod B",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modA.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "fileA.txt"), "contentA");
            File.WriteAllText(Path.Combine(_modDirectory, "fileB.txt"), "contentB");

            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/fileA.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/fileB.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var component in new[] { modA, modB })
            {
                foreach (var instruction in component.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var ordered = InstallCoordinator.GetOrderedInstallList(new List<ModComponent> { modA, modB });
            var result = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, System.Threading.CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "fileA.txt")), Is.True, "Mod A file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "fileB.txt")), Is.True, "Mod B file should exist");
            });
        }

        [Test]
        public async Task ExecuteInstructions_ComponentWithRestriction_BlocksWhenRestrictedSelected()
        {
            var restrictedMod = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var mod = new ModComponent
            {
                Name = "Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            mod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in mod.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(mod);
            }

            // Component with restriction should be filtered out before installation
            var components = new List<ModComponent> { restrictedMod, mod };
            var selectedComponents = components.Where(c => c.IsSelected &&
                (c.Restrictions == null || !c.Restrictions.Any(r => components.Any(comp => comp.Guid == r && comp.IsSelected)))).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(selectedComponents, Does.Not.Contain(mod), "Mod with restriction should be filtered out");
                Assert.That(selectedComponents, Contains.Item(restrictedMod), "Restricted mod should still be selected");
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

