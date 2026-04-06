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
    public sealed class ComplexConditionalExecutionScenariosTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComplexConditional_" + Guid.NewGuid());
            _modDirectory = Path.Combine(_testDirectory, "Mods");
            _kotorDirectory = Path.Combine(_testDirectory, "KOTOR");
            Directory.CreateDirectory(_modDirectory);
            Directory.CreateDirectory(_kotorDirectory);
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override"));
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

        #region Complex Dependency Chains

        [Test]
        public void ShouldRunInstruction_WithDiamondDependencyPattern_AllMet_ReturnsTrue()
        {
            // A -> B, C
            // B -> D
            // C -> D
            // Instruction depends on D
            var modA = new ModComponent { Name = "A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "B", Guid = Guid.NewGuid(), IsSelected = true, Dependencies = new List<Guid> { modA.Guid } };
            var modC = new ModComponent { Name = "C", Guid = Guid.NewGuid(), IsSelected = true, Dependencies = new List<Guid> { modA.Guid } };
            var modD = new ModComponent { Name = "D", Guid = Guid.NewGuid(), IsSelected = true, Dependencies = new List<Guid> { modB.Guid, modC.Guid } };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { modD.Guid }
            };

            var components = new List<ModComponent> { modA, modB, modC, modD };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when diamond dependency pattern is all met");
        }

        [Test]
        public void ShouldRunInstruction_WithDiamondDependencyPattern_OneBranchUnmet_ReturnsFalse()
        {
            // A -> B, C
            // B -> D (B not selected)
            // C -> D
            // Instruction depends on D
            var modA = new ModComponent { Name = "A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "B", Guid = Guid.NewGuid(), IsSelected = false, Dependencies = new List<Guid> { modA.Guid } };
            var modC = new ModComponent { Name = "C", Guid = Guid.NewGuid(), IsSelected = true, Dependencies = new List<Guid> { modA.Guid } };
            var modD = new ModComponent { Name = "D", Guid = Guid.NewGuid(), IsSelected = false, Dependencies = new List<Guid> { modB.Guid, modC.Guid } };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { modD.Guid }
            };

            var components = new List<ModComponent> { modA, modB, modC, modD };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when diamond dependency pattern has unmet branch");
        }

        #endregion

        #region Complex Restriction Patterns

        [Test]
        public void ShouldRunInstruction_WithMultipleRestrictionGroups_NoneSelected_ReturnsTrue()
        {
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = false };
            var rest3 = new ModComponent { Name = "Rest 3", Guid = Guid.NewGuid(), IsSelected = false };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid, rest3.Guid }
            };

            var components = new List<ModComponent> { rest1, rest2, rest3 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when all restrictions are not selected");
        }

        [Test]
        public void ShouldRunInstruction_WithMultipleRestrictionGroups_OneSelected_ReturnsFalse()
        {
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = true };
            var rest3 = new ModComponent { Name = "Rest 3", Guid = Guid.NewGuid(), IsSelected = false };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid, rest3.Guid }
            };

            var components = new List<ModComponent> { rest1, rest2, rest3 };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when any restriction is selected");
        }

        #endregion

        #region Mixed Dependencies and Restrictions with Options

        [Test]
        public void ShouldRunInstruction_WithComponentAndOptionDependencies_AllMet_ReturnsTrue()
        {
            var depComponent = new ModComponent { Name = "Dep Component", Guid = Guid.NewGuid(), IsSelected = true };
            var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Dep Option", Guid = Guid.NewGuid(), IsSelected = true };
            parentComponent.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid, option.Guid }
            };

            var components = new List<ModComponent> { depComponent, parentComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when both component and option dependencies are met");
        }

        [Test]
        public void ShouldRunInstruction_WithComponentAndOptionRestrictions_NoneSelected_ReturnsTrue()
        {
            var restComponent = new ModComponent { Name = "Rest Component", Guid = Guid.NewGuid(), IsSelected = false };
            var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Rest Option", Guid = Guid.NewGuid(), IsSelected = false };
            parentComponent.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restComponent.Guid, option.Guid }
            };

            var components = new List<ModComponent> { restComponent, parentComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction should run when both component and option restrictions are not selected");
        }

        [Test]
        public void ShouldRunInstruction_WithComponentAndOptionRestrictions_OptionSelected_ReturnsFalse()
        {
            var restComponent = new ModComponent { Name = "Rest Component", Guid = Guid.NewGuid(), IsSelected = false };
            var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
            var option = new Option { Name = "Rest Option", Guid = Guid.NewGuid(), IsSelected = true };
            parentComponent.Options.Add(option);

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restComponent.Guid, option.Guid }
            };

            var components = new List<ModComponent> { restComponent, parentComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when option restriction is selected");
        }

        #endregion

        #region Instruction Execution with Complex Conditionals

        [Test]
        public async Task ExecuteInstructions_WithMixedConditionals_ExecutesOnlyValid()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep 2", Guid = Guid.NewGuid(), IsSelected = false };
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = true };

            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");
            File.WriteAllText(Path.Combine(_modDirectory, "file4.txt"), "content4");

            // Instruction 1: No conditions - should run
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
                Dependencies = new List<Guid> { dep1.Guid }
            });

            // Instruction 3: Dependency not met - should not run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { dep2.Guid }
            });

            // Instruction 4: Restriction selected - should not run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file4.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { rest2.Guid }
            });

            var components = new List<ModComponent> { dep1, dep2, rest1, rest2, component };

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            // Check which instructions should run
            var shouldRun1 = ModComponent.ShouldRunInstruction(component.Instructions[0], components);
            var shouldRun2 = ModComponent.ShouldRunInstruction(component.Instructions[1], components);
            var shouldRun3 = ModComponent.ShouldRunInstruction(component.Instructions[2], components);
            var shouldRun4 = ModComponent.ShouldRunInstruction(component.Instructions[3], components);

            Assert.Multiple(() =>
            {
                Assert.That(shouldRun1, Is.True, "Instruction 1 should run (no conditions)");
                Assert.That(shouldRun2, Is.True, "Instruction 2 should run (dependency met)");
                Assert.That(shouldRun3, Is.False, "Instruction 3 should not run (dependency not met)");
                Assert.That(shouldRun4, Is.False, "Instruction 4 should not run (restriction selected)");
            });
        }

        #endregion

        #region Component Installation with Complex Conditionals

        [Test]
        public void ShouldInstallComponent_WithComplexDependencyChain_AllMet_ReturnsTrue()
        {
            var modA = new ModComponent { Name = "A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "B", Guid = Guid.NewGuid(), IsSelected = true, Dependencies = new List<Guid> { modA.Guid } };
            var modC = new ModComponent { Name = "C", Guid = Guid.NewGuid(), IsSelected = true, Dependencies = new List<Guid> { modB.Guid } };
            var modD = new ModComponent
            {
                Name = "D",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modC.Guid }
            };

            var components = new List<ModComponent> { modA, modB, modC, modD };

            bool shouldInstall = modD.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.True, "Component should install when all dependencies in chain are met");
        }

        [Test]
        public void ShouldInstallComponent_WithComplexDependencyChain_OneUnmet_ReturnsFalse()
        {
            var modA = new ModComponent { Name = "A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "B", Guid = Guid.NewGuid(), IsSelected = false, Dependencies = new List<Guid> { modA.Guid } };
            var modC = new ModComponent { Name = "C", Guid = Guid.NewGuid(), IsSelected = false, Dependencies = new List<Guid> { modB.Guid } };
            var modD = new ModComponent
            {
                Name = "D",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { modC.Guid }
            };

            var components = new List<ModComponent> { modA, modB, modC, modD };

            bool shouldInstall = modD.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.False, "Component should not install when dependency chain is broken");
        }

        [Test]
        public void ShouldInstallComponent_WithMultipleDependenciesAndRestrictions_Complex_ReturnsCorrectly()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep 2", Guid = Guid.NewGuid(), IsSelected = true };
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = false };

            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid },
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid }
            };

            var components = new List<ModComponent> { dep1, dep2, rest1, rest2, component };

            bool shouldInstall = component.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.True, "Component should install when all dependencies met and no restrictions selected");
        }

        [Test]
        public void ShouldInstallComponent_WithMultipleDependenciesAndRestrictions_OneRestrictionSelected_ReturnsFalse()
        {
            var dep1 = new ModComponent { Name = "Dep 1", Guid = Guid.NewGuid(), IsSelected = true };
            var dep2 = new ModComponent { Name = "Dep 2", Guid = Guid.NewGuid(), IsSelected = true };
            var rest1 = new ModComponent { Name = "Rest 1", Guid = Guid.NewGuid(), IsSelected = false };
            var rest2 = new ModComponent { Name = "Rest 2", Guid = Guid.NewGuid(), IsSelected = true };

            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { dep1.Guid, dep2.Guid },
                Restrictions = new List<Guid> { rest1.Guid, rest2.Guid }
            };

            var components = new List<ModComponent> { dep1, dep2, rest1, rest2, component };

            bool shouldInstall = component.ShouldInstallComponent(components);

            Assert.That(shouldInstall, Is.False, "Component should not install when any restriction is selected");
        }

        #endregion
    }
}

