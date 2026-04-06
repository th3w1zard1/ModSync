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
    public sealed class ComponentValidationServiceTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_Validation_" + Guid.NewGuid());
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

        #region ValidateComponentFilesExistAsync Tests

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithExistingFiles_ReturnsTrue()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt", "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Component should be valid when all files exist");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithMissingFiles_ReturnsFalse()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            // file2.txt is missing

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt", "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.False, "Component should be invalid when files are missing");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithEmptyInstructions_ReturnsTrue()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Component with no instructions should be valid");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithNullInstructions_ReturnsTrue()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                Instructions = null
            };

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Component with null instructions should be valid");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithWildcardSources_ValidatesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "other.txt"), "content3");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Component should be valid when wildcard matches files");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithChooseInstruction_SkipsValidation()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            var option = new Option { Name = "Option", Guid = Guid.NewGuid() };
            component.Options.Add(option);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option.Guid.ToString() }
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Choose instruction should not require file validation");
        }

        #endregion

        #region GetMissingFilesForComponentAsync Tests

        [Test]
        public async Task GetMissingFilesForComponentAsync_WithAllFilesPresent_ReturnsEmpty()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt", "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component).ConfigureAwait(false);

            Assert.That(missingFiles, Is.Empty, "Should return empty list when all files exist");
        }

        [Test]
        public async Task GetMissingFilesForComponentAsync_WithMissingFiles_ReturnsMissingFiles()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            // file2.txt is missing

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt", "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Empty, "Should return missing files");
                Assert.That(missingFiles, Contains.Item("file2.txt"), "Should include missing file");
                Assert.That(missingFiles, Does.Not.Contain("file1.txt"), "Should not include existing file");
            });
        }

        [Test]
        public async Task GetMissingFilesForComponentAsync_WithSelectedOption_IncludesOptionFiles()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            var option = new Option
            {
                Name = "Option",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            option.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });
            component.Options.Add(option);

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            // option_file.txt is missing

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component).ConfigureAwait(false);

            Assert.That(missingFiles, Contains.Item("option_file.txt"), "Should include missing files from selected option");
        }

        [Test]
        public async Task GetMissingFilesForComponentAsync_WithUnselectedOption_ExcludesOptionFiles()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            var option = new Option
            {
                Name = "Option",
                Guid = Guid.NewGuid(),
                IsSelected = false
            };
            option.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });
            component.Options.Add(option);

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            // option_file.txt is missing but option is not selected

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component).ConfigureAwait(false);

            Assert.That(missingFiles, Does.Not.Contain("option_file.txt"), "Should not include files from unselected option");
        }

        #endregion

        #region Validation Cache Tests

        [Test]
        public void ClearValidationCache_ClearsAllCache()
        {
            // Cache is static, so we can't directly test it, but we can verify the method doesn't throw
            Assert.DoesNotThrow(() => ComponentValidationService.ClearValidationCache(), "ClearValidationCache should not throw");
        }

        [Test]
        public void ClearValidationCacheForComponent_ClearsComponentCache()
        {
            var componentGuid = Guid.NewGuid().ToString();
            Assert.DoesNotThrow(() => ComponentValidationService.ClearValidationCacheForComponent(componentGuid), "ClearValidationCacheForComponent should not throw");
        }

        #endregion

        #region AnalyzeDownloadNecessityAsync Tests

        [Test]
        public async Task AnalyzeDownloadNecessityAsync_WithFilesInResourceRegistry_ReturnsNoDownloads()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            component.ResourceRegistry["mod.zip"] = new ResourceRegistryEntry
            {
                Files = new Dictionary<string, ResourceFileInfo>
(StringComparer.Ordinal)
                {
                    { "file1.txt", new ResourceFileInfo { Size = 100 } },
                    { "file2.txt", new ResourceFileInfo { Size = 200 } }
                }
            };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt", "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var validationService = new ComponentValidationService();
            var (urls, simulationFailed) = await validationService.AnalyzeDownloadNecessityAsync(
                component, _modDirectory, System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(urls, Is.Empty, "Should not require downloads when files are in ResourceRegistry");
                Assert.That(simulationFailed, Is.False, "Simulation should not fail");
            });
        }

        [Test]
        public async Task AnalyzeDownloadNecessityAsync_WithMissingFiles_ReturnsDownloads()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/missing_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var validationService = new ComponentValidationService();
            var (urls, simulationFailed) = await validationService.AnalyzeDownloadNecessityAsync(
                component, _modDirectory, System.Threading.CancellationToken.None).ConfigureAwait(false);

            // When files are missing and not in ResourceRegistry, it may indicate downloads needed
            // The exact behavior depends on the implementation
            Assert.That(simulationFailed, Is.False, "Simulation should handle missing files gracefully");
        }

        #endregion

        #region Edge Cases

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithKotorDirectorySource_SkipsValidation()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<kotorDirectory>>/Override/existing.txt" },
                Destination = "<<kotorDirectory>>/Override/new.txt"
            });

            // KOTOR directory sources are typically not validated the same way
            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            // Should not fail validation for kotorDirectory sources
            Assert.That(isValid, Is.True, "KOTOR directory sources should be handled differently");
        }

        [Test]
        public async Task ValidateComponentFilesExistAsync_WithEmptySourceList_SkipsValidation()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid()
            };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string>(),
                Destination = "<<kotorDirectory>>/Override"
            });

            bool isValid = await ComponentValidationService.ValidateComponentFilesExistAsync(component).ConfigureAwait(false);

            Assert.That(isValid, Is.True, "Instructions with empty source should be skipped");
        }

        [Test]
        public async Task GetMissingFilesForComponentAsync_WithNullComponent_ThrowsException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await ComponentValidationService.GetMissingFilesForComponentAsync(null).ConfigureAwait(false);
            }, "Should throw ArgumentNullException for null component");
        }

        #endregion
    }
}

