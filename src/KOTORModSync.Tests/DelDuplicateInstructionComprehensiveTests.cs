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
    public sealed class DelDuplicateInstructionComprehensiveTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_DelDuplicate_" + Guid.NewGuid());
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

        #region Basic DelDuplicate Tests

        [Test]
        public async Task DelDuplicate_BasicTextureConflict_DeletesSpecifiedExtension()
        {
            // Create duplicate files with different extensions
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc content");

            var component = new ModComponent { Name = "DelDuplicate Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" }, // Compatible extensions
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc" // Delete .tpc when duplicates exist
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "DelDuplicate should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True,
                    ".tga should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False,
                    ".tpc should be deleted");
            });
        }

        [Test]
        public async Task DelDuplicate_MultipleDuplicates_DeletesAllMatchingExtensions()
        {
            // Create multiple duplicate sets
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.tga"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.tpc"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.tga"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.tpc"), "content2");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file3.tga"), "content3");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file3.tpc"), "content3");

            var component = new ModComponent { Name = "Multiple Duplicates", Guid = Guid.NewGuid(), IsSelected = true };
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.tga")), Is.True,
                    "file1.tga should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.tpc")), Is.False,
                    "file1.tpc should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.tga")), Is.True,
                    "file2.tga should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.tpc")), Is.False,
                    "file2.tpc should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.tga")), Is.True,
                    "file3.tga should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.tpc")), Is.False,
                    "file3.tpc should be deleted");
            });
        }

        [Test]
        public async Task DelDuplicate_NoDuplicates_DoesNothing()
        {
            // Create files with no duplicates
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.tga"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.tpc"), "content2");

            var component = new ModComponent { Name = "No Duplicates", Guid = Guid.NewGuid(), IsSelected = true };
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.tga")), Is.True,
                    "file1.tga should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.tpc")), Is.True,
                    "file2.tpc should remain (no duplicate)");
            });
        }

        #endregion

        #region Extension Combination Tests

        [Test]
        public async Task DelDuplicate_ThreeExtensions_DeletesSpecifiedOne()
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True,
                    ".tga should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False,
                    ".tpc should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.dds")), Is.True,
                    ".dds should remain");
            });
        }

        [Test]
        public async Task DelDuplicate_CaseInsensitiveMatching_HandlesCaseVariations()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "TEXTURE.TGA"), "tga");
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

            // Should match despite case difference
            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
        }

        #endregion

        #region Edge Cases

        [Test]
        public async Task DelDuplicate_EmptyDirectory_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Empty Directory", Guid = Guid.NewGuid(), IsSelected = true };
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

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Empty directory should be handled gracefully");
        }

        [Test]
        public async Task DelDuplicate_OnlyOneExtension_DoesNothing()
        {
            // Only one extension type, no duplicates possible
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.tga"), "content1");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.tga"), "content2");

            var component = new ModComponent { Name = "Single Extension", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tga" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.tga")), Is.True,
                    "file1.tga should remain");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.tga")), Is.True,
                    "file2.tga should remain");
            });
        }

        #endregion
    }
}

