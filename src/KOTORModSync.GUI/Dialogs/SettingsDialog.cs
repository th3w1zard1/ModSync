// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetBrains.Annotations;
using KOTORModSync.Controls;
using KOTORModSync.Core;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Utility;
using KOTORModSync.Services;

namespace KOTORModSync.Dialogs
{
    public partial class SettingsDialog : Window
    {
        [CanBeNull]
        private MainConfig _mainConfigInstance;
        private bool _mouseDownForWindowMoving;
        private PointerPoint _originalPoint;
        private readonly List<GitHubRelease> _holopatcherReleases = new List<GitHubRelease>();
        private bool _isDownloading = false;
        private string _selectedHolopatcherVersion;

        private sealed class GitHubRelease
        {
            public string TagName { get; set; }
            public string Name { get; set; }
            public bool PreRelease { get; set; }
            public bool Draft { get; set; }
            public List<GitHubAsset> Assets { get; set; }
            public string Body { get; set; }
        }

        private sealed class GitHubAsset
        {
            public string Name { get; set; }
            public string BrowserDownloadUrl { get; set; }
        }

        public MainConfig MainConfigInstance
        {
            get => _mainConfigInstance;
            set
            {
                _mainConfigInstance = value;
                DataContext = this;
            }
        }

        [CanBeNull]
        public MainWindow ParentWindow { get; set; }

        public SettingsDialog()
        {
            InitializeComponent();
            PointerPressed += InputElement_OnPointerPressed;
            PointerMoved += InputElement_OnPointerMoved;
            PointerReleased += InputElement_OnPointerReleased;
            PointerExited += InputElement_OnPointerReleased;

            Opened += async (s, e) =>
                await InitializeHolopatcherVersionsAsync();
            Closing += OnClosing;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void InitializeFromMainWindow(Window mainWindow)
        {
            if (mainWindow is MainWindow mw)
            {
                MainConfigInstance = mw.MainConfigInstance;
                ParentWindow = mw;
            }

            Logger.LogVerbose("SettingsDialog.InitializeFromMainWindow start");
            Logger.LogVerbose(
                $"SettingsDialog: Source='{MainConfigInstance?.sourcePathFullName}', Dest='{MainConfigInstance?.destinationPathFullName}'"
            );

            // CRITICAL: Load theme settings FIRST before applying any theme changes
            LoadThemeSettings();

            // Now apply the current theme to the dialog
            ThemeManager.ApplyCurrentToWindow(this);

            LoadDirectorySettings();
            LoadTelemetrySettings();
            LoadFileEncodingSettings();
            LoadHolopatcherVersionSettings();
            LoadPatcherEngineSettings();
            LoadNexusModsApiKeySettings();
            LoadFileWatcherSettings();

            Logger.LogVerbose("SettingsDialog.InitializeFromMainWindow end");
        }

        private void LoadPatcherEngineSettings()
        {
            try
            {
                ComboBox engineCombo = this.FindControl<ComboBox>("PatcherEngineComboBox");
                TextBox kPathBox = this.FindControl<TextBox>("KPatcherPathTextBox");
                if (engineCombo == null || MainConfigInstance == null)
                {
                    return;
                }

                engineCombo.SelectionChanged -= PatcherEngineComboBox_SelectionChanged;
                engineCombo.Items.Clear();
                engineCombo.Items.Add(new ComboBoxItem { Content = "HoloPatcher (bundled / Resources)", Tag = PatcherEngines.Holopatcher });
                engineCombo.Items.Add(new ComboBoxItem { Content = "KPatcher (external CLI)", Tag = PatcherEngines.KPatcher });

                string current = MainConfigInstance.patcherEngine ?? PatcherEngines.Holopatcher;
                int selectIdx = string.Equals(current, PatcherEngines.KPatcher, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                engineCombo.SelectedIndex = selectIdx;

                engineCombo.SelectionChanged += PatcherEngineComboBox_SelectionChanged;

                if (kPathBox != null)
                {
                    kPathBox.TextChanged -= KPatcherPathTextBox_TextChanged;
                    kPathBox.Text = MainConfigInstance.kpatcherExecutablePath ?? string.Empty;
                    kPathBox.TextChanged += KPatcherPathTextBox_TextChanged;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to load patcher engine settings");
            }
        }

        private void PatcherEngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is ComboBox cb) || !(cb.SelectedItem is ComboBoxItem item) || MainConfigInstance is null)
            {
                return;
            }

            string tag = item.Tag?.ToString() ?? PatcherEngines.Holopatcher;
            MainConfigInstance.patcherEngine = tag;
            Logger.LogVerbose($"[Settings] Patcher engine: {tag}");
        }

        private void KPatcherPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!(sender is TextBox tb) || MainConfigInstance is null)
            {
                return;
            }

