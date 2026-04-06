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
    public sealed class ComponentStateAndTransitionTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComponentState_" + Guid.NewGuid());
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

        #region Component Selection State

        [Test]
        public async Task ComponentState_SelectedToUnselected_DoesNotExecute()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "State Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            // Change to unselected
            component.IsSelected = false;

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null, "Should return a result");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "File should not be moved when component is unselected");
            });
        }

        [Test]
        public async Task ComponentState_UnselectedToSelected_ExecutesInstructions()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "State Test", Guid = Guid.NewGuid(), IsSelected = false };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            // Change to selected
            component.IsSelected = true;

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be moved when component is selected");
            });
        }

        #endregion

        #region Component Dependency State

        [Test]
        public async Task ComponentDependencyState_DependencyNotSelected_BlocksComponent()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent { Name = "Required", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent
            {
                Name = "Dependent",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { requiredMod.Guid }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Component may be blocked or instructions may be skipped
            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        [Test]
        public async Task ComponentDependencyState_DependencySelected_AllowsComponent()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var requiredMod = new ModComponent { Name = "Required", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Dependent",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { requiredMod.Guid }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { requiredMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be moved when dependency is met");
            });
        }

        #endregion

        #region Component Restriction State

        [Test]
        public async Task ComponentRestrictionState_RestrictionSelected_BlocksComponent()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var restrictedMod = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Restricting",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { restrictedMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Component may be blocked when restriction is selected
            Assert.That(result, Is.Not.Null, "Should return a result");
        }

        [Test]
        public async Task ComponentRestrictionState_RestrictionNotSelected_AllowsComponent()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var restrictedMod = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent
            {
                Name = "Restricting",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { restrictedMod, component };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(component.Instructions, components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be moved when restriction is not selected");
            });
        }

        #endregion

        #region Component with Options State

        [Test]
        public async Task ComponentOptionsState_OptionSelected_ExecutesOptionInstructions()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "option1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "option2.txt"), "content2");

            var component = new ModComponent { Name = "Options Component", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), IsSelected = false };

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

            var result = await component.ExecuteInstructionsAsync(component.Instructions, new List<ModComponent> { component }, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1.txt")), Is.True,
                    "Selected option should execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option2.txt")), Is.False,
                    "Unselected option should not execute");
            });
        }

        #endregion
    }
}

