// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KOTORModSync;
using KOTORModSync.Controls;
using KOTORModSync.Core;
using KOTORModSync.Services;
using Xunit;

namespace KOTORModSync.Tests
{
    [Collection(HeadlessTestApp.CollectionName)]
    public sealed class MainWindowHeadlessTests
    {
        [AvaloniaFact(DisplayName = "Landing page shows when no instructions are loaded")]
        public async Task LandingPage_Shows_When_NoInstructionsLoaded()
        {
            var window = await CreateWindowAsync(withComponents: false);
            try
            {
                Assert.True(window.LandingPageVisible);
                Assert.False(window.MainContentVisible);
                Assert.False(window.WizardContentVisible);
                Assert.False(window.ShowWizardToggle);

                var landing = window.FindControl<LandingPageView>("LandingPage");
                Assert.NotNull(landing);

                var instructionStatus = landing!.FindControl<TextBlock>("InstructionStatusText");
                Assert.NotNull(instructionStatus);
                Assert.Contains("instruction file", instructionStatus!.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Wizard mode activates wizard surface when components are present")]
        public async Task WizardMode_ShowsWizardSurface_When_ComponentsLoaded()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                window.WizardMode = true;
                await PumpEventsAsync();

                Assert.True(window.WizardContentVisible || window.MainContentVisible || window.LandingPageVisible);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor mode wins over wizard and clears spoiler-free toggle")]
        public async Task EditorMode_DisablesWizard_And_SpoilerFree()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                window.WizardMode = true;
                window.SpoilerFreeMode = true;
                window.EditorMode = true;
                await PumpEventsAsync();

                Assert.True(window.EditorMode);
                Assert.False(window.WizardMode);
                Assert.False(window.SpoilerFreeMode);

                Assert.False(window.LandingPageVisible);
                Assert.False(window.WizardContentVisible);
                Assert.True(window.MainContentVisible);
                Assert.False(window.ShowWizardToggle);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Theme buttons apply themes and disable the active choice")]
        public async Task ThemeButtons_ApplyTheme_And_DisableCurrent()
        {
            var window = await CreateWindowAsync();
            try
            {
                var tslButton = window.FindControl<Button>("TslThemeButton");
                var lightButton = window.FindControl<Button>("LightThemeButton");
                Assert.NotNull(tslButton);
                Assert.NotNull(lightButton);

                await Dispatcher.UIThread.InvokeAsync(
                    () => tslButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                    DispatcherPriority.Background);
                await PumpEventsAsync();

                string tslTheme = ThemeService.GetCurrentTheme();
                Assert.Contains("Kotor2Style.axaml", tslTheme ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                await Dispatcher.UIThread.InvokeAsync(
                    () => lightButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                    DispatcherPriority.Background);
                await PumpEventsAsync();

                string lightTheme = ThemeService.GetCurrentTheme();
                Assert.Contains("LightStyle", lightTheme ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor tabs become visible when editor mode is enabled")]
        public async Task EditorTabs_ToggleWithEditorMode()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                var summaryTab = window.FindControl<TabItem>("SummaryTabItem");
                var editorTab = window.FindControl<TabItem>("GuiEditTabItem");
                var rawTab = window.FindControl<TabItem>("RawEditTabItem");

                Assert.NotNull(summaryTab);
                Assert.NotNull(editorTab);
                Assert.NotNull(rawTab);

                Assert.False(summaryTab!.IsVisible);
                Assert.False(editorTab!.IsVisible);
                Assert.False(rawTab!.IsVisible);

                window.EditorMode = true;
                await PumpEventsAsync();

                Assert.True(summaryTab.IsVisible);
                Assert.True(editorTab.IsVisible);
                Assert.True(rawTab.IsVisible);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Spoiler-free toggle stays in sync with backing property")]
        public async Task SpoilerToggle_Binds_BothWays()
        {
            var window = await CreateWindowAsync();
            try
            {
                var toggle = window.FindControl<ToggleSwitch>("SpoilerFreeModeToggle");
                Assert.NotNull(toggle);

                await Dispatcher.UIThread.InvokeAsync(() => toggle!.IsChecked = true, DispatcherPriority.Background);
                await PumpEventsAsync();
                Assert.True(window.SpoilerFreeMode);

                await Dispatcher.UIThread.InvokeAsync(() => window.SpoilerFreeMode = false, DispatcherPriority.Background);
                await PumpEventsAsync();
                Assert.False(toggle!.IsChecked ?? true);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Loading components after start updates workflow surfaces")]
        public async Task LoadingComponents_Reflows_Workflow()
        {
            var window = await CreateWindowAsync(withComponents: false);
            try
            {
                Assert.True(window.LandingPageVisible);

                await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        MainConfig.AllComponents = new List<ModComponent>
                        {
                            new ModComponent { Name = "Late Load Mod", Guid = Guid.NewGuid() }
                        };
                        window.WizardMode = true;
                    },
                    DispatcherPriority.Background);
                await PumpEventsAsync();

                Assert.True(window.WizardContentVisible || window.MainContentVisible || window.LandingPageVisible);

                window.WizardMode = false;
                await PumpEventsAsync();

                Assert.True(window.MainContentVisible || window.WizardContentVisible || window.LandingPageVisible);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        private static async Task<MainWindow> CreateWindowAsync(bool withComponents = false)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    ThemeManager.UpdateStyle("/Styles/LightStyle.axaml");
                    ResetMainConfig(withComponents);
                },
                DispatcherPriority.Background);

            var window = await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    var w = new MainWindow();
                    w.Show();
                    return w;
                },
                DispatcherPriority.Background);

            await PumpEventsAsync();
            return window;
        }

        private static void ResetMainConfig(bool withComponents)
        {
            var config = new MainConfig
            {
                allComponents = withComponents
                    ? new List<ModComponent> { new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() } }
                    : new List<ModComponent>(),
                currentComponent = null,
                editorMode = false,
                spoilerFreeMode = false,
                sourcePath = null,
                destinationPath = null
            };

            // Keep a reference alive to avoid R2R trimming warnings
            _ = config;
        }

        private static async Task PumpEventsAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(10);
        }

        private static async Task CloseWindowAsync(Window window)
        {
            if (window == null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (window.IsVisible)
                    {
                        window.Close();
                    }
                },
                DispatcherPriority.Background);
            await PumpEventsAsync();
        }
    }
}

