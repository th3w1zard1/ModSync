// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using Xunit;

namespace KOTORModSync.Tests
{
    [Collection(HeadlessTestApp.CollectionName)]
    public sealed class HeadlessBootstrapTests
    {
        [AvaloniaFact(DisplayName = "Headless bootstrap applies Fluent theme pipeline with light variant")]
        public void HeadlessApp_UsesFluentThemeAndLightVariant()
        {
            Application app = Application.Current ?? throw new Xunit.Sdk.XunitException("Headless application was not initialized.");

            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
            Assert.NotEmpty(app.Styles);
        }
    }
}
