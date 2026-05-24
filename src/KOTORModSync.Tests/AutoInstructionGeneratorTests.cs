// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using KOTORModSync.Core;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Utility;

using NUnit.Framework;

using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class AutoInstructionGeneratorTests
    {
        private string _testDirectory;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_AutoInstructionTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testDirectory);

            var mainConfig = new MainConfig();
            mainConfig.sourcePath = new DirectoryInfo(_testDirectory);
            mainConfig.destinationPath = new DirectoryInfo(Path.Combine(_testDirectory, "KOTOR"));
            Directory.CreateDirectory(mainConfig.destinationPath.FullName);
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

            }
        }

        [Test]
        public void GenerateInstructions_TSLPatcherWithChangesIni_CreatesPatcherInstruction()
        {

            string archivePath = CreateTestArchive("tslpatcher_simple.zip", archive =>
            {

                AddTextFileToArchive(archive, "tslpatchdata/changes.ini", "[Settings]\nLookupGameFolder=1");
                AddTextFileToArchive(archive, "TSLPatcher.exe", "fake exe");
                AddTextFileToArchive(archive, "example.2da", "2DA V2.0");
            });

            var component = new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() };

            bool result = AutoInstructionGenerator.GenerateInstructions(component, archivePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True, "Should successfully generate instructions");
                Assert.That(component.Instructions, Has.Count.EqualTo(3), "Should have Extract + Patcher + Move instructions");
            });

            Assert.Multiple(() =>
            {
                Assert.That(component.Instructions[0].Action, Is.EqualTo(Instruction.ActionType.Extract));
                Assert.That(component.Instructions[0].Source[0], Does.Contain("tslpatcher_simple.zip"));

                Assert.That(component.Instructions[1].Action, Is.EqualTo(Instruction.ActionType.Patcher));
                Assert.That(component.Instructions[2].Action, Is.EqualTo(Instruction.ActionType.Move));
                Assert.That(component.InstallationMethod, Is.EqualTo("Hybrid (TSLPatcher + Loose Files)"));
            });
        }

        [Test]
        public void GenerateInstructions_TSLPatcherWithNamespacesIni_CreatesChooseWithOptions()
        {

            string archivePath = CreateTestArchive("tslpatcher_namespaces.zip", archive =>
            {

                AddTextFileToArchive(archive, "tslpatchdata/namespaces.ini",
                    "[Namespaces]\n1=Option1\n\n[Option1]\nName=First Option\nDescription=First option description\nIniName=changes1.ini");
                AddTextFileToArchive(archive, "tslpatchdata/changes1.ini", "[Settings]\nLookupGameFolder=1");
                AddTextFileToArchive(archive, "TSLPatcher.exe", "fake exe");
            });

            var component = new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() };

            bool result = AutoInstructionGenerator.GenerateInstructions(component, archivePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(component.Instructions, Has.Count.EqualTo(2), "Should have Extract + Choose instructions");
            });
            Assert.Multiple(() =>
            {
                Assert.That(component.Instructions[0].Action, Is.EqualTo(Instruction.ActionType.Extract));
                Assert.That(component.Instructions[1].Action, Is.EqualTo(Instruction.ActionType.Choose));
                Assert.That(component.Options, Has.Count.EqualTo(1), "Should create one option");
            });
            Assert.That(component.Options[0].Instructions, Has.Count.EqualTo(1), "Option should have Patcher instruction");
            Assert.That(component.Options[0].Instructions[0].Action, Is.EqualTo(Instruction.ActionType.Patcher));
        }

        [Test]
        public void GenerateInstructions_HybridWithTSLPatcherAndLooseFiles_TSLPatcherComesFirst()
        {

            string archivePath = CreateTestArchive("hybrid_mod.zip", archive =>
            {

                AddTextFileToArchive(archive, "tslpatchdata/changes.ini", "[Settings]\nLookupGameFolder=1");
                AddTextFileToArchive(archive, "TSLPatcher.exe", "fake exe");

                AddTextFileToArchive(archive, "Override1/appearance.2da", "2DA");
                AddTextFileToArchive(archive, "Override2/dialog.dlg", "DLG");
            });

            var component = new ModComponent { Name = "Hybrid Mod", Guid = Guid.NewGuid() };

            bool result = AutoInstructionGenerator.GenerateInstructions(component, archivePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(component.InstallationMethod, Is.EqualTo("Hybrid (TSLPatcher + Loose Files)"));

                Assert.That(component.Instructions[0].Action, Is.EqualTo(Instruction.ActionType.Extract), "First should be Extract");
                Assert.That(component.Instructions[1].Action, Is.EqualTo(Instruction.ActionType.Patcher), "Second should be Patcher (TSLPatcher before Move)");
                Assert.That(component.Instructions[2].Action, Is.EqualTo(Instruction.ActionType.Choose), "Third should be Choose for multiple folders");
            });
        }

        [Test]
        public void GenerateInstructions_MultipleFolders_CreatesChooseWithMoveOptions()
        {

            string archivePath = CreateTestArchive("multi_folder.zip", archive =>
            {
                AddTextFileToArchive(archive, "Folder1/appearance.2da", "2DA");
                AddTextFileToArchive(archive, "Folder2/dialog.dlg", "DLG");
                AddTextFileToArchive(archive, "Folder3/heads.2da", "2DA");
            });

            var component = new ModComponent { Name = "Multi Folder Mod", Guid = Guid.NewGuid() };

            bool result = AutoInstructionGenerator.GenerateInstructions(component, archivePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(component.Instructions[0].Action, Is.EqualTo(Instruction.ActionType.Extract));
                Assert.That(component.Instructions[1].Action, Is.EqualTo(Instruction.ActionType.Choose));
                Assert.That(component.Options, Has.Count.EqualTo(3), "Should create three options for three folders");
            });

            foreach (Option option in component.Options)
            {
                Assert.That(option.Instructions, Has.Count.EqualTo(1));
                Assert.Multiple(() =>
                {
                    Assert.That(option.Instructions[0].Action, Is.EqualTo(Instruction.ActionType.Move));
                    Assert.That(option.Instructions[0].Destination, Does.Contain("Override"));
                });
            }
        }

        [Test]
        public void GenerateInstructions_SingleFolder_CreatesSimpleMoveInstruction()
        {

            string archivePath = CreateTestArchive("single_folder.zip", archive =>
            {
                AddTextFileToArchive(archive, "Override/appearance.2da", "2DA");
                AddTextFileToArchive(archive, "Override/dialog.dlg", "DLG");
            });

            var component = new ModComponent { Name = "Single Folder Mod", Guid = Guid.NewGuid() };

            bool result = AutoInstructionGenerator.GenerateInstructions(component, archivePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(component.Instructions, Has.Count.EqualTo(2), "Should have Extract + Move");
            });
            Assert.Multiple(() =>
            {
                Assert.That(component.Instructions[0].Action, Is.EqualTo(Instruction.ActionType.Extract));
                Assert.That(component.Instructions[1].Action, Is.EqualTo(Instruction.ActionType.Move));
                Assert.That(component.Instructions[1].Destination, Does.Contain("Override"));
            });
        }

        [Test]
        public void GenerateInstructions_FlatFiles_CreatesSimpleMoveInstruction()
        {

            string archivePath = CreateTestArchive("flat_files.zip", archive =>
            {
                AddTextFileToArchive(archive, "appearance.2da", "2DA");
                AddTextFileToArchive(archive, "dialog.dlg", "DLG");
            });

            var component = new ModComponent { Name = "Flat Files Mod", Guid = Guid.NewGuid() };

            bool result = AutoInstructionGenerator.GenerateInstructions(component, archivePath);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(component.Instructions, Has.Count.EqualTo(2), "Should have Extract + Move");
            });
            Assert.That(component.Instructions[1].Action, Is.EqualTo(Instruction.ActionType.Move));
        }

        [Test]
        public void GenerateInstructions_MultipleArchives_DoesNotDuplicate()
        {

            string archive1Path = CreateTestArchive("archive1.zip", archive =>
            {
                AddTextFileToArchive(archive, "file1.2da", "2DA");
            });

            string archive2Path = CreateTestArchive("archive2.zip", archive =>
            {
                AddTextFileToArchive(archive, "file2.dlg", "DLG");
            });

            var component = new ModComponent { Name = "Multi Archive Mod", Guid = Guid.NewGuid() };

            bool result1 = AutoInstructionGenerator.GenerateInstructions(component, archive1Path);
            int instructionsAfterFirst = component.Instructions.Count;

            bool result2 = AutoInstructionGenerator.GenerateInstructions(component, archive2Path);
            int instructionsAfterSecond = component.Instructions.Count;

            Assert.Multiple(() =>
            {
                Assert.That(result1, Is.True);
                Assert.That(result2, Is.True);
                Assert.That(instructionsAfterFirst, Is.EqualTo(2), "First archive should create 2 instructions");
                Assert.That(instructionsAfterSecond, Is.EqualTo(4), "Second archive should ADD 2 more (not replace)");
            });

            var archive1Instructions = component.Instructions.Where(i =>
                i.Source != null && i.Source.Any(s => NetFrameworkCompatibility.Contains(s, "archive1", StringComparison.Ordinal))).ToList();
            var archive2Instructions = component.Instructions.Where(i =>
                i.Source != null && i.Source.Any(s => NetFrameworkCompatibility.Contains(s, "archive2", StringComparison.Ordinal))).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(archive1Instructions, Has.Count.EqualTo(2), "Archive1 instructions should still exist");
                Assert.That(archive2Instructions, Has.Count.EqualTo(2), "Archive2 instructions should be added");
            });
        }

        [Test]
        public void GenerateInstructions_SameArchiveTwice_DoesNotCreateDuplicates()
        {

            string archivePath = CreateTestArchive("test.zip", archive =>
            {
                AddTextFileToArchive(archive, "file1.2da", "2DA");
            });

            var component = new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() };

            AutoInstructionGenerator.GenerateInstructions(component, archivePath);
            var firstGuids = component.Instructions.Select(i => i.GetHashCode()).ToList();

            AutoInstructionGenerator.GenerateInstructions(component, archivePath);
            var secondGuids = component.Instructions.Select(i => i.GetHashCode()).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(component.Instructions, Has.Count.EqualTo(2), "Should still have 2 instructions");
                Assert.That(firstGuids, Is.EqualTo(secondGuids), "Instructions should not be regenerated - same GUIDs expected");
            });
        }

        #region Helper Methods

        private string CreateTestArchive(string fileName, Action<SharpCompress.Archives.IWritableArchive<SharpCompress.Writers.Zip.ZipWriterOptions>> populateArchive)
        {
            if (_testDirectory is null)
            {
                throw new InvalidOperationException("Test directory is null");
            }
            string archivePath = Path.Combine(_testDirectory, fileName);

            using (var archive = ZipArchive.CreateArchive())
            {
                populateArchive(archive);
                archive.SaveTo(archivePath, CompressionType.Deflate);
            }

            return archivePath;
        }

        private static void AddTextFileToArchive(SharpCompress.Archives.IWritableArchive<SharpCompress.Writers.Zip.ZipWriterOptions> archive, string path, string content)
        {
            var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            archive.AddEntry(path, memoryStream, closeStream: true);
        }

        #endregion
    }
}
