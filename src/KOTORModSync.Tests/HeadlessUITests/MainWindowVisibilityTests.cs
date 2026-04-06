// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using KOTORModSync;
using KOTORModSync.Controls;
using KOTORModSync.Core;
using Xunit;

namespace KOTORModSync.Tests.HeadlessUITests
{
    [Collection(HeadlessTestApp.CollectionName)]
    public sealed class MainWindowVisibilityTests
    {
        private static async Task<MainWindow> CreateWindowAsync(bool withComponents = false, bool editorMode = false, bool wizardMode = false)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    ResetMainConfig(withComponents);
                },
                DispatcherPriority.Background);

            var window = await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    var w = new MainWindow();
                    w.WizardMode = false;
                    w.EditorMode = editorMode;
                    w.WizardMode = wizardMode;
                    w.Show();
                    return w;
                },
                DispatcherPriority.Background);

            await PumpEventsAsync();
            return window;
        }

        private static void ResetMainConfig(bool withComponents)
        {
            MainConfig.AllComponents = withComponents
                ? new List<ModComponent> { new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() } }
                : new List<ModComponent>();
        }

        private static async Task PumpEventsAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
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
                    window.Close();
                },
                DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #region Workflow Surface Visibility Tests

        [AvaloniaFact(DisplayName = "Initial state shows landing page")]
        public async Task Visibility_InitialState_ShowsLandingPage()
        {
            var window = await CreateWindowAsync(withComponents: false);
            try
            {
                Assert.True(window.LandingPageVisible, "Landing page should be visible initially");
                Assert.False(window.MainContentVisible, "Main content should not be visible initially");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible initially");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor mode shows main content")]
        public async Task Visibility_WithEditorMode_ShowsMainContent()
        {
            var window = await CreateWindowAsync(withComponents: false, editorMode: true);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible in editor mode");
                Assert.True(window.MainContentVisible, "Main content should be visible in editor mode");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible in editor mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor mode with components shows main content")]
        public async Task Visibility_WithEditorModeAndComponents_ShowsMainContent()
        {
            var window = await CreateWindowAsync(withComponents: true, editorMode: true);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Wizard mode with components shows wizard content")]
        public async Task Visibility_WithWizardModeAndComponents_ShowsWizardContent()
        {
            var window = await CreateWindowAsync(withComponents: true, wizardMode: true);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible");
                Assert.False(window.MainContentVisible, "Main content should not be visible in wizard mode");
                Assert.True(window.WizardContentVisible, "Wizard content should be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Wizard mode without components shows landing page")]
        public async Task Visibility_WithWizardModeButNoComponents_ShowsLandingPage()
        {
            var window = await CreateWindowAsync(withComponents: false, wizardMode: true);
            try
            {
                await PumpEventsAsync();
                Assert.True(window.LandingPageVisible, "Landing page should be visible when no components");
                Assert.False(window.MainContentVisible, "Main content should not be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible without components");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor mode disables wizard mode")]
        public async Task Visibility_EditorModeDisablesWizardMode()
        {
            var window = await CreateWindowAsync(withComponents: true, wizardMode: true);
            try
            {
                await PumpEventsAsync();
                await Dispatcher.UIThread.InvokeAsync(() => window.EditorMode = true, DispatcherPriority.Background);
                await PumpEventsAsync();

                Assert.False(window.WizardMode, "Wizard mode should be disabled when editor mode is enabled");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Components loaded shows main content")]
        public async Task Visibility_WithComponentsLoaded_ShowsMainContent()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible when components are loaded");
                Assert.True(window.MainContentVisible, "Main content should be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Toggle Visibility Tests

        [AvaloniaFact(DisplayName = "Spoiler-free toggle visible when not editor mode")]
        public async Task ToggleVisibility_SpoilerFreeMode_VisibleWhenNotEditorMode()
        {
            var window = await CreateWindowAsync(editorMode: false);
            try
            {
                await PumpEventsAsync();
                var spoilerToggle = window.FindControl<ToggleSwitch>("SpoilerFreeModeToggle");
                Assert.True(spoilerToggle?.IsVisible, "Spoiler-free toggle should be visible when not in editor mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Spoiler-free toggle hidden when editor mode")]
        public async Task ToggleVisibility_SpoilerFreeMode_HiddenWhenEditorMode()
        {
            var window = await CreateWindowAsync(editorMode: true);
            try
            {
                await PumpEventsAsync();
                var spoilerToggle = window.FindControl<ToggleSwitch>("SpoilerFreeModeToggle");
                Assert.False(spoilerToggle?.IsVisible, "Spoiler-free toggle should be hidden in editor mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor mode toggle visible when not wizard mode")]
        public async Task ToggleVisibility_EditorModeToggle_VisibleWhenNotWizardMode()
        {
            var window = await CreateWindowAsync(wizardMode: false);
            try
            {
                await PumpEventsAsync();
                var editorToggle = window.FindControl<ToggleSwitch>("EditorModeToggle");
                Assert.True(editorToggle?.IsVisible, "Editor mode toggle should be visible when not in wizard mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor mode toggle hidden when wizard mode")]
        public async Task ToggleVisibility_EditorModeToggle_HiddenWhenWizardMode()
        {
            var window = await CreateWindowAsync(wizardMode: true);
            try
            {
                await PumpEventsAsync();
                var editorToggle = window.FindControl<ToggleSwitch>("EditorModeToggle");
                Assert.False(editorToggle?.IsVisible, "Editor mode toggle should be hidden in wizard mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Wizard mode toggle visible when show wizard toggle")]
        public async Task ToggleVisibility_WizardModeToggle_VisibleWhenShowWizardToggle()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var wizardToggle = window.FindControl<ToggleSwitch>("WizardModeToggle");
                Assert.True(window.ShowWizardToggle, "ShowWizardToggle should be true");
                Assert.True(wizardToggle?.IsVisible, "Wizard mode toggle should be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Wizard mode toggle hidden when editor mode")]
        public async Task ToggleVisibility_WizardModeToggle_HiddenWhenEditorMode()
        {
            var window = await CreateWindowAsync(editorMode: true);
            try
            {
                await PumpEventsAsync();
                var wizardToggle = window.FindControl<ToggleSwitch>("WizardModeToggle");
                Assert.False(window.ShowWizardToggle, "ShowWizardToggle should be false in editor mode");
                Assert.False(wizardToggle?.IsVisible, "Wizard mode toggle should be hidden in editor mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Tab Visibility Tests

        [AvaloniaFact(DisplayName = "Editor tabs visible when editor mode")]
        public async Task TabVisibility_EditorTabs_VisibleWhenEditorMode()
        {
            var window = await CreateWindowAsync(editorMode: true);
            try
            {
                await PumpEventsAsync();
                var summaryTab = window.FindControl<TabItem>("SummaryTabItem");
                var editorTab = window.FindControl<TabItem>("GuiEditTabItem");
                var rawTab = window.FindControl<TabItem>("RawEditTabItem");

                Assert.True(summaryTab?.IsVisible, "Summary tab should be visible in editor mode");
                Assert.True(editorTab?.IsVisible, "Editor tab should be visible in editor mode");
                Assert.True(rawTab?.IsVisible, "Raw tab should be visible in editor mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Editor tabs hidden when not editor mode")]
        public async Task TabVisibility_EditorTabs_HiddenWhenNotEditorMode()
        {
            var window = await CreateWindowAsync(editorMode: false);
            try
            {
                await PumpEventsAsync();
                var summaryTab = window.FindControl<TabItem>("SummaryTabItem");
                var editorTab = window.FindControl<TabItem>("GuiEditTabItem");
                var rawTab = window.FindControl<TabItem>("RawEditTabItem");

                Assert.False(summaryTab?.IsVisible, "Summary tab should be hidden when not in editor mode");
                Assert.False(editorTab?.IsVisible, "Editor tab should be hidden when not in editor mode");
                Assert.False(rawTab?.IsVisible, "Raw tab should be hidden when not in editor mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Initial tab hidden by default")]
        public async Task TabVisibility_InitialTab_HiddenByDefault()
        {
            var window = await CreateWindowAsync();
            try
            {
                await PumpEventsAsync();
                var initialTab = window.FindControl<TabItem>("InitialTab");
                Assert.False(initialTab?.IsVisible, "Initial tab should be hidden by default");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Component Visibility Tests

        [AvaloniaFact(DisplayName = "Home button visible when main content visible")]
        public async Task ComponentVisibility_HomeButton_VisibleWhenMainContentVisible()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var homeButton = window.FindControl<Button>("HomeButton");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.True(homeButton?.IsVisible, "Home button should be visible when main content is visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Home button hidden when wizard mode")]
        public async Task ComponentVisibility_HomeButton_HiddenWhenWizardMode()
        {
            var window = await CreateWindowAsync(withComponents: true, wizardMode: true);
            try
            {
                await PumpEventsAsync();
                var homeButton = window.FindControl<Button>("HomeButton");
                Assert.False(window.MainContentVisible, "Main content should not be visible in wizard mode");
                Assert.False(homeButton?.IsVisible, "Home button should be hidden in wizard mode");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Theme switcher visible when main content visible")]
        public async Task ComponentVisibility_ThemeSwitcher_VisibleWhenMainContentVisible()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var lightThemeButton = window.FindControl<Button>("LightThemeButton");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.True(lightThemeButton?.IsVisible, "Theme switcher should be visible when main content is visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Mod list sidebar visible when main content visible")]
        public async Task ComponentVisibility_ModListSidebar_VisibleWhenMainContentVisible()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var modListSidebar = window.FindControl<ModListSidebar>("ModListSidebar");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.True(modListSidebar?.IsVisible, "Mod list sidebar should be visible when main content is visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Complex State Combinations

        [AvaloniaFact(DisplayName = "All combinations: EditorMode=true, WizardMode=false, Components=false")]
        public async Task Visibility_AllCombinations_EditorModeTrue_WizardModeFalse_ComponentsFalse()
        {
            var window = await CreateWindowAsync(withComponents: false, editorMode: true, wizardMode: false);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "All combinations: EditorMode=true, WizardMode=false, Components=true")]
        public async Task Visibility_AllCombinations_EditorModeTrue_WizardModeFalse_ComponentsTrue()
        {
            var window = await CreateWindowAsync(withComponents: true, editorMode: true, wizardMode: false);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "All combinations: EditorMode=false, WizardMode=true, Components=false")]
        public async Task Visibility_AllCombinations_EditorModeFalse_WizardModeTrue_ComponentsFalse()
        {
            var window = await CreateWindowAsync(withComponents: false, editorMode: false, wizardMode: true);
            try
            {
                await PumpEventsAsync();
                Assert.True(window.LandingPageVisible, "Landing page should be visible");
                Assert.False(window.MainContentVisible, "Main content should not be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "All combinations: EditorMode=false, WizardMode=true, Components=true")]
        public async Task Visibility_AllCombinations_EditorModeFalse_WizardModeTrue_ComponentsTrue()
        {
            var window = await CreateWindowAsync(withComponents: true, editorMode: false, wizardMode: true);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible");
                Assert.False(window.MainContentVisible, "Main content should not be visible");
                Assert.True(window.WizardContentVisible, "Wizard content should be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "All combinations: EditorMode=false, WizardMode=false, Components=false")]
        public async Task Visibility_AllCombinations_EditorModeFalse_WizardModeFalse_ComponentsFalse()
        {
            var window = await CreateWindowAsync(withComponents: false, editorMode: false, wizardMode: false);
            try
            {
                await PumpEventsAsync();
                Assert.True(window.LandingPageVisible, "Landing page should be visible");
                Assert.False(window.MainContentVisible, "Main content should not be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "All combinations: EditorMode=false, WizardMode=false, Components=true")]
        public async Task Visibility_AllCombinations_EditorModeFalse_WizardModeFalse_ComponentsTrue()
        {
            var window = await CreateWindowAsync(withComponents: true, editorMode: false, wizardMode: false);
            try
            {
                await PumpEventsAsync();
                Assert.False(window.LandingPageVisible, "Landing page should not be visible");
                Assert.True(window.MainContentVisible, "Main content should be visible");
                Assert.False(window.WizardContentVisible, "Wizard content should not be visible");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion
    }
}
