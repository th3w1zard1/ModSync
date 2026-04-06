// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System.IO;

using KOTORModSync.Core;
using KOTORModSync.Core.Services;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class PatcherEngineSettingsTests
    {
        private string _savedEngine;
        private string _savedKPath;

        [SetUp]
        public void Save()
        {
            _savedEngine = MainConfig.PatcherEngine;
            _savedKPath = MainConfig.KPatcherExecutablePath;
        }

        [TearDown]
        public void Restore()
        {
            MainConfig.Instance.patcherEngine = _savedEngine;
            MainConfig.Instance.kpatcherExecutablePath = _savedKPath;
        }

        [Test]
        public void FindKPatcherExecutableAsync_UsesConfiguredPath_WhenFileExists()
        {
            string tempExe = Path.Combine(Path.GetTempPath(), "KOTORModSync_kpatcher_test_" + Path.GetRandomFileName());
            File.WriteAllText(tempExe, string.Empty);
            try
            {
                MainConfig.Instance.patcherEngine = PatcherEngines.KPatcher;
                MainConfig.Instance.kpatcherExecutablePath = tempExe;

                (string path, bool found) = InstallationService.FindKPatcherExecutableAsync().GetAwaiter().GetResult();

                Assert.That(found, Is.True);
                Assert.That(path, Is.EqualTo(tempExe));
            }
            finally
            {
                try
                {
                    File.Delete(tempExe);
                }
                catch
                {
                }
            }
        }
    }
}
