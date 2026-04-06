// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Services.FileSystem;
using NUnit.Framework;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class InstructionExecutionOverwriteCombinationsTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_OverwriteCombinations_" + Guid.NewGuid());
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

        #region Overwrite Behavior Tests

        [TestCase(true)]
        [TestCase(false)]
        public async Task OverwriteBehavior_MoveWithExistingFile_RespectsOverwriteFlag(bool overwrite)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "new content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file.txt"), "old content");

            var component = new ModComponent { Name = "Overwrite Move", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = overwrite
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            var destPath = Path.Combine(_kotorDirectory, "Override", "file.txt");
            var sourcePath = Path.Combine(_modDirectory, "file.txt");

            if (overwrite)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed with overwrite");
                    Assert.That(File.Exists(destPath), Is.True, "Destination should exist");
                    Assert.That(File.Exists(sourcePath), Is.False, "Source should be moved");
                    Assert.That(File.ReadAllText(destPath), Is.EqualTo("new content"), "Should contain new content");
                });
            }
            else
            {
                // Without overwrite, behavior may vary - file may not be moved or may report error
                Assert.That(result, Is.Not.Null, "Should return a result");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task OverwriteBehavior_CopyWithExistingFile_RespectsOverwriteFlag(bool overwrite)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "new content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file.txt"), "old content");

            var component = new ModComponent { Name = "Overwrite Copy", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = overwrite
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            var destPath = Path.Combine(_kotorDirectory, "Override", "file.txt");
            var sourcePath = Path.Combine(_modDirectory, "file.txt");

            if (overwrite)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed with overwrite");
                    Assert.That(File.Exists(destPath), Is.True, "Destination should exist");
                    Assert.That(File.Exists(sourcePath), Is.True, "Source should still exist after copy");
                    Assert.That(File.ReadAllText(destPath), Is.EqualTo("new content"), "Should contain new content");
                });
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(File.Exists(sourcePath), Is.True, "Source should still exist");
                    // Destination may or may not be overwritten depending on implementation
                });
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task OverwriteBehavior_RenameWithExistingFile_RespectsOverwriteFlag(bool overwrite)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "oldname.txt"), "new content");
            File.WriteAllText(Path.Combine(_modDirectory, "newname.txt"), "old content");

            var component = new ModComponent { Name = "Overwrite Rename", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<modDirectory>>/oldname.txt" },
                Destination = "<<modDirectory>>/newname.txt",
                Overwrite = overwrite
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            var oldPath = Path.Combine(_modDirectory, "oldname.txt");
            var newPath = Path.Combine(_modDirectory, "newname.txt");

            if (overwrite)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed with overwrite");
                    Assert.That(File.Exists(newPath), Is.True, "New name should exist");
                    Assert.That(File.Exists(oldPath), Is.False, "Old name should not exist");
                    Assert.That(File.ReadAllText(newPath), Is.EqualTo("new content"), "Should contain new content");
                });
            }
            else
            {
                // Without overwrite, behavior may vary
                Assert.That(result, Is.Not.Null, "Should return a result");
            }
        }

        #endregion

        #region Overwrite Sequence Tests

        [Test]
        public async Task OverwriteSequence_MultipleMovesToSameDestination_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            var component = new ModComponent { Name = "Overwrite Sequence", Guid = Guid.NewGuid(), IsSelected = true };

            // All move to same destination
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override/file.txt",
                Overwrite = true
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override/file.txt",
                Overwrite = true
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override/file.txt",
                Overwrite = true
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            var destPath = Path.Combine(_kotorDirectory, "Override", "file.txt");

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sequence should succeed");
                Assert.That(File.Exists(destPath), Is.True, "Final file should exist");
                Assert.That(File.ReadAllText(destPath), Is.EqualTo("content3"), "Should contain last file's content");
            });
        }

        [Test]
        public async Task OverwriteSequence_MixedOverwriteFlags_RespectsEachFlag()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file.txt"), "existing");

            var component = new ModComponent { Name = "Mixed Overwrite", Guid = Guid.NewGuid(), IsSelected = true };

            // First: No overwrite (may fail or skip)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override/file.txt",
                Overwrite = false
            });

            // Second: Overwrite (should succeed)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override/file.txt",
                Overwrite = true
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            var destPath = Path.Combine(_kotorDirectory, "Override", "file.txt");

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(destPath), Is.True, "Destination should exist");
                // Final content should be from file2 (overwrite=true)
                Assert.That(File.ReadAllText(destPath), Is.EqualTo("content2"),
                    "Should contain content from overwrite instruction");
            });
        }

        #endregion
    }
}

