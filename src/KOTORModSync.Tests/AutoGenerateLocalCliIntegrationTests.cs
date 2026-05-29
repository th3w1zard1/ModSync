// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using KOTORModSync.Core;
using KOTORModSync.Core.Services;

using NUnit.Framework;

using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class AutoGenerateLocalCliIntegrationTests
    {
        private string _testDirectory;
        private MainConfig _mainConfig;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_AutoGenCli_" + Guid.NewGuid());
            Directory.CreateDirectory(_testDirectory);

            _mainConfig = new MainConfig();
            _mainConfig.sourcePath = new DirectoryInfo(_testDirectory);
            _mainConfig.destinationPath = new DirectoryInfo(Path.Combine(_testDirectory, "KOTOR"));
            Directory.CreateDirectory(_mainConfig.destinationPath.FullName);
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

        [Test]
        public async Task TryGenerateFromLocalArchives_AddsInstructions_WhenArchiveMatchesModLink()
        {
            const string archiveName = "auto-gen-test-mod.zip";
            CreateTslPatcherArchive(archiveName);

            var component = new ModComponent
            {
                Name = "Auto Gen Test Mod",
                Guid = Guid.NewGuid(),
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["https://example.com/auto-gen-test-mod.zip"] = new ResourceMetadata(),
                },
            };

            Assert.That(component.Instructions, Is.Empty);

            int generated = await ComponentProcessingService.TryGenerateFromLocalArchivesAsync(
                new List<ModComponent> { component }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(generated, Is.EqualTo(1));
                Assert.That(component.Instructions.Count, Is.GreaterThan(0));
            });
        }

        [Test]
        public async Task TryGenerateFromLocalArchives_SkipsComponentsWithExistingInstructions()
        {
            const string archiveName = "existing-instructions-mod.zip";
            CreateTslPatcherArchive(archiveName);

            var component = new ModComponent
            {
                Name = "Existing Instructions Mod",
                Guid = Guid.NewGuid(),
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["https://example.com/existing-instructions-mod.zip"] = new ResourceMetadata(),
                },
            };
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/placeholder.zip" },
            });

            int before = component.Instructions.Count;
            int generated = await ComponentProcessingService.TryGenerateFromLocalArchivesAsync(
                new List<ModComponent> { component }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(generated, Is.EqualTo(0));
                Assert.That(component.Instructions.Count, Is.EqualTo(before));
            });
        }

        private void CreateTslPatcherArchive(string archiveName)
        {
            string archivePath = Path.Combine(_testDirectory, archiveName);
            using (var archive = ZipArchive.CreateArchive())
            {
                var exeStream = new MemoryStream();
                var exeWriter = new BinaryWriter(exeStream);
                exeWriter.Write(new byte[] { 0x4D, 0x5A });
                exeWriter.Flush();
                exeStream.Position = 0;
                archive.AddEntry("TSLPatcher.exe", exeStream, closeStream: true);

                var changesStream = new MemoryStream();
                var changesWriter = new StreamWriter(changesStream);
                changesWriter.WriteLine("[Settings]");
                changesWriter.WriteLine("Version=1.0");
                changesWriter.Flush();
                changesStream.Position = 0;
                archive.AddEntry("tslpatchdata/changes.ini", changesStream, closeStream: true);

                using (FileStream fileStream = File.Create(archivePath))
                {
                    archive.SaveTo(fileStream, new ZipWriterOptions(CompressionType.Deflate));
                }
            }
        }
    }
}
