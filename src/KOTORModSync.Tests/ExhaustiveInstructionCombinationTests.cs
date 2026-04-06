// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections;
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
    public sealed class ExhaustiveInstructionCombinationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ExhaustiveTests_" + Guid.NewGuid());
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

        #region Exhaustive Overwrite Combinations

        public static IEnumerable<TestCaseData> OverwriteCombinations()
        {
            yield return new TestCaseData(Instruction.ActionType.Move, true, true).SetName("Move_OverwriteTrue_FileExists");
            yield return new TestCaseData(Instruction.ActionType.Move, true, false).SetName("Move_OverwriteFalse_FileExists");
            yield return new TestCaseData(Instruction.ActionType.Move, false, true).SetName("Move_OverwriteTrue_FileNotExists");
            yield return new TestCaseData(Instruction.ActionType.Move, false, false).SetName("Move_OverwriteFalse_FileNotExists");
            yield return new TestCaseData(Instruction.ActionType.Copy, true, true).SetName("Copy_OverwriteTrue_FileExists");
            yield return new TestCaseData(Instruction.ActionType.Copy, true, false).SetName("Copy_OverwriteFalse_FileExists");
            yield return new TestCaseData(Instruction.ActionType.Copy, false, true).SetName("Copy_OverwriteTrue_FileNotExists");
            yield return new TestCaseData(Instruction.ActionType.Copy, false, false).SetName("Copy_OverwriteFalse_FileNotExists");
            yield return new TestCaseData(Instruction.ActionType.Rename, true, true).SetName("Rename_OverwriteTrue_FileExists");
            yield return new TestCaseData(Instruction.ActionType.Rename, true, false).SetName("Rename_OverwriteFalse_FileExists");
            yield return new TestCaseData(Instruction.ActionType.Rename, false, true).SetName("Rename_OverwriteTrue_FileNotExists");
            yield return new TestCaseData(Instruction.ActionType.Rename, false, false).SetName("Rename_OverwriteFalse_FileNotExists");
        }

        [TestCaseSource(nameof(OverwriteCombinations))]
        public async Task Instruction_OverwriteCombinations_BehavesCorrectly(
            Instruction.ActionType actionType, bool destFileExists, bool overwrite)
        {
            string sourceFile = Path.Combine(_modDirectory, "source.txt");
            string destFile = Path.Combine(_kotorDirectory, "Override",
                actionType == Instruction.ActionType.Rename ? "old.txt" : "source.txt");

            File.WriteAllText(sourceFile, "source content");

            if (destFileExists)
            {
                File.WriteAllText(destFile, "existing content");
            }

            var component = new ModComponent { Name = "Overwrite Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = actionType,
                Source = new List<string> {
                    actionType == Instruction.ActionType.Rename
                        ? "<<kotorDirectory>>/Override/old.txt"
                        : "<<modDirectory>>/source.txt"
                },
                Destination = actionType == Instruction.ActionType.Rename ? "source.txt" : "<<kotorDirectory>>/Override",
                Overwrite = overwrite
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            if (actionType == Instruction.ActionType.Rename && !destFileExists)
            {
                // Rename requires source to exist
                File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old.txt"), "old content");
                result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
            }

            Assert.Multiple(() =>
            {
                if (destFileExists && !overwrite)
                {
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when skipping");
                    if (actionType != Instruction.ActionType.Rename || destFileExists)
                    {
                        Assert.That(File.ReadAllText(destFile), Is.EqualTo("existing content"),
                            "Existing file should not be overwritten");
                    }
                }
                else if (destFileExists && overwrite)
                {
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when overwriting");
                }
                else
                {
                    // File doesn't exist - should succeed
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                        "Should succeed when destination doesn't exist");
                }
            });
        }

        #endregion

        #region Exhaustive Dependency/Restriction Combinations

        public static IEnumerable<TestCaseData> DependencyRestrictionCombinations()
        {
            // Format: (dependencySelected, restrictionSelected, expectedResult)
            yield return new TestCaseData(true, false, true).SetName("DependencySelected_RestrictionNotSelected_Executes");
            yield return new TestCaseData(true, true, false).SetName("DependencySelected_RestrictionSelected_Skips");
            yield return new TestCaseData(false, false, false).SetName("DependencyNotSelected_RestrictionNotSelected_Skips");
            yield return new TestCaseData(false, true, false).SetName("DependencyNotSelected_RestrictionSelected_Skips");
        }

        [TestCaseSource(nameof(DependencyRestrictionCombinations))]
        public async Task Instruction_DependencyRestrictionCombinations_ExecutesCorrectly(
            bool dependencySelected, bool restrictionSelected, bool shouldExecute)
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = dependencySelected };
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = restrictionSelected };
            var component = new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid },
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { depComponent, restrictedComponent, component },
                fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            bool fileExists = File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt"));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should complete successfully");
                Assert.That(fileExists, Is.EqualTo(shouldExecute),
                    $"File should {(shouldExecute ? "exist" : "not exist")} based on dependency/restriction");
            });
        }

        #endregion

        #region Exhaustive Instruction Type Sequences

        public static IEnumerable<TestCaseData> InstructionSequenceCombinations()
        {
            // Test common instruction sequences
            yield return new TestCaseData(
                new[] { Instruction.ActionType.Extract, Instruction.ActionType.Move },
                "ExtractThenMove"
            ).SetName("Sequence_ExtractThenMove");

            yield return new TestCaseData(
                new[] { Instruction.ActionType.Extract, Instruction.ActionType.Copy },
                "ExtractThenCopy"
            ).SetName("Sequence_ExtractThenCopy");

            yield return new TestCaseData(
                new[] { Instruction.ActionType.Move, Instruction.ActionType.Rename },
                "MoveThenRename"
            ).SetName("Sequence_MoveThenRename");

            yield return new TestCaseData(
                new[] { Instruction.ActionType.Copy, Instruction.ActionType.Delete },
                "CopyThenDelete"
            ).SetName("Sequence_CopyThenDelete");

            yield return new TestCaseData(
                new[] { Instruction.ActionType.Extract, Instruction.ActionType.Move, Instruction.ActionType.Delete },
                "ExtractMoveDelete"
            ).SetName("Sequence_ExtractMoveDelete");
        }

        [TestCaseSource(nameof(InstructionSequenceCombinations))]
        public async Task Instruction_SequenceCombinations_ExecutesInOrder(
            Instruction.ActionType[] sequence, string testName)
        {
            // Setup based on sequence
            string archivePath = null;
            if (sequence.Contains(Instruction.ActionType.Extract))
            {
                archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "file.txt", "content" }
                });
            }

            if (sequence.Contains(Instruction.ActionType.Move) || sequence.Contains(Instruction.ActionType.Copy))
            {
                File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");
            }

            if (sequence.Contains(Instruction.ActionType.Rename))
            {
                File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old.txt"), "content");
            }

            if (sequence.Contains(Instruction.ActionType.Delete))
            {
                File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "delete.txt"), "content");
            }

            var component = new ModComponent { Name = "Sequence Test", Guid = Guid.NewGuid(), IsSelected = true };

            foreach (var actionType in sequence)
            {
                var instruction = new Instruction { Action = actionType };

                switch (actionType)
                {
                    case Instruction.ActionType.Extract:
                        instruction.Source = new List<string> { "<<modDirectory>>/mod.zip" };
                        instruction.Destination = "<<modDirectory>>/extracted";
                        break;
                    case Instruction.ActionType.Move:
                        instruction.Source = new List<string> { "<<modDirectory>>/file.txt" };
                        instruction.Destination = "<<kotorDirectory>>/Override";
                        break;
                    case Instruction.ActionType.Copy:
                        instruction.Source = new List<string> { "<<modDirectory>>/file.txt" };
                        instruction.Destination = "<<kotorDirectory>>/Override";
                        break;
                    case Instruction.ActionType.Rename:
                        instruction.Source = new List<string> { "<<kotorDirectory>>/Override/old.txt" };
                        instruction.Destination = "new.txt";
                        break;
                    case Instruction.ActionType.Delete:
                        instruction.Source = new List<string> { "<<kotorDirectory>>/Override/delete.txt" };
                        break;
                }

                component.Instructions.Add(instruction);
            }

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                $"Sequence {testName} should execute successfully");
        }

        #endregion

        #region Exhaustive Wildcard Pattern Tests

        public static IEnumerable<TestCaseData> WildcardPatternCombinations()
        {
            yield return new TestCaseData("*.txt", new[] { "file1.txt", "file2.txt" }, new[] { "file.dat" })
                .SetName("Wildcard_AllTxtFiles");
            yield return new TestCaseData("file*.txt", new[] { "file1.txt", "file2.txt" }, new[] { "other.txt", "file.dat" })
                .SetName("Wildcard_FilePrefixTxt");
            yield return new TestCaseData("test?.txt", new[] { "test1.txt" }, new[] { "test10.txt", "test.txt" })
                .SetName("Wildcard_SingleChar");
            yield return new TestCaseData("*.*", new[] { "file1.txt", "file2.dat", "test.txt" }, Array.Empty<string>())
                .SetName("Wildcard_AllFiles");
        }

        [TestCaseSource(nameof(WildcardPatternCombinations))]
        public async Task Move_WildcardPatternCombinations_MatchesCorrectly(
            string pattern, string[] shouldMatch, string[] shouldNotMatch)
        {
            // Create all test files
            foreach (var file in shouldMatch.Concat(shouldNotMatch))
            {
                File.WriteAllText(Path.Combine(_modDirectory, file), "content");
            }

            var component = new ModComponent { Name = "Wildcard Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{pattern}" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Wildcard move should succeed");

                foreach (var file in shouldMatch)
                {
                    Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", file)), Is.True,
                        $"File {file} should match pattern {pattern} and be moved");
                }

                foreach (var file in shouldNotMatch)
                {
                    Assert.That(File.Exists(Path.Combine(_modDirectory, file)), Is.True,
                        $"File {file} should NOT match pattern {pattern} and remain in source");
                }
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