            string t = tb.Text?.Trim() ?? string.Empty;
            MainConfigInstance.kpatcherExecutablePath = string.IsNullOrEmpty(t) ? null : t;
        }

        private void LoadDirectorySettings()
        {
            try
            {
                DirectoryPickerControl modDirectoryPicker =
                    this.FindControl<DirectoryPickerControl>(name: "ModDirectoryPicker");
                DirectoryPickerControl kotorDirectoryPicker =
                    this.FindControl<DirectoryPickerControl>(name: "KotorDirectoryPicker");

                // Load from MainConfig first, then from AppSettings as fallback
                string modPath = MainConfigInstance?.sourcePathFullName ?? string.Empty;
                string kotorPath = MainConfigInstance?.destinationPathFullName ?? string.Empty;

                // If MainConfig doesn't have paths, try loading from AppSettings
                if (string.IsNullOrEmpty(modPath) || string.IsNullOrEmpty(kotorPath))
                {
                    Models.AppSettings appSettings = Models.SettingsManager.LoadSettings();
                    if (
                        string.IsNullOrEmpty(modPath)
                        && !string.IsNullOrEmpty(appSettings.SourcePath)
                    )
                    {
                        modPath = appSettings.SourcePath;
                    }

                    if (
                        string.IsNullOrEmpty(kotorPath)
                        && !string.IsNullOrEmpty(appSettings.DestinationPath)
                    )
                    {
                        kotorPath = appSettings.DestinationPath;
                    }
                }

                if (modDirectoryPicker != null && !string.IsNullOrEmpty(modPath))
                {
                    Logger.LogVerbose($"SettingsDialog: Applying mod dir -> '{modPath}'");
                    UpdateDirectoryPickerWithPath(modDirectoryPicker, modPath);
                    // Set file watcher setting
                    modDirectoryPicker.EnableFileWatcher = MainConfigInstance?.enableFileWatcher ?? false;
                }

                if (kotorDirectoryPicker != null && !string.IsNullOrEmpty(kotorPath))
                {
                    Logger.LogVerbose($"SettingsDialog: Applying kotor dir -> '{kotorPath}'");
                    UpdateDirectoryPickerWithPath(kotorDirectoryPicker, kotorPath);
                    // Set file watcher setting
                    kotorDirectoryPicker.EnableFileWatcher = MainConfigInstance?.enableFileWatcher ?? false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to load directory settings");
            }
        }

        private static void UpdateDirectoryPickerWithPath(
            DirectoryPickerControl picker,
            string path
        )
        {
            if (picker is null || string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(
                        () => UpdateDirectoryPickerWithPath(picker, path),
                        DispatcherPriority.Normal
                    );
                    return;
                }

                // Set the path first
                picker.SetCurrentPathFromSettings(path);

                // Then explicitly set the ComboBox SelectedItem to match
                ComboBox comboBox = picker.FindControl<ComboBox>("PathSuggestions");
                if (comboBox != null)
                {
                    // Ensure the path is in the ItemsSource
                    List<string> currentItems =
                        (comboBox.ItemsSource as IEnumerable<string>)?.ToList()
                        ?? new List<string>();
                    if (!currentItems.Contains(path, StringComparer.Ordinal))
                    {
                        currentItems.Insert(0, path);

                        if (currentItems.Count > 20)
                        {
                            currentItems = currentItems.Take(20).ToList();
                        }

                        comboBox.ItemsSource = currentItems;
                    }

                    // Set the SelectedItem to the current path
                    comboBox.SelectedItem = path;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to update directory picker with path: {path}");
            }
        }

        private void UpdateDirectoryPickerFileWatcherSettings(bool enableFileWatcher)
        {
            try
            {
                DirectoryPickerControl modDirectoryPicker =
                    this.FindControl<DirectoryPickerControl>(name: "ModDirectoryPicker");
                DirectoryPickerControl kotorDirectoryPicker =
                    this.FindControl<DirectoryPickerControl>(name: "KotorDirectoryPicker");

                if (modDirectoryPicker != null)
                {
                    modDirectoryPicker.EnableFileWatcher = enableFileWatcher;
                    Logger.LogVerbose($"SettingsDialog: Updated mod directory picker file watcher setting: {enableFileWatcher}");
                }

                if (kotorDirectoryPicker != null)
                {
                    kotorDirectoryPicker.EnableFileWatcher = enableFileWatcher;
                    Logger.LogVerbose($"SettingsDialog: Updated kotor directory picker file watcher setting: {enableFileWatcher}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to update directory picker file watcher settings");
            }
        }

        private void LoadHolopatcherVersionSettings()
        {
            try
            {
                // Get the selected version from MainConfig or AppSettings
                string selectedVersion = MainConfigInstance?.selectedHolopatcherVersion;

                if (string.IsNullOrEmpty(selectedVersion))
                {
                    Models.AppSettings appSettings = Models.SettingsManager.LoadSettings();
                    selectedVersion = appSettings.SelectedHolopatcherVersion;
                }

                // Store the selected version for later use when ComboBox is populated
                _selectedHolopatcherVersion = selectedVersion;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to load HoloPatcher version settings");
            }
        }

        private void LoadFileEncodingSettings()
        {
            try
            {
                ComboBox fileEncodingComboBox = this.FindControl<ComboBox>("FileEncodingComboBox");
                if (fileEncodingComboBox != null && MainConfigInstance != null)
                {
                    string encoding = MainConfigInstance.fileEncoding ?? "utf-8";
                    fileEncodingComboBox.SelectedIndex =
                        encoding.Equals("windows-1252", StringComparison.OrdinalIgnoreCase)
                        || encoding.Equals("cp-1252", StringComparison.OrdinalIgnoreCase)
                        || encoding.Equals("cp1252", StringComparison.OrdinalIgnoreCase)
                            ? 1
                            : 0;
                    Logger.LogVerbose(
                        $"SettingsDialog: File encoding set to '{encoding}' (index {fileEncodingComboBox.SelectedIndex})"
                    );
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to load file encoding settings");
            }
        }

        private void LoadTelemetrySettings()
        {
            try
            {
                var telemetryConfig = TelemetryConfiguration.Load();

                CheckBox enableTelemetryCheckBox = this.FindControl<CheckBox>(
                    "EnableTelemetryCheckBox"
                );

                if (enableTelemetryCheckBox != null)
                {
                    enableTelemetryCheckBox.IsChecked = telemetryConfig.IsEnabled;
                }

                Logger.LogVerbose(
                    $"[Telemetry] Loaded telemetry settings: Enabled={telemetryConfig.IsEnabled}"
                );
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to load telemetry settings");
            }
        }

        private void LoadNexusModsApiKeySettings()
        {
            try
            {
                // Load from MainConfig first, then from AppSettings as fallback
                string apiKey = MainConfigInstance?.nexusModsApiKey;

                // If MainConfig doesn't have the API key, try loading from AppSettings
                if (string.IsNullOrEmpty(apiKey))
                {
                    Models.AppSettings appSettings = Models.SettingsManager.LoadSettings();
                    apiKey = appSettings.NexusModsApiKey;
                }

                // Update MainConfig with the loaded API key if it's not already set
                if (
                    MainConfigInstance != null
                    && !string.IsNullOrEmpty(apiKey)
                    && string.IsNullOrEmpty(MainConfigInstance.nexusModsApiKey)
                )
                {
                    MainConfigInstance.nexusModsApiKey = apiKey;
                    Logger.LogVerbose($"SettingsDialog: Loaded Nexus Mods API key from settings");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to load Nexus Mods API key settings");
            }
        }

        private void LoadFileWatcherSettings()
        {
            try
            {
                // Load from MainConfig first, then from AppSettings as fallback
                bool enableFileWatcher = MainConfigInstance?.enableFileWatcher ?? false;

                // If MainConfig doesn't have the setting, try loading from AppSettings
                if (!enableFileWatcher)
                {
                    Models.AppSettings appSettings = Models.SettingsManager.LoadSettings();
                    enableFileWatcher = appSettings.EnableFileWatcher;
                }

                // Update MainConfig with the loaded setting if it's not already set
                if (MainConfigInstance != null && !MainConfigInstance.enableFileWatcher)
                {
                    MainConfigInstance.enableFileWatcher = enableFileWatcher;
                    Logger.LogVerbose(
                        $"SettingsDialog: Loaded file watcher setting: {enableFileWatcher}"
                    );
                }

                // Update the DirectoryPickerControl instances with the setting
                UpdateDirectoryPickerFileWatcherSettings(enableFileWatcher);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to load file watcher settings");
            }
        }

        private void LoadThemeSettings()
        {
            try
            {
                ComboBox themeComboBox = this.FindControl<ComboBox>("ThemeComboBox");
                if (themeComboBox != null)
                {
                    // CRITICAL: Temporarily detach the SelectionChanged event to prevent theme changes during loading
                    themeComboBox.SelectionChanged -= ThemeComboBox_SelectionChanged;

                    string currentThemePath = ThemeService.GetCurrentTheme();
                    ThemeType currentTheme = ThemeService.GetCurrentThemeType();

                    Logger.LogVerbose($"[LoadThemeSettings] Current theme path from ThemeService: '{currentThemePath}'");
                    Logger.LogVerbose($"[LoadThemeSettings] Current theme type: {currentTheme}");

                    int selectedIndex = (int)currentTheme;
                    if (selectedIndex >= 0 && selectedIndex < themeComboBox.Items.Count)
                    {
                        themeComboBox.SelectedIndex = selectedIndex;
                        Logger.LogVerbose($"SettingsDialog: Theme set to {currentTheme} (index {selectedIndex})");
                    }

                    // Re-attach the event handler after loading is complete
                    themeComboBox.SelectionChanged += ThemeComboBox_SelectionChanged;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to load theme settings");
            }
        }

        private static void SaveThemeToSettings()
        {
            try
            {
                // Load existing settings
                Models.AppSettings settings = Models.SettingsManager.LoadSettings();

                // Update only the theme
                string currentTheme = ThemeService.GetCurrentTheme();
                settings.Theme = currentTheme;

                // Save immediately
                Models.SettingsManager.SaveSettings(settings);

                Logger.LogVerbose(
                    $"[SaveThemeToSettings] Saved theme to settings: '{currentTheme}'"
                );
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to save theme to settings");
            }
        }

        private void SaveTelemetrySettings()
        {
            try
            {
                var telemetryConfig = TelemetryConfiguration.Load();

                CheckBox enableTelemetryCheckBox = this.FindControl<CheckBox>(
                    "EnableTelemetryCheckBox"
                );

                bool wasEnabled = telemetryConfig.IsEnabled;
                bool isNowEnabled = enableTelemetryCheckBox?.IsChecked ?? true;

                telemetryConfig.SetUserConsent(isNowEnabled);

                TelemetryService.Instance.UpdateConfiguration(telemetryConfig);

                Logger.Log($"[Telemetry] Telemetry settings saved: Enabled={isNowEnabled}");

                if (!wasEnabled && isNowEnabled)
                {
                    Logger.Log("[Telemetry] Telemetry has been enabled");
                }
                else if (wasEnabled && !isNowEnabled)
                {
                    Logger.Log("[Telemetry] Telemetry has been disabled");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to save telemetry settings");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private void SaveAppSettings()
        {
            try
            {
                Logger.LogVerbose("[SettingsDialog.SaveAppSettings] === STARTING SAVE ===");

                if (MainConfigInstance is null)
                {
                    Logger.LogWarning("Cannot save settings: MainConfigInstance is null");
                    return;
                }

                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings] BEFORE UpdateMainConfigFromDirectoryPickers:"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   MainConfigInstance.sourcePath: '{MainConfigInstance.sourcePathFullName}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   MainConfigInstance.destinationPath: '{MainConfigInstance.destinationPathFullName}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   MainConfigInstance.debugLogging: '{MainConfigInstance.debugLogging}'"
                );

                // Update MainConfig with current directory picker values
                UpdateMainConfigFromDirectoryPickers();

                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings] AFTER UpdateMainConfigFromDirectoryPickers:"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   MainConfigInstance.sourcePath: '{MainConfigInstance.sourcePathFullName}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   MainConfigInstance.destinationPath: '{MainConfigInstance.destinationPathFullName}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   MainConfigInstance.debugLogging: '{MainConfigInstance.debugLogging}'"
                );

                string currentThemePath = ThemeManager.GetCurrentStylePath();
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings] Current theme from ThemeManager: '{currentThemePath}'"
                );

                var settings = Models.AppSettings.FromCurrentState(
                    MainConfigInstance,
                    currentThemePath
                );

                // Update the selected HoloPatcher version
                MainConfigInstance.selectedHolopatcherVersion = _selectedHolopatcherVersion;
                settings.SelectedHolopatcherVersion = _selectedHolopatcherVersion;

                // Update the selected theme
                settings.Theme = ThemeService.GetCurrentTheme();

                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings] AppSettings created from MainConfig:"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   settings.SourcePath: '{settings.SourcePath}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   settings.DestinationPath: '{settings.DestinationPath}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   settings.Theme: '{settings.Theme}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   settings.DebugLogging: '{settings.DebugLogging}'"
                );
                Logger.LogVerbose(
                    $"[SettingsDialog.SaveAppSettings]   settings.SelectedHolopatcherVersion: '{settings.SelectedHolopatcherVersion}'"
                );

                Models.SettingsManager.SaveSettings(settings);

                Logger.Log(
                    "[SettingsDialog.SaveAppSettings] === Application settings saved successfully ==="
                );
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to save application settings");
            }
        }

        private void UpdateMainConfigFromDirectoryPickers()
        {
            try
            {
                DirectoryPickerControl modDirectoryPicker =
                    this.FindControl<DirectoryPickerControl>(name: "ModDirectoryPicker");
                DirectoryPickerControl kotorDirectoryPicker =
                    this.FindControl<DirectoryPickerControl>(name: "KotorDirectoryPicker");

                if (modDirectoryPicker != null)
                {
                    string modPath = modDirectoryPicker.GetCurrentPathForSettings();
                    if (!string.IsNullOrEmpty(modPath) && Directory.Exists(modPath))
                    {
                        MainConfigInstance.sourcePath = new DirectoryInfo(modPath);
                        Logger.LogVerbose($"SettingsDialog: Updated MainConfig.sourcePath from picker -> '{modPath}'");
                    }
                    // Update file watcher setting from picker
                    MainConfigInstance.enableFileWatcher = modDirectoryPicker.EnableFileWatcher;
                }

                if (kotorDirectoryPicker != null)
                {
                    string kotorPath = kotorDirectoryPicker.GetCurrentPathForSettings();
                    if (!string.IsNullOrEmpty(kotorPath) && Directory.Exists(kotorPath))
                    {
                        MainConfigInstance.destinationPath = new DirectoryInfo(kotorPath);
                        Logger.LogVerbose($"SettingsDialog: Updated MainConfig.destinationPath from picker -> '{kotorPath}'");
                    }
                    // Update file watcher setting from picker (use the same setting for both pickers)
                    MainConfigInstance.enableFileWatcher = kotorDirectoryPicker.EnableFileWatcher;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to update MainConfig from directory pickers");
            }
        }

        [UsedImplicitly]
        private void OnDirectoryChanged(object sender, DirectoryChangedEventArgs e)
        {
            if (MainConfigInstance is null)
            {
                return;
            }

            try
            {
                Logger.LogVerbose(
                    $"SettingsDialog.OnDirectoryChanged type={e.PickerType} path='{e.Path}'"
                );
                switch (e.PickerType)
                {
                    case DirectoryPickerType.ModDirectory:
                        {
                            var modDirectory = new DirectoryInfo(e.Path);
                            MainConfigInstance.sourcePath = modDirectory;
                            Logger.LogVerbose(
                                $"SettingsDialog: MainConfig.sourcePath set -> '{MainConfigInstance.sourcePathFullName}'"
                            );
                            break;
                        }
                    case DirectoryPickerType.KotorDirectory:
                        {
                            var kotorDirectory = new DirectoryInfo(e.Path);
                            MainConfigInstance.destinationPath = kotorDirectory;
                            Logger.LogVerbose(
                                $"SettingsDialog: MainConfig.destinationPath set -> '{MainConfigInstance.destinationPathFullName}'"
                            );
                            break;
                        }
                }

                if (ParentWindow is null)
                {
                    return;
                }

                Logger.LogVerbose(
                    "SettingsDialog: Triggering parent window directory synchronization"
                );
                ParentWindow.SyncDirectoryPickers(e.PickerType, e.Path);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        [UsedImplicitly]
        private void HolopatcherVersionComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            try
            {
                var comboBox = sender as ComboBox;
                if (comboBox?.SelectedItem is GitHubRelease selectedRelease)
                {
                    _selectedHolopatcherVersion = selectedRelease.TagName;
                    Logger.LogVerbose(
                        $"HoloPatcher version selection changed to: {selectedRelease.TagName}"
                    );
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to handle HoloPatcher version selection change");
            }
        }

        [UsedImplicitly]
        private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (
                    !(sender is ComboBox comboBox)
                    || !(comboBox.SelectedItem is ComboBoxItem selectedItem)
                )
                {
                    return;
                }

                string themeTag = selectedItem.Tag?.ToString();
                if (string.IsNullOrEmpty(themeTag))
                {
                    return;
                }

                if (Enum.TryParse(themeTag, out Services.ThemeType themeType))
                {
                    // CRITICAL: Close the dropdown before applying theme to prevent UI rebuild conflicts
                    comboBox.IsDropDownOpen = false;

                    // Yield to dispatcher so the dropdown can finish closing before we rebuild styles
                    await Dispatcher.UIThread.InvokeAsync(
                        () => ThemeService.ApplyTheme(themeType),
                        DispatcherPriority.Background
                    );

                    // CRITICAL: Save the theme immediately to settings file so it persists
                    SaveThemeToSettings();
                    await Logger.LogVerboseAsync($"Theme changed to: {themeType}");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "Failed to change theme");
            }
        }

        [UsedImplicitly]
        private void FileEncodingComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (
                !(sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            )
            {
                return;
            }

            if (MainConfigInstance is null)
            {
                return;
            }

            string encodingTag = selectedItem.Tag?.ToString() ?? "utf-8";
            MainConfigInstance.fileEncoding = encodingTag;
            Logger.LogVerbose($"File encoding changed to: {encodingTag}");
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Save all settings when the dialog closes
                SaveTelemetrySettings();
                SaveAppSettings();

                // Notify parent window that settings have been updated
                ParentWindow?.RefreshFromSettings();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to save settings on dialog close");
            }
        }

        [UsedImplicitly]
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Settings are saved automatically on close via OnClosing event
            Close(dialogResult: true);
        }

        [UsedImplicitly]
        private void Cancel_Click(object sender, RoutedEventArgs e) => Close(dialogResult: false);

        [UsedImplicitly]
        private async void ViewPrivacyDetails_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var telemetryConfig = TelemetryConfiguration.Load();
                string privacySummary = telemetryConfig.GetPrivacySummary();

                await InformationDialog
                    .ShowInformationDialogAsync(parentWindow: null, message: privacySummary)
                    ;
            }
            catch (Exception ex)
            {
                await Logger
                    .LogExceptionAsync(ex, "[Telemetry] Failed to show privacy details")
                    ;
            }
        }

        [UsedImplicitly]
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        [UsedImplicitly]
        private void ToggleMaximizeButton_Click(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e
        )
        {
            if (!(sender is Button maximizeButton))
            {
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                maximizeButton.Content = "▢";
            }
            else
            {
                WindowState = WindowState.Maximized;
                maximizeButton.Content = "▣";
            }
        }

        [UsedImplicitly]
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Settings are saved automatically on close via OnClosing event
            Close();
        }

        private void InputElement_OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (!_mouseDownForWindowMoving)
            {
                return;
            }

            PointerPoint currentPoint = e.GetCurrentPoint(this);
            var newPoint = new PixelPoint(
                Position.X + (int)(currentPoint.Position.X - _originalPoint.Position.X),
                Position.Y + (int)(currentPoint.Position.Y - _originalPoint.Position.Y)
            );
            Position = newPoint;
        }

        private void InputElement_OnPointerPressed(object sender, PointerEventArgs e)
        {
            if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen)
            {
                return;
            }

            if (ShouldIgnorePointerForWindowDrag(e))
            {
                return;
            }

            _mouseDownForWindowMoving = true;
            _originalPoint = e.GetCurrentPoint(this);
        }

        private void InputElement_OnPointerReleased(object sender, PointerEventArgs e) =>
            _mouseDownForWindowMoving = false;

        private bool ShouldIgnorePointerForWindowDrag(PointerEventArgs e)
        {
            if (!(e.Source is Visual source))
            {
                return false;
            }

            Visual current = source;
            while (current != null && current != this)
            {
                switch (current)
                {
                    case Button _:
                    case TextBox _:
                    case ComboBox _:
                    case ListBox _:
                    case MenuItem _:
                    case Menu _:
                    case Expander _:
                    case Slider _:
                    case TabControl _:
                    case TabItem _:
                    case Control control when control.ContextMenu?.IsOpen == true:
                        return true;
                    default:
                        current = current.GetVisualParent();
                        break;
                }
            }
            return false;
        }

        private async Task InitializeHolopatcherVersionsAsync()
        {
            try
            {
                await FetchHolopatcherReleasesAsync();
                await Dispatcher.UIThread.InvokeAsync(() =>
                    UpdateCurrentVersionLabel()
                );
            }
            catch (Exception ex)
            {
                await Logger
                    .LogExceptionAsync(ex, "Failed to initialize HoloPatcher versions")
                    ;
            }
        }

        private async void RefreshVersionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isDownloading)
                {
                    return;
                }

                Button button = this.FindControl<Button>("RefreshVersionsButton");
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "Refreshing...";
                }

                await FetchHolopatcherReleasesAsync();

                // Update UI on main thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (button != null)
                    {
                        button.IsEnabled = true;
                        button.Content = "Refresh Versions";
                    }
                });
            }
            catch (Exception ex)
            {
                await Logger
                    .LogExceptionAsync(ex, "Failed to refresh HoloPatcher versions")
                    ;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Button button = this.FindControl<Button>("RefreshVersionsButton");
                    if (button != null)
                    {
                        button.IsEnabled = true;
                        button.Content = "Refresh Versions";
                    }
                });
            }
        }

        private async void DownloadVersionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isDownloading)
                {
                    return;
                }

                ComboBox comboBox = this.FindControl<ComboBox>("HolopatcherVersionComboBox");
                if (comboBox?.SelectedItem is null)
                {
                    await InformationDialog
                        .ShowInformationDialogAsync(
                            this,
                            "Please select a HoloPatcher version to download.",
                            "No Version Selected"
                        )
                        ;
                    return;
                }

                var selectedRelease = comboBox.SelectedItem as GitHubRelease;
                if (selectedRelease is null)
                {
                    return;
                }

                _isDownloading = true;
                await SetDownloadUI(isDownloading: true, $"Downloading {selectedRelease.TagName}...")
                    ;

                await DownloadAndInstallHolopatcherAsync(selectedRelease);
            }
            catch (Exception ex)
            {
                await Logger
                    .LogExceptionAsync(ex, "Failed to download HoloPatcher");
            }
            finally
            {
                _isDownloading = false;
                await SetDownloadUI(isDownloading: false, "");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "MA0051:Method is too long",
            Justification = "<Pending>"
        )]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Minor Code Smell",
            "S1075:URIs should not be hardcoded",
            Justification = "<Pending>"
        )]
        private async Task FetchHolopatcherReleasesAsync()
        {
            try
            {
                await Logger
                    .LogAsync("Fetching HoloPatcher releases from GitHub...");

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("KOTORModSync/1.0");
                    client.Timeout = TimeSpan.FromSeconds(30);

                    string url = "https://api.github.com/repos/NickHugi/PyKotor/releases";

                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        _holopatcherReleases.Clear();

                        foreach (JsonElement releaseElement in doc.RootElement.EnumerateArray())
                        {
                            string tagName = releaseElement.GetProperty("tag_name").GetString();

                            // Filter for patcher releases (tags ending with "-patcher")
                            // Format: vx.y.z-patcher or vx.y-patcher
                            if (
                                string.IsNullOrEmpty(tagName)
                                || !tagName
                                    .ToLowerInvariant()
                                    .EndsWith("-patcher", StringComparison.Ordinal)
                            )
                            {
                                continue;
                            }

                            var release = new GitHubRelease
                            {
                                TagName = tagName,
                                Name = releaseElement.GetProperty("name").GetString(),
                                PreRelease = releaseElement.GetProperty("prerelease").GetBoolean(),
                                Draft = releaseElement.GetProperty("draft").GetBoolean(),
                                Body = releaseElement.TryGetProperty("body", out JsonElement body)
                                    ? body.GetString()
                                    : "",
                                Assets = new List<GitHubAsset>(),
                            };

                            if (
                                releaseElement.TryGetProperty(
                                    "assets",
                                    out JsonElement assetsElement
                                )
                            )
                            {
                                foreach (JsonElement assetElement in assetsElement.EnumerateArray())
                                {
                                    release.Assets.Add(
                                        new GitHubAsset
                                        {
                                            Name = assetElement.GetProperty("name").GetString(),
                                            BrowserDownloadUrl = assetElement
                                                .GetProperty("browser_download_url")
                                                .GetString(),
                                        }
                                    );
                                }
                            }

                            if (!release.Draft)
                            {
                                _holopatcherReleases.Add(release);
                            }
                        }
                    }
                }

                // Update ComboBox on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ComboBox comboBox = this.FindControl<ComboBox>("HolopatcherVersionComboBox");
                    if (comboBox != null)
                    {
                        comboBox.ItemsSource = _holopatcherReleases;
                        comboBox.DisplayMemberBinding = new Avalonia.Data.Binding("TagName");

                        // Select the saved version if available, otherwise default to v1.52-patcher, otherwise first item
                        if (_holopatcherReleases.Count > 0)
                        {
                            int selectedIndex = -1;

                            // Try to select the saved version first
                            if (!string.IsNullOrEmpty(_selectedHolopatcherVersion))
                            {
                                selectedIndex = _holopatcherReleases.FindIndex(r =>
                                    r.TagName.Equals(
                                        _selectedHolopatcherVersion,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                );
                            }

                            // Fallback to v1.52-patcher if saved version not found
                            if (selectedIndex < 0)
                            {
                                selectedIndex = _holopatcherReleases.FindIndex(r =>
                                    r.TagName.Equals(
                                        "v1.52-patcher",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                );
                            }

                            // Final fallback to first item
                            if (selectedIndex < 0)
                            {
                                selectedIndex = 0;
                            }

                            comboBox.SelectedIndex = selectedIndex;
                        }
                    }
                });

                await Logger
                    .LogAsync($"Found {_holopatcherReleases.Count} HoloPatcher releases")
                    ;
            }
            catch (Exception ex)
            {
                await Logger
                    .LogExceptionAsync(ex, "Failed to fetch HoloPatcher releases from GitHub")
                    ;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "MA0051:Method is too long",
            Justification = "<Pending>"
        )]
        private async Task DownloadAndInstallHolopatcherAsync(GitHubRelease release)
        {
            try
            {
                // Extract version from tag for display (e.g., "v1.5.2-patcher" -> "1.5.2")
                string versionNumber = ExtractVersionFromTag(release.TagName);
                string displayVersion = !string.IsNullOrEmpty(versionNumber)
                    ? $"v{versionNumber}"
                    : release.TagName;

                await SetDownloadUI(isDownloading: true, $"Downloading PyKotor {displayVersion}...")
                    ;
                await Logger
                    .LogAsync(
                        $"Downloading PyKotor source code {release.TagName} (version {displayVersion})..."
                    )
                    ;

                string baseDir = UtilityHelper.GetBaseDirectory();
                string resourcesDir = UtilityHelper.GetResourcesDirectory(baseDir);

                // Download the PyKotor source code from GitHub
                // Tag format: vx.y.z-patcher or vx.y-patcher
                string downloadUrl =
                    $"https://github.com/NickHugi/PyKotor/archive/refs/tags/{release.TagName}.zip";
                string tempFile = Path.Combine(
                    Path.GetTempPath(),
                    $"PyKotor-{release.TagName}.zip"
                );

                await Logger.LogAsync($"Download URL: {downloadUrl}");

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("KOTORModSync/1.0");

                    HttpResponseMessage response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();

                    using (
                        var fs = new FileStream(
                            tempFile,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None
                        )
                    )
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                await SetDownloadUI(isDownloading: true, "Extracting PyKotor...");
                await Logger.LogAsync("Download complete. Extracting...");

                // Extract to temp directory
                string tempDir = Path.Combine(
                    Path.GetTempPath(),
                    "pykotor_extract_" + Guid.NewGuid().ToString()
                );
                Directory.CreateDirectory(tempDir);

                try
                {
                    ZipFile.ExtractToDirectory(tempFile, tempDir);

                    // Find the extracted PyKotor directory (GitHub adds a folder like PyKotor-{tag})
                    string[] extractedDirs = Directory.GetDirectories(tempDir, "PyKotor-*");
                    if (extractedDirs.Length == 0)
                    {
                        throw new DirectoryNotFoundException(
                            "Could not find PyKotor directory in extracted archive."
                        );
                    }

                    await SetDownloadUI(isDownloading: true, "Installing PyKotor...");
                    string pyKotorExtracted = extractedDirs[0];
                    string pyKotorDest = Path.Combine(resourcesDir, "PyKotor");

                    // Remove old PyKotor if it exists
                    if (Directory.Exists(pyKotorDest))
                    {
                        await Logger
                            .LogAsync($"Removing existing PyKotor at '{pyKotorDest}'...")
                            ;
                        Directory.Delete(pyKotorDest, recursive: true);
                    }

                    // Copy the new PyKotor (entire directory structure)
                    await Logger
                        .LogAsync(
                            $"Copying PyKotor from '{pyKotorExtracted}' to '{pyKotorDest}'..."
                        )
                        ;
                    CopyDirectoryRecursive(pyKotorExtracted, pyKotorDest);

                    await Logger
                        .LogAsync(
                            $"PyKotor {release.TagName} installed successfully to '{pyKotorDest}'"
                        )
                        ;

                    // Log directory structure for debugging
                    await LogDirectoryStructureAsync(pyKotorDest);

                    await SetDownloadUI(isDownloading: false, "");

                    // Wait a moment for file system to fully update
                    await Task.Delay(500);

                    // Update the current version label with retry mechanism
                    await UpdateCurrentVersionLabelWithRetryAsync();

                    // Update the parent MainWindow version display if available
                    if (ParentWindow != null)
                    {
                        try
                        {
                            await Avalonia
                                .Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                    ParentWindow.UpdateHolopatcherVersionDisplayWithRetryAsync()
                                )
                                ;
                        }
                        catch (Exception ex)
                        {
                            await Logger
                                .LogExceptionAsync(
                                    ex,
                                    "Failed to update MainWindow version display"
                                )
                                ;
                        }
                    }

                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        $"PyKotor {displayVersion} source code has been installed.\n\nHoloPatcher will use this version.",
                        "Installation Complete"
                    );

                    // Ensure the combobox still shows the selected version after installation
                    ComboBox comboBox = this.FindControl<ComboBox>("HolopatcherVersionComboBox");
                    if (comboBox != null && comboBox.SelectedItem is null)
                    {
                        // Re-fetch releases to ensure combobox is properly populated
                        await FetchHolopatcherReleasesAsync();
                    }
                }
                finally
                {
                    // Cleanup
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }

                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            }
            catch (Exception ex)
            {
                await SetDownloadUI(isDownloading: false, "");

                await Logger
                    .LogExceptionAsync(ex, "Failed to download and install PyKotor")
                    ;
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"Failed to install PyKotor:\n\n{ex.Message}",
                    "Installation Failed"
                );
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            // Create destination directory if it doesn't exist
            Directory.CreateDirectory(destDir);

            // Copy all files in the current directory
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, overwrite: true);
            }

            // Recursively copy all subdirectories
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string destSubDir = Path.Combine(destDir, dirName);
                CopyDirectoryRecursive(dir, destSubDir);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "MA0051:Method is too long",
            Justification = "<Pending>"
        )]
        private static async Task LogDirectoryStructureAsync(string pyKotorPath)
        {
            try
            {
                await Logger
                    .LogAsync($"[DirectoryStructure] Logging structure of: {pyKotorPath}")
                    ;

                if (!Directory.Exists(pyKotorPath))
                {
                    await Logger
                        .LogAsync("[DirectoryStructure] PyKotor directory does not exist!")
                        ;
                    return;
                }

                // Log root level directories
                string[] rootDirs = Directory.GetDirectories(pyKotorPath);
                await Logger
                    .LogAsync(
                        $"[DirectoryStructure] Root directories ({rootDirs.Length}): {string.Join(", ", rootDirs.Select(Path.GetFileName))}"
                    )
                    ;

                // Check Tools directory
                string toolsPath = Path.Combine(pyKotorPath, "Tools");
                if (Directory.Exists(toolsPath))
                {
                    string[] toolDirs = Directory.GetDirectories(toolsPath);

                    await Logger.LogAsync($"[DirectoryStructure] Tools directories ({toolDirs.Length}): {string.Join(", ", toolDirs.Select(Path.GetFileName))}");

                    // Check HoloPatcher specifically
                    string holopatcherPath = Path.Combine(toolsPath, "HoloPatcher");
                    if (Directory.Exists(holopatcherPath))
                    {
                        string[] holopatcherDirs = Directory.GetDirectories(holopatcherPath);

                        await Logger.LogAsync($"[DirectoryStructure] HoloPatcher directories ({holopatcherDirs.Length}): {string.Join(", ", holopatcherDirs.Select(Path.GetFileName))}");

                        // Check src directory
                        string srcPath = Path.Combine(holopatcherPath, "src");
                        if (Directory.Exists(srcPath))
                        {
                            string[] srcDirs = Directory.GetDirectories(srcPath);

                            await Logger.LogAsync($"[DirectoryStructure] HoloPatcher/src directories ({srcDirs.Length}): {string.Join(", ", srcDirs.Select(Path.GetFileName))}");

                            // Check final holopatcher directory
                            string finalHolopatcherPath = Path.Combine(srcPath, "holopatcher");
                            if (Directory.Exists(finalHolopatcherPath))
                            {
                                string[] finalDirs = Directory.GetDirectories(finalHolopatcherPath);
                                string[] finalFiles = Directory.GetFiles(finalHolopatcherPath);

                                await Logger.LogAsync($"[DirectoryStructure] Final holopatcher directory exists with {finalDirs.Length} subdirs and {finalFiles.Length} files");
                                await Logger.LogAsync($"[DirectoryStructure] Files: {string.Join(", ", finalFiles.Select(Path.GetFileName))}");
                            }
                            else
                            {
                                await Logger.LogAsync("[DirectoryStructure] Final holopatcher directory does not exist!");
                            }
                        }
                        else
                        {
                            await Logger.LogAsync("[DirectoryStructure] HoloPatcher/src directory does not exist!");
                        }
                    }
                    else
                    {
                        await Logger.LogAsync("[DirectoryStructure] HoloPatcher directory does not exist!");
                    }
                }
                else
                {
                    await Logger.LogAsync("[DirectoryStructure] Tools directory does not exist!");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "Failed to log directory structure");
            }
        }

        private async Task SetDownloadUI(bool isDownloading, string statusText)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProgressBar progressBar = this.FindControl<ProgressBar>("DownloadProgressBar");
                    TextBlock statusTextBlock = this.FindControl<TextBlock>("DownloadStatusText");
                    Button refreshButton = this.FindControl<Button>("RefreshVersionsButton");
                    Button downloadButton = this.FindControl<Button>("DownloadVersionButton");

                    if (progressBar != null)
                    {
                        progressBar.IsVisible = isDownloading;
                    }

                    if (statusTextBlock != null)
                    {
                        statusTextBlock.IsVisible = isDownloading;
                        statusTextBlock.Text = statusText;
                    }

                    if (refreshButton != null)
                    {
                        refreshButton.IsEnabled = !isDownloading;
                    }

                    if (downloadButton != null)
                    {
                        downloadButton.IsEnabled = !isDownloading;
                        downloadButton.Content = isDownloading
                            ? "Downloading..."
                            : "Download Selected";
                    }
                });
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "Failed to set download UI state");
            }
        }

        private void UpdateCurrentVersionLabel()
        {
            try
            {
                TextBlock label = this.FindControl<TextBlock>("CurrentVersionLabel");
                if (label is null)
                {
                    return;
                }

                string versionInfo = GetInstalledPyKotorVersion();
                label.Text = versionInfo;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to update current version label");
            }
        }

        private async Task UpdateCurrentVersionLabelWithRetryAsync(int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    string versionInfo = GetInstalledPyKotorVersion();

                    // Update UI on main thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        TextBlock label = this.FindControl<TextBlock>("CurrentVersionLabel");
                        if (label is null)
                        {
                            return;
                        }

                        label.Text = versionInfo;
                    });

                    // If we got a successful version (not missing/incomplete), we're done
                    if (!versionInfo.Contains("missing") && !versionInfo.Contains("incomplete"))
                    {
                        await Logger.LogVerboseAsync($"[UpdateCurrentVersionLabelWithRetryAsync] Success on attempt {i + 1}: {versionInfo}");
                        return;
                    }

                    // If this isn't the last attempt, wait and try again
                    if (i < maxRetries - 1)
                    {
                        await Logger.LogVerboseAsync($"[UpdateCurrentVersionLabelWithRetryAsync] Attempt {i + 1} failed with: {versionInfo}, retrying in 1 second...");
                        await Task.Delay(1000);
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogExceptionAsync(ex, $"[UpdateCurrentVersionLabelWithRetryAsync] Failed to update current version label on attempt {i + 1}");
                    if (i < maxRetries - 1)
                    {
                        await Task.Delay(1000);
                    }
                }
            }
        }

        /// <summary>
        /// Extracts version number from PyKotor/HoloPatcher tag name.
        /// Handles formats: vx.y.z-patcher, vx.y-patcher, vx.y.z-alpha-patcher, vx.y-rc1-patcher, etc.
        /// </summary>
        internal static string ExtractVersionFromTag(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return null;
            }

            // Use safe string parsing instead of regex to avoid ReDoS attacks
            // Handle patterns like "v1.5.2-patcher", "v1.5-patcher", "v1.5.2-alpha-patcher", "v1.5-rc1-patcher"
            string lowerTag = tagName.ToLowerInvariant();

            // Must end with "-patcher"
            if (!lowerTag.EndsWith("-patcher", StringComparison.Ordinal))
            {
                return null;
            }

            // Remove the "-patcher" suffix
            string versionPart = lowerTag.Substring(0, lowerTag.Length - 8); // Remove "-patcher"

            // Remove optional "v" prefix
            if (versionPart.StartsWith("v", StringComparison.Ordinal))
            {
                versionPart = versionPart.Substring(1);
            }

            // Remove optional suffix like "-alpha", "-beta", "-rc1", etc.
            int dashIndex = versionPart.LastIndexOf('-');
            if (dashIndex > 0)
            {
                string suffix = versionPart.Substring(dashIndex + 1);
                // Check if it's a valid version suffix
                if (
                    suffix.StartsWith("alpha", StringComparison.Ordinal)
                    || suffix.StartsWith("beta", StringComparison.Ordinal)
                    || suffix.StartsWith("rc", StringComparison.Ordinal)
                    || suffix.StartsWith("dev", StringComparison.Ordinal)
                    || suffix.StartsWith("snapshot", StringComparison.Ordinal)
                    || suffix.StartsWith("preview", StringComparison.Ordinal)
                )
                {
                    versionPart = versionPart.Substring(0, dashIndex);
                }
            }

            // Validate the version format (x.y or x.y.z)
            string[] parts = versionPart.Split('.');
            if (parts.Length < 2 || parts.Length > 3)
            {
                return null;
            }

            // Ensure all parts are numeric
            foreach (string part in parts)
            {
                if (
                    !int.TryParse(
                        part,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out _
                    )
                )
                {
                    return null;
                }
            }

            return versionPart;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "MA0051:Method is too long",
            Justification = "<Pending>"
        )]
        private static string GetInstalledPyKotorVersion()
        {
            try
            {
                string baseDir = UtilityHelper.GetBaseDirectory();
                string resourcesDir = UtilityHelper.GetResourcesDirectory(baseDir);

                // Check for PyKotor source directory
                string pyKotorPath = Path.Combine(resourcesDir, "PyKotor");
                string holopatcherPath = Path.Combine(
                    pyKotorPath,
                    "Tools",
                    "HoloPatcher",
                    "src",
                    "holopatcher"
                );

                Logger.LogVerbose(
                    $"[GetInstalledPyKotorVersion] Checking PyKotor at: {pyKotorPath}"
                );
                Logger.LogVerbose(
                    $"[GetInstalledPyKotorVersion] Checking HoloPatcher at: {holopatcherPath}"
                );

                if (!Directory.Exists(pyKotorPath))
                {
                    Logger.LogVerbose("[GetInstalledPyKotorVersion] PyKotor directory not found");
                    return "Current: PyKotor not found";
                }

                // Check if Tools directory exists
                string toolsPath = Path.Combine(pyKotorPath, "Tools");
                if (!Directory.Exists(toolsPath))
                {
                    Logger.LogVerbose("[GetInstalledPyKotorVersion] Tools directory not found");
                    return "Current: PyKotor found but Tools missing";
                }

                // Check if HoloPatcher directory exists
                string holopatcherDir = Path.Combine(toolsPath, "HoloPatcher");
                if (!Directory.Exists(holopatcherDir))
                {
                    Logger.LogVerbose(
                        "[GetInstalledPyKotorVersion] HoloPatcher directory not found"
                    );
                    return "Current: PyKotor found but HoloPatcher missing";
                }

                // Check if src directory exists
                string srcPath = Path.Combine(holopatcherDir, "src");
                if (!Directory.Exists(srcPath))
                {
                    Logger.LogVerbose(
                        "[GetInstalledPyKotorVersion] HoloPatcher src directory not found"
                    );
                    return "Current: PyKotor found but HoloPatcher incomplete";
                }

                if (!Directory.Exists(holopatcherPath))
                {
                    Logger.LogVerbose(
                        "[GetInstalledPyKotorVersion] HoloPatcher source directory not found"
                    );
                    return "Current: PyKotor found but HoloPatcher incomplete";
                }

                // Try to read version from config.py
                string configPath = Path.Combine(holopatcherPath, "config.py");
                Logger.LogVerbose(
                    $"[GetInstalledPyKotorVersion] Looking for config.py at: {configPath}"
                );

                if (File.Exists(configPath))
                {
                    try
                    {
                        string configContent = File.ReadAllText(configPath);
                        // Look for the version in a safer way to avoid regex ReDoS
                        int versionIndex = configContent.IndexOf(
                            "\"currentVersion\":",
                            StringComparison.Ordinal
                        );
                        if (versionIndex >= 0)
                        {
                            int valueStart = configContent.IndexOf('"', versionIndex + 17); // Skip "currentVersion":
                            if (valueStart >= 0)
                            {
                                valueStart++; // Skip the opening quote
                                int valueEnd = configContent.IndexOf('"', valueStart);
                                if (valueEnd > valueStart)
                                {
                                    string version = configContent.Substring(
                                        valueStart,
                                        valueEnd - valueStart
                                    );
                                    Logger.LogVerbose(
                                        $"[GetInstalledPyKotorVersion] Found version in config.py: {version}"
                                    );
                                    return $"Current: PyKotor v{version} (Python source)";
                                }
                            }
                        }
                        Logger.LogVerbose(
                            "[GetInstalledPyKotorVersion] Version not found in config.py"
                        );
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "Error reading config.py");
                    }
                }
                else
                {
                    Logger.LogVerbose("[GetInstalledPyKotorVersion] config.py not found");
                }

                // If we got here, PyKotor exists but we couldn't determine version
                return "Current: PyKotor source installed (version unknown)";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error getting installed PyKotor version");
                return "Current: Error checking version";
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "MA0004:Use Task",
            Justification = "<Pending>"
        )]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "MA0051:Method is too long",
            Justification = "<Pending>"
        )]
        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Button button = this.FindControl<Button>("CheckForUpdatesButton");
                TextBlock statusText = this.FindControl<TextBlock>("UpdateStatusText");

                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "Checking...";
                }

                if (statusText != null)
                {
                    statusText.Text = "Checking for updates...";
                }

                // Get the auto-update service from the application
                var app = Application.Current as App;
                if (app is null)
                {
                    if (statusText != null)
                    {
                        statusText.Text = "Error: Unable to access update service.";
                    }

                    await Logger
                        .LogAsync("Unable to access App instance for update check")
                        ;
                    return;
                }

                // Create and use AutoUpdateService
                using (var updateService = new AutoUpdateService())
                {
                    updateService.Initialize();
                    bool updatesAvailable = await updateService.CheckForUpdatesAsync();

                    // Update UI on main thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (statusText != null)
                        {
                            statusText.Text = updatesAvailable
                                ? "Updates found! Check the update dialog."
                                : "You are running the latest version.";
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "Failed to check for updates");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TextBlock statusText = this.FindControl<TextBlock>("UpdateStatusText");
                    if (statusText != null)
                    {
                        statusText.Text = $"Error checking for updates: {ex.Message}";
                    }
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Button button = this.FindControl<Button>("CheckForUpdatesButton");
                    if (button != null)
                    {
                        button.IsEnabled = true;
                        button.Content = "Check for Updates";
                    }
                });
            }
        }
    }
}
