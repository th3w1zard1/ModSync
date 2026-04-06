// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Services;
using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class ValidationEdgeCasesAndErrorScenariosTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ValidationEdgeCases_" + Guid.NewGuid());
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
            ComponentValidationService.ClearValidationCache();
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

        #region Null and Empty Edge Cases

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithNullComponent_ThrowsException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await ComponentValidationService.ValidateComponentFilesExistAsync(null).ConfigureAwait(false);
            }, "Should throw ArgumentNullException for null component");
        }

        [Test]
        public async Task GetMissingFilesForComponentAsync_WithNullComponent_ThrowsException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await ComponentValidationService.GetMissingFilesForComponentAsync(null).ConfigureAwait(false);
            }, "Should throw ArgumentNullException for null component");
        }

        [Test]
        public void ShouldRunInstruction_WithNullInstruction_ThrowsException()
        {
            var components = new List<ModComponent>();

            Assert.Throws<ArgumentNullException>(() =>
            {
                ModComponent.ShouldRunInstruction(null, components);
            }, "Should throw ArgumentNullException for null instruction");
        }

        [Test]
        public void ShouldRunInstruction_WithNullComponents_ThrowsException()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            Assert.Throws<ArgumentNullException>(() =>
            {
                ModComponent.ShouldRunInstruction(instruction, null);
            }, "Should throw ArgumentNullException for null components list");
        }

        [Test]
        public void ShouldInstallComponent_WithNullComponents_ThrowsException()
        {
            var component = new ModComponent
            {
                Name = "Test",
                Guid = Guid.NewGuid()
            };

            Assert.Throws<ArgumentNullException>(() =>
            {
                component.ShouldInstallComponent(null);
            }, "Should throw ArgumentNullException for null components list");
        }

        #endregion

        #region Empty Collections Edge Cases

        [Test]
        public void ShouldRunInstruction_WithEmptyComponentsList_NoDependencies_ReturnsTrue()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var components = new List<ModComponent>();

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction with no dependencies should run even with empty components list");
        }

        [Test]
        public void ShouldRunInstruction_WithEmptyComponentsList_WithDependencies_ReturnsFalse()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { Guid.NewGuid() }
            };

            var components = new List<ModComponent>();

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction with dependencies should not run when components list is empty");
        }

        [Test]
        public void ShouldRunInstruction_WithEmptyDependencies_ReturnsTrue()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid>()
            };

            var components = new List<ModComponent>();

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction with empty dependencies should run");
        }

        [Test]
        public void ShouldRunInstruction_WithEmptyRestrictions_ReturnsTrue()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid>()
            };

            var components = new List<ModComponent>();

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction with empty restrictions should run");
        }

        #endregion

        #region Invalid GUID Edge Cases

        [Test]
        public void ShouldRunInstruction_WithInvalidDependencyGuid_ReturnsFalse()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { Guid.NewGuid() } // GUID not in components list
            };

            var components = new List<ModComponent>();

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction with invalid dependency GUID should not run");
        }

        [Test]
        public void ShouldRunInstruction_WithInvalidRestrictionGuid_ReturnsTrue()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { Guid.NewGuid() } // GUID not in components list
            };

            var components = new List<ModComponent>();

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.True, "Instruction with invalid restriction GUID should run (restriction not found)");
        }

        #endregion

        #region Component State Edge Cases

        [Test]
        public void ShouldRunInstruction_WithDependencyNotSelected_ReturnsFalse()
        {
            var depComponent = new ModComponent
            {
                Name = "Dependency",
                Guid = Guid.NewGuid(),
                IsSelected = false
            };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            };

            var components = new List<ModComponent> { depComponent };

            bool shouldRun = ModComponent.ShouldRunInstruction(instruction, components);

            Assert.That(shouldRun, Is.False, "Instruction should not run when dependency is not selected");
        }

        [Test]
        public void ShouldRunInstruction_WithRestrictionSelected_ReturnsFalse()
        {
            var restrictedComponent = new ModComponent
            {
                Name = "Restricted",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
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

        #endregion

        #region File System Edge Cases

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithLongPath_HandlesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            // Create a long path
            string longPath = Path.Combine(_modDirectory, new string('a', 200), "file.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(longPath));
            File.WriteAllText(longPath, "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { Path.Combine("<<modDirectory>>", new string('a', 200), "file.txt") },
                Destination = "<<kotorDirectory>>/Override"
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Should handle long paths correctly");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithSpecialCharacters_HandlesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            string specialPath = Path.Combine(_modDirectory, "file with spaces & symbols!.txt");
            File.WriteAllText(specialPath, "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file with spaces & symbols!.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Should handle special characters in paths");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithUnicodeCharacters_HandlesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            string unicodePath = Path.Combine(_modDirectory, "测试文件.txt");
            File.WriteAllText(unicodePath, "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/测试文件.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Should handle Unicode characters in paths");
        }

        #endregion

        #region Instruction Type Edge Cases

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithAllInstructionTypes_HandlesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            // Test various instruction types
            var instructionTypes = Enum.GetValues(typeof(Instruction.ActionType))
                .Cast<Instruction.ActionType>()
                .Where(t => t != Instruction.ActionType.Unset && t != Instruction.ActionType.Choose);

            foreach (var actionType in instructionTypes)
            {
                component.Instructions.Clear();
                component.Instructions.Add(new Instruction
                {
                    Action = actionType,
                    Source = new List<string> { "<<modDirectory>>/file.txt" },
                    Destination = "<<kotorDirectory>>/Override"
                });

                bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

                Assert.That(isValid, Is.True, $"Should handle {actionType} instruction type");
            }
        }

        #endregion

        #region Concurrent Validation Edge Cases

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithConcurrentCalls_HandlesCorrectly()
        {
            var component1 = new ModComponent
            {
                Name = "Component 1",
                Guid = Guid.NewGuid()
            };

            var component2 = new ModComponent
            {
                Name = "Component 2",
                Guid = Guid.NewGuid()
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            component1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var task1 = ComponentValidationService.ValidateComponentFilesExistAsync(component1);
            var task2 = ComponentValidationService.ValidateComponentFilesExistAsync(component2);

            var results = await Task.WhenAll(task1, task2).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results[0], Is.True, "Component 1 should be valid");
                Assert.That(results[1], Is.True, "Component 2 should be valid");
            });
        }

        #endregion

        #region Resource Registry Edge Cases

        [Test]
        public async Task AnalyzeDownloadNecessityAsync_WithEmptyResourceRegistry_HandlesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            component.ResourceRegistry.Clear();

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var validationService = new ComponentValidationService();
            var (urls, simulationFailed) = await validationService.AnalyzeDownloadNecessityAsync(
                component, _modDirectory, System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.That(simulationFailed, Is.False, "Should handle empty ResourceRegistry gracefully");
        }

        [Test]
        public async Task AnalyzeDownloadNecessityAsync_WithNullResourceRegistry_HandlesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            component.ResourceRegistry = null;

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var validationService = new ComponentValidationService();
            var (urls, simulationFailed) = await validationService.AnalyzeDownloadNecessityAsync(
                component, _modDirectory, System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.That(simulationFailed, Is.False, "Should handle null ResourceRegistry gracefully");
        }

        #endregion
    }
}

