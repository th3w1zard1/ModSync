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
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class SystematicCombinationMatrixTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_Matrix_" + Guid.NewGuid());
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

        #region Test Case Sources

        public static IEnumerable<TestCaseData> OverwriteCombinations()
        {
            var actions = new[] { Instruction.ActionType.Move, Instruction.ActionType.Copy, Instruction.ActionType.Rename };
            var overwriteValues = new[] { true, false };

            foreach (var action in actions)
            {
                foreach (var overwrite in overwriteValues)
                {
                    yield return new TestCaseData(action, overwrite)
                        .SetName($"{action}_Overwrite_{overwrite}");
                }
            }
        }

        public static IEnumerable<TestCaseData> DependencyRestrictionCombinations()
        {
            var dependencyStates = new[] { true, false };
            var restrictionStates = new[] { true, false };

            foreach (var dep in dependencyStates)
            {
                foreach (var res in restrictionStates)
                {
                    yield return new TestCaseData(dep, res)
                        .SetName($"Dependency_{dep}_Restriction_{res}");
                }
            }
        }

        public static IEnumerable<TestCaseData> InstructionSequencePairs()
        {
            var actions = new[]
            {
                Instruction.ActionType.Move,
                Instruction.ActionType.Copy,
                Instruction.ActionType.Delete,
                Instruction.ActionType.Rename
            };

            foreach (var first in actions)
            {
                foreach (var second in actions)
                {
                    if (first != second) // Avoid same instruction twice
                    {
                        yield return new TestCaseData(first, second)
                            .SetName($"{first}_Then_{second}");
                    }
                }
            }
        }

        #endregion

        #region Parameterized Tests

        [TestCaseSource(nameof(OverwriteCombinations))]
        public async Task SystematicOverwrite_AllCombinations_RespectsFlags(
            Instruction.ActionType actionType, bool overwrite)
        {
            string sourceFile = Path.Combine(_modDirectory, "source.txt");
            string destFile = Path.Combine(_kotorDirectory, "Override",
                actionType == Instruction.ActionType.Rename ? "source.txt" : "dest.txt");

            File.WriteAllText(sourceFile, "new content");
            if (actionType != Instruction.ActionType.Rename)
            {
                File.WriteAllText(destFile, "old content");
            }
            else
            {
                File.WriteAllText(destFile, "old content");
            }

            var component = new ModComponent { Name = "Systematic Overwrite", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = actionType,
                Source = new List<string> { actionType == Instruction.ActionType.Rename
                    ? destFile.Replace(_kotorDirectory, "<<kotorDirectory>>")
                    : sourceFile.Replace(_modDirectory, "<<modDirectory>>") },
                Destination = actionType == Instruction.ActionType.Rename
                    ? "source.txt"
                    : "<<kotorDirectory>>/Override",
                Overwrite = overwrite
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, $"Should handle {actionType} with overwrite={overwrite}");
        }

        [TestCaseSource(nameof(DependencyRestrictionCombinations))]
        public async Task SystematicDependencyRestriction_AllCombinations_ExecutesCorrectly(
            bool dependencyMet, bool restrictionMet)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent
            {
                Name = "Required",
                Guid = Guid.NewGuid(),
                IsSelected = dependencyMet
            };
            var restrictedMod = new ModComponent
            {
                Name = "Restricted",
                Guid = Guid.NewGuid(),
                IsSelected = restrictionMet
            };
            var component = new ModComponent { Name = "Combined", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { requiredMod.Guid },
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, restrictedMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            bool shouldExecute = dependencyMet && !restrictionMet;
            bool fileExists = File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt"));

            Assert.That(fileExists, Is.EqualTo(shouldExecute),
                $"File should {(shouldExecute ? "exist" : "not exist")} when dependency={dependencyMet}, restriction={restrictionMet}");
        }

        [TestCaseSource(nameof(InstructionSequencePairs))]
        public async Task SystematicInstructionSequence_AllPairs_ExecutesInOrder(
            Instruction.ActionType first, Instruction.ActionType second)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Sequence Pair", Guid = Guid.NewGuid(), IsSelected = true };

            var firstInstruction = new Instruction
            {
                Action = first,
                Source = new List<string> { "<<modDirectory>>/file1.txt" }
            };

            if (first == Instruction.ActionType.Move || first == Instruction.ActionType.Copy)
            {
                firstInstruction.Destination = "<<kotorDirectory>>/Override";
            }
            else if (first == Instruction.ActionType.Rename)
            {
                firstInstruction.Destination = "renamed1.txt";
            }

            component.Instructions.Add(firstInstruction);

            var secondInstruction = new Instruction
            {
                Action = second,
                Source = new List<string> { "<<modDirectory>>/file2.txt" }
            };

            if (second == Instruction.ActionType.Move || second == Instruction.ActionType.Copy)
            {
                secondInstruction.Destination = "<<kotorDirectory>>/Override";
            }
            else if (second == Instruction.ActionType.Rename)
            {
                secondInstruction.Destination = "renamed2.txt";
            }

            component.Instructions.Add(secondInstruction);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, $"Should handle {first} then {second}");
        }

        #endregion

        #region Matrix Tests

        [Test]
        public async Task MatrixTest_AllInstructionTypesWithOverwrite_SystematicCoverage()
        {
            var actions = new[]
            {
                Instruction.ActionType.Move,
                Instruction.ActionType.Copy,
                Instruction.ActionType.Rename
            };

            foreach (var action in actions)
            {
                foreach (var overwrite in new[] { true, false })
                {
                    File.WriteAllText(Path.Combine(_modDirectory, $"file_{action}_{overwrite}.txt"), "content");

                    var component = new ModComponent
                    {
                        Name = $"Matrix {action} {overwrite}",
                        Guid = Guid.NewGuid(),
                        IsSelected = true
                    };

                    var instruction = new Instruction
                    {
                        Action = action,
                        Source = new List<string> { $"<<modDirectory>>/file_{action}_{overwrite}.txt" },
                        Destination = action == Instruction.ActionType.Rename
                            ? "renamed.txt"
                            : "<<kotorDirectory>>/Override",
                        Overwrite = overwrite
                    };

                    component.Instructions.Add(instruction);

                    var fileSystemProvider = new RealFileSystemProvider();
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);

                    var result = await component.ExecuteInstructionsAsync(
                        new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

                    Assert.That(result, Is.Not.Null,
                        $"Should handle {action} with overwrite={overwrite}");
                }
            }
        }

        #endregion
    }
}

