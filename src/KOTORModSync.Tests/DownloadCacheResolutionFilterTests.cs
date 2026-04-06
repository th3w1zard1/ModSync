// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System.Collections.Generic;

using KOTORModSync.Core;
using KOTORModSync.Core.Services;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class DownloadCacheResolutionFilterTests
    {
        private bool _savedFilterSetting;

        [SetUp]
        public void SaveFilterSetting()
        {
            _savedFilterSetting = MainConfig.FilterDownloadsByResolution;
        }

        [TearDown]
        public void RestoreFilterSetting()
        {
            MainConfig.Instance.filterDownloadsByResolution = _savedFilterSetting;
        }

        [Test]
        public void CreateResolutionFilterFromMainConfig_MatchesFreshResolutionFilterForCurrentMainConfig()
        {
            var urls = new List<string>
            {
                "https://example.com/cutscenes_1920x1080.7z",
                "https://example.com/cutscenes_2560x1440.7z",
                "https://example.com/audio_patch.rar",
            };

            // Simulate GUI: DownloadCacheService constructed before user/settings change the flag.
            _ = new DownloadCacheService();

            MainConfig.Instance.filterDownloadsByResolution = false;
            List<string> expectedOff = new ResolutionFilterService(enableFiltering: false).FilterByResolution(new List<string>(urls));
            List<string> actualOff = DownloadCacheService.CreateResolutionFilterFromMainConfig().FilterByResolution(urls);
            Assert.That(actualOff, Is.EqualTo(expectedOff), "Factory must read MainConfig when invoked, not a snapshot from an earlier DownloadCacheService constructor.");

            MainConfig.Instance.filterDownloadsByResolution = true;
            List<string> expectedOn = new ResolutionFilterService(enableFiltering: true).FilterByResolution(new List<string>(urls));
            List<string> actualOn = DownloadCacheService.CreateResolutionFilterFromMainConfig().FilterByResolution(urls);
            Assert.That(actualOn, Is.EqualTo(expectedOn));
        }
    }
}
