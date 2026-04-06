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
    public sealed class SystematicInstructionSequenceTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_SystematicTests_" + Guid.NewGuid());
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

        #region Systematic Instruction Type Combinations

        [Test]
        public async Task AllInstructionTypes_ExecutedInSequence_AllSucceed()
        {
            // Create test files and archive
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "extracted.txt", "extracted content" }
            });

            File.WriteAllText(Path.Combine(_modDirectory, "move.txt"), "move content");
            File.WriteAllText(Path.Combine(_modDirectory, "copy.txt"), "copy content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "rename.txt"), "rename content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "delete.txt"), "delete content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "tga content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "tpc content");

            var component = new ModComponent { Name = "All Instructions", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/move.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/copy.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Rename
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/rename.txt" },
                Destination = "renamed.txt"
            });

            // Delete
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/delete.txt" }
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

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "All instructions should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "extracted.txt")), Is.True, "Extract should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "move.txt")), Is.True, "Move should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "copy.txt")), Is.True, "Copy should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "renamed.txt")), Is.True, "Rename should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "delete.txt")), Is.False, "Delete should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False, "DelDuplicate should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True, "DelDuplicate should keep TGA");
            });
        }

        #endregion

        #region Instruction Order Dependency Tests

        [Test]
        public async Task InstructionOrder_MoveThenCopy_SameFile_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Order Test", Guid = Guid.NewGuid(), IsSelected = true };

            // Move first
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Then try to copy (file no longer exists at source)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should handle missing file gracefully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.True, "File should exist after move");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file.txt")), Is.False, "Source file should be moved");
            });
        }

        [Test]
        public async Task InstructionOrder_ExtractThenMoveThenDelete_ExecutesInOrder()
        {
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "content" }
            });

            var component = new ModComponent { Name = "Extract Move Delete", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move extracted file
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Delete moved file
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/file.txt" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sequence should succeed");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "file.txt")), Is.False, "File should be moved from extracted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False, "File should be deleted");
            });
        }

        #endregion

        #region Multiple Component Interaction Tests

        [Test]
        public async Task MultipleComponents_SequentialExecution_EachModifiesState()
        {
            var mod1 = new ModComponent { Name = "Mod 1", Guid = Guid.NewGuid(), IsSelected = true };
            var mod2 = new ModComponent { Name = "Mod 2", Guid = Guid.NewGuid(), IsSelected = true };
            var mod3 = new ModComponent { Name = "Mod 3", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "mod1.txt"), "mod1");
            File.WriteAllText(Path.Combine(_modDirectory, "mod2.txt"), "mod2");
            File.WriteAllText(Path.Combine(_modDirectory, "mod3.txt"), "mod3");

            // Mod 1: Move file
            mod1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/mod1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Mod 2: Copy file
            mod2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/mod2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Mod 3: Delete mod1's file
            mod3.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/mod1.txt" }
            });

            var components = new List<ModComponent> { mod1, mod2, mod3 };
            var fileSystemProvider = new RealFileSystemProvider();

            foreach (var mod in components)
            {
                foreach (var instruction in mod.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(mod);
                }
            }

            // Execute in order
            foreach (var mod in components)
            {
                var result = await mod.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {mod.Name} should succeed");
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mod1.txt")), Is.False, "Mod1 file should be deleted by Mod3");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "mod2.txt")), Is.True, "Mod2 file should exist");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "mod2.txt")), Is.True, "Mod2 source should remain (copy)");
            });
        }

        #endregion

        #region Wildcard Edge Case Combinations

        [Test]
        public async Task WildcardCombinations_MultiplePatternsInSequence_ProcessesAll()
        {
            // Create various file types
            File.WriteAllText(Path.Combine(_modDirectory, "texture1.tga"), "tga1");
            File.WriteAllText(Path.Combine(_modDirectory, "texture2.tga"), "tga2");
            File.WriteAllText(Path.Combine(_modDirectory, "model1.mdl"), "mdl1");
            File.WriteAllText(Path.Combine(_modDirectory, "model2.mdl"), "mdl2");
            File.WriteAllText(Path.Combine(_modDirectory, "script1.ncs"), "ncs1");
            File.WriteAllText(Path.Combine(_modDirectory, "script2.ncs"), "ncs2");

            var component = new ModComponent { Name = "Multi-Wildcard", Guid = Guid.NewGuid(), IsSelected = true };

            // Move all TGA files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy all MDL files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/*.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Delete all NCS files from source
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<modDirectory>>/*.ncs" }
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multi-wildcard should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "TGA files should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "TGA files should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "model1.mdl")), Is.True, "MDL files should be copied");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "model2.mdl")), Is.True, "MDL files should be copied");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "texture1.tga")), Is.False, "TGA source should be moved");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "model1.mdl")), Is.True, "MDL source should remain (copy)");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "script1.ncs")), Is.False, "NCS files should be deleted");
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

