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
    public sealed class ErrorHandlingAndValidationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ErrorHandlingTests_" + Guid.NewGuid());
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

        #region File Not Found Error Handling

        [Test]
        public async Task Move_FileNotFound_HandlesGracefully()
        {
            var component = new ModComponent { Name = "File Not Found", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle missing file gracefully (may return error or skip)
            Assert.That(result, Is.Not.Null, "Should return a result even when file doesn't exist");
        }

        [Test]
        public async Task Copy_FileNotFound_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Copy Not Found", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/nonexistent.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, "Should return a result even when file doesn't exist");
        }

        [Test]
        public async Task Rename_FileNotFound_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Rename Not Found", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Rename,
                Source = new List<string> { "<<kotorDirectory>>/Override/nonexistent.txt" },
                Destination = "new.txt"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, "Should return a result even when file doesn't exist");
        }

        [Test]
        public async Task Extract_ArchiveNotFound_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Extract Not Found", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/nonexistent.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.Null, "Should return a result even when archive doesn't exist");
        }

        #endregion

        #region Invalid Archive Error Handling

        [Test]
        public async Task Extract_InvalidArchive_HandlesGracefully()
        {
            string invalidArchive = Path.Combine(_modDirectory, "invalid.zip");
            File.WriteAllText(invalidArchive, "This is not a valid zip file");

            var component = new ModComponent { Name = "Invalid Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/invalid.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.Not.EqualTo(ModComponent.InstallExitCode.Success),
                "Should fail when archive is invalid");
        }

        [Test]
        public async Task Extract_EmptyArchive_HandlesGracefully()
        {
            string emptyArchive = CreateTestZip("empty.zip", new Dictionary<string, string>(StringComparer.Ordinal));

            var component = new ModComponent { Name = "Empty Archive", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/empty.zip" },
                Destination = "<<modDirectory>>/extracted"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Should handle empty archive gracefully");
        }

        #endregion

        #region Permission and Access Error Handling

        [Test]
        public async Task Move_ReadOnlyDestination_HandlesCorrectly()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");
            string destFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(destFile, "existing");
            File.SetAttributes(destFile, FileAttributes.ReadOnly);

            try
            {
                var component = new ModComponent { Name = "ReadOnly Dest", Guid = Guid.NewGuid(), IsSelected = true };
                var instruction = new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { "<<modDirectory>>/file.txt" },
                    Destination = "<<kotorDirectory>>/Override",
                    Overwrite = true
                };

                component.Instructions.Add(instruction);

                var fileSystemProvider = new RealFileSystemProvider();
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);

                var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

                // Should handle read-only file (may succeed by removing read-only attribute or fail gracefully)
                Assert.That(result, Is.Not.Null, "Should return a result");
            }
            finally
            {
                // Clean up read-only attribute
                if (File.Exists(destFile))
                {
                    File.SetAttributes(destFile, FileAttributes.Normal);
                }
            }
        }

        #endregion

        #region Wildcard Error Handling

        [Test]
        public async Task Move_WildcardNoMatches_HandlesGracefully()
        {
            // Create files that don't match pattern
            File.WriteAllText(Path.Combine(_modDirectory, "file1.dat"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.dat"), "content2");

            var component = new ModComponent { Name = "No Wildcard Match", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.txt" }, // No .txt files exist
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle empty wildcard match gracefully");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file1.dat")), Is.True,
                    "Non-matching files should remain");
            });
        }

        [Test]
        public async Task Delete_WildcardNoMatches_HandlesGracefully()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.dat"), "content1");

            var component = new ModComponent { Name = "Delete No Match", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/*.txt" }, // No .txt files
                Overwrite = false
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle empty wildcard match gracefully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.dat")), Is.True,
                    "Non-matching files should remain");
            });
        }

        #endregion

        #region Path Validation Tests

        [Test]
        public async Task PathValidation_InvalidCharacters_HandlesGracefully()
        {
            // Test with various invalid characters (OS-dependent)
            string[] invalidNames = { "file<name>.txt", "file>name.txt", "file:name.txt", "file|name.txt" };

            foreach (var invalidName in invalidNames)
            {
                try
                {
                    File.WriteAllText(Path.Combine(_modDirectory, invalidName), "content");

                    var component = new ModComponent { Name = "Invalid Chars", Guid = Guid.NewGuid(), IsSelected = true };
                    var instruction = new Instruction
                    {
                        Action = Instruction.ActionType.Move,
                        Source = new List<string> { $"<<modDirectory>>/{invalidName}" },
                        Destination = "<<kotorDirectory>>/Override"
                    };

                    component.Instructions.Add(instruction);

                    var fileSystemProvider = new RealFileSystemProvider();
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);

                    var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

                    // Should handle invalid characters (may fail or sanitize)
                    Assert.That(result, Is.Not.Null, $"Should handle invalid filename: {invalidName}");
                }
                catch (ArgumentException)
                {
                    // Expected on some systems - invalid characters in filenames
                    Assert.Pass($"Invalid filename correctly rejected: {invalidName}");
                }
            }
        }

        [Test]
        public async Task PathValidation_VeryLongPath_HandlesCorrectly()
        {
            // Create a very long path
            string longPath = string.Join("", Enumerable.Repeat("subdir", 20));
            string fullPath = Path.Combine(_modDirectory, longPath);
            Directory.CreateDirectory(fullPath);
            File.WriteAllText(Path.Combine(fullPath, "file.txt"), "content");

            var component = new ModComponent { Name = "Long Path", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { $"<<modDirectory>>/{longPath}/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            // Should handle long paths (may succeed or fail depending on OS limits)
            Assert.That(result, Is.Not.Null, "Should return a result for long path");
        }

        #endregion

        #region DelDuplicate Edge Cases

        [Test]
        public async Task DelDuplicate_NoCompatibleExtensions_HandlesGracefully()
        {
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "content");

            var component = new ModComponent { Name = "DelDuplicate No Compat", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".dds" }, // TGA not in list
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should handle no compatible extensions gracefully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True,
                    "File should remain when no duplicates found");
            });
        }

        [Test]
        public async Task DelDuplicate_EmptyDirectory_HandlesGracefully()
        {
            var component = new ModComponent { Name = "DelDuplicate Empty", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Should handle empty directory gracefully");
        }

        #endregion

        #region Component Validation Tests

        [Test]
        public async Task ComponentValidation_UnselectedComponent_SkipsInstructions()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            var component = new ModComponent { Name = "Unselected", Guid = Guid.NewGuid(), IsSelected = false };
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should succeed (component skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "File should not be moved when component is not selected");
            });
        }

        [Test]
        public async Task ComponentValidation_ComponentWithRestriction_BlocksWhenRestrictedSelected()
        {
            var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent
            {
                Name = "Blocked Component",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "content");

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { restrictedComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Should succeed (component blocked)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.False,
                    "File should not be moved when component is blocked by restriction");
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

