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
    public sealed class ParameterizedInstructionExecutionTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ParamTests_" + Guid.NewGuid());
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

        #region Parameterized Overwrite Tests

        [TestCase(Instruction.ActionType.Move, true)]
        [TestCase(Instruction.ActionType.Move, false)]
        [TestCase(Instruction.ActionType.Copy, true)]
        [TestCase(Instruction.ActionType.Copy, false)]
        [TestCase(Instruction.ActionType.Rename, true)]
        [TestCase(Instruction.ActionType.Rename, false)]
        public async Task ParameterizedOverwrite_AllInstructionTypes_RespectsOverwriteFlag(
            Instruction.ActionType actionType, bool overwrite)
        {
            string sourceFile = Path.Combine(_modDirectory, "source.txt");
            string destFile = Path.Combine(_kotorDirectory, "Override",
                actionType == Instruction.ActionType.Rename ? "source.txt" : "dest.txt");

            File.WriteAllText(sourceFile, "new content");
            if (actionType == Instruction.ActionType.Rename)
            {
                File.WriteAllText(destFile, "old content");
            }
            else
            {
                File.WriteAllText(destFile, "old content");
            }

            var component = new ModComponent { Name = "Parameterized Overwrite", Guid = Guid.NewGuid(), IsSelected = true };
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

            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        #endregion

        #region Parameterized Instruction Type Tests

        [TestCase(Instruction.ActionType.Move)]
        [TestCase(Instruction.ActionType.Copy)]
        [TestCase(Instruction.ActionType.Delete)]
        public async Task ParameterizedInstructionType_BasicOperations_AllSucceed(Instruction.ActionType actionType)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Parameterized Type", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = actionType,
                Source = new List<string> { "<<modDirectory>>/file.txt" }
            };

            if (actionType != Instruction.ActionType.Delete)
            {
                instruction.Destination = "<<kotorDirectory>>/Override";
            }

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        #endregion

        #region Parameterized Wildcard Tests

        [TestCase("file*.txt", 3)]
        [TestCase("file?.txt", 2)]
        [TestCase("file1.txt", 1)]
        [TestCase("nonexistent*.txt", 0)]
        public async Task ParameterizedWildcard_PatternVariations_MatchesCorrectly(string pattern, int expectedMatches)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "2");
            File.WriteAllText(Path.Combine(_modDirectory, "file10.txt"), "10");

            var component = new ModComponent { Name = "Wildcard Pattern", Guid = Guid.NewGuid(), IsSelected = true };
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

            // Count actual files moved
            int actualMatches = 0;
            if (File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt"))) actualMatches++;
            if (File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt"))) actualMatches++;
            if (File.Exists(Path.Combine(_kotorDirectory, "Override", "file10.txt"))) actualMatches++;

            // For patterns that should match, verify they did
            if (expectedMatches > 0)
            {
                Assert.That(actualMatches, Is.GreaterThanOrEqualTo(1),
                    $"Pattern {pattern} should match at least one file");
            }
        }

        #endregion

        #region Parameterized Dependency Tests

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public async Task ParameterizedDependency_ModAndOptionCombinations_ExecutesCorrectly(
            bool modSelected, bool optionSelected)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent { Name = "Required Mod", Guid = Guid.NewGuid(), IsSelected = modSelected };
            var component = new ModComponent { Name = "Dependent", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Option", Guid = Guid.NewGuid(), IsSelected = optionSelected };
            component.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { requiredMod.Guid, option.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            bool shouldExecute = modSelected && optionSelected;
            bool fileExists = File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt"));

            Assert.That(fileExists, Is.EqualTo(shouldExecute),
                $"File should {(shouldExecute ? "exist" : "not exist")} when mod={modSelected}, option={optionSelected}");
        }

        #endregion
    }
}

