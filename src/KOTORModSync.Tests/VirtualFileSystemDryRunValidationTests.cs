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

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class VirtualFileSystemDryRunValidationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_VFSDryRunTests_" + Guid.NewGuid());
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

        #region VFS Dry-Run Validation Tests

        [Test]
        public async Task VFSDryRun_ExtractMoveSequence_TracksFileStateCorrectly()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" }
            });

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Test Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS dry-run should succeed");
                // Verify VFS tracked the file state
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "VFS should track moved file");
                Assert.That(vfs.FileExists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.False, "VFS should track that source file was moved");
            });
        }

        [Test]
        public async Task VFSDryRun_CopyDeleteSequence_TracksFileStateCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "source.txt"), "content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old.txt"), "old content");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Copy Delete", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/source.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" }
            });

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS dry-run should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "source.txt")), Is.True, "VFS should track copied file");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "old.txt")), Is.False, "VFS should track deleted file");
                Assert.That(vfs.FileExists(Path.Combine(_modDirectory, "source.txt")), Is.True, "VFS should track that source file remains (copy)");
            });
        }

        [Test]
        public async Task VFSDryRun_RenameInstruction_TracksFileStateCorrectly()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old.txt"), "content");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Rename", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" },
                Destination = "new.txt"
            });

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS dry-run should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "new.txt")), Is.True, "VFS should track renamed file");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "old.txt")), Is.False, "VFS should track that old filename is gone");
            });
        }

        [Test]
        public async Task VFSDryRun_DelDuplicateInstruction_TracksFileStateCorrectly()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc content");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS DelDuplicate", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS dry-run should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True, "VFS should track that TGA remains");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False, "VFS should track that TPC was deleted");
            });
        }

        [Test]
        public async Task VFSDryRun_OverwriteBehavior_TracksFileStateCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "new.txt"), "new content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "new.txt"), "old content");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Overwrite", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/new.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS dry-run should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "new.txt")), Is.True, "VFS should track overwritten file");
            });
        }

        [Test]
        public async Task VFSDryRun_WildcardOperations_TracksAllFiles()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.dat"), "content3");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Wildcard", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS dry-run should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "VFS should track first wildcard match");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "VFS should track second wildcard match");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file3.dat")), Is.False, "VFS should not track non-matching file");
            });
        }

        [Test]
        public async Task VFSDryRun_ComplexSequence_TracksAllStateChanges()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "texture.tga", "texture content" },
                { "model.mdl", "model content" }
            });

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "old tpc");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old.txt"), "old");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent { Name = "VFS Complex", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move texture
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/texture.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy model
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/extracted/model.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Delete duplicate TPC
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            // Delete old file
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" }
            });

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, vfs);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS complex sequence should succeed");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True, "VFS should track moved texture");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "model.mdl")), Is.True, "VFS should track copied model");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False, "VFS should track deleted duplicate TPC");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "old.txt")), Is.False, "VFS should track deleted old file");
            });
        }

        #endregion

        #region VFS vs Real File System Consistency

        [Test]
        public async Task VFSConsistency_DryRunThenRealRun_ProducesSameResults()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" }
            });

            var component = new ModComponent { Name = "Consistency Test", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // First: Dry-run with VFS
            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var vfsResult = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, vfs);

            // Second: Real run
            var realFs = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(realFs);
            }

            var realResult = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, realFs);

            Assert.Multiple(() =>
            {
                Assert.That(vfsResult, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS dry-run should succeed");
                Assert.That(realResult, Is.EqualTo(ModComponent.InstallExitCode.Success), "Real run should succeed");
                // Verify VFS predicted the final state correctly
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file1.txt")),
                    Is.EqualTo(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt"))),
                    "VFS should match real file system state");
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

