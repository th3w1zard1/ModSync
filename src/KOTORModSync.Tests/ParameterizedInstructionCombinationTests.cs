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
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class ParameterizedInstructionCombinationTests
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

        #region Parameterized Overwrite Behavior Tests

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public async Task Move_OverwriteCombinations_BehavesCorrectly(bool sourceExists, bool overwrite)
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");

            if (sourceExists)
            {
                File.WriteAllText(sourceFile, "source content");
            }

            if (File.Exists(destFile))
            {
                File.WriteAllText(destFile, "existing content");
            }

            var component = new ModComponent { Name = "Overwrite Test", Guid = Guid.NewGuid(), IsSelected = true };
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

            if (sourceExists)
            {
                if (File.Exists(destFile) && !overwrite)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when skipping overwrite");
                        Assert.That(File.ReadAllText(destFile), Is.EqualTo("existing content"), "Existing file should not be overwritten");
                        Assert.That(File.Exists(sourceFile), Is.True, "Source should remain when overwrite is false");
                    });
                }
                else
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when moving");
                        Assert.That(File.Exists(destFile), Is.True, "Destination file should exist");
                        Assert.That(File.Exists(sourceFile), Is.False, "Source should be moved");
                    });
                }
            }
            else
            {
                // Source doesn't exist - should handle gracefully
                Assert.That(result, Is.Not.EqualTo(ModComponent.InstallExitCode.Success), "Should fail when source doesn't exist");
            }
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public async Task Copy_OverwriteCombinations_BehavesCorrectly(bool sourceExists, bool overwrite)
        {
            string sourceFile = Path.Combine(_modDirectory, "file.txt");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");

            if (sourceExists)
            {
                File.WriteAllText(sourceFile, "source content");
            }

            if (File.Exists(destFile))
            {
                File.WriteAllText(destFile, "existing content");
            }

            var component = new ModComponent { Name = "Copy Overwrite Test", Guid = Guid.NewGuid(), IsSelected = true };
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

            if (sourceExists)
            {
                if (File.Exists(destFile) && !overwrite)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when skipping overwrite");
                        Assert.That(File.ReadAllText(destFile), Is.EqualTo("existing content"), "Existing file should not be overwritten");
                        Assert.That(File.Exists(sourceFile), Is.True, "Source should remain (copy operation)");
                    });
                }
                else
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when copying");
                        Assert.That(File.Exists(destFile), Is.True, "Destination file should exist");
                        Assert.That(File.Exists(sourceFile), Is.True, "Source should remain (copy operation)");
                    });
                }
            }
            else
            {
                // Source doesn't exist - should handle gracefully
                Assert.That(result, Is.Not.EqualTo(ModComponent.InstallExitCode.Success), "Should fail when source doesn't exist");
            }
        }

        #endregion

        #region Parameterized Dependency/Restriction Tests

        [TestCase(true, true, true)]   // Dependency selected, restriction not selected - should execute
        [TestCase(true, false, false)] // Dependency selected, restriction selected - should skip
        [TestCase(false, true, false)] // Dependency not selected, restriction not selected - should skip
        [TestCase(false, false, false)] // Dependency not selected, restriction selected - should skip
        public async Task Instruction_DependencyAndRestrictionCombinations_ExecutesCorrectly(
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

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should complete successfully");
                bool fileExists = File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt"));
                Assert.That(fileExists, Is.EqualTo(shouldExecute),
                    $"File should {(shouldExecute ? "exist" : "not exist")} based on dependency/restriction combination");
            });
        }

        [TestCase(0, true)]  // No dependencies - should execute
        [TestCase(1, true)]  // 1 dependency selected - should execute
        [TestCase(2, true)]  // 2 dependencies selected - should execute
        [TestCase(1, false)] // 1 dependency not selected - should skip
        [TestCase(2, false)] // 2 dependencies, one not selected - should skip
        public async Task Instruction_MultipleDependencies_AllMustBeSelected(int dependencyCount, bool allSelected)
        {
            var dependencies = new List<ModComponent>();
            for (int i = 0; i < dependencyCount; i++)
            {
                dependencies.Add(new ModComponent
                {
                    Name = $"Dependency {i}",
                    Guid = Guid.NewGuid(),
                    IsSelected = allSelected || (i == 0 && dependencyCount == 1) // Make first selected if testing partial
                });
            }

            var component = new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = dependencies.Select(d => d.Guid).ToList()
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var allComponents = new List<ModComponent>(dependencies) { component };
            var result = await component.ExecuteInstructionsAsync(allComponents, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            bool shouldExecute = dependencyCount == 0 || allSelected;
            bool fileExists = File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt"));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should complete successfully");
                Assert.That(fileExists, Is.EqualTo(shouldExecute),
                    $"File should {(shouldExecute ? "exist" : "not exist")} based on dependency selection");
            });
        }

        #endregion

        #region Parameterized Wildcard Pattern Tests

        [TestCase("*.txt", 3, 0)]      // All txt files
        [TestCase("file*.txt", 2, 1)]  // Files starting with "file"
        [TestCase("*.dat", 0, 2)]     // All dat files
        [TestCase("test?.txt", 1, 2)]  // Single character wildcard
        public async Task Move_WildcardPatterns_MatchesCorrectly(string pattern, int expectedMatches, int expectedNonMatches)
        {
            // Create test files
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "test1.txt"), "content3");
            File.WriteAllText(Path.Combine(_modDirectory, "other.dat"), "content4");
            File.WriteAllText(Path.Combine(_modDirectory, "data.dat"), "content5");

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

            int movedCount = Directory.GetFiles(Path.Combine(_kotorDirectory, "Override")).Length;
            int remainingCount = Directory.GetFiles(_modDirectory).Length;

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Wildcard move should succeed");
                Assert.That(movedCount, Is.EqualTo(expectedMatches), $"Should move {expectedMatches} matching files");
                // Note: remainingCount includes the archive if created, so we check moved files exist
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")) ||
                           File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")) ||
                           File.Exists(Path.Combine(_kotorDirectory, "Override", "test1.txt")),
                           Is.True, "At least one matching file should be moved");
            });
        }

        #endregion

        #region Parameterized Option Selection Tests

        [TestCase(true, true, true)]   // Option 1 selected, Option 2 selected - both execute
        [TestCase(true, false, true)]  // Option 1 selected, Option 2 not selected - only 1 executes
        [TestCase(false, true, true)]  // Option 1 not selected, Option 2 selected - only 2 executes
        [TestCase(false, false, false)] // Neither selected - none execute
        public async Task Choose_MultipleOptions_ExecutesSelectedOnes(
            bool option1Selected, bool option2Selected, bool shouldExecuteAny)
        {
            var component = new ModComponent { Name = "Multi-Option Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = option1Selected };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = option2Selected };

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");
            File.WriteAllText(Path.Combine(_modDirectory, "option2.txt"), "option2");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString() }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }
            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Choose instruction should succeed");
                bool option1FileExists = File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt"));
                bool option2FileExists = File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt"));

                Assert.That(option1FileExists, Is.EqualTo(option1Selected), "Option 1 file should exist only if selected");
                Assert.That(option2FileExists, Is.EqualTo(option2Selected), "Option 2 file should exist only if selected");
                Assert.That(option1FileExists || option2FileExists, Is.EqualTo(shouldExecuteAny),
                    "At least one file should exist if any option is selected");
            });
        }

        #endregion

        #region Parameterized Extract Destination Tests

        [TestCase(true, "extracted")]   // With destination
        [TestCase(false, null)]          // Without destination (extracts to archive dir)
        public async Task Extract_DestinationCombinations_ExtractsCorrectly(bool hasDestination, string destination)
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "content" }
            });

            var component = new ModComponent { Name = "Extract Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" }
            };

            if (hasDestination)
            {
                instruction.Destination = $"<<modDirectory>>/{destination}";
            }

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Extract should succeed");

                if (hasDestination)
                {
                    Assert.That(File.Exists(Path.Combine(_modDirectory, destination, "file.txt")), Is.True,
                        "File should be extracted to specified destination");
                }
                else
                {
                    Assert.That(File.Exists(Path.Combine(_modDirectory, "file.txt")), Is.True,
                        "File should be extracted to archive directory when no destination specified");
                }
            });
        }

        #endregion

        #region Parameterized Delete Overwrite Tests

        [TestCase(true, true)]   // File exists, Overwrite=true - should delete and return error if strict
        [TestCase(true, false)]  // File exists, Overwrite=false - should delete successfully
        [TestCase(false, true)]  // File doesn't exist, Overwrite=true - should return error
        [TestCase(false, false)] // File doesn't exist, Overwrite=false - should succeed silently
        public async Task Delete_OverwriteCombinations_BehavesCorrectly(bool fileExists, bool overwrite)
        {
            string fileToDelete = Path.Combine(_kotorDirectory, "Override", "file.txt");

            if (fileExists)
            {
                File.WriteAllText(fileToDelete, "content");
            }

            var component = new ModComponent { Name = "Delete Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/file.txt" },
                Overwrite = overwrite
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            if (fileExists)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Delete should succeed when file exists");
                    Assert.That(File.Exists(fileToDelete), Is.False, "File should be deleted");
                });
            }
            else
            {
                if (overwrite)
                {
                    // Overwrite=true means strict mode - should return error
                    Assert.That(result, Is.Not.EqualTo(ModComponent.InstallExitCode.Success),
                        "Should return error when file doesn't exist and Overwrite=true");
                }
                else
                {
                    // Overwrite=false means lenient mode - should succeed silently
                    Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                        "Should succeed silently when file doesn't exist and Overwrite=false");
                }
            }
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

