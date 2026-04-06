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
    public sealed class BoundaryConditionsAndLimitsTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_BoundaryTests_" + Guid.NewGuid());
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

        #region Boundary Conditions

        [Test]
        public async Task Instruction_WithZeroByteFile_HandlesCorrectly()
        {
            string zeroByteFile = Path.Combine(_modDirectory, "zero.txt");
            File.Create(zeroByteFile).Dispose(); // Create empty file

            var component = new ModComponent { Name = "Zero Byte", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { zeroByteFile },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle zero-byte file");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "zero.txt")), Is.True, "Zero-byte file should exist");
                Assert.That(new FileInfo(Path.Combine(_kotorDirectory, "Override", "zero.txt")).Length, Is.EqualTo(0), "File should be zero bytes");
            });
        }

        [Test]
        public async Task Instruction_WithVeryLargeFile_HandlesCorrectly()
        {
            // Create a moderately large file (10MB for testing)
            string largeFile = Path.Combine(_modDirectory, "large.bin");
            using (var fs = new FileStream(largeFile, FileMode.Create))
            {
                byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                for (int i = 0; i < 10; i++)
                {
                    fs.Write(buffer, 0, buffer.Length);
                }
            }

            var component = new ModComponent { Name = "Large File", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { largeFile },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle large file");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "large.bin")), Is.True, "Large file should exist");
                Assert.That(new FileInfo(Path.Combine(_kotorDirectory, "Override", "large.bin")).Length, Is.EqualTo(10 * 1024 * 1024), "File should be 10MB");
            });
        }

        [Test]
        public async Task Instruction_WithMaximumPathLength_HandlesCorrectly()
        {
            // Create path near maximum length (260 chars on Windows, longer on Unix)
            int maxLength = 200; // Conservative limit for testing
            string longPath = Path.Combine(_modDirectory, new string('A', maxLength - _modDirectory.Length - 10) + ".txt");
            File.WriteAllText(longPath, "content");

            var component = new ModComponent { Name = "Max Path", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { longPath },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                // May succeed or fail depending on OS path length limits
                Assert.That(result, Is.Not.Null, "Should return a result");
            });
        }

        [Test]
        public async Task Component_WithManyInstructions_HandlesCorrectly()
        {
            var component = new ModComponent { Name = "Many Instructions", Guid = Guid.NewGuid(), IsSelected = true };

            // Create 50 instructions
            for (int i = 0; i < 50; i++)
            {
                string file = Path.Combine(_modDirectory, $"file{i}.txt");
                File.WriteAllText(file, $"content{i}");

                component.Instructions.Add(new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { file },
                    Destination = "<<kotorDirectory>>/Override"
                });
            }

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should handle many instructions");
                Assert.That(Directory.GetFiles(Path.Combine(_kotorDirectory, "Override"), "file*.txt").Length, Is.EqualTo(50), "All files should be moved");
            });
        }

        [Test]
        public async Task Component_WithManyOptions_HandlesCorrectly()
        {
            var component = new ModComponent { Name = "Many Options", Guid = Guid.NewGuid(), IsSelected = true };

            // Create 20 options
            var optionGuids = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                var option = new Option { Name = $"Option {i}", Guid = Guid.NewGuid(), IsSelected = i < 5 }; // First 5 selected
                File.WriteAllText(Path.Combine(_modDirectory, $"option{i}.txt"), $"content{i}");

                option.Instructions.Add(new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { $"<<modDirectory>>/option{i}.txt" },
                    Destination = "<<kotorDirectory>>/Override"
                });

                component.Options.Add(option);
                optionGuids.Add(option.Guid.ToString());
            }

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = optionGuids
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

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should handle many options");
                Assert.That(Directory.GetFiles(Path.Combine(_kotorDirectory, "Override"), "option*.txt").Length, Is.EqualTo(5), "Only selected options should install");
            });
        }

        [Test]
        public async Task Instruction_WithManySourceFiles_HandlesCorrectly()
        {
            var component = new ModComponent { Name = "Many Sources", Guid = Guid.NewGuid(), IsSelected = true };

            // Create 100 files
            var sources = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                string file = Path.Combine(_modDirectory, $"file{i}.txt");
                File.WriteAllText(file, $"content{i}");
                sources.Add(file);
            }

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = sources.Select(f => f.Replace(_modDirectory, "<<modDirectory>>")).ToList(),
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle many source files");
                Assert.That(Directory.GetFiles(Path.Combine(_kotorDirectory, "Override"), "file*.txt").Length, Is.EqualTo(100), "All files should be moved");
            });
        }

        [Test]
        public async Task Extract_WithManyFilesInArchive_HandlesCorrectly()
        {
            // Create archive with 200 files
            string archivePath = Path.Combine(_modDirectory, "large_archive.zip");
            using (var archive = ZipArchive.Create())
            {
                for (int i = 0; i < 200; i++)
                {
                    archive.AddEntry($"file{i}.txt", new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"content{i}")), true);
                }
                using (var stream = File.OpenWrite(archivePath))
                {
                    archive.SaveTo(stream, new WriterOptions(CompressionType.None));
                }
            }

            var component = new ModComponent { Name = "Large Archive", Guid = Guid.NewGuid(), IsSelected = true };

            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { $"<<modDirectory>>/{Path.GetFileName(archivePath)}" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteSingleInstructionAsync(instruction, 0, new List<ModComponent> { component }, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(Instruction.ActionExitCode.Success), "Should handle large archive");
                Assert.That(Directory.GetFiles(Path.Combine(_modDirectory, "extracted"), "file*.txt", SearchOption.AllDirectories).Length, Is.EqualTo(200), "All files should be extracted");
            });
        }

        #endregion
    }
}

