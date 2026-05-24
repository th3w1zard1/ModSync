// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using Avalonia.Headless.XUnit;

using KOTORModSync.Services;

using Xunit;

namespace KOTORModSync.Tests
{
    [Collection(HeadlessTestApp.CollectionName)]
    public sealed class AutoUpdateServiceHeadlessTests
    {
        [AvaloniaFact(DisplayName = "AutoUpdateService initializes with real NetSparkle client under headless app")]
        public void Initialize_WithRealNetSparkleClient_CompletesUnderHeadlessApp()
        {
            var settings = new AutoUpdateSettings
            {
                AppCastUrl = "https://example.com/appcast.xml",
                SignaturePublicKey = "jZSQV+2C1HL2Ufek3ekC7gtgOk5ctuDQzngh86OEdlA=",
            };

            using var client = new NetSparkleUpdateClient();
            using var service = new AutoUpdateService(client, settings);

            service.Initialize();
            service.Initialize();

            Assert.Equal(settings.AppCastUrl, service.CurrentSettings.AppCastUrl);
            Assert.Equal(settings.SignaturePublicKey, service.CurrentSettings.SignaturePublicKey);
        }

        [AvaloniaFact(DisplayName = "AutoUpdateService default constructor wires real NetSparkle client under headless app")]
        public void DefaultConstructor_InitializesRealClientUnderHeadlessApp()
        {
            using var service = new AutoUpdateService();

            service.Initialize();
            service.StopUpdateCheckLoop();

            Assert.False(string.IsNullOrWhiteSpace(service.CurrentSettings.AppCastUrl));
            Assert.False(string.IsNullOrWhiteSpace(service.CurrentSettings.SignaturePublicKey));
        }
    }
}
