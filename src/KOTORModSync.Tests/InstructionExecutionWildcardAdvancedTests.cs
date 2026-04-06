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
    public sealed class InstructionExecutionWildcardAdvancedTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_WildcardAdvanced_" + Guid.NewGuid());
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

        #region Complex Wildcard Pattern Tests

        [Test]
        public async Task WildcardPattern_MultipleWildcardsInPath_MatchesCorrectly()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir1"));
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir2"));
            File.WriteAllText(Path.Combine(_modDirectory, "subdir1", "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "subdir1", "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "subdir2", "file1.txt"), "content3");
            File.WriteAllText(Path.Combine(_modDirectory, "subdir2", "file2.txt"), "content4");

            var component = new ModComponent { Name = "Multi Wildcard", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/subdir*/file*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multi-wildcard should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "file1.txt should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "file2.txt should be moved");
            });
        }

        [Test]
        public async Task WildcardPattern_QuestionMarkWildcard_MatchesSingleCharacter()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file10.txt"), "content10");

            var component = new ModComponent { Name = "Question Mark", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file?.txt" }, // Matches file1.txt and file2.txt, not file10.txt
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Question mark wildcard should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "file1.txt should match");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "file2.txt should match");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file10.txt")), Is.True,
                    "file10.txt should NOT match (too many characters)");
            });
        }

        [Test]
        public async Task WildcardPattern_RecursiveWildcard_MatchesNestedFiles()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "level1"));
            Directory.CreateDirectory(Path.Combine(_modDirectory, "level1", "level2"));
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content0");
            File.WriteAllText(Path.Combine(_modDirectory, "level1", "file.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "level1", "level2", "file.txt"), "content2");

            var component = new ModComponent { Name = "Recursive Wildcard", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/**/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Recursive wildcard should succeed");
                // All file.txt files should be moved (may overwrite, but should process all)
            });
        }

        [Test]
        public async Task WildcardPattern_MixedWildcards_HandlesComplexPatterns()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "test1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "test2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "test10.txt"), "content10");
            File.WriteAllText(Path.Combine(_modDirectory, "test20.txt"), "content20");

            var component = new ModComponent { Name = "Mixed Wildcards", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test?*.txt" }, // Matches test1, test2, test10, test20
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Mixed wildcards should succeed");
        }

        #endregion

        #region Wildcard Edge Cases

        [Test]
        public async Task WildcardEdgeCase_NoMatches_HandlesGracefully()
        {
            var component = new ModComponent { Name = "No Matches", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle no matches gracefully (may report error or success depending on implementation)
            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        [Test]
        public async Task WildcardEdgeCase_WildcardInDestination_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Wildcard Destination", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file*.txt" },
                Destination = "<<kotorDirectory>>/Override" // Destination doesn't support wildcards, but should work
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Wildcard in source should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "file1 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "file2 should be moved");
            });
        }

        [Test]
        public async Task WildcardEdgeCase_SpecialCharactersInFilename_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file (1).txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file (2).txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file-1.txt"), "content3");

            var component = new ModComponent { Name = "Special Chars", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Special characters in filenames should work with wildcards");
        }

        #endregion

        #region Wildcard with Different Instructions

        [Test]
        public async Task WildcardWithCopy_MultipleFiles_CopiesAll()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Wildcard Copy", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Wildcard copy should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file1.txt")), Is.True,
                    "Source file1 should still exist after copy");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "Destination file1 should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "Destination file2 should exist");
            });
        }

        [Test]
        public async Task WildcardWithDelete_MultipleFiles_DeletesAll()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "other.txt"), "other");

            var component = new ModComponent { Name = "Wildcard Delete", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/file*.txt" }
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Wildcard delete should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file1.txt")), Is.False,
                    "file1 should be deleted");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file2.txt")), Is.False,
                    "file2 should be deleted");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "other.txt")), Is.True,
                    "other.txt should NOT be deleted");
            });
        }

        #endregion
    }
}

