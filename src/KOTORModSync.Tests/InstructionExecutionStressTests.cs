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
    public sealed class InstructionExecutionStressTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_StressTests_" + Guid.NewGuid());
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

        #region Stress Tests

        [Test]
        public async Task StressTest_ManyInstructions_ExecutesAll()
        {
            // Create many files
            for (int i = 0; i < 50; i++)
            {
                File.WriteAllText(Path.Combine(_modDirectory, $"file{i:D3}.txt"), $"content{i}");
            }

            var component = new ModComponent { Name = "Many Instructions", Guid = Guid.NewGuid(), IsSelected = true };

            // Add many move instructions
            for (int i = 0; i < 50; i++)
            {
                component.Instructions.Add(new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { $"<<modDirectory>>/file{i:D3}.txt" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Many instructions should succeed");
                for (int i = 0; i < 50; i++)
                {
                    Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", $"file{i:D3}.txt")), Is.True,
                        $"File {i} should be moved");
                }
            });
        }

        [Test]
        public async Task StressTest_ManyComponents_ExecutesInOrder()
        {
            var components = new List<ModComponent>();

            // Create 20 components
            for (int i = 0; i < 20; i++)
            {
                File.WriteAllText(Path.Combine(_modDirectory, $"mod{i}.txt"), $"mod{i}");
                var component = new ModComponent
                {
                    Name = $"Mod {i}",
                    Guid = Guid.NewGuid(),
                    IsSelected = true
                };

                component.Instructions.Add(new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { $"<<modDirectory>>/mod{i}.txt" },
                    Destination = "<<kotorDirectory>>/Override"
                });

                components.Add(component);
            }

            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var mod in ordered)
            {
                foreach (var instruction in mod.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(mod);
                }
            }

            foreach (var mod in ordered)
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should install");
            }

            // Verify all files were moved
            for (int i = 0; i < 20; i++)
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", $"mod{i}.txt")), Is.True,
                    $"Mod {i} file should exist");
            }
        }

        [Test]
        public async Task StressTest_ManyOptions_ExecutesSelectedOnes()
        {
            var component = new ModComponent { Name = "Many Options", Guid = Guid.NewGuid(), IsSelected = true };

            // Create 10 options, alternating selected
            for (int i = 0; i < 10; i++)
            {
                File.WriteAllText(Path.Combine(_modDirectory, $"option{i}.txt"), $"option{i}");
                var option = new Option
                {
                    Name = $"Option {i}",
                    Guid = Guid.NewGuid(),
                    IsSelected = (i % 2 == 0) // Even numbers selected
                };

                option.Instructions.Add(new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { $"<<modDirectory>>/option{i}.txt" },
                    Destination = "<<kotorDirectory>>/Override"
                });

                component.Options.Add(option);
            }

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = component.Options.Select(o => o.Guid.ToString()).ToList()
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Many options should succeed");
                // Even-numbered options should be installed
                for (int i = 0; i < 10; i += 2)
                {
                    Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", $"option{i}.txt")), Is.True,
                        $"Option {i} should be installed");
                }
                // Odd-numbered options should NOT be installed
                for (int i = 1; i < 10; i += 2)
                {
                    Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", $"option{i}.txt")), Is.False,
                        $"Option {i} should NOT be installed");
                }
            });
        }

        [Test]
        public async Task StressTest_DeeplyNestedDirectories_HandlesCorrectly()
        {
            // Create deeply nested directory structure
            string deepPath = _modDirectory;
            for (int i = 0; i < 10; i++)
            {
                deepPath = Path.Combine(deepPath, $"level{i}");
                Directory.CreateDirectory(deepPath);
            }

            File.WriteAllText(Path.Combine(deepPath, "file.txt"), "content");

            var component = new ModComponent { Name = "Deep Nested", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/level0/level1/level2/level3/level4/level5/level6/level7/level8/level9/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Deep nesting should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "File should be moved from deep nesting");
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
                    archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
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

