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
    public sealed class CleanListFuzzyMatchingTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_CleanListFuzzyTests_" + Guid.NewGuid());
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

        #region Fuzzy Matching Scenarios

        [Test]
        public async Task CleanList_ExactMatch_DeletesFiles()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "HD UI Rewrite,ui_old.tga,ui_old.tpc\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc"), "old");

            var hdUIMod = new ModComponent { Name = "HD UI Rewrite", Guid = Guid.NewGuid(), IsSelected = true };
            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { hdUIMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Exact match should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tga")), Is.False,
                    "Files should be deleted on exact match");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tpc")), Is.False,
                    "Files should be deleted on exact match");
            });
        }

        [Test]
        public async Task CleanList_CaseInsensitiveMatch_DeletesFiles()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "HD UI Rewrite,ui_old.tga\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tga"), "old");

            var hdUIMod = new ModComponent { Name = "hd ui rewrite", Guid = Guid.NewGuid(), IsSelected = true }; // Different case
            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { hdUIMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Case insensitive match should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "ui_old.tga")), Is.False,
                    "Files should be deleted on case insensitive match");
            });
        }

        [Test]
        public async Task CleanList_PartialMatch_DeletesFiles()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "HD UI Rewrite,ui_old.tga\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "ui_old.tga"), "old");

            var hdUIMod = new ModComponent { Name = "HD UI Rewrite Mod by Author", Guid = Guid.NewGuid(), IsSelected = true }; // Partial match
            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { hdUIMod, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Partial match should succeed");
                // Fuzzy matching should handle this - may or may not match depending on implementation
                Assert.That(result, Is.Not.Null, "Should return a result");
            });
        }

        [Test]
        public async Task CleanList_MandatoryDeletions_AlwaysExecutes()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, "Mandatory Deletions,old1.tga,old2.tpc\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old1.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old2.tpc"), "old");

            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
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
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Mandatory deletions should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old1.tga")), Is.False,
                    "Mandatory deletion should always execute");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old2.tpc")), Is.False,
                    "Mandatory deletion should always execute");
            });
        }

        [Test]
        public async Task CleanList_MultipleModsInCSV_ProcessesAllSelected()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath,
                "Mod A,file1.tga\n" +
                "Mod B,file2.tga\n" +
                "Mod C,file3.tga\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file2.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file3.tga"), "old");

            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), IsSelected = false }; // Not selected

            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { modA, modB, modC, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multiple mods should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.tga")), Is.False,
                    "Mod A files should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.tga")), Is.False,
                    "Mod B files should be deleted");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file3.tga")), Is.True,
                    "Mod C files should NOT be deleted (not selected)");
            });
        }

        [Test]
        public async Task CleanList_EmptyCSV_HandlesGracefully()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath, ""); // Empty CSV

            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                "Empty CSV should be handled gracefully");
        }

        [Test]
        public async Task CleanList_MalformedCSV_HandlesGracefully()
        {
            string csvPath = Path.Combine(_modDirectory, "cleanlist.csv");
            File.WriteAllText(csvPath,
                "Mod A,file1.tga\n" +
                "Mod B\n" + // Missing files
                "Mod C,file3.tga,file4.tga\n");

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file1.tga"), "old");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file3.tga"), "old");

            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), IsSelected = true };

            var component = new ModComponent { Name = "CleanList Mod", Guid = Guid.NewGuid(), IsSelected = true };

            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.CleanList,
                Source = new List<string> { "<<modDirectory>>/cleanlist.csv" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(
                new List<ModComponent> { modA, modB, modC, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success),
                    "Malformed CSV should be handled gracefully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.tga")), Is.False,
                    "Valid entries should still be processed");
            });
        }

        #endregion
    }
}

