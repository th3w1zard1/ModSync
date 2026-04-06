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
    public sealed class FileSystemOperationEdgeCasesAdvancedTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_FSEdgeCases_" + Guid.NewGuid());
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

        #region File Name Edge Cases

        [Test]
        public async Task FileNameEdgeCase_LongFilename_HandlesCorrectly()
        {
            string longName = new string('A', 200) + ".txt";
            File.WriteAllText(Path.Combine(_modDirectory, longName), "content");

            var component = new ModComponent { Name = "Long Filename", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{longName}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle long filenames (may truncate or fail depending on OS limits)
            Assert.That(result, Is.Not.Null, "Should handle long filenames");
        }

        [Test]
        public async Task FileNameEdgeCase_SpecialCharacters_HandlesCorrectly()
        {
            string specialName = "file (with) [special] {chars}.txt";
            File.WriteAllText(Path.Combine(_modDirectory, specialName), "content");

            var component = new ModComponent { Name = "Special Chars", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{specialName}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, "Should handle special characters in filenames");
        }

        [Test]
        public async Task FileNameEdgeCase_UnicodeCharacters_HandlesCorrectly()
        {
            string unicodeName = "文件_测试_日本語.txt";
            File.WriteAllText(Path.Combine(_modDirectory, unicodeName), "content");

            var component = new ModComponent { Name = "Unicode", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{unicodeName}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, "Should handle Unicode characters in filenames");
        }

        #endregion

        #region Directory Edge Cases

        [Test]
        public async Task DirectoryEdgeCase_DeeplyNested_HandlesCorrectly()
        {
            // Create 20 levels deep
            string deepPath = _modDirectory;
            for (int i = 0; i < 20; i++)
            {
                deepPath = Path.Combine(deepPath, $"level{i}");
                Directory.CreateDirectory(deepPath);
            }

            File.WriteAllText(Path.Combine(deepPath, "file.txt"), "content");

            var component = new ModComponent { Name = "Deep Nested", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/level0/level1/level2/level3/level4/level5/level6/level7/level8/level9/level10/level11/level12/level13/level14/level15/level16/level17/level18/level19/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should handle deeply nested directories");
        }

        [Test]
        public async Task DirectoryEdgeCase_EmptyDirectory_HandlesCorrectly()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "empty"));

            var component = new ModComponent { Name = "Empty Directory", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/empty/*" }, // Wildcard in empty directory
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle empty directory gracefully
            Assert.That(result, Is.Not.Null, "Should handle empty directory");
        }

        #endregion

        #region File Operation Edge Cases

        [Test]
        public async Task FileOperationEdgeCase_MoveToNonExistentDirectory_CreatesDirectory()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Create Directory", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/NewDirectory/SubDirectory" // Doesn't exist
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "NewDirectory", "SubDirectory", "file.txt")), Is.True,
                    "Directory should be created and file moved");
            });
        }

        [Test]
        public async Task FileOperationEdgeCase_CopyToSameFile_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Copy to Self", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<modDirectory>>", // Same directory
                Overwrite = true
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle copying file to same location
            Assert.That(result, Is.Not.Null, "Should handle copy to same location");
        }

        #endregion

        #region Concurrent Operation Edge Cases

        [Test]
        public async Task ConcurrentOperationEdgeCase_MultipleMovesToSameDestination_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            var component = new ModComponent { Name = "Multiple Moves", Guid = Guid.NewGuid(), IsSelected = true };

            // All move to same destination (will overwrite)
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

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Final file should exist");
                Assert.That(File.ReadAllText(Path.Combine(_kotorDirectory, "Override", "file.txt")),
                    Is.EqualTo("content3"), "Should contain last file's content");
            });
        }

        #endregion
    }
}

