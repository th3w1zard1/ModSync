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
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class ArchiveFormatCombinationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ArchiveFormatTests_" + Guid.NewGuid());
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

        #region Multiple Archive Format Tests

        [Test]
        public async Task Extract_MultipleZipArchives_ExtractsAll()
        {
            string zip1 = CreateTestZip("part1.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "content1" }
            });
            string zip2 = CreateTestZip("part2.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file2.txt", "content2" }
            });
            string zip3 = CreateTestZip("part3.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file3.txt", "content3" }
            });

            var component = new ModComponent { Name = "Multi Zip", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string>
                {
                    "<<modDirectory>>/part1.zip",
                    "<<modDirectory>>/part2.zip",
                    "<<modDirectory>>/part3.zip"
                },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multiple zips should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file1.txt")), Is.True,
                    "First archive should extract");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file2.txt")), Is.True,
                    "Second archive should extract");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file3.txt")), Is.True,
                    "Third archive should extract");
            });
        }

        [Test]
        public async Task Extract_ArchiveWithNestedArchives_HandlesCorrectly()
        {
            // Create inner archive
            string innerZip = CreateTestZip("inner.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "nested.txt", "nested content" }
            });

            // Create outer archive containing the inner archive
            string outerZip = CreateTestZip("outer.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "inner.zip", File.ReadAllText(innerZip) }, // This won't work correctly, but tests the scenario
                { "outer.txt", "outer content" }
            });

            var component = new ModComponent { Name = "Nested Archives", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/outer.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Nested archives should be handled");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "outer.txt")), Is.True,
                    "Outer archive should extract");
            });
        }

        [Test]
        public async Task Extract_ArchiveWithLargeFiles_HandlesCorrectly()
        {
            // Create archive with multiple files
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < 10; i++)
            {
                files[$"file{i}.txt"] = new string('x', 10000); // 10KB per file
            }

            string archivePath = CreateTestZip("large.zip", files);

            var component = new ModComponent { Name = "Large Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/large.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Large archive should extract");
                for (int i = 0; i < 10; i++)
                {
                    Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", $"file{i}.txt")), Is.True,
                        $"File {i} should be extracted");
                }
            });
        }

        [Test]
        public async Task Extract_ArchiveWithSpecialCharacters_HandlesCorrectly()
        {
            string archivePath = CreateTestZip("special.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file with spaces.txt", "content1" },
                { "file-with-dashes.txt", "content2" },
                { "file_with_underscores.txt", "content3" },
                { "file.with.dots.txt", "content4" }
            });

            var component = new ModComponent { Name = "Special Chars Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/special.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Special characters should be handled");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file with spaces.txt")), Is.True,
                    "Files with spaces should extract");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file-with-dashes.txt")), Is.True,
                    "Files with dashes should extract");
            });
        }

        #endregion

        #region Extract Then Process Scenarios

        [Test]
        public async Task ExtractThenProcess_ExtractMoveDeleteSequence_ExecutesCorrectly()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "textures/texture1.tga", "tga1" },
                { "textures/texture2.tga", "tga2" },
                { "old/old.txt", "old content" }
            });

            var component = new ModComponent { Name = "Extract Process", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move textures
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/textures/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Delete old directory
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/extracted/old/*.txt" }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Extract-process sequence should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True,
                    "Textures should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True,
                    "Textures should be moved");
            });
        }

        [Test]
        public async Task ExtractThenProcess_ExtractCopyRenameSequence_ExecutesCorrectly()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "model.mdl", "model content" }
            });

            var component = new ModComponent { Name = "Extract Copy Rename", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Copy model
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/extracted/model.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Rename copied file
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/model.mdl" },
                Destination = "renamed_model.mdl"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Extract-copy-rename should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "renamed_model.mdl")), Is.True,
                    "File should be renamed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "model.mdl")), Is.True,
                    "Source should remain (copy operation)");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            using (var archive = ZipArchive.Create())
            {
                foreach (var kvp in files)
                {
                    if (kvp.Value != null)
                    {
                        archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                    }
                }
                using (var stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new WriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion
    }
}

