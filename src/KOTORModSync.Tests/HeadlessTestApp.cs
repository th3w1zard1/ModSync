// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(KOTORModSync.Tests.HeadlessTestApp))]

namespace KOTORModSync.Tests
{
    internal sealed class HeadlessAvaloniaApp : Application
    {
        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Light;

            Uri fluentUri = new Uri("avares://Avalonia.Themes.Fluent/FluentTheme.xaml");
            Styles.Add(new StyleInclude(fluentUri) { Source = fluentUri });

            AddStyle("/Styles/LightStyle.axaml");
            AddStyle("/Styles/KotorStyle.axaml");
            AddStyle("/Styles/Kotor2Style.axaml");
        }

        private void AddStyle(string relativePath)
        {
            Uri styleUri = new Uri("avares://KOTORModSync" + relativePath);
            Styles.Add(new StyleInclude(styleUri) { Source = styleUri });
        }
    }

    /// <summary>
    /// Centralized Avalonia headless bootstrap used by all GUI tests.
    /// Ensures a single, deterministic application instance with the same
    /// setup as the real app (ReactiveUI + theme pipeline) while keeping
    /// rendering fully headless for CI performance.
    /// </summary>
    public static class HeadlessTestApp
    {
        public const string CollectionName = "AvaloniaHeadlessCollection";

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<HeadlessAvaloniaApp>()
                .UseReactiveUI()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = true,
                });
    }

    [CollectionDefinition(HeadlessTestApp.CollectionName, DisableParallelization = true)]
    public class AvaloniaHeadlessCollection : ICollectionFixture<object>
    {
    }
}
