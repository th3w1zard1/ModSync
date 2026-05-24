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
using KOTORModSync.Core.Services.FileSystem;
using NUnit.Framework;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class ComponentValidationComplexTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;
        private ComponentValidationService _validationService;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ValidationComplexTests_" + Guid.NewGuid());
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

            _validationService = new ComponentValidationService();
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

        #region ResourceRegistry Validation

        [Test]
        public async Task ValidateComponent_WithResourceRegistry_ValidatesArchiveFiles()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" },
                { "file2.txt", "content2" }
            });

            var component = new ModComponent
            {
                Name = "ResourceRegistry Test",
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
                                { "file2.txt", true }
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

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "Archive should be found via ResourceRegistry");
            });
        }

        [Test]
        public async Task ValidateComponent_WithResourceRegistryAndMissingArchive_ReportsMissing()
        {
            var component = new ModComponent
            {
                Name = "Missing Archive",
                Guid = Guid.NewGuid(),
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        "missing.zip",
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file1.txt", true }
                            }
                        }
                    }
                },
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { "<<modDirectory>>/missing.zip" },
                        Destination = "<<modDirectory>>/extracted"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Not.Empty, "Missing archive should be reported");
                Assert.That(missingFiles, Has.Some.Contain("missing.zip"), "Missing files should include missing archive");
            });
        }

        [Test]
        public async Task AnalyzeDownloadNecessity_WithResourceRegistry_IdentifiesDownloads()
        {
            var component = new ModComponent
            {
                Name = "Download Test",
                Guid = Guid.NewGuid(),
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        "http://example.com/mod.zip",
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "file1.txt", null }
                            }
                        }
                    }
                },
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file1.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var (urls, failed) = await _validationService.AnalyzeDownloadNecessityAsync(component, _modDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(failed, Is.False, "Analysis should not fail");
                Assert.That(urls, Is.Not.Empty, "Should identify download URL");
                Assert.That(urls, Contains.Item("http://example.com/mod.zip"), "Should include ResourceRegistry URL");
            });
        }

        #endregion

        #region Complex Instruction Validation

        [Test]
        public async Task ValidateComponent_WithNestedInstructions_ValidatesAll()
        {
            string zipPath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" }
            });
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent
            {
                Name = "Nested Instructions",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                        Destination = "<<modDirectory>>/extracted"
                    },
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file2.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    },
                    new Instruction
                    {
                        Action = Instruction.ActionType.Copy,
                        Source = new List<string> { "<<modDirectory>>/extracted/file1.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "All files should be found");
            });
        }

        [Test]
        public async Task ValidateComponent_WithChooseInstruction_ValidatesOptionInstructions()
        {
            var component = new ModComponent
            {
                Name = "Choose Validation",
                Guid = Guid.NewGuid()
            };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid() };
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid() };
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString() }
            });

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "All option files should be found");
            });
        }

        #endregion

        #region Wildcard Validation Scenarios

        [Test]
        public async Task ValidateComponent_WithMultipleWildcardPatterns_ValidatesAll()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "texture1.tga"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "texture2.tga"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "texture3.tpc"), "content3");
            File.WriteAllText(Path.Combine(_modDirectory, "other.dat"), "content4");

            var component = new ModComponent
            {
                Name = "Multiple Wildcards",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string>
                        {
                            "<<modDirectory>>/*.tga",
                            "<<modDirectory>>/*.tpc"
                        },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "All wildcard patterns should match files");
            });
        }

        [Test]
        public async Task ValidateComponent_WithWildcardInSubdirectories_ValidatesRecursively()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir1"));
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir2"));
            File.WriteAllText(Path.Combine(_modDirectory, "subdir1", "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "subdir2", "file2.txt"), "content2");

            var component = new ModComponent
            {
                Name = "Recursive Wildcards",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/**/*.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "Recursive wildcard should match files in subdirectories");
            });
        }

        #endregion

        #region Validation with Dependencies

        [Test]
        public async Task ValidateComponent_WithInstructionDependencies_ValidatesWhenDependencyMet()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent
            {
                Name = "Dependent",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file.txt" },
                        Destination = "<<kotorDirectory>>/Override",
                        Dependencies = new List<Guid> { depComponent.Guid }
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "File should be found when dependency is met");
            });
        }

        [Test]
        public async Task ValidateComponent_WithInstructionRestrictions_ValidatesWhenRestrictionNotMet()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = false };
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent
            {
                Name = "Component",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file.txt" },
                        Destination = "<<kotorDirectory>>/Override",
                        Restrictions = new List<Guid> { restrictedComponent.Guid }
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "File should be found when restriction is not met");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            using (var archive = ZipArchive.CreateArchive())
            {
                foreach (var kvp in files)
                {
                    archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                }
                using (var stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new SharpCompress.Writers.Zip.ZipWriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion
    }
}

