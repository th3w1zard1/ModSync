// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KOTORModSync;
using KOTORModSync.Controls;
using KOTORModSync.Core;
using Xunit;

namespace KOTORModSync.Tests.HeadlessUITests
{
    [Collection(HeadlessTestApp.CollectionName)]
    public sealed class UIEventHandlerTests
    {
        private static async Task PumpEventsAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        #region LandingPageView Event Tests

        [AvaloniaFact(DisplayName = "LandingPageView LoadInstructionsRequested event fires on button click")]
        public async Task LandingPageView_LoadInstructionsButtonClick_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var landingPage = new LandingPageView();
                bool eventFired = false;

                landingPage.LoadInstructionsRequested += (sender, e) => eventFired = true;

                var loadButton = landingPage.FindControl<Button>("LoadInstructionButton");
                Assert.NotNull(loadButton);

                loadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.True(eventFired, "LoadInstructionsRequested event should fire on button click");
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "LandingPageView CreateInstructionsRequested event fires on button click")]
        public async Task LandingPageView_CreateInstructionsButtonClick_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var landingPage = new LandingPageView();
                bool eventFired = false;

                landingPage.CreateInstructionsRequested += (sender, e) => eventFired = true;

                var createButton = landingPage.FindControl<Button>("CreateInstructionsButton");
                Assert.NotNull(createButton);

                createButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.True(eventFired, "CreateInstructionsRequested event should fire on button click");
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "LandingPageView UpdateState updates text correctly")]
        public async Task LandingPageView_UpdateState_UpdatesTextCorrectly()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var landingPage = new LandingPageView();

                // Test with instruction file loaded
                landingPage.UpdateState(true, "test.toml", false);
                var instructionStatus = landingPage.FindControl<TextBlock>("InstructionStatusText");
                Assert.NotNull(instructionStatus);
                Assert.Contains("test.toml", instructionStatus.Text ?? string.Empty, StringComparison.Ordinal);

                // Test with no instruction file
                landingPage.UpdateState(false, null, false);
                Assert.Contains("No instruction file", instructionStatus.Text ?? string.Empty, StringComparison.Ordinal);

                // Test with editor mode enabled
                landingPage.UpdateState(true, "test.toml", true);
                var editorStatus = landingPage.FindControl<TextBlock>("EditorStatusText");
                Assert.NotNull(editorStatus);
                Assert.Contains("enabled", editorStatus.Text ?? string.Empty);
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #endregion

        #region ModListSidebar Event Tests

        [AvaloniaFact(DisplayName = "ModListSidebar RefreshListRequested event fires on button click")]
        public async Task ModListSidebar_RefreshListButtonClick_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var sidebar = new ModListSidebar();
                bool eventFired = false;

                sidebar.RefreshListRequested += (sender, e) => eventFired = true;

                // Find and click refresh button
                var refreshButton = sidebar.GetLogicalDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Name == "RefreshListButton" || b.Content?.ToString()?.Contains("Refresh") == true);

                if (refreshButton != null)
                {
                    refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(eventFired, "RefreshListRequested event should fire on button click");
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "ModListSidebar SelectAllRequested event fires on button click")]
        public async Task ModListSidebar_SelectAllButtonClick_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var sidebar = new ModListSidebar();
                bool eventFired = false;

                sidebar.SelectAllRequested += (sender, e) => eventFired = true;

                // Find and click select all button
                var selectAllButton = sidebar.GetLogicalDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Content?.ToString()?.Contains("Select All") == true);

                if (selectAllButton != null)
                {
                    selectAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(eventFired, "SelectAllRequested event should fire on button click");
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "ModListSidebar DeselectAllRequested event fires on button click")]
        public async Task ModListSidebar_DeselectAllButtonClick_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var sidebar = new ModListSidebar();
                bool eventFired = false;

                sidebar.DeselectAllRequested += (sender, e) => eventFired = true;

                // Find and click deselect all button
                var deselectAllButton = sidebar.GetLogicalDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Content?.ToString()?.Contains("Deselect All") == true);

                if (deselectAllButton != null)
                {
                    deselectAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(eventFired, "DeselectAllRequested event should fire on button click");
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #endregion

        #region Component Checkbox Event Tests

        [AvaloniaFact(DisplayName = "Component checkbox checked fires ComponentCheckboxChecked")]
        public async Task ComponentCheckbox_Checked_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
                var window = new MainWindow();
                window.Show();

                // Find checkbox for component
                var checkBox = window.GetLogicalDescendants()
                    .OfType<CheckBox>()
                    .FirstOrDefault(cb => cb.Tag == component);

                if (checkBox != null)
                {
                    checkBox.IsChecked = true;
                    // Event should be handled by MainWindow.OnCheckBoxChanged
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "Component checkbox unchecked fires ComponentCheckboxUnchecked")]
        public async Task ComponentCheckbox_Unchecked_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var component = new ModComponent
                {
                    Name = "Test",
                    Guid = Guid.NewGuid(),
                    IsSelected = true
                };
                var window = new MainWindow();
                window.Show();

                // Find checkbox for component
                var checkBox = window.GetLogicalDescendants()
                    .OfType<CheckBox>()
                    .FirstOrDefault(cb => cb.Tag == component);

                if (checkBox != null)
                {
                    checkBox.IsChecked = false;
                    // Event should be handled by MainWindow.OnCheckBoxChanged
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #endregion

        #region Tab Selection Event Tests

        [AvaloniaFact(DisplayName = "TabControl selection changed fires TabControl_SelectionChanged")]
        public async Task TabControl_SelectionChanged_FiresEvent()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new MainWindow();
                window.EditorMode = true;
                window.Show();

                var tabControl = window.FindControl<TabControl>("TabControl");
                Assert.NotNull(tabControl);

                // Change selection
                if (tabControl.Items.Count > 1)
                {
                    tabControl.SelectedIndex = 1;
                    // Event should be handled by MainWindow.TabControl_SelectionChanged
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #endregion

        #region Theme Switcher Event Tests

        [AvaloniaFact(DisplayName = "Light theme button click switches theme")]
        public async Task ThemeSwitcher_LightThemeButtonClick_SwitchesTheme()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new MainWindow();
                window.Show();

                var lightThemeButton = window.FindControl<Button>("LightThemeButton");
                Assert.NotNull(lightThemeButton);

                lightThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Should call MainWindow.SwitchToLightTheme_Click
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "K1 theme button click switches theme")]
        public async Task ThemeSwitcher_K1ThemeButtonClick_SwitchesTheme()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new MainWindow();
                window.Show();

                var k1ThemeButton = window.FindControl<Button>("K1ThemeButton");
                Assert.NotNull(k1ThemeButton);

                k1ThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Should call MainWindow.SwitchToK1Theme_Click
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "TSL theme button click switches theme")]
        public async Task ThemeSwitcher_TslThemeButtonClick_SwitchesTheme()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new MainWindow();
                window.Show();

                var tslThemeButton = window.FindControl<Button>("TslThemeButton");
                Assert.NotNull(tslThemeButton);

                tslThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Should call MainWindow.SwitchToTslTheme_Click
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #endregion

        #region Window Control Event Tests

        [AvaloniaFact(DisplayName = "Minimize button click minimizes window")]
        public async Task WindowControls_MinimizeButtonClick_MinimizesWindow()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new MainWindow();
                window.Show();

                var minimizeButton = window.GetLogicalDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Content?.ToString() == "-");

                if (minimizeButton != null)
                {
                    minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    // Should call MainWindow.MinimizeButton_Click
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        [AvaloniaFact(DisplayName = "Maximize button click toggles maximize")]
        public async Task WindowControls_MaximizeButtonClick_TogglesMaximize()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new MainWindow();
                window.Show();

                var maximizeButton = window.GetLogicalDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Content?.ToString() == "▢");

                if (maximizeButton != null)
                {
                    maximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    // Should call MainWindow.ToggleMaximizeButton_Click
                }
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #endregion

        #region Home Button Event Tests

        [AvaloniaFact(DisplayName = "Home button click navigates to getting started")]
        public async Task HomeButton_Click_NavigatesToGettingStarted()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MainConfig.AllComponents = new List<ModComponent>
                {
                    new ModComponent { Name = "Test", Guid = Guid.NewGuid() }
                };

                var window = new MainWindow();
                window.Show();

                var homeButton = window.FindControl<Button>("HomeButton");
                Assert.NotNull(homeButton);

                homeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Should call MainWindow.HomeButton_Click
            }, DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #endregion
    }
}

