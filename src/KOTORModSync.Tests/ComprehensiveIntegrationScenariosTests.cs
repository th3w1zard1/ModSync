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
    public sealed class ComprehensiveIntegrationScenariosTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_Integration_" + Guid.NewGuid());
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

        #region Complex Instruction Sequences

        [Test]
        public async Task ExecuteInstructions_WithExtractThenMove_ExecutesInOrder()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            // Create archive
            var archivePath = Path.Combine(_modDirectory, "archive.zip");
            CreateTestZip(archivePath, new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "file.txt", "content" }
            });

            // Extract first
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/archive.zip" },
                Destination = "<<modDirectory>>/extracted",
                Overwrite = true
            });

            // Then move extracted file
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            var components = new List<ModComponent> { component };
            var fileSystemProvider = new RealFileSystemProvider();

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var exitCode = await component.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should be moved after extraction");
            });
        }

        [Test]
        public async Task ExecuteInstructions_WithCopyThenDelete_ExecutesInOrder()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var sourceFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(sourceFile, "content");

            // Copy first
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            // Then delete source
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/file.txt" }
            });

            var components = new List<ModComponent> { component };
            var fileSystemProvider = new RealFileSystemProvider();

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var exitCode = await component.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should be copied");
                Assert.That(File.Exists(sourceFile), Is.False, "Source file should be deleted");
            });
        }

        [Test]
        public async Task ExecuteInstructions_WithRenameThenMove_ExecutesInOrder()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var sourceFile = Path.Combine(_modDirectory, "oldname.txt");
            File.WriteAllText(sourceFile, "content");

            // Rename first
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<modDirectory>>/oldname.txt" },
                Destination = "newname.txt",
                Overwrite = true
            });

            // Then move renamed file
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/newname.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            var components = new List<ModComponent> { component };
            var fileSystemProvider = new RealFileSystemProvider();

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var exitCode = await component.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "newname.txt")), Is.True, "Renamed file should be moved");
                Assert.That(File.Exists(sourceFile), Is.False, "Old file should not exist");
            });
        }

        #endregion

        #region Component with Options Scenarios

        [Test]
        public async Task ExecuteInstructions_WithSelectedOption_ExecutesOptionInstructions()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var option = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "option_file.txt"), "option content");

            option.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            component.Options.Add(option);

            var components = new List<ModComponent> { component };
            var fileSystemProvider = new RealFileSystemProvider();

            foreach (var instruction in option.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var exitCode = await component.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option_file.txt")), Is.True, "Option file should be moved");
            });
        }

        [Test]
        public async Task ExecuteInstructions_WithUnselectedOption_SkipsOptionInstructions()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            var option = new Option
            {
                Name = "Option 1",
                Guid = Guid.NewGuid(),
                IsSelected = false
            };

            File.WriteAllText(Path.Combine(_modDirectory, "option_file.txt"), "option content");

            option.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            component.Options.Add(option);

            var components = new List<ModComponent> { component };
            var fileSystemProvider = new RealFileSystemProvider();

            var exitCode = await component.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option_file.txt")), Is.False, "Option file should not be moved when option is not selected");
            });
        }

        #endregion

        #region Conditional Instruction Execution

        [Test]
        public async Task ExecuteInstructions_WithConditionalInstructions_ExecutesOnlyValid()
        {
            var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = true };
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = false };

            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            // Instruction 1: No conditions - should run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            // Instruction 2: Dependency met - should run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid },
                Overwrite = true
            });

            // Instruction 3: Restriction not selected - should run
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedComponent.Guid },
                Overwrite = true
            });

            var components = new List<ModComponent> { depComponent, restrictedComponent, component };
            var fileSystemProvider = new RealFileSystemProvider();

            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var exitCode = await component.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success), "Installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "File1 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "File2 should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.txt")), Is.True, "File3 should be moved");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            string dir = Path.GetDirectoryName(zipPath);
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
            }

            using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
            {
                foreach (var file in files)
                {
                    _ = archive.AddEntry(file.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(file.Value)));
                }

                using (var stream = File.Create(zipPath))
                {
                    archive.SaveTo(stream, new SharpCompress.Writers.WriterOptions(SharpCompress.Common.CompressionType.None));
                }
            }

            return zipPath;
        }

        #endregion
    }
}

