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
    public sealed class InstructionExecutionProviderTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ProviderTests_" + Guid.NewGuid());
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

        #region VFS vs Real File System Provider Tests

        [Test]
        public async Task ProviderComparison_VFSAndRealFS_ProduceSameResults()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Provider Test", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Test with VFS
            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                instruction.SetParentComponent(component);
            }

            var vfsResult = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, vfs, System.Threading.CancellationToken.None, vfs);

            // Test with Real FS
            var realFs = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(realFs);
            }

            var realResult = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, realFs, System.Threading.CancellationToken.None, realFs);

            Assert.Multiple(() =>
            {
                Assert.That(vfsResult, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS should succeed");
                Assert.That(realResult, Is.EqualTo(ModComponent.InstallExitCode.Success), "Real FS should succeed");
                // VFS should predict the same state as real FS
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file1.txt")),
                    Is.EqualTo(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt"))),
                    "VFS should match real FS state");
            });
        }

        [Test]
        public async Task ProviderComparison_VFSDryRun_DoesNotModifyRealFilesystem()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "VFS Dry Run", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            // Execute with VFS (dry run)
            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            instruction.SetFileSystemProvider(vfs);
            instruction.SetParentComponent(component);

            var vfsResult = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, vfs, System.Threading.CancellationToken.None, vfs);

            // Verify real filesystem is unchanged
            Assert.Multiple(() =>
            {
                Assert.That(vfsResult, Is.EqualTo(ModComponent.InstallExitCode.Success), "VFS should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file.txt")), Is.True,
                    "Real file should still exist (VFS is dry-run)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "Real destination should not have file (VFS is dry-run)");
                Assert.That(vfs.FileExists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "VFS should track the file as moved");
            });
        }

        #endregion

        #region Instruction Execution with Different Providers

        [Test]
        public async Task ProviderSwitching_SameInstructionWithDifferentProviders_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Provider Switch", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            // First with VFS
            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            instruction.SetFileSystemProvider(vfs);
            instruction.SetParentComponent(component);

            var vfsResult = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, vfs);

            // Then with Real FS
            var realFs = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(realFs);

            var realResult = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, realFs);

            Assert.Multiple(() =>
            {
                Assert.That(vfsResult, Is.EqualTo(Instruction.ActionExitCode.Success), "VFS should succeed");
                Assert.That(realResult, Is.EqualTo(Instruction.ActionExitCode.Success), "Real FS should succeed");
            });
        }

        #endregion

        #region Instruction Execution Edge Cases

        [Test]
        public async Task InstructionExecution_ComponentWithNoInstructions_Succeeds()
        {
            var component = new ModComponent { Name = "No Instructions", Guid = Guid.NewGuid(), IsSelected = true };
            // No instructions added

            var fileSystemProvider = new RealFileSystemProvider();

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Component with no instructions should succeed");
        }

        [Test]
        public async Task InstructionExecution_InstructionWithNullParentComponent_HandlesGracefully()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Null Parent", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            // Don't set parent component

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle missing parent component gracefully
            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        [Test]
        public async Task InstructionExecution_MultipleInstructionsWithSameSource_ProcessesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Same Source", Guid = Guid.NewGuid(), IsSelected = true };

            // First instruction: Copy
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Second instruction: Move (file still exists at source after copy)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Same source should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should exist at destination");
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

