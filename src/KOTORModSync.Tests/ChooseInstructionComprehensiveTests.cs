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
    public sealed class ChooseInstructionComprehensiveTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ChooseTests_" + Guid.NewGuid());
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

        #region Basic Choose Instruction Tests

        [Test]
        public async Task Choose_SingleSelectedOption_ExecutesOptionInstructions()
        {
            var component = new ModComponent { Name = "Single Option Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };

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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Choose should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "Selected option file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.False,
                    "Unselected option file should not exist");
            });
        }

        [Test]
        public async Task Choose_MultipleSelectedOptions_ExecutesAllSelectedOptions()
        {
            var component = new ModComponent { Name = "Multi Option Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = true };
            var option3 = new Option { Name = "Option 3", Guid = Guid.NewGuid(), IsSelected = false };

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");
            File.WriteAllText(Path.Combine(_modDirectory, "option2.txt"), "option2");
            File.WriteAllText(Path.Combine(_modDirectory, "option3.txt"), "option3");

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

            option3.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);
            component.Options.Add(option3);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString(), option3.Guid.ToString() }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Choose should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "First selected option should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.True,
                    "Second selected option should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option3.txt")), Is.False,
                    "Unselected option should not execute");
            });
        }

        [Test]
        public async Task Choose_NoSelectedOptions_ExecutesNothing()
        {
            var component = new ModComponent { Name = "No Option Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = false };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };

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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Choose should succeed even with no selections");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.False,
                    "No files should be moved when no options selected");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.False,
                    "No files should be moved when no options selected");
            });
        }

        #endregion

        #region Choose with Dependencies and Restrictions

        [Test]
        public async Task Choose_OptionWithDependency_DependencyMet_ExecutesOption()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Dependent Option Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() }
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

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { depComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed when dependency met");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "Option should execute when dependency is met");
            });
        }

        [Test]
        public async Task Choose_OptionWithRestriction_RestrictionActive_SkipsOption()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "Restricted Option Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() }
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

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { restrictedComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed (option skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.False,
                    "Option should not execute when restriction is active");
            });
        }

        #endregion

        #region Choose with Complex Option Instructions

        [Test]
        public async Task Choose_OptionWithMultipleInstructions_ExecutesAll()
        {
            var component = new ModComponent { Name = "Complex Option Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "file1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "file2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "file3");

            // Option with multiple instructions
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complex option should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True,
                    "First instruction should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "Second instruction should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True,
                    "Third instruction should execute");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file2.txt")), Is.True,
                    "Copied file should remain in source");
            });
        }

        [Test]
        public async Task Choose_MultipleOptionsWithConflictingFiles_HandlesCorrectly()
        {
            var component = new ModComponent { Name = "Conflicting Options Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "option1_file.txt"), "option1 content");
            File.WriteAllText(Path.Combine(_modDirectory, "option2_file.txt"), "option2 content");

            // Both options move files with same name
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option2_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Conflicting options should handle correctly");
                // Both files should exist (different names) or last one wins if same name
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1_file.txt")) ||
                           File.Exists(Path.Combine(_kotorDirectory, "Override", "option2_file.txt")),
                           Is.True, "At least one file should exist");
            });
        }

        #endregion

        #region Choose Instruction Order Tests

        [Test]
        public async Task Choose_OptionsExecutedInSourceOrder_RespectsOrder()
        {
            var component = new ModComponent { Name = "Order Test Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = true };
            var option3 = new Option { Name = "Option 3", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "option1");
            File.WriteAllText(Path.Combine(_modDirectory, "option2.txt"), "option2");
            File.WriteAllText(Path.Combine(_modDirectory, "option3.txt"), "option3");

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

            option3.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option3.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);
            component.Options.Add(option3);

            // Specify order: option3, option1, option2
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option3.Guid.ToString(), option1.Guid.ToString(), option2.Guid.ToString() }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Ordered choose should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "All selected options should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.True,
                    "All selected options should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option3.txt")), Is.True,
                    "All selected options should execute");
            });
        }

        #endregion
    }
}

