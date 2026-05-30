// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;

using KOTORModSync.Core;
using KOTORModSync.Core.Services.FileSystem;
using KOTORModSync.Core.TSLPatcher;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class TslPatcherVfsSimulatorTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_TslPatcherSim_" + Guid.NewGuid());
            _modDirectory = Path.Combine(_testDirectory, "Mods");
            _kotorDirectory = Path.Combine(_testDirectory, "KOTOR");
            Directory.CreateDirectory(_modDirectory);
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override"));

            MainConfig.Instance = new MainConfig
            {
                sourcePath = new DirectoryInfo(_modDirectory),
                destinationPath = new DirectoryInfo(_kotorDirectory),
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

        [Test]
        public async Task SimulateInstallAsync_ReplaceEntry_IsVisibleToFileExistsAfterNormalization()
        {
            string patcherRoot = Path.Combine(_modDirectory, "High Quality Blasters 1.1");
            string tslpatchdata = Path.Combine(patcherRoot, "tslpatchdata");
            Directory.CreateDirectory(tslpatchdata);
            File.WriteAllText(
                Path.Combine(tslpatchdata, "changes.ini"),
                "[InstallList]\r\ninstall_folder0=Override\r\n\r\n[install_folder0]\r\nReplace183=w_ionrfl_04.mdl\r\n");
            File.WriteAllText(Path.Combine(tslpatchdata, "w_ionrfl_04.mdl"), "fake mdl");

            var vfs = new VirtualFileSystemProvider();
            await TslPatcherVfsSimulator.SimulateInstallAsync(vfs, patcherRoot, _kotorDirectory).ConfigureAwait(false);

            string overrideFile = Path.Combine(_kotorDirectory, "Override", "w_ionrfl_04.mdl");
            string windowsStylePath = _kotorDirectory + "\\Override\\w_ionrfl_04.mdl";

            Assert.That(vfs.FileExists(overrideFile), Is.True, "Full path should exist in VFS after simulation");
            Assert.That(vfs.FileExists(windowsStylePath), Is.True, "Windows-style separators should resolve via normalization");
        }
    }
}
