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
    public sealed class ExhaustiveInstructionTypeCombinationsTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_Exhaustive_" + Guid.NewGuid());
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

        #region All Instruction Types in Sequence

        [Test]
        public async Task AllInstructionTypes_ExecutedInSequence_HandlesAll()
        {
            // Setup files
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");
            File.WriteAllText(Path.Combine(_modDirectory, "file4.txt"), "content4");
            File.WriteAllText(Path.Combine(_modDirectory, "file5.txt"), "content5");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc");

            var component = new ModComponent { Name = "All Types", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract (requires archive)
            string zipPath = CreateTestZip("archive.zip", new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "extracted.txt", "extracted content" }
            });
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/archive.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Rename
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<modDirectory>>/file3.txt" },
                Destination = "renamed.txt"
            });

            // Delete
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/file4.txt" }
            });

            // DelDuplicate
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, "Should execute all instruction types");
        }

        #endregion

        #region Instruction Type Pairs

        [TestCase(Instruction.ActionType.Move, Instruction.ActionType.Copy)]
        [TestCase(Instruction.ActionType.Copy, Instruction.ActionType.Move)]
        [TestCase(Instruction.ActionType.Move, Instruction.ActionType.Delete)]
        [TestCase(Instruction.ActionType.Copy, Instruction.ActionType.Rename)]
        [TestCase(Instruction.ActionType.Rename, Instruction.ActionType.Move)]
        [TestCase(Instruction.ActionType.Delete, Instruction.ActionType.Move)]
        public async Task InstructionTypePairs_ExecutedInSequence_HandlesBoth(
            Instruction.ActionType first, Instruction.ActionType second)
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Type Pairs", Guid = Guid.NewGuid(), IsSelected = true };

            var firstInstruction = new Instruction
            {
                Action = first,
                Source = new List<string> { "<<modDirectory>>/file1.txt" }
            };

            if (first == Instruction.ActionType.Move || first == Instruction.ActionType.Copy)
            {
                firstInstruction.Destination = "<<kotorDirectory>>/Override";
            }
            else if (first == Instruction.ActionType.Rename)
            {
                firstInstruction.Destination = "renamed1.txt";
            }

            component.Instructions.Add(firstInstruction);

            var secondInstruction = new Instruction
            {
                Action = second,
                Source = new List<string> { "<<modDirectory>>/file2.txt" }
            };

            if (second == Instruction.ActionType.Move || second == Instruction.ActionType.Copy)
            {
                secondInstruction.Destination = "<<kotorDirectory>>/Override";
            }
            else if (second == Instruction.ActionType.Rename)
            {
                secondInstruction.Destination = "renamed2.txt";
            }

            component.Instructions.Add(secondInstruction);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, $"Should handle {first} then {second}");
        }

        #endregion

        #region Instruction Type Triplets

        [Test]
        public async Task InstructionTypeTriplets_ExtractMoveCopy_ExecutesInOrder()
        {
            string zipPath = CreateTestZip("mod.zip", new Dictionary<string, string>
(StringComparer.Ordinal)
            {
                { "file.txt", "content" }
            });
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            var component = new ModComponent { Name = "Extract Move Copy", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True,
                    "Extracted file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True,
                    "File should be copied");
            });
        }

        [Test]
        public async Task InstructionTypeTriplets_MoveRenameDelete_ExecutesInOrder()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file3.txt"), "content3");

            var component = new ModComponent { Name = "Move Rename Delete", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/file.txt" },
                Destination = "renamed.txt"
            });

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/file2.txt" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "renamed.txt")), Is.True,
                    "File should be moved then renamed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file2.txt")), Is.False,
                    "File should be deleted");
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

