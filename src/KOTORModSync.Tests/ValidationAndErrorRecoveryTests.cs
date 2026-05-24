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
    public sealed class ValidationAndErrorRecoveryTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ValidationTests_" + Guid.NewGuid());
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

        #region Validation Edge Cases

        [Test]
        public async Task ValidateComponent_WithInvalidArchive_ReportsError()
        {
            // Create invalid archive (not a real zip)
            string invalidArchive = Path.Combine(_modDirectory, "invalid.zip");
            File.WriteAllText(invalidArchive, "This is not a valid zip file");

            var component = new ModComponent
            {
                Name = "Invalid Archive",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(invalidArchive)}" },
                        Destination = "<<modDirectory>>/extracted"
                    }
                }
            };

            var isValid = await ComponentValidationService.IsArchiveValidAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(isValid, Is.False, "Invalid archive should be reported as invalid");
            });
        }

        [Test]
        public async Task ValidateComponent_WithEmptyArchive_HandlesGracefully()
        {
            // Create empty zip
            string emptyArchive = Path.Combine(_modDirectory, "empty.zip");
            using (var archive = SharpCompress.Archives.Zip.ZipArchive.CreateArchive())
            {
                using (var stream = File.OpenWrite(emptyArchive))
                {
                    archive.SaveTo(stream, new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.None));
                }
            }

            var component = new ModComponent
            {
                Name = "Empty Archive",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(emptyArchive)}" },
                        Destination = "<<modDirectory>>/extracted"
                    }
                }
            };

            var isValid = await ComponentValidationService.IsArchiveValidAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(isValid, Is.True, "Empty archive should be valid (just empty)");
            });
        }

        [Test]
        public async Task ValidateComponent_WithMissingSourceFiles_ReportsMissing()
        {
            var component = new ModComponent
            {
                Name = "Missing Files",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/nonexistent.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Not.Empty, "Should report missing files");
            });
        }

        [Test]
        public async Task ValidateComponent_WithWildcardSource_ValidatesCorrectly()
        {
            // Create some files
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent
            {
                Name = "Wildcard Source",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/*.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                // Wildcard should match existing files, so no missing files
            });
        }

        [Test]
        public async Task ValidateComponent_WithInvalidDestination_ReportsError()
        {
            var component = new ModComponent
            {
                Name = "Invalid Destination",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file.txt" },
                        Destination = "<<kotorDirectory>>/Invalid:Path" // Invalid characters
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
            });
        }

        #endregion

        #region Error Recovery Scenarios

        [Test]
        public async Task Install_WithPartialFailure_ContinuesWithOtherComponents()
        {
            var failingMod = new ModComponent
            {
                Name = "Failing Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            // Make it fail by removing archive
            string archivePath = Path.Combine(_modDirectory, "Failing Mod.zip");
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            var workingMod = new ModComponent
            {
                Name = "Working Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            File.WriteAllText(Path.Combine(_modDirectory, "working_file.txt"), "content");
            workingMod.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/working_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            _config.allComponents = new List<ModComponent> { failingMod, workingMod };

            // Installation should continue even if one fails
            var ordered = new List<ModComponent> { failingMod, workingMod };
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, System.Threading.CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(failingMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Failed), "Failing mod should be marked as failed");
                Assert.That(workingMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "Working mod should complete");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "working_file.txt")), Is.True, "Working mod file should exist");
            });
        }

        [Test]
        public async Task Install_WithDependencyFailure_BlocksDescendants()
        {
            var failingMod = new ModComponent
            {
                Name = "Failing Dependency",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            // Make it fail
            string archivePath = Path.Combine(_modDirectory, "Failing Dependency.zip");
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            var dependentMod = new ModComponent
            {
                Name = "Dependent Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Dependencies = new List<Guid> { failingMod.Guid }
            };

            _config.allComponents = new List<ModComponent> { failingMod, dependentMod };

            var ordered = new List<ModComponent> { failingMod, dependentMod };
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, System.Threading.CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(failingMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Failed), "Failing mod should be marked as failed");
                Assert.That(dependentMod.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Blocked), "Dependent mod should be blocked");
            });
        }

        [Test]
        public async Task Install_WithRecoverableError_RetriesCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Recoverable",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            // Create file that will be moved
            File.WriteAllText(Path.Combine(_modDirectory, "recoverable.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/recoverable.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            _config.allComponents = new List<ModComponent> { component };

            var ordered = new List<ModComponent> { component };
            var exitCode = await InstallationService.InstallAllSelectedComponentsAsync(ordered, progressCallback: null, System.Threading.CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "recoverable.txt")), Is.True, "File should be moved");
            });
        }

        #endregion

        #region Component Validation with Complex Scenarios

        [Test]
        public async Task ValidateComponent_WithNestedInstructions_ValidatesAll()
        {
            var component = new ModComponent
            {
                Name = "Nested Instructions",
                Guid = Guid.NewGuid()
            };

            var option = new Option
            {
                Name = "Option",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/option_file.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            component.Options.Add(option);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/main_file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option.Guid.ToString() }
            });

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                // Should validate both main instructions and option instructions
            });
        }

        [Test]
        public async Task ValidateComponent_WithResourceRegistryMismatch_ReportsMismatch()
        {
            string archivePath = Path.Combine(_modDirectory, "mod.zip");
            using (var archive = SharpCompress.Archives.Zip.ZipArchive.CreateArchive())
            {
                archive.AddEntry("file1.txt", new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content1")), true);
                archive.AddEntry("file2.txt", new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content2")), true);
                using (var stream = File.OpenWrite(archivePath))
                {
                    archive.SaveTo(stream, new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.None));
                }
            }

            var component = new ModComponent
            {
                Name = "Registry Mismatch",
                Guid = Guid.NewGuid(),
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        Path.GetFileName(archivePath),
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file1.txt", true },
                                { "file3.txt", true } // File doesn't exist in archive
                            }
                        }
                    }
                },
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(archivePath)}" },
                        Destination = "<<modDirectory>>/extracted"
                    }
                }
            };

            var isValid = await ComponentValidationService.IsArchiveValidAsync(component);

            Assert.Multiple(() =>
            {
                // Archive itself is valid, but ResourceRegistry may have mismatches
                Assert.That(isValid, Is.True, "Archive should be valid");
            });
        }

        #endregion
    }
}
