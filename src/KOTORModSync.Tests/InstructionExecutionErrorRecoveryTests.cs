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
    public sealed class InstructionExecutionErrorRecoveryTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ErrorRecovery_" + Guid.NewGuid());
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

        #region Error Recovery Tests

        [Test]
        public async Task ErrorRecovery_FirstInstructionFails_SubsequentInstructionsStillExecute()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Error Recovery", Guid = Guid.NewGuid(), IsSelected = true };

            // First instruction: Move non-existent file (will fail)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Second instruction: Move existing file (should succeed)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Third instruction: Move another existing file (should succeed)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                // Component may fail overall, but subsequent instructions should still execute
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "Second instruction should execute even if first fails");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "Third instruction should execute even if first fails");
            });
        }

        [Test]
        public async Task ErrorRecovery_MiddleInstructionFails_ContinuesExecution()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            var component = new ModComponent { Name = "Middle Error", Guid = Guid.NewGuid(), IsSelected = true };

            // First instruction: Should succeed
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Second instruction: Move non-existent file (will fail)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Third instruction: Should succeed
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Fourth instruction: Should succeed
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "First instruction should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "Third instruction should execute after middle failure");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True,
                    "Fourth instruction should execute after middle failure");
            });
        }

        [Test]
        public async Task ErrorRecovery_InvalidDestination_ReportsErrorButContinues()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Invalid Destination", Guid = Guid.NewGuid(), IsSelected = true };

            // First instruction: Invalid destination (will fail)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Invalid:Path/With:Colons" // Invalid path
            });

            // Second instruction: Valid destination (should succeed)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                // First instruction may fail, but second should still execute
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "Second instruction should execute even if first has invalid destination");
            });
        }

        [Test]
        public async Task ErrorRecovery_PartialWildcardMatch_ContinuesWithMatches()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            // file3.txt does not exist

            var component = new ModComponent { Name = "Partial Wildcard", Guid = Guid.NewGuid(), IsSelected = true };

            // Wildcard that matches some files but not all
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "Matching file1 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "Matching file2 should be moved");
            });
        }

        #endregion

        #region Error State Tests

        [Test]
        public async Task ErrorState_ComponentWithAllFailedInstructions_ReportsFailure()
        {
            var component = new ModComponent { Name = "All Fail", Guid = Guid.NewGuid(), IsSelected = true };

            // All instructions will fail (non-existent files)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.EqualTo(ModComponent.InstallExitCode.Success),
                "Component with all failed instructions should report failure");
        }

        #endregion
    }
}

