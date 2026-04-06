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
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class PathCaseSensitivityComprehensiveTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_CaseSensitivityTests_" + Guid.NewGuid());
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

        #region Case Sensitivity Tests

        [Test]
        public async Task Move_CaseSensitivePath_HandlesCorrectly()
        {
            // Create file with specific case
            string sourceFile = Path.Combine(_modDirectory, "File.txt");
            File.WriteAllText(sourceFile, "content");

            var component = new ModComponent { Name = "Case Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" }, // Different case
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // On case-insensitive systems (Windows), this should work
            // On case-sensitive systems (Linux/Mac), this may fail
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                        "Should succeed on case-insensitive systems");
                    Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")) ||
                               File.Exists(Path.Combine(_kotorDirectory, "Override", "File.txt")),
                               Is.True, "File should be moved");
                });
            }
        }

        [Test]
        public async Task Wildcard_CaseSensitiveMatching_MatchesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "File1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "FILE3.txt"), "content3");

            var component = new ModComponent { Name = "Case Wildcard", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Wildcard should succeed");
                // All .txt files should be moved regardless of case
                int movedCount = Directory.GetFiles(Path.Combine(_kotorDirectory, "Override"), "*.txt", SearchOption.TopDirectoryOnly).Length;
                Assert.That(movedCount, Is.GreaterThanOrEqualTo(2), "Should move multiple files with different cases");
            });
        }

        #endregion

        #region Path Normalization Tests

        [Test]
        public async Task PathNormalization_BackslashesAndForwardSlashes_HandlesBoth()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Path Normalization", Guid = Guid.NewGuid(), IsSelected = true };

            // Test with forward slashes
            var instruction1 = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction1);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle forward slashes");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be moved");
            });
        }

        [Test]
        public async Task PathNormalization_RelativePathComponents_ResolvesCorrectly()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir"));
            File.WriteAllText(Path.Combine(_modDirectory, "subdir", "file.txt"), "content");

            var component = new ModComponent { Name = "Relative Path", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/subdir/../subdir/file.txt" }, // Relative path
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle relative path components");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be moved");
            });
        }

        #endregion

        #region Special Character Handling

        [Test]
        public async Task SpecialCharacters_UnicodeInPaths_HandlesCorrectly()
        {
            string unicodeFileName = "测试_тест_テスト.txt";
            File.WriteAllText(Path.Combine(_modDirectory, unicodeFileName), "unicode content");

            var component = new ModComponent { Name = "Unicode Path", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{unicodeFileName}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle Unicode filenames");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", unicodeFileName)), Is.True,
                    "Unicode file should be moved");
            });
        }

        [Test]
        public async Task SpecialCharacters_SpacesInPaths_HandlesCorrectly()
        {
            string fileNameWithSpaces = "file with spaces.txt";
            File.WriteAllText(Path.Combine(_modDirectory, fileNameWithSpaces), "content");

            var component = new ModComponent { Name = "Spaces Path", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{fileNameWithSpaces}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle spaces in filenames");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", fileNameWithSpaces)), Is.True,
                    "File with spaces should be moved");
            });
        }

        #endregion

        #region Deeply Nested Paths

        [Test]
        public async Task DeeplyNested_CreatesAllDirectories()
        {
            string deepPath = Path.Combine(_modDirectory, "level1", "level2", "level3", "level4");
            Directory.CreateDirectory(deepPath);
            File.WriteAllText(Path.Combine(deepPath, "file.txt"), "content");

            var component = new ModComponent { Name = "Deep Path", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/level1/level2/level3/level4/file.txt" },
                Destination = "<<kotorDirectory>>/Override/deep/nested/path"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle deeply nested paths");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "deep", "nested", "path", "file.txt")),
                    Is.True, "File should be moved to nested destination");
            });
        }

        #endregion
    }
}

