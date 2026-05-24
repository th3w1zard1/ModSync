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

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class VirtualFileSystemComplexTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_VFSTests_" + Guid.NewGuid());
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

        #region VFS Complex Sequences

        [Test]
        public async Task VFS_ExtractMoveCopyDeleteSequence_TracksStateCorrectly()
        {
            // Create archive
            string zipPath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" }
            });

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Sequence", Guid = Guid.NewGuid(), IsSelected = true };

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

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS sequence should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "VFS should track moved file");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "VFS should track copied file");
                Assert.That(vfs.FileExists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.False, "VFS should track deleted file");
            });
        }

        [Test]
        public async Task VFS_WithOverwriteOperations_TracksStateCorrectly()
        {
            // Create initial file
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "old content");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "new content");

            var component = new ModComponent { Name = "VFS Overwrite", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            };

            component.Instructions.Add(instruction);

            instruction.SetFileSystemProvider(vfs);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "VFS overwrite should succeed");
                Assert.That(vfs.FileExists(destFile), Is.True, "VFS should track overwritten file");
            });
        }

        [Test]
        public async Task VFS_WithRenameOperation_TracksStateCorrectly()
        {
            string sourceFile = Path.Combine(_kotorDirectory, "Override", "old.txt");
            File.WriteAllText(sourceFile, "content");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Rename", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" },
                Destination = "new.txt"
            };

            component.Instructions.Add(instruction);

            instruction.SetFileSystemProvider(vfs);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "VFS rename should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "old.txt")), Is.False, "VFS should track old file as deleted");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "new.txt")), Is.True, "VFS should track new file");
            });
        }

        [Test]
        public async Task VFS_WithDelDuplicateOperation_TracksStateCorrectly()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS DelDuplicate", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            };

            component.Instructions.Add(instruction);

            instruction.SetFileSystemProvider(vfs);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "VFS DelDuplicate should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False, "VFS should track deleted TPC");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True, "VFS should track remaining TGA");
            });
        }

        #endregion

        #region VFS Validation Scenarios

        [Test]
        public async Task VFS_ValidateComponent_WithComplexInstructions_ValidatesCorrectly()
        {
            // Create files for validation
            string zipPath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" }
            });
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent
            {
                Name = "VFS Validation",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                        Destination = "<<modDirectory>>/extracted"
                    },
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file2.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "All files should be found in VFS");
            });
        }

        [Test]
        public async Task VFS_ValidateComponent_WithWildcards_ValidatesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "other.dat"), "content3");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent
            {
                Name = "VFS Wildcard",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/*.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "Wildcard pattern should match existing files");
            });
        }

        #endregion

        #region VFS Multi-Component Scenarios

        [Test]
        public async Task VFS_MultipleComponents_SequentialOperations_TracksStateCorrectly()
        {
            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "fileA.txt"), "contentA");
            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/fileA.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "fileB.txt"), "contentB");
            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/fileB.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            foreach (var component in new[] { modA, modB })
            {
                foreach (var instruction in component.Instructions)
                {
                    instruction.SetFileSystemProvider(vfs);
                    instruction.SetParentComponent(component);
                }
            }

            var resultA = await modA.ExecuteInstructionsAsync(modA.Instructions, new List<ModComponent> { modA, modB }, System.Threading.CancellationToken.None, vfs);
            var resultB = await modB.ExecuteInstructionsAsync(modB.Instructions, new List<ModComponent> { modA, modB }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(resultA, Is.EqualTo(ModComponent.InstallExitCode.Success), "Mod A should succeed");
                Assert.That(resultB, Is.EqualTo(ModComponent.InstallExitCode.Success), "Mod B should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "fileA.txt")), Is.True, "VFS should track Mod A file");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "fileB.txt")), Is.True, "VFS should track Mod B file");
            });
        }

        [Test]
        public async Task VFS_ComponentWithDependency_TracksDependentOperations()
        {
            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var depMod = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "dep.txt"), "dep content");
            depMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/dep.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var dependentMod = new ModComponent
            {
                Name = "Dependent",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depMod.Guid }
            };
            File.WriteAllText(Path.Combine(_modDirectory, "dependent.txt"), "dependent content");
            dependentMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/dependent.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            foreach (var component in new[] { depMod, dependentMod })
            {
                foreach (var instruction in component.Instructions)
                {
                    instruction.SetFileSystemProvider(vfs);
                    instruction.SetParentComponent(component);
                }
            }

            var ordered = InstallCoordinator.GetOrderedInstallList(new List<ModComponent> { depMod, dependentMod });
            var result = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, System.Threading.CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "dep.txt")), Is.True, "VFS should track dependency file");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "dependent.txt")), Is.True, "VFS should track dependent file");
            });
        }

        #endregion

        #region VFS Choose Instruction Scenarios

        [Test]
        public async Task VFS_ChooseInstruction_WithMultipleOptions_TracksStateCorrectly()
        {
            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Choose", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
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

            chooseInstruction.SetFileSystemProvider(vfs);
            chooseInstruction.SetParentComponent(component);

            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(vfs);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteSingleInstructionAsync(chooseInstruction, 0, new List<ModComponent> { component }, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Choose should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "VFS should track selected option file");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.False, "VFS should not track unselected option file");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            using (var archive = ZipArchive.CreateArchive())
            {
                foreach (var kvp in files)
                {
                    archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                }
                using (var stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new SharpCompress.Writers.Zip.ZipWriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion
    }
}

