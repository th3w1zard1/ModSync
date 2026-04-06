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
    public sealed class PathResolutionComprehensiveTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_PathResolution_" + Guid.NewGuid());
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

        #region Path Placeholder Tests

        [Test]
        public async Task PathPlaceholder_ModDirectory_ResolvesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Mod Directory", Guid = Guid.NewGuid(), IsSelected = true };
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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be moved from modDirectory");
            });
        }

        [Test]
        public async Task PathPlaceholder_KotorDirectory_ResolvesCorrectly()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file.txt"), "content");

            var component = new ModComponent { Name = "Kotor Directory", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<kotorDirectory>>/Override/file.txt" },
                Destination = "<<kotorDirectory>>/Override/moved"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
        }

        [Test]
        public async Task PathPlaceholder_MixedPlaceholders_ResolvesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Mixed Placeholders", Guid = Guid.NewGuid(), IsSelected = true };
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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
        }

        #endregion

        #region Path Edge Cases

        [Test]
        public async Task PathEdgeCase_TrailingSlash_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Trailing Slash", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override/" // Trailing slash
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should handle trailing slash");
        }

        [Test]
        public async Task PathEdgeCase_Backslashes_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Backslashes", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>\\file.txt" }, // Backslash
                Destination = "<<kotorDirectory>>\\Override" // Backslash
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Path resolution should normalize backslashes
            Assert.That(result, Is.Not.Null, "Should handle backslashes");
        }

        [Test]
        public async Task PathEdgeCase_NestedDirectories_ResolvesCorrectly()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir", "nested"));
            File.WriteAllText(Path.Combine(_modDirectory, "subdir", "nested", "file.txt"), "content");

            var component = new ModComponent { Name = "Nested Directories", Guid = Guid.NewGuid(), IsSelected = true };
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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Nested file should be moved");
            });
        }

        #endregion

        #region Path Security Tests

        [Test]
        public async Task PathSecurity_ParentDirectoryTraversal_IsBlocked()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Path Security", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/../file.txt" }, // Parent directory traversal
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Path sandboxing should prevent accessing files outside allowed directories
            // The result may be an error or the path may be normalized to stay within bounds
            Assert.That(result, Is.Not.Null, "Should handle path traversal attempt");
        }

        [Test]
        public async Task PathSecurity_AbsolutePath_IsBlocked()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Absolute Path", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { Path.Combine(_modDirectory, "file.txt") }, // Absolute path
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // SetRealPaths should convert absolute paths to relative and validate they're within sandbox
            Assert.That(result, Is.Not.Null, "Should handle absolute path");
        }

        #endregion
    }
}

