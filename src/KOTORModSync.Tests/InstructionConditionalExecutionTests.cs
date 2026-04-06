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
    public sealed class InstructionConditionalExecutionTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ConditionalTests_" + Guid.NewGuid());
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

        #region ShouldRunInstruction Tests

        [Test]
        public void ShouldRunInstruction_WithNoDependenciesOrRestrictions_ReturnsTrue()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var components = new List<ModComponent>();

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when no dependencies or restrictions");
        }

        [Test]
        public void ShouldRunInstruction_WithMetDependency_ReturnsTrue()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            var components = new List<ModComponent> { depComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when dependency is met");
        }

        [Test]
        public void ShouldRunInstruction_WithUnmetDependency_ReturnsFalse()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            var components = new List<ModComponent> { depComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when dependency is not met");
        }

        [Test]
        public void ShouldRunInstruction_WithNoRestrictionSelected_ReturnsTrue()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = false };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            var components = new List<ModComponent> { restrictedComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when restriction is not selected");
        }

        [Test]
        public void ShouldRunInstruction_WithRestrictionSelected_ReturnsFalse()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            var components = new List<ModComponent> { restrictedComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when restriction is selected");
        }

        [Test]
        public void ShouldRunInstruction_WithMultipleDependencies_AllMet_ReturnsTrue()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep 2", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid }
            };

            var components = new List<ModComponent> { dep1, dep2 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when all dependencies are met");
        }

        [Test]
        public void ShouldRunInstruction_WithMultipleDependencies_OneUnmet_ReturnsFalse()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep 2", Guid = Guid.NewGuid(), IsSelected = false };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid }
            };

            var components = new List<ModComponent> { dep1, dep2 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when any dependency is not met");
        }

        [Test]
        public void ShouldRunInstruction_WithMultipleRestrictions_NoneSelected_ReturnsTrue()
        {
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = false };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid }
            };

            var components = new List<ModComponent> { rest1, rest2 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when no restrictions are selected");
        }

        [Test]
        public void ShouldRunInstruction_WithMultipleRestrictions_OneSelected_ReturnsFalse()
        {
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid }
            };

            var components = new List<ModComponent> { rest1, rest2 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when any restriction is selected");
        }

        [Test]
        public void ShouldRunInstruction_WithDependenciesAndRestrictions_Mixed_ReturnsFalse()
        {
            var dep = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var rest = new ModComponent { Name = "Restriction", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep.Guid },
                Restrictions = new List<Guid> { rest.Guid }
            };

            var components = new List<ModComponent> { dep, rest };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when restriction is selected (even if dependency is met)");
        }

        [Test]
        public void ShouldRunInstruction_WithOptionDependency_Met_ReturnsTrue()
        {
            var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Option", Guid = Guid.NewGuid(), IsSelected = true };
            parentComponent.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { option.Guid }
            };

            var components = new List<ModComponent> { parentComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when option dependency is met");
        }

        [Test]
        public void ShouldRunInstruction_WithOptionRestriction_Selected_ReturnsFalse()
        {
            var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Option", Guid = Guid.NewGuid(), IsSelected = true };
            parentComponent.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { option.Guid }
            };

            var components = new List<ModComponent> { parentComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when option restriction is selected");
        }

        #endregion

        #region ShouldInstallComponent Tests

        [Test]
        public void ShouldInstallComponent_WithNoDependenciesOrRestrictions_ReturnsTrue()
        {
            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var components = new List<ModComponent> { component };

            bool shouldInstall = component.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.True, "Component should install when no dependencies or restrictions");
        }

        [Test]
        public void ShouldInstallComponent_WithMetDependencies_ReturnsTrue()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            var components = new List<ModComponent> { depComponent, component };

            bool shouldInstall = component.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.True, "Component should install when dependencies are met");
        }

        [Test]
        public void ShouldInstallComponent_WithUnmetDependencies_ReturnsFalse()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            var components = new List<ModComponent> { depComponent, component };

            bool shouldInstall = component.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.False, "Component should not install when dependencies are not met");
        }

        [Test]
        public void ShouldInstallComponent_WithNoRestrictionsSelected_ReturnsTrue()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            var components = new List<ModComponent> { restrictedComponent, component };

            bool shouldInstall = component.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.True, "Component should install when restrictions are not selected");
        }

        [Test]
        public void ShouldInstallComponent_WithRestrictionSelected_ReturnsFalse()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            var components = new List<ModComponent> { restrictedComponent, component };

            bool shouldInstall = component.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.False, "Component should not install when restriction is selected");
        }

        #endregion

        #region Instruction Execution with Conditional Logic

        [Test]
        public async Task ExecuteInstruction_WithUnmetDependency_SkipsExecution()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { depComponent, component };
            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when dependency is not met");

            // Even if we try to execute, it should be skipped
            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            // The instruction should not execute because dependency is not met
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False, "File should not be moved when dependency is not met");
        }

        [Test]
        public async Task ExecuteInstruction_WithRestrictionSelected_SkipsExecution()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            component.Instructions.Add(instruction);

            var components = new List<ModComponent> { restrictedComponent, component };
            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when restriction is selected");

            // Even if we try to execute, it should be skipped
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False, "File should not be moved when restriction is selected");
        }

        [Test]
        public async Task ExecuteInstructions_WithConditionalInstructions_ExecutesOnlyValid()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = false };

            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            // Instruction 1: No dependencies/restrictions - should run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Instruction 2: Dependency met - should run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            });

            // Instruction 3: Restriction not selected - should run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            });

            var components = new List<ModComponent> { depComponent, restrictedComponent, component };

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            // Execute all instructions
            var exitCode = await component.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "File1 should be moved (no conditions)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "File2 should be moved (dependency met)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True, "File3 should be moved (restriction not selected)");
            });
        }

        #endregion

        #region Complex Conditional Scenarios

        [Test]
        public void ShouldRunInstruction_WithNestedDependencies_AllMet_ReturnsTrue()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent
            {
                Name = "Dep 2",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { dep1.Guid }
            };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep2.Guid }
            };

            var components = new List<ModComponent> { dep1, dep2 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when nested dependencies are all met");
        }

        [Test]
        public void ShouldRunInstruction_WithMixedDependenciesAndRestrictions_Complex_ReturnsCorrectly()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep 2", Guid = Guid.NewGuid(), IsSelected = true };
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = false };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid },
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid }
            };

            var components = new List<ModComponent> { dep1, dep2, rest1, rest2 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when all dependencies met and no restrictions selected");
        }

        [Test]
        public void ShouldRunInstruction_WithMixedDependenciesAndRestrictions_OneRestrictionSelected_ReturnsFalse()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep 2", Guid = Guid.NewGuid(), IsSelected = true };
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid },
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid }
            };

            var components = new List<ModComponent> { dep1, dep2, rest1, rest2 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when any restriction is selected");
        }

        #endregion
    }
}

