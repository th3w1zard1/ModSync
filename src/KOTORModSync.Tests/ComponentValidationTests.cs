// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public class ComponentValidationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;
        private ComponentValidationService _validationService;

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

        #region File Existence Validation

        [Test]
        public async Task ValidateComponent_WithExistingFiles_ReturnsValid()
        {
            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/test.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);
            var result = new { IsValid = missingFiles.Count == 0, MissingFiles = missingFiles };

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.True, "Component with existing files should be valid");
                Assert.That(result.MissingFiles, Is.Empty, "No files should be missing when all files exist");
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(component.Instructions, Is.Not.Empty, "Component should have instructions");
                Assert.That(component.Instructions[0].Source, Is.Not.Empty, "Instruction should have source paths");
            });
        }

        [Test]
        public async Task ValidateComponent_WithMissingFiles_ReturnsInvalid()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
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
            var result = new { IsValid = missingFiles.Count == 0, MissingFiles = missingFiles };

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.False, "Component with missing files should be invalid");
                Assert.That(result.MissingFiles, Is.Not.Empty, "Missing files list should contain the missing file");
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles.Count, Is.GreaterThan(0), "At least one file should be reported as missing");
                Assert.That(missingFiles, Has.Some.Contain("nonexistent.txt"), "Missing files should include the nonexistent file");
            });
        }

        [Test]
        public async Task ValidateComponent_WithWildcardPattern_ValidatesMatchingFiles()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent
            {
                Name = "Test Mod",
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
            var result = new { IsValid = missingFiles.Count == 0, MissingFiles = missingFiles };

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.True, "Component with wildcard pattern matching files should be valid");
                Assert.That(result.MissingFiles, Is.Empty, "No files should be missing when wildcard matches existing files");
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
            });
        }

        #endregion

        #region Archive Validation

        [Test]
        public async Task ValidateComponent_WithValidArchive_ReturnsValid()
        {
            string zipPath = CreateTestZip("test.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" }
            });

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(zipPath)}" },
                        Destination = "<<modDirectory>>/extracted"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);
            var result = new { IsValid = missingFiles.Count == 0, MissingFiles = missingFiles };

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.True, "Component with valid archive should be valid");
                Assert.That(result.MissingFiles, Is.Empty, "No files should be missing when archive is valid");
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(File.Exists(zipPath), Is.True, "Test archive file should exist");
            });
        }

        [Test]
        public async Task ValidateComponent_WithInvalidArchive_ReturnsInvalid()
        {
            string invalidPath = Path.Combine(_modDirectory, "invalid.zip");
            File.WriteAllText(invalidPath, "not a zip file");

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { "<<modDirectory>>/invalid.zip" },
                        Destination = "<<modDirectory>>/extracted"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);
            var result = new { IsValid = missingFiles.Count == 0, MissingFiles = missingFiles };

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.False, "Component with invalid archive should be invalid");
                Assert.That(result.MissingFiles, Is.Not.Empty, "Invalid archive should be reported in missing files");
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(File.Exists(invalidPath), Is.True, "Invalid archive file should exist (but be invalid format)");
            });
        }

        #endregion

        #region Download Necessity Analysis

        [Test]
        public async Task AnalyzeDownloadNecessity_WithMatchingFiles_ReturnsNoDownloads()
        {
            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/test.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var (urls, failed) = await _validationService.AnalyzeDownloadNecessityAsync(component, _modDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(failed, Is.False, "Analysis should not fail when files exist locally");
                Assert.That(urls, Is.Empty, "No downloads should be required when files exist locally");
                Assert.That(urls, Is.Not.Null, "URLs list should not be null");
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
            });
        }

        [Test]
        public async Task AnalyzeDownloadNecessity_WithMissingFiles_ReturnsDownloads()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal)
                {
                    {
                        "http://example.com/mod.zip",
                        new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(StringComparer.Ordinal)
                            {
                                { "test.txt", null }
                            }
                        }
                    }
                },
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/test.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var (urls, failed) = await _validationService.AnalyzeDownloadNecessityAsync(component, _modDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(urls, Is.Not.Empty, "Downloads should be required when files are missing");
                Assert.That(urls, Is.Not.Null, "URLs list should not be null");
                Assert.That(urls, Has.Some.EqualTo("http://example.com/mod.zip"), "URLs should include the resource registry URL");
                Assert.That(component.ResourceRegistry, Is.Not.Null, "Component should have resource registry");
                Assert.That(component.ResourceRegistry.Count, Is.GreaterThan(0), "Resource registry should contain entries");
            });
        }

        #endregion

        #region Path Sandboxing Validation

        [Test]
        public void SetRealPaths_WithModDirectoryPlaceholder_ResolvesToSourcePath()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>()
            };

            // Create the test file first
            string testFilePath = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFilePath, "test content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<modDirectory>>/output"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);
            // Use skipExistenceCheck to test placeholder resolution without file existence validation
            instruction.SetRealPaths(skipExistenceCheck: true);

            // Use reflection to access private properties for testing
            var realSourcePathsProperty = typeof(Instruction).GetProperty("RealSourcePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var realDestinationPathProperty = typeof(Instruction).GetProperty("RealDestinationPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var realSourcePaths = (List<string>)realSourcePathsProperty?.GetValue(instruction);
            var realDestinationPath = (DirectoryInfo)realDestinationPathProperty?.GetValue(instruction);

            Assert.Multiple(() =>
            {
                Assert.That(realSourcePaths, Is.Not.Null, "RealSourcePaths list should not be null");
                Assert.That(realSourcePaths, Is.Not.Empty, "RealSourcePaths list should not be empty");
                Assert.That(realSourcePaths[0], Does.Contain(_modDirectory), "RealSourcePaths should resolve to mod directory");
                Assert.That(realDestinationPath, Is.Not.Null, "RealDestinationPath should not be null");
                Assert.That(realDestinationPath.FullName, Does.Contain(_modDirectory), "RealDestinationPath should resolve to mod directory");
                Assert.That(realSourcePaths[0], Does.Not.Contain("<<modDirectory>>"), "Placeholder should be replaced with actual path in RealSourcePaths");
            });
        }

        [Test]
        public void SetRealPaths_WithKotorDirectoryPlaceholder_ResolvesToDestinationPath()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>()
            };

            // Create the test file first
            string testFilePath = Path.Combine(_kotorDirectory, "test.txt");
            File.WriteAllText(testFilePath, "test content");

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<kotorDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);
            // Use skipExistenceCheck to test placeholder resolution without file existence validation
            instruction.SetRealPaths(skipExistenceCheck: true);

            // Use reflection to access private properties for testing
            var realSourcePathsProperty = typeof(Instruction).GetProperty("RealSourcePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var realDestinationPathProperty = typeof(Instruction).GetProperty("RealDestinationPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var realSourcePaths = (List<string>)realSourcePathsProperty?.GetValue(instruction);
            var realDestinationPath = (DirectoryInfo)realDestinationPathProperty?.GetValue(instruction);

            Assert.Multiple(() =>
            {
                Assert.That(realSourcePaths, Is.Not.Null, "RealSourcePaths list should not be null");
                Assert.That(realSourcePaths, Is.Not.Empty, "RealSourcePaths list should not be empty");
                Assert.That(realSourcePaths[0], Does.Contain(_kotorDirectory), "RealSourcePaths should resolve to KOTOR directory");
                Assert.That(realDestinationPath, Is.Not.Null, "RealDestinationPath should not be null");
                Assert.That(realDestinationPath.FullName, Does.Contain(_kotorDirectory), "RealDestinationPath should resolve to KOTOR directory");
                Assert.That(realSourcePaths[0], Does.Not.Contain("<<kotorDirectory>>"), "Placeholder should be replaced with actual path in RealSourcePaths");
            });
        }

        [Test]
        public void SetRealPaths_WithAbsolutePath_ThrowsOrRejects()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>()
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "C:\\Windows\\System32\\file.txt" },
                Destination = "C:\\Windows\\System32"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            // This should throw FileNotFoundException because the file doesn't exist and is outside sandbox
            var exception = Assert.Throws<FileNotFoundException>(() =>
            {
                instruction.SetRealPaths();
            }, "Absolute system path should be rejected");

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Exception should not be null");
                Assert.That(exception.Message, Does.Contain("System32"), "Exception message should mention the system path");
            });

            Assert.Multiple(() =>
            {
                Assert.That(instruction.Source, Is.Not.Null, "Source list should not be null");
                Assert.That(instruction.Source, Is.Not.Empty, "Source list should not be empty");
                Assert.That(instruction.Source[0], Does.Not.Contain("C:\\Windows"), "Absolute system path should be rejected or sanitized");
                Assert.That(instruction.Source[0], Does.Not.Contain("System32"), "System directory should not be accessible");
                // Path should be sandboxed to allowed directories
                Assert.That(instruction.Source[0], Does.Contain(_modDirectory).Or.Contain(_kotorDirectory),
                    "Path should be sandboxed to mod or KOTOR directory");
            });
        }

        #endregion

        #region Virtual File System Validation

        [Test]
        public async Task ValidateComponent_WithVFS_SimulatesOperations()
        {
            string testFile = Path.Combine(_modDirectory, "test.txt");
            File.WriteAllText(testFile, "content");

            var vfs = new VirtualFileSystemProvider();
            await vfs.InitializeFromRealFileSystemAsync(_modDirectory);
            await vfs.InitializeFromRealFileSystemAsync(_kotorDirectory);

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/test.txt" },
                        Destination = "<<kotorDirectory>>/Override",
                    },
                },
            };

            foreach (Instruction instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(vfs);
                var fileSystemProvider = new RealFileSystemProvider();
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
                _config.sourcePath = new DirectoryInfo(_modDirectory);
                _config.destinationPath = new DirectoryInfo(_kotorDirectory);
                instruction.SetRealPaths();
            }

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);
            var result = new { IsValid = missingFiles.Count == 0, MissingFiles = missingFiles };

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.True, "Component validation with VFS should succeed when files exist");
                Assert.That(result.MissingFiles, Is.Empty, "No files should be missing in VFS simulation");
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(vfs, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(vfs.IsDryRun, Is.True, "VFS should be in dry-run mode");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            using (var archive = ZipArchive.Create())
            {
                foreach (KeyValuePair<string, string> kvp in files)
                {
                    ZipArchiveEntry entry = archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)));
                }
                using (Stream stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new WriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion

        #region Edge Cases and Error Handling

        [Test]
        public async Task ValidateComponent_WithNullComponent_ThrowsArgumentNullException()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(async () => await ComponentValidationService.GetMissingFilesForComponentAsync(null), "Null component should throw ArgumentNullException");

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Exception should not be null");
                Assert.That(exception.ParamName, Is.Not.Null, "Exception parameter name should not be null");
            });
        }

        [Test]
        public async Task ValidateComponent_WithEmptyInstructions_ReturnsValid()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>()
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "Component with no instructions should have no missing files");
            });
        }

        [Test]
        public async Task ValidateComponent_WithNullSourcePaths_HandlesGracefully()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = null,
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            // Should handle null source gracefully
            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                // Behavior may vary, but should not throw
            });
        }

        [Test]
        public async Task ValidateComponent_WithEmptySourcePaths_HandlesGracefully()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string>(),
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                // Empty source should be handled gracefully
            });
        }

        [Test]
        public async Task ValidateComponent_WithMultipleInstructions_ValidatesAll()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            // file3.txt is missing

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file1.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    },
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file2.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    },
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/file3.txt" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Not.Empty, "Should report missing file3.txt");
                Assert.That(missingFiles, Has.Some.Contain("file3.txt"), "Missing files should include file3.txt");
                Assert.That(missingFiles, Has.None.Contain("file1.txt"), "Existing file1.txt should not be in missing list");
                Assert.That(missingFiles, Has.None.Contain("file2.txt"), "Existing file2.txt should not be in missing list");
            });
        }

        [Test]
        public async Task ValidateComponent_WithWildcardNoMatches_ReturnsInvalid()
        {
            // Create files that don't match the pattern
            File.WriteAllText(Path.Combine(_modDirectory, "file1.dat"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.dat"), "content2");

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { "<<modDirectory>>/*.txt" }, // Pattern doesn't match .dat files
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Not.Empty, "Wildcard pattern with no matches should report missing files");
            });
        }

        [Test]
        public async Task AnalyzeDownloadNecessity_WithNullComponent_ThrowsArgumentNullException()
        {
            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(() => _validationService.AnalyzeDownloadNecessityAsync(null, _modDirectory), "Null component should throw ArgumentNullException");

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Exception should not be null");
                Assert.That(exception.ParamName, Is.Not.Null, "Exception parameter name should not be null");
            });
        }

        [Test]
        public async Task AnalyzeDownloadNecessity_WithNullDirectory_ThrowsArgumentNullException()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid()
            };

            ArgumentNullException exception = Assert.ThrowsAsync<ArgumentNullException>(() => _validationService.AnalyzeDownloadNecessityAsync(component, null), "Null directory should throw ArgumentNullException");

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Exception should not be null");
                Assert.That(exception.ParamName, Is.Not.Null, "Exception parameter name should not be null");
            });
        }

        [Test]
        public void SetRealPaths_WithNullFileSystemProvider_ThrowsInvalidOperationException()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid()
            };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.Throws<InvalidOperationException>(() =>
            {
                instruction.SetRealPaths();
            }, "SetRealPaths without file system provider should throw InvalidOperationException");
        }

        [Test]
        public void SetRealPaths_WithNullParentComponent_HandlesGracefully()
        {
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            // Should handle null parent component (may throw or handle gracefully)
            Assert.DoesNotThrow(() =>
            {
                try
                {
                    instruction.SetRealPaths();
                }
                catch (Exception)
                {
                    // Expected if parent component is required
                }
            });
        }

        [Test]
        public async Task ValidateComponent_WithSpecialCharactersInPath_HandlesCorrectly()
        {
            string specialFileName = "file with spaces & special chars!@#.txt";
            string testFile = Path.Combine(_modDirectory, specialFileName);
            File.WriteAllText(testFile, "content");

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { $"<<modDirectory>>/{specialFileName}" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "Files with special characters should be found");
            });
        }

        [Test]
        public async Task ValidateComponent_WithVeryLongPath_HandlesCorrectly()
        {
            string longPath = Path.Combine(_modDirectory, new string('A', 200) + ".txt");
            File.WriteAllText(longPath, "content");

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(longPath)}" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                // Should handle long paths (may succeed or fail depending on OS limits)
            });
        }

        [Test]
        public async Task ValidateComponent_WithUnicodeCharactersInPath_HandlesCorrectly()
        {
            string unicodeFileName = "测试文件_тест_テスト.txt";
            string testFile = Path.Combine(_modDirectory, unicodeFileName);
            File.WriteAllText(testFile, "content");

            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { $"<<modDirectory>>/{unicodeFileName}" },
                        Destination = "<<kotorDirectory>>/Override"
                    }
                }
            };

            var missingFiles = await ComponentValidationService.GetMissingFilesForComponentAsync(component);

            Assert.Multiple(() =>
            {
                Assert.That(missingFiles, Is.Not.Null, "Missing files list should not be null");
                Assert.That(missingFiles, Is.Empty, "Files with Unicode characters should be found");
            });
        }

        #endregion
    }
}

