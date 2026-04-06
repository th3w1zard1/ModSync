// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetBrains.Annotations;
using KOTORModSync.CallbackDialogs;
using KOTORModSync.Controls;
using KOTORModSync.Core;
using KOTORModSync.Core.FileSystemUtils;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Utility;
using KOTORModSync.Dialogs;
using KOTORModSync.Models;
using KOTORModSync.Services;
using ReactiveUI;
using SharpCompress.Archives;
using static KOTORModSync.Core.Services.ModManagementService;
using Activity = System.Diagnostics.Activity;
using ComponentProcessingService = KOTORModSync.Core.Services.ComponentProcessingService;
using ComponentValidationService = KOTORModSync.Core.Services.ComponentValidationService;
using DownloadCacheEntry = KOTORModSync.Core.Services.DownloadCacheService.DownloadCacheEntry;
using NotNullAttribute = JetBrains.Annotations.NotNullAttribute;
using TelemetryConfiguration = KOTORModSync.Core.Services.TelemetryConfiguration;
using TelemetryService = KOTORModSync.Core.Services.TelemetryService;

namespace KOTORModSync
{
    [SuppressMessage(category: "ReSharper", checkId: "UnusedParameter.Local")]
    public sealed partial class MainWindow : Window
    {
        public static readonly DirectProperty<MainWindow, ModComponent> CurrentComponentProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, ModComponent>(
                nameof(CurrentComponent),
                o => (o?.CurrentComponent),
                (o, v) => o.CurrentComponent = v
            );
        private bool _ignoreWindowMoveWhenClickingComboBox;
        private bool _initialize = true;
        private bool _installRunning;
        private bool _autoGenerateRunning;
        private bool _mouseDownForWindowMoving;
        private PointerPoint _originalPoint;
        private OutputWindow _outputWindow;
        private bool _progressWindowClosed;
        private string _searchText;
        private CancellationTokenSource _modSuggestCts;
        private CancellationTokenSource _installSuggestCts;
        private CancellationTokenSource _validationCts;
        private bool _suppressPathEvents;
        private bool _suppressComboEvents;
        private bool _suppressComponentCheckboxEvents;
        private bool? _rootSelectionState;
        private bool _editorMode;
        private bool _spoilerFreeMode;
        private bool _isClosingProgressWindow;
        private bool _wizardMode = true; // Default to true
        private bool _isWizardMode;
        private string _lastLoadedFileName;
        private CancellationTokenSource _preResolveCts;
        private DispatcherTimer _preResolveDebounceTimer;
        private string _currentDragFilePath;
        private bool _dragOverlayVisible;
        private DispatcherTimer _dragOverlayDebounceTimer;
        private DragEventArgs _pendingDragEventArgs;
        private bool _pendingDragOverlayUpdate;
        private bool _landingPageVisible = true;
        private bool _mainContentVisible;
        private bool _wizardContentVisible;
        private bool _showWizardToggle;
        private bool _autoLoadedInstructionFile;

        public static bool HasFetchedDownloads { get; private set; }

        private DispatcherTimer _downloadAnimationTimer;
        private int _downloadAnimationDots;

        public ModManagementService ModManagementService { get; }

        public DownloadCacheService DownloadCacheService { get; }

        private readonly ModListService _modListService;
        private readonly ValidationService _validationService;
        private readonly UIStateService _uiStateService;
        private readonly SelectionService _selectionService;
        private readonly FileSystemService _fileSystemService;
        private readonly GuiPathService _guiPathService;
        private readonly DialogService _dialogService;
        private readonly DragDropService _dragDropService;
        private readonly Services.FileLoadingService _fileLoadingService;
        private readonly Services.ComponentEditorService _componentEditorService;
        private readonly ComponentSelectionService _componentSelectionService;
        private readonly DownloadOrchestrationService _downloadOrchestrationService;
        private readonly FilterUIService _filterUiService;
        private readonly InstructionBrowsingService _instructionBrowsingService;
        private readonly InstructionGenerationService _instructionGenerationService;
        private readonly ValidationDisplayService _validationDisplayService;
        private readonly StepNavigationService _stepNavigationService;

        private readonly TelemetryService _telemetryService;

        private ListBox ModListBox
        {
            get
            {
                // Check if we're on the UI thread
                if (Dispatcher.UIThread.CheckAccess())
                {
                    // Safe to access UI elements directly
                    return ModListSidebar?.ModListBox;
                }
                else
                {
                    // We're on a background thread, use dispatcher to access UI
                    return Dispatcher.UIThread.InvokeAsync(() => ModListSidebar?.ModListBox).GetAwaiter().GetResult();
                }
            }
        }
        public bool IsClosingMainWindow { get; set; }
        public string LastLoadedInstructionFileName => _lastLoadedFileName;
        public bool? RootSelectionState
        {
            get => _rootSelectionState;
            set
            {
                if (_rootSelectionState == value)
                {
                    return;
                }

                _suppressSelectAllCheckBoxEvents = true;

                try
                {

                    _ = SetAndRaise(RootSelectionStateProperty, ref _rootSelectionState, value);

                    _componentSelectionService.HandleSelectAllCheckbox(value, ComponentCheckboxChecked, ComponentCheckboxUnchecked);

                    UpdateModCounts();
                    UpdateStepProgress();
                    ResetDownloadStatusDisplay();
                }
                finally
                {
                    _suppressSelectAllCheckBoxEvents = false;
                }
            }
        }
        public bool EditorMode
        {
            get => _editorMode;
            set
            {
                if (_editorMode == value)
                {
                    return;
                }

                _ = SetAndRaise(EditorModeProperty, ref _editorMode, value);

                // When EditorMode is enabled, disable SpoilerFreeMode (developers don't need spoiler protection)
                if (value)
                {
                    SpoilerFreeMode = false;
                }

                UpdateMenuVisibility();
                RefreshModListItems();
                BuildGlobalActionsMenu();
                UpdateStepProgress();
                RefreshModListVisuals();

                // If turning on EditorMode, disable WizardMode
                if (value)
                {
                    WizardMode = false;
                }

                UpdateWorkflowSurfaces();
            }
        }

        public static readonly DirectProperty<MainWindow, bool> EditorModeProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool>(
                nameof(EditorMode),
                o => o._editorMode,
                (o, v) => o.EditorMode = v
            );

        public bool SpoilerFreeMode
        {
            get => _spoilerFreeMode;
            set
            {
                if (_spoilerFreeMode == value)
                {
                    return;
                }

                _ = SetAndRaise(SpoilerFreeModeProperty, ref _spoilerFreeMode, value);
                RefreshModListItems();
                RefreshModListVisuals();
                RefreshAllTooltips();
            }
        }

        public static readonly DirectProperty<MainWindow, bool> SpoilerFreeModeProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool>(
                nameof(SpoilerFreeMode),
                o => o._spoilerFreeMode,
                (o, v) => o.SpoilerFreeMode = v
            );

        public bool WizardMode
        {
            get => _wizardMode;
            set
            {
                if (_wizardMode == value)
                {
                    return;
                }

                _ = SetAndRaise(WizardModeProperty, ref _wizardMode, value);

                // If turning on WizardMode, initialize it
                if (value && !EditorMode && MainConfig.AllComponents != null && MainConfig.AllComponents.Count > 0)
                {
                    EnterWizardMode();
                }
                // If turning off WizardMode, exit it
                else if (!value && _isWizardMode)
                {
                    ExitWizardMode();
                }

                /*
                 * When the wizard is dismissed while instructions are still loaded we reroute the user back to the Getting Started experience rather than the landing page.
                 * This keeps the active project in focus and mirrors the normal non-wizard flow where players review steps, validation, and progress from the main tab set.
                 */
                if (!value && !EditorMode)
                {
                    bool hasComponentsLoaded = MainConfig.AllComponents != null && MainConfig.AllComponents.Count > 0;

                    if (hasComponentsLoaded
                        && TabControl != null
                        && InitialTab != null)
                    {
                        InitialTab.IsVisible = true;
                        SetTabInternal(TabControl, InitialTab);
                    }
                }

                UpdateWorkflowSurfaces();
            }
        }

        public static readonly DirectProperty<MainWindow, bool> WizardModeProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool>(
                nameof(WizardMode),
                o => o._wizardMode,
                (o, v) => o.WizardMode = v
            );

        public bool LandingPageVisible
        {
            get => _landingPageVisible;
            private set => _ = SetAndRaise(LandingPageVisibleProperty, ref _landingPageVisible, value);
        }

        public static readonly DirectProperty<MainWindow, bool> LandingPageVisibleProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool>(
                nameof(LandingPageVisible),
                o => o._landingPageVisible,
                (o, v) => o.LandingPageVisible = v
            );

        public bool MainContentVisible
        {
            get => _mainContentVisible;
            private set => _ = SetAndRaise(MainContentVisibleProperty, ref _mainContentVisible, value);
        }

        public static readonly DirectProperty<MainWindow, bool> MainContentVisibleProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool>(
                nameof(MainContentVisible),
                o => o._mainContentVisible,
                (o, v) => o.MainContentVisible = v
            );

        public bool WizardContentVisible
        {
            get => _wizardContentVisible;
            private set => _ = SetAndRaise(WizardContentVisibleProperty, ref _wizardContentVisible, value);
        }

        public static readonly DirectProperty<MainWindow, bool> WizardContentVisibleProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool>(
                nameof(WizardContentVisible),
                o => o._wizardContentVisible,
                (o, v) => o.WizardContentVisible = v
            );

        public bool ShowWizardToggle
        {
            get => _showWizardToggle;
            private set => _ = SetAndRaise(ShowWizardToggleProperty, ref _showWizardToggle, value);
        }

        public static readonly DirectProperty<MainWindow, bool> ShowWizardToggleProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool>(
                nameof(ShowWizardToggle),
                o => o._showWizardToggle,
                (o, v) => o.ShowWizardToggle = v
            );

        public static readonly DirectProperty<MainWindow, bool?> RootSelectionStateProperty =
            AvaloniaProperty.RegisterDirect<MainWindow, bool?>(
                nameof(RootSelectionState),
                o => o._rootSelectionState,
                (o, v) => o.RootSelectionState = v
            );

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                DataContext = this;

                Logger.Initialize();

                // Initialize core services first before any UI operations
                _ = new InstallationService();

                ModManagementService = new ModManagementService(MainConfigInstance);
                ModManagementService.ModOperationCompleted += OnModOperationCompleted;
                ModManagementService.ModValidationCompleted += OnModValidationCompleted;

                DownloadCacheService = new DownloadCacheService();
                DownloadCacheService.SetDownloadManager();

                _modListService = new ModListService(MainConfigInstance);
                _validationService = new ValidationService(MainConfigInstance);
                _uiStateService = new UIStateService(MainConfigInstance, _validationService);

                // Load settings BEFORE initializing UI controls
                LoadSettings();

                // Now initialize UI controls (services are ready)
                Task _task = InitializeControls();
                InitializeTopMenu();
                UpdateMenuVisibility();
                InitializeDirectoryPickers();
                InitializeModListBox();
                _ = new InstructionManagementService();
                _selectionService = new SelectionService(MainConfigInstance);
                _fileSystemService = new FileSystemService();
                _componentSelectionService = new ComponentSelectionService(MainConfigInstance);
                _guiPathService = new GuiPathService(MainConfigInstance, _componentSelectionService);
                _dialogService = new DialogService(this);
                _ = new MenuBuilderService(ModManagementService, this);
                _dragDropService = new DragDropService(this, () => MainConfig.AllComponents, () => ProcessComponentsAsync(MainConfig.AllComponents));
                _fileLoadingService = new Services.FileLoadingService(MainConfigInstance, this);
                _componentEditorService = new Services.ComponentEditorService(MainConfigInstance, this);
                _downloadOrchestrationService = new DownloadOrchestrationService(DownloadCacheService, MainConfigInstance, this);
                _downloadOrchestrationService.DownloadStateChanged += OnDownloadStateChanged;
                _filterUiService = new FilterUIService(MainConfigInstance);

                InitializeDownloadAnimationTimer();
                _instructionBrowsingService = new InstructionBrowsingService(MainConfigInstance, _dialogService);
                _instructionGenerationService = new InstructionGenerationService(MainConfigInstance, this);
                _validationDisplayService = new ValidationDisplayService(_validationService, () => MainConfig.AllComponents);
                _ = new SettingsService(MainConfigInstance, this);
                _stepNavigationService = new StepNavigationService(MainConfigInstance, _validationService);

                _telemetryService = TelemetryService.Instance;

                CallbackObjects.SetCallbackObjects(
                    new ConfirmationDialogCallback(this),
                    new OptionsDialogCallback(this),
                    new InformationDialogCallback(this)
                );
                PropertyChanged += SearchText_PropertyChanged;

                // Subscribe to theme changes to refresh step indicators
                ThemeManager.StyleChanged += OnThemeChanged;

                if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
                {
                    ConsoleConfig.DisableQuickEdit();
                    ConsoleConfig.DisableConsoleCloseButton();
                }
                AddHandler(DragDrop.DropEvent, Drop);
                AddHandler(DragDrop.DragOverEvent, DragOver);
                AddHandler(DragDrop.DragLeaveEvent, DragLeave);

                InitializeModDirectoryWatcher();

                BuildGlobalActionsMenu();
                if (LandingPage != null)
                {
                    LandingPage.LoadInstructionsRequested += OnLandingPageLoadInstructionsRequested;
                    LandingPage.CreateInstructionsRequested += OnLandingPageCreateInstructionsRequested;
                    LandingPage.OpenSponsorPageRequested += OnLandingPageOpenSponsorPageRequested;
                }
                UpdateWorkflowSurfaces();

                Opened += async (s, e) =>
                {
                    await InitializeTelemetryIfEnabled();
                    UpdateHolopatcherVersionDisplay();

                    // Update theme button states now that UI is fully loaded
                    UpdateThemeButtonStates();

                    // Detect game version if destination path is set
                    if (MainConfig.DestinationPath?.Exists == true)
                    {
                        _componentSelectionService.DetectGameVersion();
                    }

                    if (
                        !_autoLoadedInstructionFile
                        && !string.IsNullOrWhiteSpace(CLIArguments.InstructionFile)
                        && File.Exists(CLIArguments.InstructionFile)
                    )
                    {
                        _autoLoadedInstructionFile = true;
                        LandingPageVisible = false;
                        ShowWizardToggle = false;
                        AutoLoadInstructionFileAsync(CLIArguments.InstructionFile);
                    }
                };
            }
            catch (Exception e)
            {
                Logger.LogException(e, customMessage: "A fatal error has occurred loading the main window");
                _telemetryService?.RecordError("MainWindow.Constructor", e.Message, e.StackTrace);
                throw;
            }
        }

        private async Task InitializeTelemetryIfEnabled()
        {
            try
            {
                var config = TelemetryConfiguration.Load();

                if (config.IsEnabled)
                {
                    _telemetryService.Initialize();
                    await Logger.LogVerboseAsync("[Telemetry] Telemetry initialized from configuration");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[Telemetry] Error initializing telemetry");
            }
            await Task.CompletedTask;
        }
        private static void UpdatePathDisplays(TextBlock modPathDisplay, TextBlock kotorPathDisplay)
        {
            if (modPathDisplay != null)
            {
                modPathDisplay.Text = MainConfig.SourcePath?.FullName ?? "Not set";
            }

            if (kotorPathDisplay != null)
            {
                kotorPathDisplay.Text = MainConfig.DestinationPath?.FullName ?? "Not set";
            }
        }
        private void UpdatePathDisplays()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(UpdatePathDisplays, DispatcherPriority.Normal);
                return;
            }
            TextBlock modPathDisplay = this.FindControl<TextBlock>(name: "CurrentModPathDisplay");
            TextBlock kotorPathDisplay = this.FindControl<TextBlock>(name: "CurrentKotorPathDisplay");
            UpdatePathDisplays(modPathDisplay, kotorPathDisplay);

            RefreshAllTooltips();
        }

        private void LoadSettings()
        {
            try
            {
                Logger.LogVerbose("[MainWindow.LoadSettings] === STARTING LOAD ===");
                Logger.LogVerbose("[MainWindow.LoadSettings] BEFORE loading settings:");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   MainConfigInstance.sourcePath: '{MainConfigInstance.sourcePathFullName}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   MainConfigInstance.destinationPath: '{MainConfigInstance.destinationPathFullName}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   MainConfigInstance.debugLogging: '{MainConfigInstance.debugLogging}'");

                AppSettings settings = SettingsManager.LoadSettings();

                Logger.LogVerbose("[MainWindow.LoadSettings] Settings loaded from file:");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   settings.SourcePath: '{settings.SourcePath}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   settings.DestinationPath: '{settings.DestinationPath}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   settings.Theme: '{settings.Theme}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   settings.DebugLogging: '{settings.DebugLogging}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   settings.SpoilerFreeMode: '{settings.SpoilerFreeMode}'");

                settings.ApplyToMainConfig(MainConfigInstance, out string theme, out bool spoilerFreeMode);

                Logger.LogVerbose("[MainWindow.LoadSettings] AFTER ApplyToMainConfig:");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   MainConfigInstance.sourcePath: '{MainConfigInstance.sourcePathFullName}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   MainConfigInstance.destinationPath: '{MainConfigInstance.destinationPathFullName}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   MainConfigInstance.debugLogging: '{MainConfigInstance.debugLogging}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   theme: '{theme}'");
                Logger.LogVerbose($"[MainWindow.LoadSettings]   spoilerFreeMode: '{spoilerFreeMode}'");

                // Detect game version if destination path is set (don't do this here - _componentSelectionService not initialized yet)
                // Detection will happen automatically when path is set via GuiPathService

                EditorMode = false;
                SpoilerFreeMode = spoilerFreeMode;

                // Apply theme (defaults to Fluent Light if not specified)
                string themeToApply = string.IsNullOrEmpty(theme) ? "/Styles/FluentLightStyle.axaml" : theme;
                ApplyTheme(themeToApply);

                // Set TargetGame from theme (they're the same thing)
                MainConfig.TargetGame = GetTargetGameFromTheme(themeToApply);

                ThemeManager.ApplyCurrentToWindow(this);

                UpdatePathDisplays();

                UpdateDirectoryPickersFromSettings(settings);

                // Update HoloPatcher version display
                UpdateHolopatcherVersionDisplay();

                // Update theme button states to reflect current theme
                // Note: Buttons may not exist yet if called before UI is fully loaded,
                // so we also call this in the Opened event handler
                UpdateThemeButtonStates();

                Logger.LogVerbose("Settings loaded and applied successfully");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, customMessage: "Failed to load settings");
            }
        }

        private void UpdateDirectoryPickersFromSettings(AppSettings settings)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => UpdateDirectoryPickersFromSettings(settings), DispatcherPriority.Normal);
                return;
            }
            try
            {

                if (!string.IsNullOrEmpty(settings.SourcePath))
                {
                    DirectoryPickerControl modPicker = this.FindControl<DirectoryPickerControl>("ModDirectoryPicker");
                    DirectoryPickerControl step1ModPicker = GettingStartedTabControl?.FindControl<DirectoryPickerControl>("Step1ModDirectoryPicker");
                    UpdateDirectoryPickerWithPath(modPicker, settings.SourcePath);
                    UpdateDirectoryPickerWithPath(step1ModPicker, settings.SourcePath);
                }

                if (!string.IsNullOrEmpty(settings.DestinationPath))
                {
                    DirectoryPickerControl kotorPicker = this.FindControl<DirectoryPickerControl>("KotorDirectoryPicker");
                    DirectoryPickerControl step1KotorPicker = GettingStartedTabControl?.FindControl<DirectoryPickerControl>("Step1KotorDirectoryPicker");
                    UpdateDirectoryPickerWithPath(kotorPicker, settings.DestinationPath);
                    UpdateDirectoryPickerWithPath(step1KotorPicker, settings.DestinationPath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to update directory pickers from settings");
            }
        }

        private static void UpdateDirectoryPickerWithPath(DirectoryPickerControl picker, string path)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => UpdateDirectoryPickerWithPath(picker, path), DispatcherPriority.Normal);
                return;
            }
            if (picker is null || string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                // SetCurrentPathFromSettings already handles everything including ItemsSource and suggestions
                picker.SetCurrentPathFromSettings(path);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to update directory picker with path: {path}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                Logger.LogVerbose("[MainWindow.SaveSettings] === SAVING SETTINGS ON APP CLOSE ===");
                Logger.LogVerbose($"[MainWindow.SaveSettings] MainConfigInstance.sourcePath: '{MainConfigInstance.sourcePathFullName}'");
                Logger.LogVerbose($"[MainWindow.SaveSettings] MainConfigInstance.destinationPath: '{MainConfigInstance.destinationPathFullName}'");
                Logger.LogVerbose($"[MainWindow.SaveSettings] MainConfig.TargetGame: '{MainConfig.TargetGame}'");
                Logger.LogVerbose($"[MainWindow.SaveSettings] SpoilerFreeMode: '{SpoilerFreeMode}'");

                // Get theme from TargetGame (they're the same thing)
                string themeToSave = !string.IsNullOrEmpty(MainConfig.TargetGame)
                    ? ThemeManager.GetCurrentStylePath()
                    : "/Styles/FluentLightStyle.axaml";

                Logger.LogVerbose($"[MainWindow.SaveSettings] Theme to save: '{themeToSave}'");

                var settings = AppSettings.FromCurrentState(MainConfigInstance, themeToSave, SpoilerFreeMode);

                Logger.LogVerbose("[MainWindow.SaveSettings] Settings created:");
                Logger.LogVerbose($"[MainWindow.SaveSettings]   settings.SourcePath: '{settings.SourcePath}'");
                Logger.LogVerbose($"[MainWindow.SaveSettings]   settings.DestinationPath: '{settings.DestinationPath}'");
                Logger.LogVerbose($"[MainWindow.SaveSettings]   settings.Theme: '{settings.Theme}'");
                Logger.LogVerbose($"[MainWindow.SaveSettings]   settings.SpoilerFreeMode: '{settings.SpoilerFreeMode}'");

                SettingsManager.SaveSettings(settings);
                Logger.LogVerbose($"[MainWindow.SaveSettings] === Settings saved successfully with theme: {themeToSave} (TargetGame: {MainConfig.TargetGame}) ===");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, customMessage: "Failed to save settings");
            }
        }

        /// <summary>
        /// Refreshes the main window from saved settings, typically called after settings dialog is closed.
        /// </summary>
        public void RefreshFromSettings()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshFromSettings, DispatcherPriority.Normal);
                return;
            }
            try
            {
                Logger.LogVerbose("MainWindow.RefreshFromSettings - reloading settings");

                // Reload settings from disk
                AppSettings settings = SettingsManager.LoadSettings();
                settings.ApplyToMainConfig(MainConfigInstance, out _, out bool spoilerFreeMode);  // theme is not used

                // Apply spoiler-free mode
                SpoilerFreeMode = spoilerFreeMode;

                // Update directory pickers with the loaded settings
                UpdateDirectoryPickersFromSettings(settings);

                // Update path displays
                UpdatePathDisplays();

                // Update HoloPatcher version display
                UpdateHolopatcherVersionDisplay();

                // Update theme button states to reflect current theme
                UpdateThemeButtonStates();

                Logger.LogVerbose("MainWindow.RefreshFromSettings - completed successfully");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to refresh from settings");
            }
        }

        [UsedImplicitly]
        private void ModPathInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ModPathInput_LostFocus(sender, e), DispatcherPriority.Normal);
                return;
            }
            if (sender is TextBox tb)
            {
                if (_suppressPathEvents)
                {
                    return;
                }

                bool applied = TryApplySourcePath(tb.Text);
                if (applied)
                {
                    UpdatePathDisplays();
                    UpdateStepProgress();
                }
                UpdatePathSuggestions(tb, this.FindControl<ComboBox>(name: "ModPathSuggestions"), ref _modSuggestCts);
            }
        }

        [UsedImplicitly]
        private void InstallPathInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => InstallPathInput_LostFocus(sender, e), DispatcherPriority.Normal);
                return;
            }
            if (sender is TextBox tb)
            {
                if (_suppressPathEvents)
                {
                    return;
                }

                bool applied = TryApplyInstallPath(tb.Text);
                if (applied)
                {
                    UpdatePathDisplays();
                    UpdateStepProgress();
                }
                UpdatePathSuggestions(tb, this.FindControl<ComboBox>(name: "InstallPathSuggestions"), ref _installSuggestCts);
            }
        }

        [UsedImplicitly]
        private void ModPathSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ModPathSuggestions_SelectionChanged(sender, e), DispatcherPriority.Normal);
                return;
            }
            if (_suppressComboEvents)
            {
                return;
            }
            if (!(sender is ComboBox comboBox) || !(comboBox.SelectedItem is string path))
            {
                return;
            }

            _suppressPathEvents = true;
            _suppressComboEvents = true;
            TextBox modInput = this.FindControl<TextBox>(name: "ModPathInput");
            if (modInput != null)
            {
                modInput.Text = path;
                if (TryApplySourcePath(path))
                {
                    UpdatePathDisplays();
                    UpdateStepProgress();
                }

            }
            _suppressPathEvents = false;
            _suppressComboEvents = false;
        }

        [UsedImplicitly]
        private void InstallPathSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => InstallPathSuggestions_SelectionChanged(sender, e), DispatcherPriority.Normal);
                return;
            }
            if (_suppressComboEvents)
            {
                return;
            }
            if (!(sender is ComboBox comboBox) || !(comboBox.SelectedItem is string path))
            {
                return;
            }
            _suppressPathEvents = true;
            _suppressComboEvents = true;
            TextBox installInput = this.FindControl<TextBox>(name: "InstallPathInput");
            if (installInput != null)
            {
                installInput.Text = path;
                if (TryApplyInstallPath(path))
                {
                    UpdatePathDisplays();
                    UpdateStepProgress();
                }

            }
            _suppressPathEvents = false;
            _suppressComboEvents = false;
        }

        [UsedImplicitly]
        private bool TryApplySourcePath(string text)
        {

            bool result = _guiPathService.TryApplySourcePath(text);
            if (result)
            {
                _ = GuiPathService.AddToRecentModsAsync(text);
            }
            return result;
        }

        private bool TryApplyInstallPath(string text)
        {
            return _guiPathService.TryApplyDestinationPath(text);
        }

        private static void UpdatePathSuggestions(TextBox input, ComboBox combo, ref CancellationTokenSource cts)
        {
            GuiPathService.UpdatePathSuggestions(input, combo, ref cts);
        }
        [UsedImplicitly]
        private void OnPathInputKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }
                if (!(sender is TextBox tb))
                {
                    return;
                }
                if (_suppressPathEvents)
                {
                    return;
                }
                string name = tb.Name ?? string.Empty;
                bool pathSet = false;
                if (string.Equals(name, "ModPathInput", StringComparison.Ordinal))
                {
                    pathSet = TryApplySourcePath(tb.Text);
                }
                else if (string.Equals(name, "InstallPathInput", StringComparison.Ordinal))
                {
                    pathSet = TryApplyInstallPath(tb.Text);
                }

                if (pathSet)
                {
                    UpdateStepProgress();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void BrowseModDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] result = await _dialogService.ShowFileDialogAsync(isFolderDialog: true, windowName: "Select your mod workspace directory");
                if (!(result?.Length > 0))
                {
                    return;
                }
                TextBox modInput = this.FindControl<TextBox>(name: "ModPathInput");
                if (modInput is null)
                {
                    return;
                }
                modInput.Text = result[0];
                if (!TryApplySourcePath(result[0]))
                {
                    return;
                }
                UpdatePathDisplays();
                await UpdateStepProgressAsync();

                ComboBox modCombo = this.FindControl<ComboBox>(name: "ModPathSuggestions");
                if (modCombo != null)
                {
                    UpdatePathSuggestions(modInput, modCombo, ref _modSuggestCts);
                }
            }
            catch (Exception exc)
            {
                await Logger.LogExceptionAsync(exc, "[BrowseModDir_Click] Error browsing mod directory");
            }
        }

        public static List<ModComponent> ComponentsList => MainConfig.AllComponents;

        public static List<string> GetTierOptions()
        {
            return CategoryTierDefinitions.TierDefinitions.Keys.ToList();
        }
        [CanBeNull]
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (string.Equals(_searchText, value, StringComparison.Ordinal))
                {
                    return;
                }
                _searchText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
            }
        }

        public MainConfig MainConfigInstance { get; set; } = new MainConfig();

        [CanBeNull]
        public ModComponent CurrentComponent
        {
            get => MainConfig.CurrentComponent;
            set
            {

                if (MainConfig.CurrentComponent != null)
                {
                    MainConfig.CurrentComponent.PropertyChanged -= OnCurrentComponentPropertyChanged;
                }

                MainConfig.CurrentComponent = value;
                RaisePropertyChanged(CurrentComponentProperty, null, value);

                if (MainConfig.CurrentComponent != null)
                {
                    MainConfig.CurrentComponent.PropertyChanged += OnCurrentComponentPropertyChanged;
                }
            }
        }

        private bool IgnoreInternalTabChange { get; set; }

        private void OnCurrentComponentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnCurrentComponentPropertyChanged(sender, e), DispatcherPriority.Normal);
                return;
            }
            if (sender is ModComponent component && component == MainConfig.CurrentComponent)
            {
                // Notify that CurrentComponent "changed" to trigger binding refresh in tabs
                // Even though the object reference is the same, this forces nested bindings to re-evaluate
                RaisePropertyChanged(CurrentComponentProperty, component, component);

                RefreshComponentValidationState(component);
            }
        }

        private void InitializeTopMenu()
        {
            var menu = new Menu();
            var fileMenu = new MenuItem { Header = "File" };

            var saveMenuItem = new MenuItem
            {
                Header = "Save",
                IsVisible = EditorMode,
                Command = ReactiveCommand.Create(() => SaveModFileAs_ClickWithFormat(new object(), new RoutedEventArgs())),
            };

            var fileItems = new List<MenuItem>
        {
            new MenuItem
            {
                Header = "Open File",
                Command = ReactiveCommand.Create( () => LoadFile_Click(new object(), new RoutedEventArgs()) ),
            },
            new MenuItem
            {
                Header = "Close",
                Command = ReactiveCommand.Create( () => CloseTOMLFile_Click(new object(), new RoutedEventArgs()) ),
                IsVisible = EditorMode,
            },
            saveMenuItem,
            new MenuItem
            {
                Header = "Exit",
                Command = ReactiveCommand.Create( () => CloseButton_Click(new object(), new RoutedEventArgs()) ),
            },
        };
            fileMenu.ItemsSource = fileItems;

            var toolsMenu = new MenuItem { Header = "Tools" };

            // Editor Mode checkbox menu item
            var editorModeMenuItem = new MenuItem
            {
                Header = EditorMode ? "✓ Editor Mode" : "Editor Mode",
            };
            editorModeMenuItem.Click += (s, e) =>
            {
                EditorMode = !EditorMode;
            };

            // Subscribe to EditorMode changes to keep menu item in sync
            PropertyChanged += (s, e) =>
            {
                if (string.Equals(e.PropertyName, nameof(EditorMode), StringComparison.Ordinal))
                {
                    editorModeMenuItem.Header = EditorMode ? "✓ Editor Mode" : "Editor Mode";
                }
            };

            var toolItems = new List<MenuItem>
            {
                editorModeMenuItem,
                new MenuItem { Header = "-" }, // Separator
                new MenuItem
                {
                    Header = "Fix iOS case sensitivity.",
                    Command = ReactiveCommand.Create( () => FixIosCaseSensitivityClick(new object(), new RoutedEventArgs()) ),
                },
                new MenuItem
                {
                    Header = "Fix file/folder permissions.",
                    Command = ReactiveCommand.Create( () => FixPathPermissionsClick(new object(), new RoutedEventArgs()) ),
                },
                new MenuItem
                {
                    Header = "Manage Checkpoints",
                    Command = ReactiveCommand.Create( () => OpenCheckpointManagement_Click(new object(), new RoutedEventArgs()) ),
                },
                new MenuItem
                {
                    Header = "Run HoloPatcher",
                    Command = ReactiveCommand.Create( () => RunHolopatcherButton_Click(new object(), new RoutedEventArgs()) ),
                },
                new MenuItem
                {
                    Header = "Settings",
                    Command = ReactiveCommand.Create( () => OpenSettings_Click(new object(), new RoutedEventArgs()) ),
                },
                new MenuItem
                {
                    Header = "Open Output Window",
                    Command = ReactiveCommand.Create( () => OpenOutputWindow_Click(new object(), new RoutedEventArgs()) ),
                },
            };
            ToolTip.SetTip(
                editorModeMenuItem,
                value:
                "Toggle to enable Editor Mode: exposes Raw/Editor tabs, editing buttons, and creation tools. When off, the UI is simplified for end users installing mods."
            );
            ToolTip.SetTip(
                toolItems[2],
                value:
                "Lowercase all files/folders recursively at the given path. Necessary for iOS installs."
            );
            ToolTip.SetTip(
                toolItems[3],
                value:
                "Fixes various file/folder permissions. On Unix, this will also find case-insensitive duplicate file/folder names."
            );
            if (UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
            {
                var filePermFixTool = new MenuItem
                {
                    Header = "Fix file and folder permissions",
                    Command = ReactiveCommand.Create(() => ResolveDuplicateFilesAndFolders(new object(), new RoutedEventArgs())),
                };
                ToolTip.SetTip(
                    filePermFixTool,
                    "(Linux/Mac only) This will acquire a list of any case-insensitive duplicates in the mod workspace or"
                    + " the kotor directory, including subfolders, and resolve them."
                );
                toolItems.Add(filePermFixTool);
            }
            toolsMenu.ItemsSource = toolItems;

            var helpMenu = new MenuItem { Header = "Help" };
            var deadlystreamMenu = new MenuItem
            {
                Header = "DeadlyStream",
                ItemsSource = new[]
                {
                    new MenuItem
                    {
                        Header = "Discord",
                        Command = ReactiveCommand.Create(() => UrlUtilities.OpenUrl("https://discord.gg/nDkHXfc36s")),
                    },
                    new MenuItem
                    {
                        Header = "Website",
                        Command = ReactiveCommand.Create(() => UrlUtilities.OpenUrl("https://deadlystream.com")),
                    },
                },
            };
            var neocitiesMenu = new MenuItem
            {
                Header = "KOTOR Community Portal",
                ItemsSource = new[]
                {
                    new MenuItem
                    {
                        Header = "Discord",
                        Command = ReactiveCommand.Create(() => UrlUtilities.OpenUrl("https://discord.com/invite/kotor")),
                    },
                    new MenuItem
                    {
                        Header = "Website",
                        Command = ReactiveCommand.Create(() => UrlUtilities.OpenUrl("https://kotor.neocities.org")),
                    },
                },
            };
            var pcgamingwikiMenu = new MenuItem
            {
                Header = "PCGamingWiki",
                ItemsSource = new[]
                {
                    new MenuItem
                    {
                        Header = "KOTOR 1",
                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl( "https://www.pcgamingwiki.com/wiki/Star_Wars:_Knights_of_the_Old_Republic" ) ),
                    },
                    new MenuItem
                    {
                        Header = "KOTOR 2: TSL",
                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl( "https://www.pcgamingwiki.com/wiki/Star_Wars:_Knights_of_the_Old_Republic_II_-_The_Sith_Lords" ) ),
                    },
                },
            };
            helpMenu.ItemsSource = new[] { deadlystreamMenu, neocitiesMenu, pcgamingwikiMenu };

            var engineRewritesMenu = new MenuItem
            {
                Header = "Open-Source Odyssey/Aurora Engines",
                ItemsSource = new[]
                {
                    new MenuItem
                    {
                        Header = "KotOR.js",
                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/KobaltBlu/KotOR.js") ),
                    },
                    new MenuItem
                    {
                        Header = "NorthernLights",
                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/lachjames/NorthernLights") ),
                    },
                    new MenuItem
                    {
                        Header = "reone",
                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/seedhartha/reone") ),
                    },
                },
            };
            var otherProjectsMenu = new MenuItem
            {
                Header = "Other Projects",
                ItemsSource = new[]
                {
                    new MenuItem
                    {
                        Header = "PyKotor Library",
                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/NickHugi/PyKotor") ),
                        ItemsSource = new []
                        {
                            new MenuItem
                            {
                                Header = "HoloPatcher",
                                ItemsSource = new []
                                {
                                    new MenuItem
                                    {
                                        Header = "DeadlyStream",
                                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://deadlystream.com/files/file/2243-holopatcher") ),
                                    },
                                    new MenuItem
                                    {
                                        Header = "GitHub",
                                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/NickHugi/PyKotor") ),
                                    },
                                },
                            },
                            new MenuItem
                            {
                                Header = "Holocron Toolset",
                                ItemsSource = new []
                                {
                                    new MenuItem
                                    {
                                        Header = "GitHub",
                                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/NickHugi/PyKotor/blob/master/Tools/HolocronToolset") ),
                                    },
                                    new MenuItem
                                    {
                                        Header = "DeadlyStream",
                                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://deadlystream.com/files/file/1982-holocron-toolset") ),
                                    },
                                    new MenuItem
                                    {
                                        Header = "Discord",
                                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://discord.gg/hfAqtkVEzQ") ),
                                    },
                                },
                            },
                            new MenuItem
                            {
                                Header = "Auto-Translate / Font Creator",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://deadlystream.com/files/file/2375-kotor-autotranslate-tool") ),
                            },
                            new MenuItem
                            {
                                Header = "KotorDiff",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://deadlystream.com/files/file/2364-kotordiff") ),
                            },
                        },
                    },
                    new MenuItem
                    {
                        Header = "LIP Composer / reone toolkit",
                        ItemsSource = new []
                        {
                            new MenuItem
                            {
                                Header = "DeadlyStream",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://deadlystream.com/files/file/1862-reone-toolkit") ),
                            },
                            new MenuItem
                            {
                                Header = "GitHub",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/seedhartha/reone/wiki/Tooling") ),
                            },
                        },
                    },
                    engineRewritesMenu,
                },
            };
            var aboutMenu = new MenuItem
            {
                Header = "About",
                ItemsSource = new[]
                {
                    new MenuItem
                    {
                        Header = "The ModSync Project",
                        ItemsSource = new []
                        {
                            new MenuItem
                            {
                                Header = "DeadlyStream",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://deadlystream.com/files/file/2317-kotormodsync/") ),
                            },
                            new MenuItem
                            {
                                Header = "GitHub",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/th3w1zard1/KOTORModSync") ),
                            },
                        },
                    },
                    new MenuItem
                    {
                        Header = "HoloPatcher",
                        ItemsSource = new []
                        {
                            new MenuItem
                            {
                                Header = "DeadlyStream",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://deadlystream.com/files/file/2243-holopatcher") ),
                            },
                            new MenuItem
                            {
                                Header = "GitHub",
                                Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl("https://github.com/NickHugi/PyKotor") ),
                            },
                        },
                    },
                },
            };
            var moreMenu = new MenuItem
            {
                Header = "More",
                ItemsSource = new[]
                {
                    otherProjectsMenu,
                    new MenuItem
                    {
                        Header = "Modding Tools",
                        Command = ReactiveCommand.Create( () => UrlUtilities.OpenUrl(url: "https://deadlystream.github.io/ds-kotor-modding-wiki/en/#!pages/tools_overview.md") ),
                    },
                },
            };
            menu.ItemsSource = new[] { fileMenu, toolsMenu, helpMenu, aboutMenu, moreMenu };
            Menu topMenu = this.FindControl<Menu>(name: "TopMenu");
            if (topMenu is null)
            {
                return;
            }

            topMenu.ItemsSource = menu.Items;
        }

        private void UpdateMenuVisibility()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(UpdateMenuVisibility, DispatcherPriority.Normal);
                return;
            }
            Menu topMenu = this.FindControl<Menu>(name: "TopMenu");
            if (topMenu is null)
            {
                return;
            }

            if (topMenu.Items[0] is MenuItem fileMenu && fileMenu.Items is IList fileItems)
            {

                if (fileItems.Count > 1 && fileItems[1] is MenuItem closeItem)
                {
                    closeItem.IsVisible = EditorMode;
                }

                if (fileItems.Count > 2 && fileItems[2] is MenuItem saveItem)
                {
                    saveItem.IsVisible = EditorMode;
                }
            }

            if (
                topMenu.Items[1] is MenuItem toolsMenu
                && toolsMenu.Items is IList toolItems
                && toolItems.Count > 0
                && toolItems[0] is MenuItem docsItem
            )
            {
                docsItem.IsVisible = EditorMode;
            }
        }

        private void RefreshSingleComponentVisuals(ModComponent component)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RefreshSingleComponentVisuals(component), DispatcherPriority.Normal);
                return;
            }
            ModListService.RefreshSingleComponentVisuals(ModListBox, component);
        }

        private void RefreshModListItems()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshModListItems, DispatcherPriority.Normal);
                return;
            }
            try
            {
                _modListService.RefreshModListItems(ModListBox, EditorMode, BuildContextMenuForComponent);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        private void DragOver(object sender, DragEventArgs e)
        {
            Logger.LogVerbose($"[DragOver] ENTER - ContainsFiles: {e.Data.Contains(DataFormats.Files)}, _dragOverlayVisible: {_dragOverlayVisible}, _pendingDragOverlayUpdate: {_pendingDragOverlayUpdate}, _currentDragFilePath: '{_currentDragFilePath}'");

            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;

                // Get the current file path
                string currentFilePath = null;
                try
                {
                    if (e.Data.Get(DataFormats.Files) is IEnumerable<IStorageItem> items)
                    {
                        IStorageItem storageItem = items.FirstOrDefault();
                        currentFilePath = storageItem?.TryGetLocalPath();
                        Logger.LogVerbose($"[DragOver] Got file path: '{currentFilePath}'");
                    }
                    else
                    {
                        Logger.LogVerbose("[DragOver] Failed to get items from DataFormats.Files");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogVerbose($"[DragOver] Exception getting file path: {ex.Message}");
                    // If we can't get path but overlay is already visible, keep it visible
                    if (_dragOverlayVisible)
                    {
                        Logger.LogVerbose("[DragOver] Can't get path but overlay visible, returning early");
                        e.Handled = true;
                        return;
                    }
                    Logger.LogVerbose("[DragOver] Can't get path and overlay not visible, returning");
                    e.Handled = true;
                    return;
                }

                if (string.IsNullOrEmpty(currentFilePath))
                {
                    Logger.LogVerbose($"[DragOver] File path is null/empty, _dragOverlayVisible: {_dragOverlayVisible}");
                    // Can't get path, but already showing something - keep it visible
                    if (_dragOverlayVisible)
                    {
                        Logger.LogVerbose("[DragOver] Path empty but overlay visible, returning early");
                        e.Handled = true;
                        return;
                    }
                    // Can't get path and nothing showing - can't show overlay
                    Logger.LogVerbose("[DragOver] Path empty and overlay not visible, returning");
                    e.Handled = true;
                    return;
                }

                // Check if overlay is already visible for the same file - early return to prevent flickering
                // Always check UI state on UI thread to prevent race conditions, like ShowLoadingOverlay does
                if (_dragOverlayVisible && string.Equals(currentFilePath, _currentDragFilePath, StringComparison.Ordinal))
                {
                    Logger.LogVerbose($"[DragOver] Overlay visible and same file ('{currentFilePath}'), checking UI state on UI thread");
                    // Cancel any pending updates since we might already be showing the correct file
                    _dragOverlayDebounceTimer?.Stop();
                    _pendingDragOverlayUpdate = false;

                    // Always check actual UI state on UI thread for consistency
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            Border overlay = this.FindControl<Border>("DragDropOverlay");
                            bool overlayIsVisible = overlay != null && overlay.IsVisible;
                            Logger.LogVerbose($"[DragOver] Overlay.IsVisible: {overlayIsVisible}");
                            if (!overlayIsVisible)
                            {
                                // UI says not visible, but flag says visible - update flag and allow update
                                Logger.LogVerbose("[DragOver] UI says not visible, allowing update");
                                _dragOverlayVisible = false;
                            }
                            else
                            {
                                Logger.LogVerbose("[DragOver] Overlay already visible for same file, no update needed");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException(ex, "[DragOver] Error checking overlay state");
                        }
                    });

                    // Return early to prevent scheduling another update
                    // The UI thread check above will fix the flag if needed
                    Logger.LogVerbose("[DragOver] Overlay flag says visible for same file, cancelling debounce and returning early");
                    e.Handled = true;
                    return;
                }

                // File changed or overlay not visible - schedule update with debounce
                bool fileChanged = !string.Equals(currentFilePath, _currentDragFilePath, StringComparison.Ordinal);
                Logger.LogVerbose($"[DragOver] File changed: {fileChanged} (old: '{_currentDragFilePath}', new: '{currentFilePath}'), overlay visible: {_dragOverlayVisible}, scheduling debounced update");

                _currentDragFilePath = currentFilePath; // Set immediately so subsequent checks work
                _pendingDragEventArgs = e;
                _pendingDragOverlayUpdate = true;

                // Cancel existing timer if running
                bool timerWasRunning = _dragOverlayDebounceTimer?.IsEnabled ?? false;
                _dragOverlayDebounceTimer?.Stop();
                Logger.LogVerbose($"[DragOver] Timer was running: {timerWasRunning}, stopped it");

                // Create timer if it doesn't exist
                if (_dragOverlayDebounceTimer == null)
                {
                    Logger.LogVerbose("[DragOver] Creating new debounce timer");
                    _dragOverlayDebounceTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(50), // 50ms debounce delay
                    };
                    _dragOverlayDebounceTimer.Tick += (s, args) =>
                    {
                        Logger.LogVerbose($"[DragOver.Timer] Timer tick fired, _pendingDragOverlayUpdate: {_pendingDragOverlayUpdate}, _pendingDragEventArgs is null: {_pendingDragEventArgs == null}");
                        _dragOverlayDebounceTimer.Stop();
                        if (_pendingDragOverlayUpdate && _pendingDragEventArgs != null)
                        {
                            // Verify the overlay isn't already showing the correct file before updating
                            // Always check on UI thread for consistency
                            bool shouldUpdate = true;
                            try
                            {
                                if (_pendingDragEventArgs.Data.Get(DataFormats.Files) is IEnumerable<IStorageItem> items)
                                {
                                    IStorageItem storageItem = items.FirstOrDefault();
                                    string pendingFilePath = storageItem?.TryGetLocalPath();
                                    if (_dragOverlayVisible && string.Equals(pendingFilePath, _currentDragFilePath, StringComparison.Ordinal))
                                    {
                                        // Always check actual UI state on UI thread
                                        Dispatcher.UIThread.Post(() =>
                                        {
                                            try
                                            {
                                                Border overlay = this.FindControl<Border>("DragDropOverlay");
                                                if (overlay != null && overlay.IsVisible)
                                                {
                                                    // Overlay is already showing the correct file, update is not needed
                                                    Logger.LogVerbose("[DragOver.Timer] Overlay already showing correct file, update not needed");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Logger.LogException(ex, "[DragOver.Timer] Error checking overlay state");
                                            }
                                        });

                                        // Trust the flag TODO - STUB: For now, but the UI thread check will verify
                                        shouldUpdate = false;
                                        Logger.LogVerbose("[DragOver.Timer] Flag says overlay visible for same file, skipping update");
                                    }
                                }
                            }
                            catch
                            {
                                // If we can't check, proceed with update
                            }

                            if (shouldUpdate)
                            {
                                Logger.LogVerbose("[DragOver.Timer] Calling ShowDragDropOverlay");
                                _pendingDragOverlayUpdate = false;
                                ShowDragDropOverlay(_pendingDragEventArgs);
                            }
                            else
                            {
                                _pendingDragOverlayUpdate = false;
                            }
                            _pendingDragEventArgs = null;
                        }
                        else
                        {
                            Logger.LogVerbose("[DragOver.Timer] Skipping ShowDragDropOverlay - update cancelled or no event args");
                        }
                    };
                }

                // Start/reset the timer
                _dragOverlayDebounceTimer.Start();
                Logger.LogVerbose("[DragOver] Started debounce timer");
            }
            else
            {
                Logger.LogVerbose("[DragOver] No files in data, hiding overlay if visible");
                e.DragEffects = DragDropEffects.None;
                // Cancel any pending overlay updates
                _dragOverlayDebounceTimer?.Stop();
                _pendingDragOverlayUpdate = false;
                _pendingDragEventArgs = null;
                if (_dragOverlayVisible)
                {
                    Logger.LogVerbose("[DragOver] Calling HideDragDropOverlay");
                    HideDragDropOverlay();
                }
            }
            e.Handled = true;
            Logger.LogVerbose("[DragOver] EXIT");
        }

        private void DragLeave(object sender, DragEventArgs e)
        {
            Logger.LogVerbose($"[DragLeave] ENTER - _dragOverlayVisible: {_dragOverlayVisible}, _pendingDragOverlayUpdate: {_pendingDragOverlayUpdate}, _currentDragFilePath: '{_currentDragFilePath}'");
            _currentDragFilePath = null;
            // Cancel any pending overlay updates
            bool timerWasRunning = _dragOverlayDebounceTimer?.IsEnabled ?? false;
            _dragOverlayDebounceTimer?.Stop();
            Logger.LogVerbose($"[DragLeave] Timer was running: {timerWasRunning}, stopped it");
            _pendingDragOverlayUpdate = false;
            _pendingDragEventArgs = null;
            Logger.LogVerbose("[DragLeave] Calling HideDragDropOverlay");
            HideDragDropOverlay();
            Logger.LogVerbose("[DragLeave] EXIT");
        }
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void Drop(object sender, DragEventArgs e)
        {
            await Logger.LogVerboseAsync($"[Drop] ENTER - _dragOverlayVisible: {_dragOverlayVisible}, _pendingDragOverlayUpdate: {_pendingDragOverlayUpdate}, _currentDragFilePath: '{_currentDragFilePath}'");
            try
            {
                // Cancel any pending overlay updates
                bool timerWasRunning = _dragOverlayDebounceTimer?.IsEnabled ?? false;
                _dragOverlayDebounceTimer?.Stop();
                await Logger.LogVerboseAsync($"[Drop] Timer was running: {timerWasRunning}, stopped it");
                _pendingDragOverlayUpdate = false;
                _pendingDragEventArgs = null;

                if (!e.Data.Contains(DataFormats.Files))
                {
                    await Logger.LogVerboseAsync("[Drop] No files dropped");
                    return;
                }

                if (!(e.Data.Get(DataFormats.Files) is IEnumerable<IStorageItem> items))
                {
                    await Logger.LogVerboseAsync("[Drop] Dropped items were not IStorageItem enumerable");
                    return;
                }

                IStorageItem storageItem = items.FirstOrDefault();
                string filePath = storageItem?.TryGetLocalPath();
                await Logger.LogVerboseAsync($"[Drop] Got file path: '{filePath}'");
                if (string.IsNullOrEmpty(filePath))
                {
                    await Logger.LogVerboseAsync("[Drop] Dropped item had no path");
                    HideDragDropOverlay();
                    return;
                }
                _currentDragFilePath = null;
                await Logger.LogVerboseAsync("[Drop] Calling HideDragDropOverlay");
                HideDragDropOverlay();
                string fileExt = Path.GetExtension(filePath);
                switch (storageItem)
                {

                    case IStorageFile _ when fileExt.Equals(value: ".toml", StringComparison.OrdinalIgnoreCase)
                                             || fileExt.Equals(value: ".tml", StringComparison.OrdinalIgnoreCase)
                                             || fileExt.Equals(value: ".json", StringComparison.OrdinalIgnoreCase)
                                             || fileExt.Equals(value: ".yaml", StringComparison.OrdinalIgnoreCase)
                                             || fileExt.Equals(value: ".yml", StringComparison.OrdinalIgnoreCase)
                                             || fileExt.Equals(value: ".xml", StringComparison.OrdinalIgnoreCase):
                        {

                            _ = await LoadInstructionFileAsync(filePath, fileType: "config file");
                            break;
                        }
                    case IStorageFile _ when EditorMode && ArchiveHelper.IsArchive(filePath):
                        {
                            // EditorMode: Handle archive drops - create/find mod and generate instructions
                            await HandleArchiveOrFolderDrop(filePath, isFolder: false);
                            break;
                        }
                    case IStorageFolder _ when EditorMode:
                        {
                            // EditorMode: Handle folder drops - create/find mod and generate instructions
                            await HandleArchiveOrFolderDrop(filePath, isFolder: true);
                            break;
                        }
                    case IStorageFile _:
                        (IArchive archive, FileStream archiveStream) = ArchiveHelper.OpenArchive(filePath);
                        if (archive is null || archiveStream is null)
                        {
                            await Logger.LogVerboseAsync("Dropped item was not an archive");
                            return;
                        }
                        string exePath = ArchiveHelper.AnalyzeArchiveForExe(archiveStream, archive);
                        await Logger.LogVerboseAsync(exePath);
                        break;
                    case IStorageFolder _:
                        await Logger.LogVerboseAsync("Dropped item was a folder, not supported (Editor Mode required)");
                        break;
                    default:
                        await Logger.LogVerboseAsync("Dropped item was not a valid file or folder");
                        break;
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex);
            }
            finally
            {
                HideDragDropOverlay();
            }
        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private void ShowDragDropOverlay(DragEventArgs e)
        {
            Logger.LogVerbose($"[ShowDragDropOverlay] ENTER - _dragOverlayVisible: {_dragOverlayVisible}, _currentDragFilePath: '{_currentDragFilePath}'");
            try
            {
                if (!(e.Data.Get(DataFormats.Files) is IEnumerable<IStorageItem> items))
                {
                    Logger.LogVerbose("[ShowDragDropOverlay] Failed to get items from DataFormats.Files");
                    if (_dragOverlayVisible)
                    {
                        Logger.LogVerbose("[ShowDragDropOverlay] Calling HideDragDropOverlay (no items)");
                        HideDragDropOverlay();
                    }
                    return;
                }

                IStorageItem storageItem = items.FirstOrDefault();
                string filePath = storageItem?.TryGetLocalPath();
                Logger.LogVerbose($"[ShowDragDropOverlay] Got file path: '{filePath}'");

                if (string.IsNullOrEmpty(filePath))
                {
                    Logger.LogVerbose("[ShowDragDropOverlay] File path is null/empty");
                    if (_dragOverlayVisible)
                    {
                        Logger.LogVerbose("[ShowDragDropOverlay] Calling HideDragDropOverlay (empty path)");
                        HideDragDropOverlay();
                    }
                    return;
                }

                _currentDragFilePath = filePath;

                // Always use Dispatcher.UIThread.Post for consistency, like ShowLoadingOverlay does
                // This prevents race conditions from rapid DragOver events
                // All checks and updates happen in the posted callback to ensure atomic operations
                Logger.LogVerbose("[ShowDragDropOverlay] Posting UpdateDragDropOverlayUI to UI thread");
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        Logger.LogVerbose("[ShowDragDropOverlay.Post] Posted UpdateDragDropOverlayUI callback executing");
                        UpdateDragDropOverlayUI(filePath, storageItem);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "[ShowDragDropOverlay.Post] Error in UpdateDragDropOverlayUI");
                        _dragOverlayVisible = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[ShowDragDropOverlay] Error in ShowDragDropOverlay");
                _dragOverlayVisible = false;
            }
            Logger.LogVerbose("[ShowDragDropOverlay] EXIT");
        }

        private void UpdateDragDropOverlayUI(string filePath, IStorageItem storageItem)
        {
            Logger.LogVerbose($"[UpdateDragDropOverlayUI] ENTER - filePath: '{filePath}', _dragOverlayVisible: {_dragOverlayVisible}, _currentDragFilePath: '{_currentDragFilePath}'");
            try
            {
                Border overlay = this.FindControl<Border>("DragDropOverlay");
                TextBlock titleBlock = this.FindControl<TextBlock>("DragDropTitle");
                TextBlock fileNameBlock = this.FindControl<TextBlock>("DragDropFileName");
                TextBlock fileInfoBlock = this.FindControl<TextBlock>("DragDropFileInfo");
                TextBlock actionTextBlock = this.FindControl<TextBlock>("DragDropActionText");
                Border actionBorder = this.FindControl<Border>("DragDropActionBorder");

                bool controlsFound = overlay != null && titleBlock != null && fileNameBlock != null &&
                                   fileInfoBlock != null && actionTextBlock != null && actionBorder != null;
                Logger.LogVerbose($"[UpdateDragDropOverlayUI] Controls found: {controlsFound} (overlay: {overlay != null}, title: {titleBlock != null}, fileName: {fileNameBlock != null}, fileInfo: {fileInfoBlock != null}, actionText: {actionTextBlock != null}, actionBorder: {actionBorder != null})");

                if (!controlsFound)
                {
                    Logger.LogVerbose("[UpdateDragDropOverlayUI] Missing controls, setting _dragOverlayVisible = false and returning");
                    _dragOverlayVisible = false;
                    return;
                }

                bool overlayIsVisible = overlay.IsVisible;
                bool isSameFile = string.Equals(filePath, _currentDragFilePath, StringComparison.Ordinal);
                Logger.LogVerbose($"[UpdateDragDropOverlayUI] overlay.IsVisible: {overlayIsVisible}, isSameFile: {isSameFile}");

                // Early return if overlay is already visible for the same file - prevents flickering
                if (overlayIsVisible && isSameFile)
                {
                    // Already showing the correct file, no update needed
                    Logger.LogVerbose("[UpdateDragDropOverlayUI] Overlay already visible for same file, returning early");
                    return;
                }

                Logger.LogVerbose($"[UpdateDragDropOverlayUI] Proceeding to update overlay (was visible: {overlayIsVisible}, will set visible: true)");

                string fileName = Path.GetFileName(filePath);
                string fileExt = Path.GetExtension(filePath).ToLowerInvariant();
                long fileSize = 0;
                string fileSizeText = "Unknown size";

                if (storageItem is IStorageFile && File.Exists(filePath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        fileSize = fileInfo.Length;
                        fileSizeText = FormatFileSize(fileSize);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "Error getting file info");
                    }
                }

                // Determine file type and action
                string actionDescription = "";
                IBrush statusBgBrush = ThemeResourceHelper.DragDropErrorBackgroundBrush;
                IBrush statusBorderBrush = ThemeResourceHelper.DragDropErrorBorderBrush;

                if (storageItem is IStorageFolder)
                {
                    if (EditorMode)
                    {
                        actionDescription = "📁 This folder will create a new ModComponent (or update an existing matching one) and auto-generate instructions from its contents.\n\nA Copy instruction will be created to copy the folder files to the game directory.";
                        titleBlock.Text = "📁 Create Mod From Folder";
                        statusBgBrush = ThemeResourceHelper.DragDropSuccessBackgroundBrush;
                        statusBorderBrush = ThemeResourceHelper.DragDropSuccessBorderBrush;
                    }
                    else
                    {
                        actionDescription = "📁 Folders are only supported in Editor Mode. Please enable Editor Mode or drop a file instead.";
                        titleBlock.Text = "❌ Folders Require Editor Mode";
                    }
                }
                else if (string.IsNullOrEmpty(fileExt))
                {
                    actionDescription = "⚠️ Unknown file type. Only configuration files (.toml, .json, .yaml) or archives (.zip, .rar, .7z) are supported.";
                    titleBlock.Text = "❓ Unknown File Type";
                }
                else if (fileExt.Equals(".toml", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".tml", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
                {
                    string formatType = "Configuration file";
                    if (fileExt.Equals(".toml", StringComparison.OrdinalIgnoreCase) || fileExt.Equals(".tml", StringComparison.OrdinalIgnoreCase))
                    {
                        formatType = "TOML configuration";
                    }
                    else if (fileExt.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        formatType = "JSON configuration";
                    }
                    else if (fileExt.Equals(".yaml", StringComparison.OrdinalIgnoreCase) || fileExt.Equals(".yml", StringComparison.OrdinalIgnoreCase))
                    {
                        formatType = "YAML configuration";
                    }
                    else if (fileExt.Equals(".md", StringComparison.OrdinalIgnoreCase) || fileExt.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
                    {
                        formatType = "Markdown documentation";
                    }

                    actionDescription = $"✅ This {formatType} will be loaded into KOTORModSync.\n\nAll mod components in the file will replace your current setup.";
                    titleBlock.Text = "📄 Load Configuration File";
                    statusBgBrush = ThemeResourceHelper.DragDropSuccessBackgroundBrush;
                    statusBorderBrush = ThemeResourceHelper.DragDropSuccessBorderBrush;
                }
                else if (fileExt.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".tar", StringComparison.OrdinalIgnoreCase) ||
                         fileExt.Equals(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    if (EditorMode)
                    {
                        actionDescription = $"📦 This archive will create a new ModComponent (or update an existing matching one) and auto-generate instructions from its contents.\n\nThe archive structure will be analyzed to create installation instructions automatically.";
                        titleBlock.Text = "📦 Create Mod From Archive";
                        statusBgBrush = ThemeResourceHelper.DragDropSuccessBackgroundBrush;
                        statusBorderBrush = ThemeResourceHelper.DragDropSuccessBorderBrush;
                    }
                    else
                    {
                        actionDescription = $"📦 This archive file will be analyzed to detect if it contains an executable installer.\n\nThe archive structure will be examined for mod installation files.";
                        titleBlock.Text = "📦 Analyze Archive";
                        statusBgBrush = ThemeResourceHelper.DragDropInfoBackgroundBrush;
                        statusBorderBrush = ThemeResourceHelper.DragDropInfoBorderBrush;
                    }
                }
                else
                {
                    actionDescription = $"⚠️ File type '{fileExt}' is not supported.\n\nSupported types: Configuration files (.toml, .json, .yaml) or archives (.zip, .rar, .7z).";
                    titleBlock.Text = "❌ File Type Not Supported";
                    statusBgBrush = ThemeResourceHelper.DragDropErrorBackgroundBrush;
                    statusBorderBrush = ThemeResourceHelper.DragDropErrorBorderBrush;
                }

                fileNameBlock.Text = fileName;
                fileInfoBlock.Text = $"Size: {fileSizeText}\nType: {fileExt.ToUpperInvariant().TrimStart('.')} file";
                actionTextBlock.Text = actionDescription;

                actionBorder.Background = statusBgBrush;
                actionBorder.BorderBrush = statusBorderBrush;

                // Only update visibility if not already visible to prevent flickering
                bool wasVisible = overlay.IsVisible;
                if (!wasVisible)
                {
                    Logger.LogVerbose("[UpdateDragDropOverlayUI] Setting overlay.IsVisible = true");
                    overlay.IsVisible = true;
                }
                else
                {
                    Logger.LogVerbose("[UpdateDragDropOverlayUI] Overlay already visible, skipping visibility change");
                }

                _dragOverlayVisible = true;
                Logger.LogVerbose($"[UpdateDragDropOverlayUI] Set _dragOverlayVisible = true, overlay.IsVisible is now: {overlay.IsVisible}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[UpdateDragDropOverlayUI] Error showing drag-drop overlay");
                _dragOverlayVisible = false;
            }
            Logger.LogVerbose("[UpdateDragDropOverlayUI] EXIT");
        }

        private void HideDragDropOverlay()
        {
            Logger.LogVerbose($"[HideDragDropOverlay] ENTER - _dragOverlayVisible: {_dragOverlayVisible}, _currentDragFilePath: '{_currentDragFilePath}'");
            _currentDragFilePath = null;
            _dragOverlayVisible = false;

            // Always use Dispatcher.UIThread.Post for consistency, like HideLoadingOverlay does
            // This prevents race conditions from rapid drag events
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Border overlay = this.FindControl<Border>("DragDropOverlay");
                    bool overlayWasVisible = overlay != null && overlay.IsVisible;
                    Logger.LogVerbose($"[HideDragDropOverlay] Overlay found: {overlay != null}, was visible: {overlayWasVisible}");
                    if (overlayWasVisible)
                    {
                        Logger.LogVerbose("[HideDragDropOverlay] Setting overlay.IsVisible = false");
                        overlay.IsVisible = false;
                    }
                    else
                    {
                        Logger.LogVerbose("[HideDragDropOverlay] Overlay not visible, skipping visibility change");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "[HideDragDropOverlay] Error hiding drag-drop overlay");
                }
            });
            Logger.LogVerbose("[HideDragDropOverlay] EXIT");
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private async Task HandleArchiveOrFolderDrop(string itemPath, bool isFolder)
        {
            try
            {
                if (!EditorMode)
                {
                    await Logger.LogVerboseAsync("[HandleArchiveOrFolderDrop] Editor Mode required for archive/folder drops");
                    return;
                }

                // Show loading overlay
                string itemName = Path.GetFileName(itemPath);
                ShowLoadingOverlay($"Processing {itemName}...");

                try
                {
                    // Extract a name for the mod from the folder/archive name
                    string modName = isFolder
                        ? Path.GetFileName(itemPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        : Path.GetFileNameWithoutExtension(itemPath);

                    if (string.IsNullOrWhiteSpace(modName))
                    {
                        modName = $"Dropped {(isFolder ? "Folder" : "Archive")} {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    }

                    // Find existing mod components that fuzzily match by name
                    ModComponent existingComponent = MainConfig.AllComponents
                        .Where(c => FuzzyMatcher.FuzzyMatch(c.Name, c.Author, modName, "Unknown Author"))
                        .OrderByDescending(c => FuzzyMatcher.GetMatchScore(c.Name, c.Author, modName, "Unknown Author"))
                        .FirstOrDefault();

                    ModComponent targetComponent = existingComponent;

                    if (targetComponent == null)
                    {
                        // Create new mod component
                        await Logger.LogAsync($"Creating new mod component for dropped {(isFolder ? "folder" : "archive")}: {modName}");
                        targetComponent = ModManagementService.CreateMod(
                            name: modName,
                            author: "Unknown Author",
                            category: "Uncategorized"
                        );
                    }
                    else
                    {
                        await Logger.LogAsync($"Found existing mod component matching '{modName}': '{targetComponent.Name}' (GUID: {targetComponent.Guid})");
                    }

                    // Generate instructions
                    bool instructionsGenerated = false;
                    if (isFolder)
                    {
                        // For folders, create a Copy instruction
                        if (targetComponent.Instructions.Count == 0)
                        {
                            instructionsGenerated = await GenerateInstructionsFromFolder(targetComponent, itemPath);
                        }
                        else
                        {
                            await Logger.LogWarningAsync($"Mod '{targetComponent.Name}' already has instructions. Skipping auto-generation.");
                        }
                    }
                    else
                    {
                        // For archives, use AutoInstructionGenerator
                        if (targetComponent.Instructions.Count == 0)
                        {
                            instructionsGenerated = Core.Services.AutoInstructionGenerator.GenerateInstructions(targetComponent, itemPath);
                            if (instructionsGenerated)
                            {
                                targetComponent.IsDownloaded = true;
                                await Logger.LogAsync($"Generated {targetComponent.Instructions.Count} instruction(s) from archive '{itemPath}'");
                            }
                            else
                            {
                                await Logger.LogWarningAsync($"Failed to generate instructions from archive '{itemPath}'");
                            }
                        }
                        else
                        {
                            await Logger.LogWarningAsync($"Mod '{targetComponent.Name}' already has instructions. Skipping auto-generation.");
                        }
                    }

                    // Refresh UI
                    await ProcessComponentsAsync(MainConfig.AllComponents);

                    // Select and show the component
                    SetCurrentModComponent(targetComponent);
                    SetTabInternal(TabControl, GuiEditTabItem);

                    if (instructionsGenerated)
                    {
                        await InformationDialog.ShowInformationDialogAsync(
                            this,
                            $"✅ Successfully processed {(isFolder ? "folder" : "archive")} '{modName}'.\n\n" +
                            $"{(existingComponent == null ? "Created new mod component" : "Updated existing mod component")} " +
                            $"'{targetComponent.Name}' with {targetComponent.Instructions.Count} instruction(s)."
                        );
                    }
                    else if (targetComponent.Instructions.Count == 0)
                    {
                        await InformationDialog.ShowInformationDialogAsync(
                            this,
                            $"⚠️ Created mod component '{targetComponent.Name}' but could not auto-generate instructions.\n\n" +
                            "You may need to manually configure the instructions in the Editor tab."
                        );
                    }
                }
                finally
                {
                    HideLoadingOverlay();
                }
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                await Logger.LogExceptionAsync(ex, $"Error handling dropped {(isFolder ? "folder" : "archive")}");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"❌ Error processing dropped {(isFolder ? "folder" : "archive")}: {ex.Message}"
                );
            }
        }

        private static async Task<bool> GenerateInstructionsFromFolder(ModComponent component, string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    await Logger.LogErrorAsync($"Folder does not exist: {folderPath}");
                    return false;
                }

                var folderInfo = new DirectoryInfo(folderPath);
                string folderName = folderInfo.Name;

                // Create a Copy instruction for the folder
                // The source will be relative to the mod directory
                // We need to figure out if this folder should be copied from modDirectory or if it's already there

                // Check if folder is already in the mod directory
                string relativePath = folderPath;
                if (MainConfig.SourcePath != null && folderPath.StartsWith(MainConfig.SourcePath.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    // Folder is already in mod directory, use relative path
                    relativePath = PathHelper.GetRelativePath(MainConfig.SourcePath.FullName, folderPath);
                }
                else
                {
                    // Folder is outside mod directory - we should copy it there first
                    // But TODO - STUB: For now, we'll create instructions assuming the folder will be in modDirectory
                }

                // Normalize path separators for the instruction
                relativePath = relativePath.Replace('\\', '/');

                // Determine destination - typically Override folder
                string destination = "<<kotorDirectory>>/Override";

                // Create Copy instruction
                var copyInstruction = new Instruction
                {
                    Action = Instruction.ActionType.Copy,
                    Source = new List<string> { $"<<modDirectory>>/{relativePath}/*" },
                    Destination = destination,
                    Overwrite = true,
                };
                copyInstruction.SetParentComponent(component);

                component.Instructions.Add(copyInstruction);

                await Logger.LogAsync($"Created Copy instruction for folder '{folderName}': {relativePath} -> {destination}");
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"Error generating instructions from folder: {folderPath}");
                return false;
            }
        }

        private void ShowLoadingOverlay(string message = "Loading...")
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Border overlay = this.FindControl<Border>("LoadingOverlay");
                    TextBlock loadingText = this.FindControl<TextBlock>("LoadingText");

                    if (overlay != null)
                    {
                        if (loadingText != null)
                        {
                            loadingText.Text = message;
                        }

                        overlay.IsVisible = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Error showing loading overlay");
                }
            });
        }

        private void HideLoadingOverlay()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Border overlay = this.FindControl<Border>("LoadingOverlay");
                    if (overlay != null)
                    {
                        overlay.IsVisible = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "[HideLoadingOverlay] Error hiding loading overlay");
                }
            });
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            await Logger.LogVerboseAsync("[MainWindow.OnClosing] === WINDOW CLOSING EVENT TRIGGERED ===");
            await Logger.LogVerboseAsync($"[MainWindow.OnClosing] IsClosingMainWindow: {IsClosingMainWindow}");

            base.OnClosing(e);

            if (IsClosingMainWindow)
            {
                await Logger.LogVerboseAsync("[MainWindow.OnClosing] IsClosingMainWindow=true, allowing close");
                return;
            }

            await Logger.LogVerboseAsync("[MainWindow.OnClosing] Cancelling close event, calling HandleClosingAsync");
            e.Cancel = true;

            await HandleClosingAsync();
        }

        private async Task<bool> HandleClosingAsync()
        {
            try
            {
                await Logger.LogVerboseAsync("[MainWindow.HandleClosingAsync] === STARTING CLOSE SEQUENCE ===");
                await Logger.LogVerboseAsync($"[MainWindow.HandleClosingAsync] EditorMode: {EditorMode}");

                bool? result = EditorMode
                    ? await ConfirmationDialog.ShowConfirmationDialogAsync(this, confirmText: "Really close KOTORModSync Please save your changes before pressing Quit?",
                        yesButtonText: "Quit",
                        noButtonText: "Cancel",
                        yesButtonTooltip: "Quit KOTORModSync",
                        noButtonTooltip: "Cancel the close sequence.",
                        closeButtonTooltip: "Cancel the close sequence."
                    )
                    : true;

                await Logger.LogVerboseAsync($"[MainWindow.HandleClosingAsync] Confirmation result: {result}");

                if (result != true)
                {
                    await Logger.LogVerboseAsync("[MainWindow.HandleClosingAsync] Close cancelled by user");
                    return false;
                }

                await Logger.LogVerboseAsync("[MainWindow.HandleClosingAsync] About to call SaveSettings()");
                await Logger.LogVerboseAsync($"[MainWindow.HandleClosingAsync] BEFORE SaveSettings - MainConfigInstance.sourcePath: '{MainConfigInstance.sourcePathFullName}'");
                await Logger.LogVerboseAsync($"[MainWindow.HandleClosingAsync] BEFORE SaveSettings - MainConfigInstance.destinationPath: '{MainConfigInstance.destinationPathFullName}'");

                SaveSettings();

                // Cancel any pending pre-resolve operations
                if (!(_preResolveCts is null))
                {
#if NET8_0_OR_GREATER
                    await _preResolveCts.CancelAsync();
#else
                    _preResolveCts.Cancel();
                    await Task.CompletedTask;
#endif
                }
                _preResolveCts?.Dispose();
                _preResolveCts = null;

                _telemetryService?.Flush();
                _telemetryService?.Dispose();

                _fileSystemService?.Dispose();

                IsClosingMainWindow = true;
                await Dispatcher.UIThread.InvokeAsync(Close);
                return true;
            }
            catch (Exception e)
            {
                await Logger.LogExceptionAsync(e, "[HandleClosingAsync] Error handling closing");
                return false;
            }
        }
        public new event EventHandler<PropertyChangedEventArgs> PropertyChanged;
        public async Task InitializeControls()
        {
            if (MainGrid.ColumnDefinitions is null || MainGrid.ColumnDefinitions.Count != 2)
            {
                throw new InvalidOperationException(message: "MainGrid incorrectly defined, expected 3 columns.");
            }

            Title = $"KOTORModSync v{MainConfig.CurrentVersion}";
            TitleTextBlock.Text = Title;
            ColumnDefinition componentListColumn = MainGrid.ColumnDefinitions[0]
                                                   ?? throw new InvalidOperationException(message: "Column 0 of MainGrid (component list column) not defined.");

            componentListColumn.Width = new GridLength(300);

            RawTabControl.GetRawEditTextBox().LostFocus += RawEditTextBox_LostFocus;
            RawTabControl.GetRawEditTextBox().DataContext = new ObservableCollection<string>();
            EditorTabControl.CurrentComponent = CurrentComponent;
            await UpdateStepProgressAsync();
            await Logger.LogVerboseAsync("Setting up window move event handlers...");

            PointerPressed += InputElement_OnPointerPressed;
            PointerMoved += InputElement_OnPointerMoved;
            PointerReleased += InputElement_OnPointerReleased;
            PointerExited += InputElement_OnPointerReleased;
            FindComboBoxesInWindow(this);
        }

        private void UpdateWorkflowSurfaces()
        {
            void UpdateCore()
            {
                bool instructionsLoaded = MainConfig.AllComponents != null && MainConfig.AllComponents.Count > 0;
                bool landingVisible = !EditorMode && !instructionsLoaded;
                bool wizardVisible = !landingVisible && _isWizardMode && instructionsLoaded && !EditorMode;
                bool mainVisible = !landingVisible && !wizardVisible;

                LandingPageVisible = landingVisible;
                WizardContentVisible = wizardVisible;
                MainContentVisible = mainVisible;
                ShowWizardToggle = (mainVisible || wizardVisible) && !EditorMode;

                LandingPage?.UpdateState(
                    instructionsLoaded,
                    instructionsLoaded ? LastLoadedInstructionFileName : null,
                    EditorMode);
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                UpdateCore();
            }
            else
            {
                Dispatcher.UIThread.Post(UpdateCore);
            }
        }

        private void OnLandingPageLoadInstructionsRequested(object sender, EventArgs e)
        {
            LoadFile_Click(sender ?? this, new RoutedEventArgs());
        }

        private void OnLandingPageCreateInstructionsRequested(object sender, EventArgs e)
        {
            EditorMode = true;
        }

        private void OnLandingPageOpenSponsorPageRequested(object sender, EventArgs e)
        {
            OpenSponsorPage_Click(sender, new RoutedEventArgs());
        }

        private void SearchText_PropertyChanged([NotNull] object sender, [NotNull] PropertyChangedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SearchText_PropertyChanged(sender, e), DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (!string.Equals(e.PropertyName, nameof(SearchText), StringComparison.Ordinal))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    FilterModList(SearchText);
                }
                else
                {
                    RefreshModList();
                }
            }
            catch (Exception exception)
            {
                Logger.LogException(exception, "[SearchText_PropertyChanged] Error searching text");
            }
        }
        private void InitializeModListBox()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(InitializeModListBox, DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (ModListBox is null)
                {
                    return;
                }

                ModListBox.SelectionChanged += ModListBox_SelectionChanged;

                SetupDragAndDrop();

                if (this.FindControl<CheckBox>("SelectAllCheckBox") is CheckBox selectAllCheckBox)
                {
                    selectAllCheckBox.IsCheckedChanged += SelectAllCheckBox_IsCheckedChanged;
                }

                SetupKeyboardShortcuts();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[InitializeModListBox] Error initializing mod list box");
            }
        }
        private void SetupKeyboardShortcuts()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(SetupKeyboardShortcuts, DispatcherPriority.Normal);
                return;
            }
            if (ModListBox is null)
            {
                return;
            }

            ModListBox.KeyDown += (sender, e) =>
            {
                try
                {
                    if (!EditorMode)
                    {
                        return;
                    }

                    if (!(ModListBox.SelectedItem is ModComponent component))
                    {
                        return;
                    }

                    switch (e.Key)
                    {

                        case Key.Up when e.KeyModifiers == KeyModifiers.Control:
                            MoveComponentListItem(component, -1);
                            e.Handled = true;
                            break;

                        case Key.Down when e.KeyModifiers == KeyModifiers.Control:
                            MoveComponentListItem(component, 1);
                            e.Handled = true;
                            break;

                        case Key.Delete:
                            SetCurrentModComponent(component);
                            _ = DeleteModWithConfirmationAsync(component);
                            e.Handled = true;
                            break;

                        case Key.Space:
                            component.IsSelected = !component.IsSelected;
                            UpdateModCounts();
                            e.Handled = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "[SetupKeyboardShortcuts] Error setting up keyboard shortcuts");
                }
            };
        }
        private async Task DeleteModWithConfirmationAsync(ModComponent component)
        {
            try
            {
                if (component is null)
                {
                    await Logger.LogVerboseAsync(message: "No component provided for deletion.");
                    return;
                }
                bool? confirm = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    confirmText: $"Are you sure you want to delete the mod '{component.Name}'? This action cannot be undone.",
                    yesButtonText: "Delete",
                    noButtonText: "Cancel",
                    yesButtonTooltip: "Delete the mod.",
                    noButtonTooltip: "Cancel the deletion of the mod.",
                    closeButtonTooltip: "Cancel the deletion of the mod."
                );
                if (confirm == true)
                {
                    SetCurrentModComponent(component);
                    RemoveComponentButton_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[DeleteModWithConfirmationAsync] Error deleting mod with confirmation");
            }
        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public ContextMenu BuildContextMenuForComponent(ModComponent component)
        {
            var contextMenu = new ContextMenu();
            if (component is null)
            {
                return contextMenu;
            }

            _ = contextMenu.Items.Add(new MenuItem
            {
                Header = component.IsSelected ? "☑️ Deselect Mod" : "☐ Select Mod",
                Command = ReactiveCommand.Create(() =>
                {
                    component.IsSelected = !component.IsSelected;
                    UpdateModCounts();
                    if (component.IsSelected)
                    {
                        ComponentCheckboxChecked(component, new HashSet<ModComponent>());
                    }
                    else
                    {
                        ComponentCheckboxUnchecked(component, new HashSet<ModComponent>());
                    }
                }),
            });

            if (EditorMode)
            {
                _ = contextMenu.Items.Add(new Separator());

                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "⬆️ Move Up",
                    Command = ReactiveCommand.Create(() => MoveComponentListItem(component, -1)),
                    InputGesture = new KeyGesture(Key.Up, KeyModifiers.Control),
                });
                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "⬇️ Move Down",
                    Command = ReactiveCommand.Create(() => MoveComponentListItem(component, 1)),
                    InputGesture = new KeyGesture(Key.Down, KeyModifiers.Control),
                });
                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "📊 Move to Top",
                    Command = ReactiveCommand.Create(() => ModManagementService.MoveModToPosition(component, 0)),
                });
                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "📊 Move to Bottom",
                    Command = ReactiveCommand.Create(() => ModManagementService.MoveModToPosition(component, MainConfig.AllComponents.Count - 1)),
                });
                _ = contextMenu.Items.Add(new Separator());

                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "🗑️ Delete Mod",
                    Command = ReactiveCommand.CreateFromTask(async () =>
                    {
                        SetCurrentModComponent(component);
                        bool? confirm = await ConfirmationDialog.ShowConfirmationDialogAsync(
                            this,
                            confirmText: $"Are you sure you want to delete the mod '{component.Name}'? This action cannot be undone.",
                            yesButtonText: "Delete",
                            noButtonText: "Cancel",
                            yesButtonTooltip: "Delete the mod.",
                            noButtonTooltip: "Cancel the deletion of the mod.",
                            closeButtonTooltip: "Cancel the deletion of the mod."
                        );
                        if (confirm == true)
                        {
                            RemoveComponentButton_Click(null, null);
                        }
                    }),
                });
                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "🔄 Duplicate Mod",
                    Command = ReactiveCommand.Create(() =>
                    {
                        ModComponent duplicated = ModManagementService.DuplicateMod(component);
                        if (duplicated != null)
                        {
                            SetCurrentModComponent(duplicated);
                            SetTabInternal(TabControl, GuiEditTabItem);
                        }
                    }),
                });
                _ = contextMenu.Items.Add(new Separator());

                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "📝 Edit Instructions",
                    Command = ReactiveCommand.Create(() =>
                    {
                        SetCurrentModComponent(component);
                        SetTabInternal(TabControl, GuiEditTabItem);
                    }),
                });
                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "📄 Edit Raw TOML",
                    Command = ReactiveCommand.Create(() =>
                    {
                        SetCurrentModComponent(component);
                        SetTabInternal(TabControl, RawEditTabItem);
                    }),
                });
                _ = contextMenu.Items.Add(new Separator());

                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "🧪 Test Install This Mod",
                    Command = ReactiveCommand.Create(() =>
                    {
                        SetCurrentModComponent(component);
                        InstallModSingle_Click(null, null);
                    }),
                });
                _ = contextMenu.Items.Add(new MenuItem
                {
                    Header = "🔍 Validate Mod Files",
                    Command = ReactiveCommand.CreateFromTask(async () =>
                    {
                        ModValidationResult validation = ModManagementService.ValidateMod(component);
                        if (!validation.IsValid)
                        {
                            await InformationDialog.ShowInformationDialogAsync(this,
                                $"Validation failed for '{component.Name}':{Environment.NewLine}{Environment.NewLine}" +
                                string.Join("\n", validation.Errors.Take(5)));
                        }
                        else
                        {
                            await InformationDialog.ShowInformationDialogAsync(this,
                                $"✅ '{component.Name}' validation passed!");
                        }
                    }),
                });
            }
            return contextMenu;
        }

        private void BuildGlobalActionsMenu()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(BuildGlobalActionsMenu, DispatcherPriority.Normal);
                return;
            }
            DropDownButton globalActionsButton = ModListSidebar?.GlobalActionsButton;
            if (globalActionsButton?.Flyout is MenuFlyout globalActionsFlyout)
            {
                BuildMenuFlyoutItems(globalActionsFlyout);
            }
        }

        private void BuildMenuFlyoutItems(MenuFlyout menu)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => BuildMenuFlyoutItems(menu), DispatcherPriority.Normal);
                return;
            }
            menu.Items.Clear();

            _ = menu.Items.Add(new MenuItem
            {
                Header = "🔄 Refresh List",
                Command = ReactiveCommand.Create(() => RefreshComponents_Click(null, null)),
                InputGesture = new KeyGesture(Key.F5),
            });
            _ = menu.Items.Add(new MenuItem
            {
                Header = "🔄 Validate All Mods",
                Command = ReactiveCommand.Create((Func<Task>)(async () =>
                {
                    Dictionary<ModComponent, ModValidationResult> results = ModManagementService.ValidateAllMods();
                    int errorCount = results.Count(r => !r.Value.IsValid);
                    int warningCount = results.Sum(r => r.Value.Warnings.Count);
                    await InformationDialog.ShowInformationDialogAsync(this,
                        "Validation complete!\n\n" +
                        $"Errors: {errorCount}\n" +
                        $"Warnings: {warningCount}\n\n" +
                        $"Valid mods: {results.Count(r => r.Value.IsValid)}/{results.Count}");
                })),
            });
            _ = menu.Items.Add(new MenuItem
            {
                Header = "🤖 Generate Instructions from ModLinks",
                Command = ReactiveCommand.Create((async () => await AutoGenerateAllComponentsAsync())),
            });
            _ = menu.Items.Add(new MenuItem
            {
                Header = "🔒 Lock Install Order",
                Command = ReactiveCommand.Create((async () => await AutoGenerateDependenciesAsync())),
            });
            _ = menu.Items.Add(new MenuItem
            {
                Header = "🗑️ Remove All Dependencies",
                Command = ReactiveCommand.Create((async () => await RemoveAllDependenciesAsync())),
            });
            _ = menu.Items.Add(new Separator());
            if (EditorMode)
            {

                _ = menu.Items.Add(new MenuItem
                {
                    Header = "➕ Add New Mod",
                    Command = ReactiveCommand.Create(() =>
                    {
                        ModComponent newMod = ModManagementService.CreateMod();
                        if (newMod is null)
                        {
                            return;
                        }

                        SetCurrentModComponent(newMod);
                        SetTabInternal(TabControl, GuiEditTabItem);
                    }),
                });
                _ = menu.Items.Add(new Separator());

                _ = menu.Items.Add(new MenuItem
                {
                    Header = "⚙️ Mod Management Tools",
                    Command = ReactiveCommand.Create(async () => await ShowModManagementDialogAsync()),
                });
                _ = menu.Items.Add(new MenuItem
                {
                    Header = "📈 Mod Statistics",
                    Command = ReactiveCommand.Create((async () =>
                    {
                        ModStatistics stats = ModManagementService.GetModStatistics();
                        string statsText = "📊 Mod Statistics\n\n" +
                                           $"Total Mods: {stats.TotalMods}\n" +
                                           $"Selected: {stats.SelectedMods}\n" +
                                           $"Downloaded: {stats.DownloadedMods}\n\n" +
                                           $"Categories:\n{string.Join("\n", stats.Categories.Select(c => $"  • {c.Key}: {c.Value}"))}\n\n" +
                                           $"Tiers:\n{string.Join("\n", stats.Tiers.Select(t => $"  • {t.Key}: {t.Value}"))}\n\n" +
                                           $"Average Instructions/Mod: {stats.AverageInstructionsPerMod:F1}\n" +
                                           $"Average Options/Mod: {stats.AverageOptionsPerMod:F1}";
                        await InformationDialog.ShowInformationDialogAsync(this, statsText);
                    })),
                });
                _ = menu.Items.Add(new Separator());

                _ = menu.Items.Add(new MenuItem
                {
                    Header = "💾 Save Config",
                    Command = ReactiveCommand.Create(() => SaveModFileAs_ClickWithFormat(null, null)),
                    InputGesture = new KeyGesture(Key.S, KeyModifiers.Control),
                });
                _ = menu.Items.Add(new MenuItem
                {
                    Header = "❌ Close TOML",
                    Command = ReactiveCommand.Create(() => CloseTOMLFile_Click(null, null)),
                });
            }
        }

        private void SetupDragAndDrop()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(SetupDragAndDrop, DispatcherPriority.Normal);
                return;
            }
            if (ModListBox is null)
            {
                return;
            }

            ModListBox.PointerPressed += ModListBox_PointerPressed;
            ModListBox.AddHandler(DragDrop.DragOverEvent, ModListBox_DragOver);
            ModListBox.AddHandler(DragDrop.DropEvent, ModListBox_Drop);
        }
        private void ModListBox_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            _dragDropService.HandlePointerPressed(e, ModListBox, EditorMode);
        }
        private void ModListBox_DragOver(object sender, DragEventArgs e)
        {
            _dragDropService.HandleDragOver(e, EditorMode, this);
        }
        private void ModListBox_Drop(object sender, DragEventArgs e)
        {
            _dragDropService.HandleDrop(e, EditorMode);
        }
        /// <summary>
        /// Event handler for the ModManagementToolsRequested event from ModListSidebar
        /// </summary>
        [UsedImplicitly]
        private void ShowModManagementDialog(object sender, RoutedEventArgs e)
        {
            _ = ShowModManagementDialogAsync();
        }

        private async Task ShowModManagementDialogAsync()
        {
            try
            {
                var dialogService = new ModManagementDialogService(this, ModManagementService,
                        () => MainConfigInstance.allComponents.ToList(),
                        (components) => MainConfigInstance.allComponents = components);
                var dialog = new ModManagementDialog(ModManagementService, dialogService);
                await dialog.ShowDialog(this);
                if (dialog.ModificationsApplied)
                {
                    await ProcessComponentsAsync(MainConfig.AllComponents);
                    await Logger.LogVerboseAsync("Applied mod management changes");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex);
            }
        }

        public void StartDragComponent(ModComponent component, PointerPressedEventArgs e) => _dragDropService.StartDragComponent(component, e, EditorMode);
        private void ModListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                Logger.LogVerbose($"[ModListBox_SelectionChanged] START - SelectedItem type: {ModListBox.SelectedItem?.GetType().Name ?? "null"}");
                if (ModListBox.SelectedItem is ModComponent component)
                {
                    Logger.LogVerbose($"[ModListBox_SelectionChanged] ModComponent selected: '{component.Name}' (GUID={component.Guid})");
                    Logger.LogVerbose($"[ModListBox_SelectionChanged] ModComponent has {component.Instructions.Count} instructions, {component.Options.Count} options");
                    Logger.LogVerbose("[ModListBox_SelectionChanged] Calling SetCurrentComponent");
                    SetCurrentModComponent(component);
                    Logger.LogVerbose("[ModListBox_SelectionChanged] SetCurrentComponent completed");
                }
                else
                {
                    Logger.LogVerbose("[ModListBox_SelectionChanged] SelectedItem is not a ModComponent");
                }
                Logger.LogVerbose("[ModListBox_SelectionChanged] COMPLETED");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, customMessage: "[ModListBox_SelectionChanged] Exception occurred");
            }
        }
        private bool _suppressSelectAllCheckBoxEvents;
        private void SelectAllCheckBox_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SelectAllCheckBox_IsCheckedChanged(sender, e), DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (!(sender is CheckBox checkBox) || _suppressSelectAllCheckBoxEvents)
                {
                    return;
                }

                _componentSelectionService.HandleSelectAllCheckbox(
                    checkBox.IsChecked,
                    ComponentCheckboxChecked,
                    ComponentCheckboxUnchecked
                );

                UpdateModCounts();
                UpdateStepProgress();

                ResetDownloadStatusDisplay();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
        private void FilterModList(string searchText)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => FilterModList(searchText), DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (ModListBox is null)
                {
                    return;
                }

                var searchOptions = new ModSearchOptions
                {
                    SearchInName = true,
                    SearchInAuthor = true,
                    SearchInCategory = true,
                    SearchInDescription = true,
                };
                List<ModComponent> filteredComponents = ModManagementService.SearchMods(searchText, searchOptions);
                PopulateModList(filteredComponents);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
        private void RefreshModList()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshModList, DispatcherPriority.Normal);
                return;
            }
            try
            {
                PopulateModList(MainConfig.AllComponents);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
        private void RefreshModListVisuals()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshModListVisuals, DispatcherPriority.Normal);
                return;
            }
            try
            {
                ModListService.RefreshModListVisuals(ModListBox, UpdateStepProgress);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        private void RefreshAllTooltips()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshAllTooltips, DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (ModListBox is null)
                {
                    return;
                }

                ModListItem[] modListItems = ModListBox.GetVisualDescendants().OfType<ModListItem>().ToArray();
                foreach (ModListItem item in modListItems)
                {
                    if (item.DataContext is ModComponent component)
                    {
                        item.UpdateTooltip(component);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[RefreshComponentValidationState] Error refreshing component validation state");
            }
        }

        /// <summary>
        /// Refreshes component validation state visuals (border colors, tooltips).
        /// NOTE: This only calls UpdateValidationState which performs fast checks only (no instructions, dependencies, restrictions).
        /// Full VFS validation only happens when Validate button is pressed.
        /// </summary>
        private void RefreshComponentValidationState(ModComponent component)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RefreshComponentValidationState(component), DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (ModListBox is null || component is null)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {

                        if (!(ModListBox.ContainerFromItem(component) is ListBoxItem container))
                        {
                            return;
                        }

                        ModListItem modListItem = container.GetVisualDescendants().OfType<ModListItem>().FirstOrDefault();
                        if (modListItem is null)
                        {
                            return;
                        }

                        // UpdateValidationState only does fast checks (no VFS simulation)
                        // Full validation with VFS only happens when Validate button is pressed
                        modListItem.UpdateValidationState(component);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "Error refreshing component validation state on UI thread");
                    }
                }, DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[RefreshComponentValidationState] Error posting component validation refresh to UI thread");
            }
        }
        private void PopulateModList(List<ModComponent> components)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => PopulateModList(components), DispatcherPriority.Normal);
                return;
            }
            try
            {
                ModListService.PopulateModList(ModListBox, components, UpdateModCounts);

                if (string.IsNullOrWhiteSpace(SearchText) && MainConfig.AllComponents.Count > 0)
                {
                    InitializeFilterUi(MainConfig.AllComponents);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
        public void UpdateModCounts()
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(UpdateModCounts, DispatcherPriority.Normal);
                    return;
                }
                _modListService.UpdateModCounts(
                    ModListSidebar?.ModCountTextBlock,
                    ModListSidebar?.SelectedCountTextBlock,
                    this.FindControl<CheckBox>("SelectAllCheckBox"),
                    suppress => _suppressSelectAllCheckBoxEvents = suppress
                );

                RefreshModListVisuals();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
        public static void FilterControlListItems(
            [NotNull] object item,
            [NotNull] string searchText)
        {
            if (searchText is null)
            {
                throw new ArgumentNullException(nameof(searchText));
            }

            if (!(item is Control controlItem))
            {
                return;
            }

            if (controlItem.Tag is ModComponent thisComponent)
            {
                ApplySearchVisibility(controlItem, thisComponent.Name, searchText);
            }

            IEnumerable<ILogical> controlItemArray = controlItem.GetLogicalChildren();
            foreach (TreeViewItem childItem in controlItemArray.OfType<TreeViewItem>())
            {
                FilterControlListItems(childItem, searchText);
            }
        }
        private static void ApplySearchVisibility(
            [NotNull] Visual item,
            [NotNull] string itemName,
            [NotNull] string searchText
        )
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (itemName is null)
            {
                throw new ArgumentNullException(nameof(itemName));
            }

            if (searchText is null)
            {
                throw new ArgumentNullException(nameof(searchText));
            }

            item.IsVisible = SearchUtilities.ShouldBeVisible(itemName, searchText);
        }
        private void FindProblemControls([CanBeNull] Control control)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => FindProblemControls(control), DispatcherPriority.Normal);
                return;
            }
            if (!(control is ILogical visual))
            {
                throw new ArgumentNullException(nameof(control));
            }

            if (control is ComboBox || control is MenuItem)
            {
                control.Tapped -= ComboBox_Opened;
                control.PointerCaptureLost -= ComboBox_Opened;
                control.Tapped += ComboBox_Opened;
                control.PointerCaptureLost += ComboBox_Opened;
            }
            if (visual.LogicalChildren.IsNullOrEmptyOrAllNull())
            {
                return;
            }

            foreach (ILogical child in visual.LogicalChildren)
            {
                if (child is Control childControl)
                {
                    FindProblemControls(childControl);
                }
            }
        }

        public void FindComboBoxesInWindow([NotNull] Window thisWindow)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => FindComboBoxesInWindow(thisWindow), DispatcherPriority.Normal);
                return;
            }
            if (thisWindow is null)
            {
                throw new ArgumentNullException(nameof(thisWindow));
            }

            FindProblemControls(thisWindow);
        }
        private void ComboBox_Opened([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            _mouseDownForWindowMoving = false;
            _ignoreWindowMoveWhenClickingComboBox = true;
        }
        private void InputElement_OnPointerMoved([NotNull] object sender, [NotNull] PointerEventArgs e)
        {
            if (!_mouseDownForWindowMoving)
            {
                return;
            }

            if (_ignoreWindowMoveWhenClickingComboBox)
            {
                _ignoreWindowMoveWhenClickingComboBox = false;
                _mouseDownForWindowMoving = false;
                return;
            }
            PointerPoint currentPoint = e.GetCurrentPoint(this);
            Position = new PixelPoint(
                Position.X + (int)(currentPoint.Position.X - _originalPoint.Position.X),
                Position.Y + (int)(currentPoint.Position.Y - _originalPoint.Position.Y)
            );
        }
        private void InputElement_OnPointerPressed([NotNull] object sender, [NotNull] PointerEventArgs e)
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
        private void InputElement_OnPointerReleased([NotNull] object sender, [NotNull] PointerEventArgs e) => _mouseDownForWindowMoving = false;
        [UsedImplicitly]
        private void CloseButton_Click([NotNull] object sender, [NotNull] RoutedEventArgs e) => Close();
        [UsedImplicitly]
        private void MinimizeButton_Click([NotNull] object sender, [NotNull] RoutedEventArgs e) => WindowState = WindowState.Minimized;
        [UsedImplicitly]
        private void SwitchToLightTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.ApplyTheme("FluentLightStyle.axaml");
            SaveThemeToSettings();
            UpdateThemeButtonStates();
        }

        [UsedImplicitly]
        private void SwitchToK1Theme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.ApplyTheme("KotorStyle.axaml");
            SaveThemeToSettings();
            UpdateThemeButtonStates();
        }

        [UsedImplicitly]
        private void SwitchToTslTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.ApplyTheme("Kotor2Style.axaml");
            SaveThemeToSettings();
            UpdateThemeButtonStates();
        }

        private void SaveThemeToSettings()
        {
            try
            {
                string currentTheme = ThemeService.GetCurrentTheme();
                if (string.IsNullOrEmpty(currentTheme))
                {
                    currentTheme = "/Styles/FluentLightStyle.axaml";
                }

                var settings = AppSettings.FromCurrentState(MainConfigInstance, currentTheme, SpoilerFreeMode);
                Models.SettingsManager.SaveSettings(settings);
                Logger.LogVerbose($"Theme saved to settings: {currentTheme}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to save theme to settings");
            }
        }

        private void UpdateThemeButtonStates()
        {
            try
            {
                string currentTheme = ThemeService.GetCurrentTheme();

                Button lightButton = this.FindControl<Button>("LightThemeButton");
                Button k1Button = this.FindControl<Button>("K1ThemeButton");
                Button tslButton = this.FindControl<Button>("TslThemeButton");

                if (lightButton != null)
                {
                    lightButton.IsEnabled = !string.Equals(currentTheme, "FluentLightStyle.axaml", StringComparison.Ordinal);
                }

                if (k1Button != null)
                {
                    k1Button.IsEnabled = !string.Equals(currentTheme, "KotorStyle.axaml", StringComparison.Ordinal);
                }

                if (tslButton != null)
                {
                    tslButton.IsEnabled = !string.Equals(currentTheme, "Kotor2Style.axaml", StringComparison.Ordinal);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to update theme button states");
            }
        }

        [ItemCanBeNull]
        public async Task<string> SaveFileAsync(
            string saveFileName = null
        )
        {
            return await _dialogService.ShowSaveFileDialogAsync(saveFileName ?? "my_instructions.toml");
        }

        [UsedImplicitly]
        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        internal async void LoadFile_Click(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e,
            CancellationToken cancellationToken = default)
        {
            _telemetryService?.RecordUiInteraction("click", "LoadFileButton");

            try
            {
                // When not in Editor Mode and components are already loaded, confirm with user
                if (!EditorMode && MainConfig.AllComponents.Count > 0)
                {
                    bool? confirm = await ConfirmationDialog.ShowConfirmationDialogAsync(
                        this,
                        confirmText: $"You currently have {MainConfig.AllComponents.Count} mod(s) loaded. Loading a new file will replace them. How would you like to proceed?",
                        yesButtonText: "Load New File",
                        noButtonText: "Cancel",
                        yesButtonTooltip: "Discard current mods and load the new instruction file",
                        noButtonTooltip: "Cancel and keep the currently loaded mods",
                        closeButtonTooltip: "Cancel and keep the currently loaded mods"
                    );

                    if (confirm != true)
                    {
                        await Logger.LogAsync("User cancelled loading new file to keep existing mods.");
                        return;
                    }
                }

                DateTime startTime = DateTime.UtcNow;

                string[] result = await _dialogService.ShowFileDialogAsync(
                    windowName: "Load an instruction file (TOML, JSON, YAML, or Markdown)",
                    isFolderDialog: false
                );
                if (result is null || result.Length <= 0)
                {
                    return;
                }

                string filePath = result[0];
                if (!PathValidator.IsValidPath(filePath))
                {
                    HideLoadingOverlay();
                    return;
                }

                string fileExtension = Path.GetExtension(filePath)?.TrimStart('.').ToLowerInvariant();
                string fileName = Path.GetFileName(filePath);

                // Show loading overlay
                ShowLoadingOverlay($"Loading {fileName}...");

                try
                {
                    // Special case: Markdown files need preferentially dialog for regex configuration
                    if (string.Equals(fileExtension, "md", StringComparison.Ordinal) || string.Equals(fileExtension, "markdown", StringComparison.Ordinal))
                    {
                        await Logger.LogAsync($"Loading Markdown file: {fileName}");
                        bool loaded = await _fileLoadingService.LoadMarkdownFileAsync(
                        filePath,
                        EditorMode,
                        () => ProcessComponentsAsync(MainConfig.AllComponents),
                        TryAutoGenerateInstructionsForComponents,
                        profile: null
                    );

                        if (loaded)
                        {
                            // Apply theme based on TargetGame from loaded file
                            if (!string.IsNullOrEmpty(MainConfig.TargetGame))
                            {
                                string themeToApply = ThemeManager.GetCurrentStylePath();
                                ApplyTheme(themeToApply);
                                ThemeManager.ApplyCurrentToWindow(this);
                                await Logger.LogVerboseAsync($"Applied theme from TargetGame '{MainConfig.TargetGame}': {themeToApply}");
                            }

                            _telemetryService?.RecordEvent("file.loaded", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["file_type"] = "markdown",
                                ["duration_ms"] = (DateTime.UtcNow - startTime).TotalMilliseconds,
                                ["component_count"] = MainConfig.AllComponents.Count,
                            });
                        }
                        return;
                    }

                    // For all other formats (TOML, JSON, YAML), use unified loader with auto-detection
                    await Logger.LogAsync($"Loading file: {fileName}");
                    bool loadedSuccess = await LoadInstructionFileAsync(filePath, fileType: "config file");

                    if (loadedSuccess)
                    {
                        // Apply theme based on TargetGame from loaded file
                        if (!string.IsNullOrEmpty(MainConfig.TargetGame))
                        {
                            string themeToApply = ThemeManager.GetCurrentStylePath();
                            ApplyTheme(themeToApply);
                            ThemeManager.ApplyCurrentToWindow(this);
                            await Logger.LogVerboseAsync($"Applied theme from TargetGame '{MainConfig.TargetGame}': {themeToApply}");
                        }

                        string detectedFormat = fileExtension ?? "unknown";
                        _telemetryService?.RecordEvent("file.loaded", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["file_type"] = detectedFormat,
                            ["duration_ms"] = (DateTime.UtcNow - startTime).TotalMilliseconds,
                            ["component_count"] = MainConfig.AllComponents.Count,
                        });
                    }
                }
                finally
                {
                    HideLoadingOverlay();
                }
            }
            catch (Exception ex)
            {
                HideLoadingOverlay();
                await Logger.LogExceptionAsync(ex);
                _telemetryService?.RecordError("file.load", ex.Message, ex.StackTrace);
            }
        }

        [UsedImplicitly]
        private void LoadInstallFile_Click(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e) => LoadFile_Click(sender, e);
        [UsedImplicitly]
        private void OpenLink_Click([NotNull] object sender, [NotNull] TappedEventArgs e)
        {
            if (!(sender is TextBlock textBlock))
            {
                return;
            }

            try
            {
                string url = textBlock.Text;
                if (string.IsNullOrEmpty(url))
                {
                    throw new InvalidOperationException(message: "url (textBlock.Text) cannot be null/empty");
                }

                UrlUtilities.OpenUrl(url);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to open URL: {ex.Message}");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void BrowseSourceFiles_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                await Logger.LogVerboseAsync("BrowseSourceFiles_Click: Event triggered");
                var button = (Button)sender;
                Instruction thisInstruction = (Instruction)button.DataContext
                                            ?? throw new InvalidOperationException(message: "Could not find instruction instance");

                await Logger.LogVerboseAsync($"BrowseSourceFiles_Click: Instruction found: {thisInstruction.Action}");

                // Find the SourceTextBox in the parent InstructionEditorControl
                var instructionEditorControl = button.FindAncestorOfType<InstructionEditorControl>();
                var sourceTextBox = instructionEditorControl?.FindControl<TextBox>("SourceTextBox");

                if (sourceTextBox != null)
                {
                    await Logger.LogVerboseAsync("BrowseSourceFiles_Click: Found SourceTextBox, calling BrowseSourceFilesAsync");
                    await _instructionBrowsingService.BrowseSourceFilesAsync(thisInstruction, sourceTextBox);
                }
                else
                {
                    await Logger.LogWarningAsync("BrowseSourceFiles_Click: Could not find SourceTextBox");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[BrowseSourceFiles_Click] Failed to browse source files");
            }
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void BrowseSourceFromFolders_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                await Logger.LogVerboseAsync("BrowseSourceFromFolders_Click: Event triggered");
                var button = (Button)sender;
                Instruction thisInstruction = (Instruction)button.DataContext
                                            ?? throw new InvalidOperationException(message: "Could not find instruction instance");

                await Logger.LogVerboseAsync($"BrowseSourceFromFolders_Click: Instruction found: {thisInstruction.Action}");

                // Find the SourceTextBox in the parent InstructionEditorControl
                var instructionEditorControl = button.FindAncestorOfType<InstructionEditorControl>();
                var sourceTextBox = instructionEditorControl?.FindControl<TextBox>("SourceTextBox");

                if (sourceTextBox != null)
                {
                    await Logger.LogVerboseAsync("BrowseSourceFromFolders_Click: Found SourceTextBox, calling BrowseSourceFoldersAsync");
                    await _instructionBrowsingService.BrowseSourceFoldersAsync(thisInstruction, sourceTextBox);
                }
                else
                {
                    await Logger.LogWarningAsync("BrowseSourceFromFolders_Click: Could not find SourceTextBox");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, customMessage: "[BrowseSourceFromFolders_Click] Exception occurred");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void BrowseModFiles_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                await Logger.LogVerboseAsync("BrowseModFiles_Click: Event triggered");

                // Get the original source button from the routed event
                // The sender might be EditorTab, but e.Source should be the original button
                Button button = null;
                if (e.Source is Button sourceButton)
                {
                    button = sourceButton;
                }
                else if (sender is Button senderButton)
                {
                    button = senderButton;
                }
                else
                {
                    // Try to find the button by traversing from the source
                    if (e.Source is Control sourceControl)
                    {
                        button = sourceControl.FindAncestorOfType<Button>();
                        if (button == null && sender is Control senderControl)
                        {
                            button = senderControl.FindAncestorOfType<Button>();
                        }
                    }
                }

                if (button == null)
                {
                    await Logger.LogErrorAsync("BrowseModFiles_Click: Could not find button from event");
                    return;
                }

                // Find the InstructionEditorControl to get the Instruction
                InstructionEditorControl instructionEditorControl = button.FindAncestorOfType<InstructionEditorControl>();
                if (instructionEditorControl == null)
                {
                    await Logger.LogErrorAsync("BrowseModFiles_Click: Could not find InstructionEditorControl");
                    return;
                }

                Instruction thisInstruction = instructionEditorControl.DataContext as Instruction
                                            ?? throw new InvalidOperationException(message: "Could not find instruction instance");

                await Logger.LogVerboseAsync($"BrowseModFiles_Click: Instruction found: {thisInstruction.Action}");

                // Find the SourceTextBox in the parent InstructionEditorControl
                TextBox sourceTextBox = instructionEditorControl.FindControl<TextBox>("SourceTextBox");

                if (sourceTextBox != null)
                {
                    await Logger.LogVerboseAsync("BrowseModFiles_Click: Found SourceTextBox, opening ModFilesBrowserDialog");

                    // Get the parent component
                    if (CurrentComponent == null)
                    {
                        await Logger.LogWarningAsync("BrowseModFiles_Click: CurrentComponent is null");
                        return;
                    }

                    // Show the ModFilesBrowserDialog
                    var dialog = await Dialogs.ModFilesBrowserDialog.ShowBrowserDialogAsync(this, CurrentComponent);

                    if (dialog != null && dialog.UserConfirmed && dialog.SelectedPaths != null && dialog.SelectedPaths.Count > 0)
                    {
                        // Update the Source textbox with selected paths
                        string selectedPathsText = string.Join(Environment.NewLine, dialog.SelectedPaths);
                        sourceTextBox.Text = selectedPathsText;

                        // Update the instruction's Source property
                        thisInstruction.Source = dialog.SelectedPaths;

                        await Logger.LogVerboseAsync($"BrowseModFiles_Click: Updated Source with {dialog.SelectedPaths.Count} selected paths");
                    }
                    else
                    {
                        await Logger.LogVerboseAsync("BrowseModFiles_Click: User cancelled or no paths selected");
                    }
                }
                else
                {
                    await Logger.LogWarningAsync("BrowseModFiles_Click: Could not find SourceTextBox");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, customMessage: "[BrowseModFiles_Click] Exception occurred");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void BrowseDestination_Click(
            [CanBeNull] object sender,
            [CanBeNull] RoutedEventArgs e)
        {
            try
            {
                Button button = (Button)sender ?? throw new InvalidOperationException();
                Instruction thisInstruction = (Instruction)button.DataContext
                                            ?? throw new InvalidOperationException("Could not find instruction instance");

                if (button.Tag is TextBox destinationTextBox)
                {
                    await _instructionBrowsingService.BrowseDestinationAsync(thisInstruction, destinationTextBox);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[BrowseDestination_Click] Failed to browse destination");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void RawTabApply_Click(
        [NotNull] object sender,
        [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }
                await Logger.LogVerboseAsync($"Selected '{CurrentComponent.Name}'");
                if (!await ShouldSaveChangesAsync())
                {
                    return;
                }

                await ProcessComponentsAsync(MainConfig.AllComponents);
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[RawTabApply_Click] Failed to apply raw tab");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void FixPathPermissionsClick(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e)
        {
            try
            {
                string[] results = await _dialogService.ShowFileDialogAsync(
                    windowName: "Select the folder(s) you'd like to fix permissions to.",
                    isFolderDialog: true,
                    allowMultiple: true
                );
                if (results is null || results.Length <= 0)
                {
                    return;
                }

                foreach (string folder in results)
                {
                    DirectoryInfo thisDir = PathHelper.TryGetValidDirectoryInfo(folder);
                    if (thisDir is null || !thisDir.Exists)
                    {
                        _ = Logger.LogErrorAsync($"Directory not found: '{folder}', skipping...");
                        continue;
                    }
                    await FilePermissionHelper.FixPermissionsAsync(thisDir);
                    await Logger.LogAsync($"Completed FixPathPermissions at '{thisDir.FullName}'");
                }
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[FixPathPermissionsClick] Failed to fix path permissions");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void FixIosCaseSensitivityClick(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e)
        {
            try
            {
                string[] results = await _dialogService.ShowFileDialogAsync(
                    windowName: "Select the folder(s) you'd like to lowercase all files/folders inside",
                    isFolderDialog: true,
                    allowMultiple: true
                );
                if (results is null || results.Length <= 0)
                {
                    return;
                }

                int numObjectsRenamed = 0;
                foreach (string folder in results)
                {
                    var thisDir = new DirectoryInfo(folder);
                    if (!thisDir.Exists)
                    {
                        _ = Logger.LogErrorAsync($"Directory not found: '{thisDir.FullName}', skipping...");
                        continue;
                    }
                    numObjectsRenamed += await UIUtilities.FixIOSCaseSensitivity(thisDir);
                }
                await Logger.LogAsync($"Successfully renamed {numObjectsRenamed} files/folders.");
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[FixIosCaseSensitivityClick] Failed to fix iOS case sensitivity");
            }
        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void ResolveDuplicateFilesAndFolders(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e)
        {
            try
            {
                bool? answer = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    "This button will resolve all case-sensitive duplicate files/folders in your install directory and your mod download directory."
                    + Environment.NewLine
                    + " WARNING: This method may take a while and cannot be stopped until it finishes. Really continue?"
                );
                if (answer != true)
                {
                    return;
                }

                await Logger.LogAsync("Finding duplicate case-insensitive folders/files in the install destination...");
                string destPath = MainConfig.DestinationPath?.FullName;
                if (string.IsNullOrEmpty(destPath))
                {
                    await Logger.LogErrorAsync("Destination path is null or empty, skipping duplicate file/folder search.");
                    return;
                }
                IEnumerable<FileSystemInfo> duplicates = PathHelper.FindCaseInsensitiveDuplicates(destPath);
                var fileSystemInfos = duplicates.ToList();
                foreach (FileSystemInfo duplicate in fileSystemInfos)
                {
                    await Logger.LogWarningAsync($"{duplicate?.FullName} is duplicated on the storage drive.");
                }
                answer = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    "Duplicate file/folder search finished."
                    + Environment.NewLine
                    + $" Found {fileSystemInfos.Count} files/folders that have duplicates in your install dir."
                    + Environment.NewLine
                    + " Delete all duplicates except the ones most recently modified?"
                );
                if (answer != true)
                {
                    return;
                }

                IEnumerable<IGrouping<string, FileSystemInfo>> groupedDuplicates = fileSystemInfos.GroupBy(fs => fs.Name.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
                foreach (IGrouping<string, FileSystemInfo> group in groupedDuplicates)
                {
                    var orderedDuplicates = group.OrderByDescending(fs => fs.LastWriteTime).ToList();
                    if (orderedDuplicates.Count <= 1)
                    {
                        continue;
                    }

                    for (int i = 1; i < orderedDuplicates.Count; i++)
                    {
                        try
                        {
                            switch (orderedDuplicates[i])
                            {
                                case FileInfo fileInfo:
                                    fileInfo.Delete();
                                    break;
                                case DirectoryInfo directoryInfo:
                                    directoryInfo.Delete(recursive: true);
                                    break;
                                default:
                                    await Logger.LogAsync($"{orderedDuplicates[i].FullName} does not exist somehow?");
                                    continue;
                            }
                            await Logger.LogAsync($"Deleted {orderedDuplicates[i].FullName}");
                        }
                        catch (Exception deletionException)
                        {
                            await Logger.LogExceptionAsync(
                                deletionException,
                                $"Failed to delete {orderedDuplicates[i].FullName}");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[ResolveDuplicateFilesAndFolders] Failed to resolve duplicate files/folders");
            }
        }

        [UsedImplicitly]
        private void ValidateButton_Click(
            [CanBeNull] object sender,
            [NotNull] RoutedEventArgs e)
        {
            _telemetryService?.RecordUiInteraction("click", "ValidateButton");

            CancelValidationIfRunning();

            var cts = new CancellationTokenSource();
            _validationCts = cts;
            _ = RunValidationAsync(sender, e, cts);
        }

        private void CancelValidationIfRunning()
        {
            if (_validationCts == null)
            {
                return;
            }

            try
            {
                if (!_validationCts.IsCancellationRequested)
                {
                    _validationCts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // Ignored – CTS may already be disposed.
            }
        }

        private async Task RunValidationAsync(
            [CanBeNull] object sender,
            [NotNull] RoutedEventArgs e,
            [NotNull] CancellationTokenSource cts)
        {
            ValidationProgressDialog progressDialog = null;
            Action<string> logHandler = null;
            Action<Exception> exceptionHandler = null;
            var token = cts.Token;
            bool dialogClosed = false;

            try
            {
                progressDialog = await ValidationProgressDialog.ShowValidationProgress(this, cts);

                if (progressDialog != null)
                {
                    progressDialog.Closed += (closedSender, closedArgs) =>
                    {
                        dialogClosed = true;
                        if (!cts.IsCancellationRequested)
                        {
                            cts.Cancel();
                        }
                    };
                }

                logHandler = message =>
                {
                    if (!token.IsCancellationRequested && !dialogClosed)
                    {
                        progressDialog?.AppendLog(message);
                    }
                };

                exceptionHandler = exception =>
                {
                    if (!token.IsCancellationRequested && !dialogClosed)
                    {
                        progressDialog?.AppendLog($"ERROR: {exception.Message}");
                    }
                };

                Logger.Logged += logHandler;
                Logger.ExceptionLogged += exceptionHandler;

                progressDialog?.UpdateStatus("Initializing dry-run validation...");
                progressDialog?.AppendLog("Starting dry-run validation process...");
                progressDialog?.AppendLog("This will simulate the installation to check for errors.");

                ComponentValidationService.ClearValidationCache();
                await Logger.LogVerboseAsync("[MainWindow] Cleared validation cache before validation");

                progressDialog?.UpdateStatus("Running dry-run validation with VirtualFileSystemProvider...");

                Core.Services.Validation.DryRunValidationResult dryRunResult = await Task.Run(() =>
                    Core.Services.Validation.DryRunValidator.ValidateInstallationAsync(
                        MainConfig.AllComponents,
                        skipDependencyCheck: false,
                        cancellationToken: token),
                    token);

                if (token.IsCancellationRequested || dialogClosed)
                {
                    return;
                }

                progressDialog?.UpdateStatus("Analyzing validation results...");
                progressDialog?.AppendLog($"Validation complete. Found {dryRunResult.Issues.Count} issue(s).");

                List<ValidationIssue> modIssues = new List<ValidationIssue>();
                foreach (Core.Services.FileSystem.ValidationIssue coreIssue in dryRunResult.Issues)
                {
                    string icon;
                    if (coreIssue.Severity == Core.Services.FileSystem.ValidationSeverity.Error ||
                        coreIssue.Severity == Core.Services.FileSystem.ValidationSeverity.Critical)
                    {
                        icon = "✗";
                    }
                    else if (coreIssue.Severity == Core.Services.FileSystem.ValidationSeverity.Warning)
                    {
                        icon = "⚠";
                    }
                    else
                    {
                        icon = "ℹ";
                    }

                    modIssues.Add(new ValidationIssue
                    {
                        Icon = icon,
                        ModName = coreIssue.AffectedComponent?.Name ?? "Unknown",
                        IssueType = coreIssue.Category ?? "Validation",
                        Description = coreIssue.Message ?? "No description available",
                        Solution = GetSolutionForIssue(coreIssue),
                        VfsIssue = coreIssue,
                        Component = coreIssue.AffectedComponent,
                    });

                    if (!token.IsCancellationRequested && !dialogClosed)
                    {
                        progressDialog?.AppendLog($"{icon} [{coreIssue.Category}] {coreIssue.Message}");
                    }
                }

                bool validationResult = dryRunResult.IsValid;

                if (!dialogClosed)
                {
                    if (validationResult)
                    {
                        progressDialog?.Complete(true, dryRunResult.GetSummaryMessage());
                    }
                    else
                    {
                        progressDialog?.Complete(false, "Validation failed. Please check the logs for details.");
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (token.IsCancellationRequested || dialogClosed)
                    {
                        return;
                    }

                    try
                    {
                        await Task.Delay(1500, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (token.IsCancellationRequested || dialogClosed)
                    {
                        return;
                    }

                    _ = await ValidationDialog.ShowValidationDialog(
                        this,
                        validationResult,
                        dryRunResult.GetSummaryMessage(),
                        modIssues.Count > 0 ? modIssues : null,
                        systemIssues: null,
                        () => OpenOutputWindow_Click(sender, e)
                    );

                    if (validationResult)
                    {
                        CheckBox step4Check = this.FindControl<CheckBox>(name: "Step4Checkbox");
                        if (step4Check != null)
                        {
                            step4Check.IsChecked = true;
                        }

                        await UpdateStepProgressAsync();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                if (!dialogClosed)
                {
                    progressDialog?.Complete(false, "Validation cancelled.");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[ValidateButton_Click] Failed to validate installation");
                if (!dialogClosed)
                {
                    progressDialog?.Complete(false, "Validation failed with an error. Check logs for details.");
                }
            }
            finally
            {
                if (logHandler != null)
                {
                    Logger.Logged -= logHandler;
                }

                if (exceptionHandler != null)
                {
                    Logger.ExceptionLogged -= exceptionHandler;
                }

                if (ReferenceEquals(_validationCts, cts))
                {
                    _validationCts.Dispose();
                    _validationCts = null;
                }
            }
        }
        private static string GetSolutionForIssue(Core.Services.FileSystem.ValidationIssue issue)
        {
            // Provide user-friendly solutions based on issue category
            if (string.Equals(issue.Category, "ArchiveValidation", StringComparison.Ordinal) ||
                string.Equals(issue.Category, "ExtractArchive", StringComparison.Ordinal))
            {
                return "Verify the archive exists and is not corrupted. Try re-downloading from the mod link.";
            }

            if ((string.Equals(issue.Category, "MoveFile", StringComparison.Ordinal) ||
                 string.Equals(issue.Category, "CopyFile", StringComparison.Ordinal)) &&
                issue.Message?.Contains("does not exist") == true)
            {
                return "The required file is missing. This may indicate an incomplete mod archive or incorrect source path.";
            }

            if ((string.Equals(issue.Category, "MoveFile", StringComparison.Ordinal) ||
                 string.Equals(issue.Category, "CopyFile", StringComparison.Ordinal)) &&
                issue.Message?.Contains("already exists") == true)
            {
                return "File conflict detected. This may be expected - ensure mod installation order is correct.";
            }

            if (string.Equals(issue.Category, "DeleteFile", StringComparison.Ordinal))
            {
                return "File does not exist to delete. This may indicate incorrect instruction order.";
            }

            if (string.Equals(issue.Category, "ExecuteProcess", StringComparison.Ordinal))
            {
                return "The required executable is missing. Verify the mod archive was extracted correctly.";
            }

            return string.Empty;
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void AddComponentButton_Click(
            [CanBeNull] object sender,
            [CanBeNull] RoutedEventArgs e)
        {
            try
            {
                ModComponent newComponent = _componentEditorService.CreateNewComponent();
                LoadComponentDetails(newComponent);
                await ProcessComponentsAsync(MainConfig.AllComponents);
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[AddComponentButton_Click] Failed to add component");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void RefreshComponents_Click(
            [CanBeNull] object sender,
            [CanBeNull] RoutedEventArgs e)
        {
            try
            {
                await ProcessComponentsAsync(MainConfig.AllComponents);
            }
            catch (Exception exc)
            {
                await Logger.LogExceptionAsync(exc, "[RefreshComponents_Click] Failed to refresh components");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void CloseTOMLFile_Click(
            [CanBeNull] object sender,
            [CanBeNull] RoutedEventArgs e)
        {
            try
            {
                await Logger.LogAsync(message: "Closing TOML configuration and clearing component list...");

                MainConfigInstance.allComponents = new List<ModComponent>();
                _lastLoadedFileName = null;

                SetCurrentModComponent(c: null);

                SummaryTabItem.IsVisible = false;
                GuiEditTabItem.IsVisible = false;
                RawEditTabItem.IsVisible = false;

                SetTabInternal(TabControl, InitialTab);

                ModListBox?.Items.Clear();
                await UpdateStepProgressAsync();
                UpdateModCounts();
                ExitWizardMode();
                UpdateWorkflowSurfaces();
                await Logger.LogAsync(message: "TOML configuration closed successfully. ModComponent list cleared.");
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[CloseTOMLFile_Click] Failed to close TOML file");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void RemoveComponentButton_Click(
            [CanBeNull] object sender,
            [CanBeNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await Logger.LogAsync(message: "No component loaded into editor - nothing to remove.");
                    return;
                }

                bool removed = await _componentEditorService.RemoveComponentAsync(CurrentComponent);

                if (removed)
                {
                    SetCurrentModComponent(c: null);
                    await ProcessComponentsAsync(MainConfig.AllComponents);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[RemoveComponentButton_Click] Failed to remove component");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void SetDirectories_Click(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e,
            CancellationToken cancellationToken = default)
        {
            try
            {
                IStorageFolder startFolder = null;
                if (!(MainConfig.DestinationPath is null))
                {
                    startFolder = await StorageProvider.TryGetFolderFromPathAsync(MainConfig.DestinationPath.FullName);
                }

                string[] result = await _dialogService.ShowFileDialogAsync(
                    windowName: "Select your <<kotorDirectory>> (path to the game install)",
                    isFolderDialog: true,
                    startFolder: startFolder
                );
                if (result?.Length > 0)
                {
                    string chosenFolder = result[0];
                    if (chosenFolder != null)
                    {
                        var kotorInstallDir = new DirectoryInfo(chosenFolder);
                        MainConfigInstance.destinationPath = kotorInstallDir;
                    }
                }
                else
                {
                    await Logger.LogVerboseAsync("User cancelled selecting <<kotorDirectory>>");
                }
                if (!(MainConfig.SourcePath is null))
                {
                    startFolder = await StorageProvider.TryGetFolderFromPathAsync(MainConfig.SourcePath.FullName) ?? startFolder;
                }

                result = await _dialogService.ShowFileDialogAsync(
                    windowName: "Select your <<modDirectory>> where ALL your mods are downloaded.",
                    isFolderDialog: true,
                    startFolder: startFolder
                );
                if (result?.Length > 0)
                {
                    string chosenFolder = result[0];
                    if (chosenFolder != null)
                    {
                        var modDirectory = new DirectoryInfo(chosenFolder);
                        MainConfigInstance.sourcePath = modDirectory;
                    }
                }
                else
                {
                    await Logger.LogVerboseAsync(message: "User cancelled selecting <<modDirectory>>");
                }

                await UpdateStepProgressAsync();
            }
            catch (ArgumentNullException)
            {
                await Logger.LogVerboseAsync(message: "User cancelled selecting <<modDirectory>>");
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[SetDirectories_Click] Failed to set directories");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void InstallModSingle_Click(
            [CanBeNull] object sender,
            [CanBeNull] RoutedEventArgs e)
        {
            try
            {
                if (_installRunning)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        "There's already another installation running, please check the output window."
                    );
                    return;
                }
                if (MainConfigInstance is null || MainConfig.DestinationPath is null)
                {
                    var informationDialog = new InformationDialog { InfoText = "Please set your directories first" };
                    _ = await informationDialog.ShowDialog<bool?>(this);
                    return;
                }
                if (CurrentComponent is null)
                {
                    var informationDialog = new InformationDialog
                    {
                        InfoText = "Please choose a mod to install from the left list first",
                    };
                    _ = await informationDialog.ShowDialog<bool?>(this);
                    return;
                }
                string name = CurrentComponent.Name;
                bool? confirm = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    CurrentComponent.Directions
                    + Environment.NewLine
                    + Environment.NewLine
                    + "Press Yes to execute the provided directions now."
                );
                if (confirm != true)
                {
                    await Logger.LogAsync($"User cancelled install of '{name}'");
                    return;
                }
                try
                {
                    _installRunning = true;
                    DateTime startTime = DateTime.UtcNow;
                    ModComponent.InstallExitCode exitCode;

                    using (Activity activity = _telemetryService?.StartActivity($"mod.install.{name}"))
                    {
                        activity?.SetTag("mod.name", name);
                        activity?.SetTag("mod.guid", CurrentComponent.Guid);

                        exitCode = await InstallationService.InstallSingleComponentAsync(
                            CurrentComponent,
                            MainConfig.AllComponents
                        );
                        _installRunning = false;

                        TimeSpan duration = DateTime.UtcNow - startTime;
                        bool success = exitCode == ModComponent.InstallExitCode.Success;

                        _telemetryService?.RecordModInstallation(
                            name,
                            success,
                            duration.TotalMilliseconds,
                            success ? null : UtilityHelper.GetEnumDescription(exitCode)?.ToString()
                        );
                    }

                    if (exitCode != ModComponent.InstallExitCode.Success)
                    {
                        await InformationDialog.ShowInformationDialogAsync(
                            this,
                            $"There was a problem installing '{name}':"
                            + Environment.NewLine
                            + UtilityHelper.GetEnumDescription(exitCode)
                            + Environment.NewLine
                            + Environment.NewLine
                            + " Check the output window for details."
                        );
                    }
                    else
                    {
                        await Logger.LogAsync($"Successfully installed '{name}'");
                        await UpdateStepProgressAsync();
                    }
                }
                catch (Exception)
                {
                    _installRunning = false;
                    throw;
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[InstallModSingle_Click] Failed to install mod single");
            }
        }

        [UsedImplicitly]
        private void StartInstall_Click(
            [CanBeNull] object sender,
            [NotNull] RoutedEventArgs e,
            [NotNull] CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogVerbose("[StartInstall_Click] Cancellation requested");
                return;
            }

            // Clear validation cache before running installation
            ComponentValidationService.ClearValidationCache();
            Logger.LogVerbose("[MainWindow] Cleared validation cache before installation");

            List<ModComponent> components = MainConfig.AllComponents;

            if (components is null || components.Count == 0)
            {
                Logger.LogWarning("[StartInstall_Click] Cannot start installation: no instruction components are loaded.");

                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        "You need to load an instruction set before starting the install wizard."
                    );
                });

                return;
            }

            _telemetryService?.RecordUiInteraction("click", "StartInstallButton");
            _telemetryService?.RecordEvent("installation.started", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["selected_mod_count"] = components.Count(c => c.IsSelected),
                ["total_mod_count"] = components.Count,
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_installRunning)
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await InformationDialog.ShowInformationDialogAsync(
                                this,
                                message: "[StartInstall_Click] There's already an installation running, please check the output window."
                            );
                        });
                        return;
                    }

                    (bool success, string informationMessage) = await InstallationService.ValidateInstallationEnvironmentAsync(
                        MainConfigInstance,
                        async message => await ConfirmationDialog.ShowConfirmationDialogAsync(this, message) == true
                    );

                    if (!success)
                    {
                        await Dispatcher.UIThread.InvokeAsync((async () =>
                        {
                            await InformationDialog.ShowInformationDialogAsync(this, informationMessage);
                        }));
                        return;
                    }

                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await StartInstallationProcess();
                    });
                }
                catch (Exception ex)
                {
                    await Logger.LogExceptionAsync(ex, "[StartInstall_Click] Failed to start installation");
                }
            }, _installSuggestCts.Token);
        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async Task StartInstallationProcess([NotNull] CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await Logger.LogVerboseAsync("[StartInstallationProcess] Cancellation requested");
                return;
            }

            try
            {
                await UpdateStepProgressAsync();
                if (await ConfirmationDialog.ShowConfirmationDialogAsync(
                        this,
                        MainConfig.InstallationWarningContent
                    )
                    != true)
                {
                    return;
                }
                if (await ConfirmationDialog.ShowConfirmationDialogAsync(this, confirmText: "Really install all mods?") != true)
                {
                    return;
                }

                var progressWindow = new ProgressWindow { ProgressBar = { Value = 0 }, Topmost = true };
                DateTime installStartTime = DateTime.UtcNow;
                int warningCount = 0;
                int errorCount = 0;
                void LogCounter(string message)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(message))
                        {
                            return;
                        }

                        if (message.IndexOf(value: "[Warning]", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            warningCount++;
                        }

                        if (message.IndexOf(value: "[Error]", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            errorCount++;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.LogException(e);
                    }
                }
                void ExceptionCounter(Exception _)
                {
                    try { errorCount++; }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex);
                    }
                }
                Logger.Logged += LogCounter;
                Logger.ExceptionLogged += ExceptionCounter;
                progressWindow.CancelRequested += (_, __) =>

                    progressWindow.Close();
                _isClosingProgressWindow = false;
                if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
                {
                    await Logger.LogVerboseAsync("[StartInstallationProcess] Disabling the close button on the console window, to prevent an install from being interrupted...");
                    ConsoleConfig.DisableConsoleCloseButton();
                }

                try
                {
                    await Logger.LogAsync("[StartInstallationProcess] Start installing all mods...");
                    _installRunning = true;

                    progressWindow.Closed += ProgressWindowClosed;
                    progressWindow.Closing += async (sender2, e2) =>
                    {

                        if (_isClosingProgressWindow)
                        {
                            return;
                        }

                        e2.Cancel = true;

                        bool? result = await ConfirmationDialog.ShowConfirmationDialogAsync(
                            this,
                            confirmText:
                            "Closing the progress window will stop the install after the current instruction completes. Really cancel the install?"
                        );

                        if (!(result is true))
                        {
                            return;
                        }

                        _isClosingProgressWindow = true;

                        progressWindow.Close();
                    };
                    progressWindow.Show();
                    _progressWindowClosed = false;
                    ModComponent.InstallExitCode exitCode = ModComponent.InstallExitCode.UnknownError;
                    var selectedMods = MainConfig.AllComponents.Where(thisComponent => thisComponent.IsSelected).ToList();
                    for (int index = 0; index < selectedMods.Count; index++)
                    {
                        if (_progressWindowClosed)
                        {
                            _installRunning = false;
                            await Logger.LogAsync(message: "[StartInstallationProcess] User cancelled install by closing the progress window.");
                            return;
                        }
                        ModComponent component = selectedMods[index];
                        await Dispatcher.UIThread.InvokeAsync(
                            async () =>
                            {
                                progressWindow.ProgressTextBlock.Text = $"Installing '{component.Name}'..."
                                                                        + Environment.NewLine
                                                                        + Environment.NewLine
                                                                        + "Executing the provided directions..."
                                                                        + Environment.NewLine
                                                                        + Environment.NewLine
                                                                        + component.Directions;
                                double percentComplete = selectedMods.Count == 0 ? 0 : (double)index / selectedMods.Count;
                                progressWindow.Topmost = true;
                                int installedCount = index;
                                progressWindow.UpdateMetrics(
                                    percentComplete,
                                    installedCount,
                                    selectedMods.Count,
                                    installStartTime,
                                    warningCount,
                                    errorCount,
                                    component.Name
                                );

                                await Task.Delay(millisecondsDelay: 100, _installSuggestCts.Token);
                                await Dispatcher.UIThread.InvokeAsync(() => { });
                                await Task.Delay(millisecondsDelay: 50, _installSuggestCts.Token);
                            }
                        );

                        await Task.Yield();
                        await Task.Delay(millisecondsDelay: 200, _installSuggestCts.Token);
                        if (!component.IsSelected)
                        {
                            await Logger.LogAsync($"Skipping install of '{component.Name}' (unchecked)");
                            continue;
                        }
                        await Logger.LogAsync($"Start Install of '{component.Name}'...");

                        if (component.WidescreenOnly && !string.IsNullOrWhiteSpace(MainConfig.WidescreenWarningContent))
                        {
                            bool shouldContinue = await ShowWidescreenNotificationAsync();
                            if (!shouldContinue)
                            {
                                await Logger.LogAsync(message: "[StartInstallationProcess] Install cancelled by user at widescreen notification");
                                exitCode = ModComponent.InstallExitCode.UserCancelledInstall;
                                break;
                            }
                        }

                        exitCode = await InstallationService.InstallSingleComponentAsync(component,
                            MainConfig.AllComponents,
                            _installSuggestCts.Token
                        );

                        await Logger.LogAsync($"Install of '{component.Name}' finished with exit code {UtilityHelper.GetEnumDescription(exitCode)}");

                        if (exitCode != ModComponent.InstallExitCode.Success)
                        {
                            bool? confirm = await ConfirmationDialog.ShowConfirmationDialogAsync(
                                this,
                                $"There was a problem installing '{component.Name}':"
                                + Environment.NewLine
                                + UtilityHelper.GetEnumDescription(exitCode)
                                + Environment.NewLine
                                + Environment.NewLine
                                + " Check the output window for details."
                                + Environment.NewLine
                                + Environment.NewLine
                                + $"Skip '{component.Name}' and install the next mod anyway? (NOT RECOMMENDED!)"
                            );
                            if (confirm is true)
                            {
                                continue;
                            }

                            await Logger.LogAsync("Install cancelled");
                            break;
                        }
                        await Logger.LogAsync($"Finished installing '{component.Name}'");

                    }
                    if (exitCode != ModComponent.InstallExitCode.Success)
                    {
                        return;
                    }

                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Install Completed. Check the output window for information."
                    );
                    await Logger.LogAsync("Install completed.");
                    await UpdateStepProgressAsync();
                }
                catch (Exception)
                {
                    await Logger.LogErrorAsync("[StartInstall_Click] Terminating install due to unhandled exception:");
                    throw;
                }
                finally
                {

                    _installRunning = false;
                    _isClosingProgressWindow = true;
                    progressWindow.Close();

                    Logger.Logged -= LogCounter;
                    Logger.ExceptionLogged -= ExceptionCounter;
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[StartInstall_Click] Error in StartInstallationProcess");
            }
        }

        private void ProgressWindowClosed(
            [CanBeNull] object sender,
            [CanBeNull] EventArgs e)
        {
            try
            {
                if (!(sender is ProgressWindow progressWindow))
                {
                    return;
                }

                progressWindow.ProgressBar.Value = 0;
                progressWindow.Closed -= ProgressWindowClosed;
                progressWindow.Dispose();
                _progressWindowClosed = true;
                if (UtilityHelper.GetOperatingSystem() != OSPlatform.Windows)
                {
                    return;
                }

                Logger.LogVerbose("[ProgressWindowClosed] Install terminated, re-enabling the close button in the console window");
                ConsoleConfig.EnableCloseButton();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[ProgressWindowClosed] Error in ProgressWindowClosed");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async void DocsButton_Click(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (MainConfig.AllComponents.Count == 0)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "No mod components available to generate documentation."
                    );
                    return;
                }

                string file = await SaveFileAsync(saveFileName: "ModList_Documentation.md");

                if (file is null)
                {
                    await Logger.LogVerboseAsync("[DocsButton_Click] Documentation export cancelled by user.");
                    return;
                }

                await Logger.LogAsync($"[DocsButton_Click] Generating documentation for {MainConfig.AllComponents.Count} mod component(s)...");
                string docs = ModComponentSerializationService.GenerateModDocumentation(
                    MainConfig.AllComponents,
                    MainConfig.PreambleContent,
                    MainConfig.EpilogueContent,
                    MainConfig.WidescreenWarningContent,
                    MainConfig.AspyrExclusiveWarningContent);

                if (string.IsNullOrWhiteSpace(docs))
                {
                    await Logger.LogWarningAsync("[DocsButton_Click] Generated documentation is empty.");
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "The generated documentation is empty. Please check your mod components."
                    );
                    return;
                }

                await FileUtilities.SaveDocsToFileAsync(file, docs);

                string successMessage = $"Successfully generated and saved documentation for {MainConfig.AllComponents.Count} mod component(s) to:\n\n{file}";
                await Logger.LogAsync($"[DocsButton_Click] Documentation saved to '{file}'");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: successMessage
                );
            }
            catch (IOException ioEx)
            {
                await Logger.LogExceptionAsync(ioEx, "[DocsButton_Click] Error in DocsButton_Click: IO error while saving documentation file");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: "Failed to save documentation file. The file may be in use or the path may be invalid."
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"Error: {ioEx.Message}"
                );
            }
            catch (UnauthorizedAccessException uaEx)
            {
                await Logger.LogExceptionAsync(uaEx, "[DocsButton_Click] Error in DocsButton_Click: Access denied while saving documentation");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: "Access denied while saving the documentation file. Please check file permissions."
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"Error: {uaEx.Message}"
                );
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[DocsButton_Click] Error in DocsButton_Click: Unexpected error generating and saving documentation");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: "An unexpected error occurred while generating and saving documentation."
                    + Environment.NewLine
                    + Environment.NewLine
                    + $"Error: {ex.Message}"
                );
            }
        }
        [UsedImplicitly]
        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void TabControl_SelectionChanged(
            [NotNull] object sender,
            [NotNull] SelectionChangedEventArgs e)
        {
            // Ensure we are on the UI thread before accessing Avalonia controls
            if (!Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(() => TabControl_SelectionChanged(sender, e));
                return;
            }
            try
            {
                await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] START - IgnoreInternalTabChange={IgnoreInternalTabChange}");
                if (IgnoreInternalTabChange)
                {
                    await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Ignoring internal tab change, returning early");
                    return;
                }
                try
                {
                    if (!(sender is TabControl tabControl))
                    {
                        await Logger.LogErrorAsync(message: "[TabControl_SelectionChanged] Sender is not a TabControl control");
                        return;
                    }
                    await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] TabControl found, SelectedIndex={tabControl.SelectedIndex}");

                    // Declare attemptedTabSelection early to avoid use-before-declaration error
                    TabItem attemptedTabSelection = null;
                    if (e.AddedItems != null && e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem attemptedTab)
                    {
                        attemptedTabSelection = attemptedTab;
                    }

                    if (CurrentComponent is null)
                    {
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] CurrentComponent is null, tabs can't be used");

                        // Check if user is trying to access Editor tab specifically
                        string attemptedTabName = GetControlNameFromHeader(attemptedTabSelection)?.ToLowerInvariant();
                        if (string.Equals(attemptedTabName, "editor", StringComparison.Ordinal))
                        {
                            await Logger.LogVerboseAsync("[TabControl_SelectionChanged] User attempted to access Editor tab without selecting a mod");
                            await InformationDialog.ShowInformationDialogAsync(
                                this,
                                "Choose a mod in the left list before clicking the Editor tabs.",
                                "Editor Tab Unavailable"
                            );
                        }

                        SetTabInternal(tabControl, InitialTab);
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Set to InitialTab");
                        return;
                    }
                    await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] CurrentComponent='{CurrentComponent.Name}' (GUID={CurrentComponent.Guid})");

                    if (e.RemovedItems.IsNullOrEmptyOrAllNull() || !(e.RemovedItems[0] is TabItem lastSelectedTabItem))
                    {
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Previous tab item could not be resolved");
                        return;
                    }
                    await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] Previous tab: '{lastSelectedTabItem.Header}' (IsVisible={lastSelectedTabItem.IsVisible})");

                    if (attemptedTabSelection is null)
                    {
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Attempted tab item could not be resolved");
                        return;
                    }
                    await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] Target tab: '{attemptedTabSelection.Header}' (IsVisible={attemptedTabSelection.IsVisible})");

                    if (MainConfig.AllComponents.IsNullOrEmptyCollection() || CurrentComponent is null)
                    {
                        await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] No config loaded (ComponentCount={MainConfig.AllComponents.Count}), defaulting to initial tab");
                        SetTabInternal(tabControl, InitialTab);
                        return;
                    }
                    string tabName = GetControlNameFromHeader(attemptedTabSelection)?.ToLowerInvariant();
                    string lastTabName = GetControlNameFromHeader(lastSelectedTabItem)?.ToLowerInvariant();
                    await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] TabNames: from='{lastTabName}' to='{tabName}'");

                    if (string.Equals(tabName, lastTabName, StringComparison.Ordinal))
                    {
                        await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] Selected tab is already the current tab '{tabName}', returning");
                        return;
                    }
                    await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] Preparing to swap tabs from '{lastTabName}' to '{tabName}'");
                    bool shouldSwapTabs = true;
                    if (string.Equals(tabName, "summary", StringComparison.Ordinal))
                    {
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Target is 'summary', refreshing markdown content");

                        // Refresh the markdown content in the SummaryTab
                        SummaryTabControl?.RefreshMarkdownContent();
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] SummaryTab markdown content refreshed");
                    }
                    else if (string.Equals(tabName, "raw", StringComparison.Ordinal))
                    {
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Target is 'raw', loading into RawTabControl");
                        await LoadIntoRawEditTextBoxAsync(CurrentComponent);
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] LoadIntoRawEditTextBox completed");
                    }
                    else if (string.Equals(lastTabName, "raw", StringComparison.Ordinal))
                    {
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Source was 'raw', checking if changes should be saved");
                        shouldSwapTabs = await ShouldSaveChangesAsync();
                        await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] ShouldSaveChanges returned: {shouldSwapTabs}");
                        if (shouldSwapTabs)
                        {
                            await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Unloading raw editor");
                            // Clear text on UI thread
                            TextBox rawTextBox = RawTabControl.GetRawEditTextBox();
                            if (rawTextBox != null)
                            {
                                rawTextBox.Text = string.Empty;
                            }
                        }
                    }
                    else if (string.Equals(tabName, "editor", StringComparison.Ordinal))
                    {
                        await Logger.LogVerboseAsync($"[TabControl_SelectionChanged] Target is 'editor', CurrentComponent='{CurrentComponent?.Name}', InstructionCount={CurrentComponent?.Instructions.Count}, OptionCount={CurrentComponent?.Options.Count}");
                    }

                    if (!shouldSwapTabs)
                    {
                        await Logger.LogVerboseAsync("[TabControl_SelectionChanged] shouldSwapTabs=false, reverting to previous tab");
                        SetTabInternal(tabControl, lastSelectedTabItem);
                        return;
                    }
                    await Logger.LogVerboseAsync("[TabControl_SelectionChanged] Setting visibility for controls based on selected tab");

                    // Note: RawTab format textboxes are managed internally by the RawTab control
                    // and should not have their visibility controlled by the main tab selection
                    await Logger.LogVerboseAsync("[TabControl_SelectionChanged] COMPLETED SUCCESSFULLY");
                }
                catch (Exception exception)
                {
                    await Logger.LogExceptionAsync(exception, "[TabControl_SelectionChanged] Inner exception");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[TabControl_SelectionChanged] Outer exception");
            }
        }

        [UsedImplicitly]
        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {

                if (!(sender is ComboBox comboBox))
                {
                    Logger.Log(message: "[ComboBox_SelectionChanged] Sender is not a ComboBox.");
                    return;
                }

                if (!(comboBox.DataContext is Instruction thisInstruction))
                {
                    Logger.Log(message: "[ComboBox_SelectionChanged] ComboBox's DataContext must be an instruction for this method.");
                    return;
                }

                string selectedItem = comboBox.SelectedItem as string;

                var itemsList = comboBox.Items.Cast<string>().ToList();
                int index = itemsList.IndexOf(selectedItem);

                thisInstruction.Arguments = index.ToString();
                thisInstruction.Action = Instruction.ActionType.Patcher;
            }
            catch (Exception exception)
            {
                Logger.LogException(exception, "[ComboBox_SelectionChanged] Error changing combo box selection");
            }
        }

        [CanBeNull]
        private TabItem GetCurrentTabItem([CanBeNull] TabControl tabControl) =>
            (tabControl ?? TabControl)?.SelectedItem as TabItem;

        [CanBeNull]
        private static string GetControlNameFromHeader([CanBeNull] TabItem tabItem) => tabItem?.Header?.ToString();
        private void SetTabInternal([NotNull] TabControl tabControl, TabItem tabItem)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetTabInternal(tabControl, tabItem), DispatcherPriority.Normal);
                return;
            }
            if (tabControl is null)
            {
                throw new ArgumentNullException(nameof(tabControl));
            }

            IgnoreInternalTabChange = true;
            tabControl.SelectedItem = tabItem;
            IgnoreInternalTabChange = false;
        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void LoadComponentDetails([NotNull] ModComponent selectedComponent)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => LoadComponentDetails(selectedComponent), DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (selectedComponent is null)
                {
                    throw new ArgumentNullException(nameof(selectedComponent));
                }

                await Logger.LogVerboseAsync(
                    $"[LoadComponentDetails] START for component '{selectedComponent.Name}' (GUID={selectedComponent.Guid})");
                await Logger.LogVerboseAsync(
                    $"[LoadComponentDetails] CurrentComponent={(CurrentComponent is null ? "null" : $"'{CurrentComponent.Name}' (GUID={CurrentComponent.Guid})")}");

                if (selectedComponent == CurrentComponent)
                {
                    await Logger.LogVerboseAsync(
                        "[LoadComponentDetails] Same component already loaded, no action needed");
                    return;
                }

                bool confirmLoadOverwrite = true;
                string currentTabName = GetControlNameFromHeader(GetCurrentTabItem(TabControl))?.ToLowerInvariant();
                await Logger.LogVerboseAsync($"[LoadComponentDetails] Current tab: '{currentTabName}'");

                if (string.Equals(currentTabName, "raw", StringComparison.Ordinal) && CurrentComponent != null)
                {
                    await Logger.LogVerboseAsync(
                        "[LoadComponentDetails] Current tab is 'raw' and switching components, checking if changes should be saved");
                    confirmLoadOverwrite = await ShouldSaveChangesAsync();
                    await Logger.LogVerboseAsync(
                        $"[LoadComponentDetails] ShouldSaveChanges returned: {confirmLoadOverwrite}");
                }

                if (!confirmLoadOverwrite)
                {
                    await Logger.LogVerboseAsync("[LoadComponentDetails] Load cancelled by user, returning");
                    return;
                }

                await Logger.LogVerboseAsync(
                    $"[LoadComponentDetails] Setting CurrentComponent to '{selectedComponent.Name}'");
                SetCurrentModComponent(selectedComponent);
                await Logger.LogVerboseAsync("[LoadComponentDetails] SetCurrentModComponent completed");

                if (string.Equals(currentTabName, "raw", StringComparison.Ordinal))
                {
                    await Logger.LogVerboseAsync(
                        "[LoadComponentDetails] Current tab is 'raw', loading new component into RawTabControl");
                    await LoadIntoRawEditTextBoxAsync(selectedComponent);
                    await Logger.LogVerboseAsync("[LoadComponentDetails] LoadIntoRawEditTextBox completed");
                }

                await Logger.LogVerboseAsync(
                    $"[LoadComponentDetails] InitialTab.IsSelected={InitialTab.IsSelected}, TabControl.SelectedIndex={TabControl.SelectedIndex}");
                if (InitialTab.IsSelected || TabControl.SelectedIndex == int.MaxValue)
                {
                    await Logger.LogVerboseAsync("[LoadComponentDetails] Switching to SummaryTabItem");
                    SetTabInternal(TabControl, SummaryTabItem);
                }
                else
                {
                    await Logger.LogVerboseAsync($"[LoadComponentDetails] Keeping current tab '{currentTabName}'");
                }

                await Logger.LogVerboseAsync("[LoadComponentDetails] COMPLETED");
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[LoadComponentDetails] Error in LoadComponentDetails");
            }
        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public void SetCurrentModComponent([CanBeNull] ModComponent c)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetCurrentModComponent(c), DispatcherPriority.Normal);
                return;
            }
            Logger.LogVerbose($"[SetCurrentModComponent] START with component={(c is null ? "null" : $"'{c.Name}' (GUID={c.Guid})")}");

            bool isNewComponent = c != null && c != CurrentComponent;
            Logger.LogVerbose($"[SetCurrentModComponent] isNewComponent={isNewComponent}, CurrentComponent={(CurrentComponent is null ? "null" : $"'{CurrentComponent.Name}'")}");
            Logger.LogVerbose("[SetCurrentModComponent] Setting CurrentComponent property");
            CurrentComponent = c;
            Logger.LogVerbose("[SetCurrentModComponent] CurrentComponent property set");
            Logger.LogVerbose($"[SetCurrentModComponent] Setting EditorTabControl.CurrentComponent (current={(EditorTabControl.CurrentComponent is null ? "null" : "not null")})");
            EditorTabControl.CurrentComponent = c;
            Logger.LogVerbose($"[SetCurrentModComponent] EditorTabControl.CurrentComponent set to {(c is null ? "null" : $"'{c.Name}'")}");
            Logger.LogVerbose("[SetCurrentModComponent] Setting SummaryTabControl.CurrentComponent");
            SummaryTabControl.CurrentComponent = c;
            Logger.LogVerbose($"[SetCurrentModComponent] SummaryTabControl.CurrentComponent set to {(c is null ? "null" : $"'{c.Name}'")}");
            if (c is null)
            {
                Logger.LogVerbose("[SetCurrentModComponent] ModComponent is null, returning early");
                return;
            }

            Logger.LogVerbose($"[SetCurrentModComponent] Making tabs visible (Summary={SummaryTabItem.IsVisible}, GuiEdit={GuiEditTabItem.IsVisible}, RawEdit={RawEditTabItem.IsVisible})");
            if (EditorMode)
            {
                SummaryTabItem.IsVisible = true;
                GuiEditTabItem.IsVisible = true;
                RawEditTabItem.IsVisible = true;

                // Cancel any existing pre-resolve operation and debounce timer before starting a new one
                _preResolveCts?.Cancel();
                _preResolveCts?.Dispose();
                _preResolveCts = null;
                _preResolveDebounceTimer?.Stop();
                _preResolveDebounceTimer = null;

                // Network calls should ONLY happen when user explicitly requests them via buttons
                // (e.g., "Resolve Filenames" or "Download" buttons)
                // No automatic network calls when clicking mod list items
            }
            Logger.LogVerbose("[SetCurrentModComponent] Tabs visibility set to true");

            Logger.LogVerbose($"[SetCurrentModComponent] Calling RefreshCategorySelectionControl for component '{c.Name}' with {c.Category.Count} categories");
            RefreshCategorySelectionControl();
            Logger.LogVerbose("[SetCurrentModComponent] RefreshCategorySelectionControl completed");

            Logger.LogVerbose("[SetCurrentModComponent] Markdown content rendering completed");

            Logger.LogVerbose($"[SetCurrentModComponent] Tab check: isNewComponent={isNewComponent}, InitialTab.IsSelected={InitialTab.IsSelected}, SelectedIndex={TabControl.SelectedIndex}, GuiEditTabItem.IsSelected={GuiEditTabItem.IsSelected}, RawEditTabItem.IsSelected={RawEditTabItem.IsSelected}");

            if (isNewComponent && RawEditTabItem.IsSelected)
            {
                Logger.LogVerbose("[SetCurrentModComponent] New component and Raw tab is selected, updating raw editor content");
                // No need for Task.Run - we're already on the UI thread and async operations will yield properly
                Dispatcher.UIThread.Post(async () =>
                {
                    if (await ShouldSaveChangesAsync())
                    {
                        await LoadIntoRawEditTextBoxAsync(c);
                        await Logger.LogVerboseAsync("[SetCurrentModComponent] Raw editor content updated");
                    }
                });
            }

            if (
                isNewComponent
                && (
                    InitialTab.IsSelected
                    || TabControl.SelectedIndex == int.MaxValue
                    || (
                        !GuiEditTabItem.IsSelected
                        && !RawEditTabItem.IsSelected
                    )
                ))
            {
                Logger.LogVerbose("[SetCurrentModComponent] Switching to SummaryTabItem");
                SetTabInternal(TabControl, SummaryTabItem);
            }
            else
            {
                Logger.LogWarning($"[SetCurrentModComponent] Not switching tabs? InitialTab.IsSelected={InitialTab.IsSelected}, TabControl.SelectedIndex={TabControl.SelectedIndex}, GuiEditTabItem.IsSelected={GuiEditTabItem.IsSelected}, RawEditTabItem.IsSelected={RawEditTabItem.IsSelected}, isNewComponent={isNewComponent}");
            }
            Logger.LogVerbose("[SetCurrentModComponent] COMPLETED");
        }

        private async Task LoadIntoRawEditTextBoxAsync([NotNull] ModComponent selectedComponent)
        {
            if (selectedComponent is null)
            {
                throw new ArgumentNullException(nameof(selectedComponent));
            }

            await Logger.LogVerboseAsync($"[LoadIntoRawEditTextBoxAsync] Loading component '{selectedComponent.Name}' into raw editor");

            // RawTab now handles multi-format serialization automatically via RefreshCurrentFormatContent()
            // This is called automatically when CurrentComponent changes, but we call it explicitly here
            // to ensure the content is refreshed even if the component reference hasn't changed
            RawTabControl.RefreshCurrentFormatContent();
        }

        private void RawEditTextBox_LostFocus([NotNull] object sender, [NotNull] RoutedEventArgs e) => e.Handled = true;

        /// <summary>
        /// Loads a config file (TOML, JSON, YAML, or embedded Markdown).
        /// Format is auto-detected by the FileLoadingService.
        /// </summary>
        private async Task<bool> LoadInstructionFileAsync(
            [NotNull] string filePath,
            [CanBeNull] string fileType = "config file")
        {
            bool result = await _fileLoadingService.LoadInstructionFileAsync(filePath, EditorMode, async () => await ProcessComponentsAsync(MainConfig.AllComponents), fileType);
            if (result)
            {
                _lastLoadedFileName = _fileLoadingService.LastLoadedFileName;

                // Apply theme based on TargetGame from loaded file (TargetGame and Theme are the same)
                if (!string.IsNullOrEmpty(MainConfig.TargetGame))
                {
                    string themeToApply = ThemeManager.GetCurrentStylePath();
                    ApplyTheme(themeToApply);
                    ThemeManager.ApplyCurrentToWindow(this);
                    await Logger.LogVerboseAsync($"Applied theme from TargetGame '{MainConfig.TargetGame}': {themeToApply}");
                }

                // In Editor Mode, ask if user wants to resolve download filenames after loading
                if (EditorMode && HasComponentsNeedingResolution(MainConfig.AllComponents))
                {
                    bool? shouldResolve = await AskToResolveFilenamesAsync("load");
                    if (shouldResolve == true)
                    {
                        await Logger.LogVerboseAsync("[LoadInstructionFileAsync] User requested filename resolution after loading");
                        await ResolveAllComponentFilenamesAsync(MainConfig.AllComponents);
                    }
                }

                // If not in Editor Mode and WizardMode is enabled, activate wizard mode
                if (!EditorMode && WizardMode && MainConfig.AllComponents != null && MainConfig.AllComponents.Count > 0)
                {
                    EnterWizardMode();
                }
            }
            UpdateWorkflowSurfaces();
            return result;
        }

        /// <summary>
        /// Public method to auto-load an instruction file from CLI arguments.
        /// </summary>
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        public async void AutoLoadInstructionFileAsync(string filePath)
        {
            try
            {
                await Logger.LogAsync($"[AutoLoadInstructionFileAsync] Loading instruction file from CLI: '{filePath}'");

                string fileName = System.IO.Path.GetFileName(filePath);
                ShowLoadingOverlay($"Loading {fileName}...");

                // Load the file while overlay is shown
                bool success = await LoadInstructionFileAsync(filePath, "CLI-specified file");

                if (success)
                {
                    await Logger.LogAsync($"[AutoLoadInstructionFileAsync] Successfully loaded instruction file");
                }
                else
                {
                    await Logger.LogWarningAsync($"[AutoLoadInstructionFileAsync] Failed to load instruction file");
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[AutoLoadInstructionFileAsync] Exception loading instruction file from CLI");
            }
            finally
            {
                HideLoadingOverlay();
            }
        }

        private async Task<bool> ShouldSaveChangesAsync(bool noPrompt = false)
        {
            try
            {
                // Get the raw text and current format on the UI thread to avoid threading issues
                string rawEditText = await Dispatcher.UIThread.InvokeAsync(() => RawTabControl.GetRawEditTextBox()?.Text ?? string.Empty);
                string currentFormat = await Dispatcher.UIThread.InvokeAsync(() => RawTabControl.GetCurrentFormat());

                bool result = await _componentEditorService.SaveChangesAsync(
                    CurrentComponent,
                    rawEditText,
                    currentFormat,
                    noPrompt
                );

                if (result && CurrentComponent != null)
                {

                    await ProcessComponentsAsync(MainConfig.AllComponents);
                }

                return result;
            }
            catch (Exception ex)
            {
                string output = "An unexpected exception was thrown. Please refer to the output window for details and report this issue to a developer.";
                await Logger.LogExceptionAsync(ex, "[ShouldSaveChangesAsync] Inner exception");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"{output}{Environment.NewLine}{Environment.NewLine}{ex.Message}"
                );
                return false;
            }
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void MoveComponentListItem([CanBeNull] ModComponent componentToMove, int relativeIndex)
        {
            try
            {
                if (componentToMove is null)
                {
                    return;
                }

                _ = ModManagementService.MoveModRelative(componentToMove, relativeIndex);
                await ProcessComponentsAsync(MainConfig.AllComponents);
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[MoveComponentListItem] Inner exception");
            }
        }

        private void OnModOperationCompleted(object sender, ModOperationEventArgs e)
        {
            try
            {
                switch (e.Operation)
                {
                    case ModOperation.Create:
                    case ModOperation.Delete:
                    case ModOperation.Move:
                        Dispatcher.UIThread.Post((async () =>
                        {

                            await ProcessComponentsAsync(MainConfig.AllComponents)
                                .ContinueWith(t =>
                                {
                                    try
                                    {
                                        if (t.Exception != null)
                                        {
                                            Logger.LogException(t.Exception.Flatten(), "[OnModOperationCompleted] Inner exception");
                                        }
                                        else
                                        {
                                            UpdateModCounts();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.LogException(ex, "[OnModOperationCompleted] Inner exception");
                                    }
                                }, TaskScheduler.FromCurrentSynchronizationContext());
                        }));
                        break;
                    case ModOperation.Read:
                    case ModOperation.Update:
                    case ModOperation.Duplicate:
                    case ModOperation.AddDependency:
                    case ModOperation.RemoveDependency:
                    case ModOperation.AddRestriction:
                    case ModOperation.RemoveRestriction:
                    case ModOperation.Batch:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(e), $"Unexpected ModOperation value: {e.Operation}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[OnModOperationCompleted] Inner exception");
            }
        }
        private void OnModValidationCompleted(object sender, ModValidationEventArgs e)
        {
            try
            {

                Dispatcher.UIThread.Post(UpdateModCounts);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[OnModValidationCompleted] Inner exception");
            }
        }

        [UsedImplicitly]
        private void MoveUpButton_Click([NotNull] object sender, [NotNull] RoutedEventArgs e) =>
            MoveComponentListItem(CurrentComponent, relativeIndex: -1);

        [UsedImplicitly]
        private void MoveDownButton_Click([NotNull] object sender, [NotNull] RoutedEventArgs e) =>
            MoveComponentListItem(CurrentComponent, relativeIndex: 1);

        [UsedImplicitly]
        private void GenerateGuidButton_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                RawTabControl.GetGuidTextBox().Text = Guid.NewGuid().ToString();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[GenerateGuidButton_Click] Inner exception");
            }
        }

        /// <summary>
        /// Event handler for the SaveConfigRequested event from ModListSidebar
        /// </summary>
        [UsedImplicitly]
        private void SaveModFileAs_Click(object sender, RoutedEventArgs e)
        {
            SaveModFileAs_ClickWithFormat(sender, e);
        }

        [SuppressMessage("Major Code Smell", "S1172:Unused method parameters should be removed", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void SaveModFileAs_ClickWithFormat(
            [CanBeNull] object sender = null,
            [CanBeNull] RoutedEventArgs e = null,
            [CanBeNull] string format = "toml")
        {
            try
            {

                string extension;
                if (string.Equals(format, "yaml", StringComparison.Ordinal))
                {
                    extension = ".yaml";
                }
                else if (string.Equals(format, "md", StringComparison.Ordinal))
                {
                    extension = ".md";
                }
                else
                {
                    extension = ".toml";
                }

                string defaultFileName = !string.IsNullOrEmpty(_lastLoadedFileName)
                    ? Path.ChangeExtension(_lastLoadedFileName, extension.TrimStart('.'))
                    : $"my_mod_instructions{extension}";

                string filePath = await SaveFileAsync(saveFileName: defaultFileName);
                if (filePath is null)
                {
                    return;
                }

                // In Editor Mode, ask if user wants to resolve download filenames before saving
                if (EditorMode && HasComponentsNeedingResolution(MainConfig.AllComponents))
                {
                    bool? shouldResolve = await AskToResolveFilenamesAsync("save");
                    if (shouldResolve == null)
                    {
                        // User cancelled
                        await Logger.LogVerboseAsync("[SaveModFileAs_ClickWithFormat] User cancelled save after filename resolution prompt");
                        return;
                    }

                    if (shouldResolve == true)
                    {
                        await Logger.LogVerboseAsync("[SaveModFileAs_ClickWithFormat] User requested filename resolution before saving");
                        await ResolveAllComponentFilenamesAsync(MainConfig.AllComponents);
                    }
                }

                await Core.Services.FileLoadingService.SaveToFileAsync(
                    MainConfig.AllComponents,
                    filePath
                );
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[SaveModFileAs_ClickWithFormat] Error in SaveModFileAs_ClickWithFormat");
            }
        }

        public void ComponentCheckboxChecked(
            [NotNull] ModComponent component,
            [CanBeNull][ItemNotNull] HashSet<ModComponent> visitedComponents = null,
            bool suppressErrors = false
            )
        {
            _componentSelectionService.HandleComponentChecked(component, visitedComponents, suppressErrors, RefreshSingleComponentVisuals);
        }

        public void ComponentCheckboxUnchecked(
            [NotNull] ModComponent component,
            [CanBeNull][ItemNotNull] HashSet<ModComponent> visitedComponents = null,
            bool suppressErrors = false)
        {
            _componentSelectionService.HandleComponentUnchecked(
                component,
                visitedComponents ?? new HashSet<ModComponent>(),
                suppressErrors,
                RefreshSingleComponentVisuals
            );
            if (!suppressErrors)
            {
                UpdateModCounts();
            }
        }
        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void OnCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            // Ensure UI thread before any Avalonia control access
            if (!Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(() => OnCheckBoxChanged(sender, e));
                return;
            }
            try
            {
                await Logger.LogVerboseAsync($"[OnCheckBoxChanged] START - sender type: {sender?.GetType().Name ?? "null"}");

                if (_suppressComponentCheckboxEvents)
                {
                    await Logger.LogVerboseAsync("[OnCheckBoxChanged] Suppressing component checkbox events, returning");
                    return;
                }

                if (!(sender is CheckBox checkBox))
                {
                    await Logger.LogVerboseAsync("[OnCheckBoxChanged] Sender is not a CheckBox, returning");
                    return;
                }

                var visualPath = new List<string>();
                StyledElement current = checkBox.Parent;
                while (current != null && visualPath.Count < 5)
                {
                    visualPath.Add($"{current.GetType().Name}(Name='{current.Name}')");
                    current = current.Parent;
                }
                await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Visual tree path: {string.Join(" -> ", visualPath)}");
                await Logger.LogVerboseAsync($"[OnCheckBoxChanged] CheckBox.Tag type: {checkBox.Tag?.GetType().Name ?? "null"}");

                // Check for Option FIRST since both Option and ModComponent might be in the visual tree
                if (checkBox.Tag is Option thisOption)
                {
                    await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Option: '{thisOption.Name}' (GUID={thisOption.Guid}), IsChecked={checkBox.IsChecked}");

                    ModComponent parentComponent = MainConfig.AllComponents.Find(c => c.Options.Contains(thisOption));

                    if (parentComponent != null)
                    {
                        if (checkBox.IsChecked == true)
                        {
                            // HandleOptionChecked will:
                            // 1. Auto-check parent component if not selected
                            // 2. Handle dependencies/restrictions via HandleComponentChecked
                            // 3. Auto-select first option if needed
                            await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Calling HandleOptionChecked for option '{thisOption.Name}'");
                            _componentSelectionService.HandleOptionChecked(thisOption, parentComponent, RefreshSingleComponentVisuals);
                        }
                        else if (checkBox.IsChecked == false)
                        {
                            // HandleOptionUnchecked will:
                            // 1. Auto-uncheck parent component if all options are unchecked
                            // 2. Handle cascading dependencies
                            await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Calling HandleOptionUnchecked for option '{thisOption.Name}'");
                            _componentSelectionService.HandleOptionUnchecked(thisOption, parentComponent, RefreshSingleComponentVisuals);
                        }

                        UpdateModCounts();
                        await UpdateStepProgressAsync();
                        ResetDownloadStatusDisplay();
                    }

                    await Logger.LogVerboseAsync("[OnCheckBoxChanged] COMPLETED (Option)");
                }
                else if (checkBox.Tag is ModComponent thisComponent)
                {
                    await Logger.LogVerboseAsync($"[OnCheckBoxChanged] ModComponent: '{thisComponent.Name}' (GUID={thisComponent.Guid}), IsChecked={checkBox.IsChecked}");
                    if (checkBox.IsChecked == true)
                    {
                        // Check if component is selectable (Aspyr exclusivity check)
                        if (!_componentSelectionService.IsComponentSelectable(thisComponent))
                        {
                            // Revert the checkbox state
                            _suppressComponentCheckboxEvents = true;
                            checkBox.IsChecked = false;
                            _suppressComponentCheckboxEvents = false;

                            await Logger.LogWarningAsync($"[OnCheckBoxChanged] Component '{thisComponent.Name}' is Aspyr-exclusive but game is not Aspyr version");
                            await InformationDialog.ShowInformationDialogAsync(
                                this,
                                title: "Aspyr Version Required",
                                message: $"This mod ({thisComponent.Name}) requires the Aspyr patch version of KOTOR 2.\n\nIf you have the legacy version, this mod will not work and has been skipped."
                            );
                            return;
                        }

                        await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Checking component '{thisComponent.Name}'");
                        // ComponentCheckboxChecked calls HandleComponentChecked which will:
                        // 1. Handle dependencies/restrictions
                        // 2. Auto-select first option if none are selected (lines 84-100 in service)
                        ComponentCheckboxChecked(thisComponent, new HashSet<ModComponent>());
                    }
                    else if (checkBox.IsChecked == false)
                    {
                        await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Unchecking component '{thisComponent.Name}'");
                        ComponentCheckboxUnchecked(thisComponent, new HashSet<ModComponent>());

                        // When unchecking a component, also uncheck all its options
                        if (thisComponent.Options != null && thisComponent.Options.Count > 0)
                        {
                            _suppressComponentCheckboxEvents = true;
                            try
                            {
                                foreach (Option option in thisComponent.Options)
                                {
                                    if (option.IsSelected)
                                    {
                                        await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Unchecking option '{option.Name}' because parent component was unchecked");
                                        option.IsSelected = false;
                                    }
                                }
                            }
                            finally
                            {
                                _suppressComponentCheckboxEvents = false;
                            }
                        }
                    }
                    else
                    {
                        await Logger.LogVerboseAsync($"[OnCheckBoxChanged] Could not determine checkBox state for component '{thisComponent.Name}' (IsChecked={checkBox.IsChecked})");
                    }
                    await Logger.LogVerboseAsync("[OnCheckBoxChanged] Updating step progress and mod counts");

                    await UpdateStepProgressAsync();
                    UpdateModCounts();

                    ResetDownloadStatusDisplay();
                    await Logger.LogVerboseAsync("[OnCheckBoxChanged] COMPLETED");
                }
                else
                {
                    await Logger.LogVerboseAsync($"CheckBox.Tag is neither a ModComponent nor an Option! Tag value: {checkBox.Tag}");
                }
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[OnCheckBoxChanged] Exception occurred");
            }
        }

        public void OnComponentCheckBoxChanged(object sender, RoutedEventArgs e) => OnCheckBoxChanged(sender, e);

        private static void SummaryOptionBorder_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            e.Handled = true;

            if (sender is Border border && border.Tag is Option option)
            {
                option.IsSelected = !option.IsSelected;
                if (option.IsSelected)
                {
                    border.Background = ThemeResourceHelper.ModListItemHoverBackgroundBrush;
                }
                else
                {
                    border.Background = Brushes.Transparent;
                }
            }
        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async Task ProcessComponentsAsync([NotNull][ItemNotNull] List<ModComponent> modComponentsList)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ModListBox?.Items.Clear();
                });

                ComponentProcessingResult result = await ComponentProcessingService.ProcessComponentsAsync(modComponentsList);
                if (result.IsEmpty)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        SummaryTabItem.IsVisible = false;
                        GuiEditTabItem.IsVisible = false;
                        RawEditTabItem.IsVisible = false;
                        SetTabInternal(TabControl, InitialTab);
                        await UpdateStepProgressAsync();
                        UpdateModCounts();
                        ExitWizardMode();
                        UpdateWorkflowSurfaces();
                    });
                    return;
                }
                if (!result.Success && result.HasCircularDependencies)
                {

                    CircularDependencyDetector.CircularDependencyResult cycleInfo =
                        CircularDependencyDetector.DetectCircularDependencies(modComponentsList);

                    if (cycleInfo.HasCircularDependencies && cycleInfo.Cycles.Count > 0)
                    {
                        (bool retry, List<ModComponent> resolvedComponents) = await CircularDependencyResolutionDialog.ShowResolutionDialog(
                            this,
                            modComponentsList,
                            cycleInfo);
                        if (retry && resolvedComponents != null)
                        {

                            await ProcessComponentsAsync(resolvedComponents);
                        }
                    }
                    return;
                }

                List<ModComponent> componentsToProcess = result.ReorderedComponents ?? result.Components;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    PopulateModList(componentsToProcess);
                    if (componentsToProcess.Count > 0 || TabControl is null)
                    {

                        if (EditorMode)
                        {
                            SummaryTabItem.IsVisible = true;
                            GuiEditTabItem.IsVisible = true;
                            RawEditTabItem.IsVisible = true;
                        }

                        UpdateStepProgress();
                    }
                    else
                    {
                        SummaryTabItem.IsVisible = false;
                        GuiEditTabItem.IsVisible = false;
                        RawEditTabItem.IsVisible = false;
                        SetTabInternal(TabControl, InitialTab);

                        UpdateStepProgress();
                    }
                    UpdateWorkflowSurfaces();
                });
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[ProcessComponentsAsync] Exception occurred");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void AddNewInstruction_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                await Logger.LogVerboseAsync($"[AddNewInstruction_Click] START - CurrentComponent={(CurrentComponent is null ? "null" : $"'{CurrentComponent.Name}'")}");
                if (CurrentComponent is null)
                {
                    await Logger.LogVerboseAsync("[AddNewInstruction_Click] CurrentComponent is null, showing dialog");
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }

                // Get the button from the event source if possible
                Button addButton = null;
                if (sender is Button button)
                {
                    addButton = button;
                }
                else if (e.Source is Button sourceButton)
                {
                    addButton = sourceButton;
                }

                Instruction thisInstruction = null;
                ModComponent thisComponent = null;

                if (addButton != null && addButton.Tag != null)
                {
                    await Logger.LogVerboseAsync($"[AddNewInstruction_Click] Button tag type: {addButton.Tag?.GetType().Name ?? "null"}");
                    thisInstruction = addButton.Tag as Instruction;
                    thisComponent = addButton.Tag as ModComponent;
                }
                else
                {
                    // If no button with tag, use CurrentComponent directly
                    thisComponent = CurrentComponent;
                }
                if (thisInstruction is null && thisComponent is null)
                {
                    await Logger.LogErrorAsync("[AddNewInstruction_Click] Cannot find instruction or component instance from button tag");
                    throw new InvalidOperationException(message: "Cannot find instruction instance from button.");
                }
                int index;
                if (!(thisComponent is null))
                {
                    await Logger.LogVerboseAsync($"[AddNewInstruction_Click] Tag is ModComponent '{thisComponent.Name}', creating new instruction");
                    index = thisComponent.Instructions.Count;
                    await Logger.LogVerboseAsync($"[AddNewInstruction_Click] Creating instruction at index {index} (total instructions: {thisComponent.Instructions.Count})");
                    thisComponent.CreateInstruction(index);
                    await Logger.LogVerboseAsync($"[AddNewInstruction_Click] New instruction created at index #{index} for component '{thisComponent.Name}'");
                }
                else
                {
                    await Logger.LogVerboseAsync("[AddNewInstruction_Click] Tag is Instruction, getting parent component");
                    ModComponent parentComponent = thisInstruction.GetParentComponent();
                    await Logger.LogVerboseAsync($"[AddNewInstruction_Click] Parent component: '{parentComponent?.Name}'");
                    index = parentComponent?.Instructions.IndexOf(thisInstruction) ?? throw new InvalidOperationException($"Could not get index of instruction '{thisInstruction.Action}' in null parentComponent'");
                    await Logger.LogVerboseAsync($"[AddNewInstruction_Click] Creating instruction at index {index}");
                    parentComponent.CreateInstruction(index);
                    await Logger.LogVerboseAsync($"[AddNewInstruction_Click] New instruction created at index #{index} for component '{parentComponent.Name}'");
                }
                await Logger.LogVerboseAsync("[AddNewInstruction_Click] Calling LoadComponentDetails");
                LoadComponentDetails(CurrentComponent);
                await Logger.LogVerboseAsync("[AddNewInstruction_Click] COMPLETED");
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, customMessage: "[AddNewInstruction_Click] Exception occurred").ConfigureAwait(false);
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void DeleteInstruction_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }

                // Get the button from the event source if possible
                Button deleteButton = null;
                if (sender is Button button)
                {
                    deleteButton = button;
                }
                else if (e.Source is Button sourceButton)
                {
                    deleteButton = sourceButton;
                }

                if (deleteButton == null || deleteButton.Tag == null)
                {
                    throw new InvalidOperationException($"Could not get button or button tag from event");
                }

                Instruction thisInstruction = deleteButton.Tag as Instruction
                    ?? throw new InvalidOperationException($"Could not get instruction instance from button's tag: {deleteButton.Content}");
                int index = thisInstruction.GetParentComponent().Instructions.IndexOf(thisInstruction);
                InstructionManagementService.DeleteInstruction(thisInstruction.GetParentComponent(), index);
                LoadComponentDetails(CurrentComponent);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception);
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void AutoGenerateInstructions_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] ===== BUTTON CLICKED =====");
                await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] Sender: {sender.GetType().Name}");
                await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] EventArgs: {e.GetType().Name}");

                if (_autoGenerateRunning)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Auto-generate already running, showing dialog");
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Auto-generation is already in progress, please wait for it to complete."
                    );
                    return;
                }

                if (CurrentComponent is null)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] No current component selected");
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }

                await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] CurrentComponent: {CurrentComponent.Name}");
                await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] CurrentComponent.ResourceRegistry count: {CurrentComponent.ResourceRegistry.Count}");
                foreach (KeyValuePair<string, ResourceMetadata> kvp in CurrentComponent.ResourceRegistry)
                {
                    await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click]   URL: {kvp.Key}");
                    await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click]   Filenames: {string.Join(", ", kvp.Value.Files.Keys)}");
                }

                // Expand the Instructions expander before proceeding
                Expander instructionsExpander = await Dispatcher.UIThread.InvokeAsync(() => this.FindControl<Expander>("InstructionsExpander"));
                if (instructionsExpander != null)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Expanding Instructions expander");
                    await Dispatcher.UIThread.InvokeAsync(() => instructionsExpander.IsExpanded = true);
                }
                else
                {
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Instructions expander not found");
                }

                await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] START - CurrentComponent='{CurrentComponent.Name}'");

                await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Showing source selection dialog");
                bool? useModLinks = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    "Where would you like to source these instructions from?",
                    "From Mod Links",
                    "From Archive on Disk");
                await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] User selected: {(useModLinks is null ? "CANCELLED" : useModLinks == true ? "From Mod Links" : "From Archive on Disk")}");
                if (useModLinks is null)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] User cancelled source selection");
                    return;
                }

                await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] Current instructions count: {CurrentComponent.Instructions.Count}");
                if (CurrentComponent.Instructions.Count > 0)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Showing replacement confirmation dialog");
                    bool? confirmed = await ConfirmationDialog.ShowConfirmationDialogAsync(
                        this,
                        "This will replace all existing instructions with auto-generated ones. How would you like to proceed?",
                        "Generate without deleting",
                        "From Scratch");

                    await Logger.LogVerboseAsync($"[AutoGenerateInstructions_Click] User replacement choice: {(confirmed is null ? "CANCELLED" : confirmed == true ? "Generate without deleting" : "From Scratch")}");
                    if (confirmed is null)
                    {
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] User cancelled replacement confirmation");
                        return;
                    }

                    bool generateWithoutDeleting = confirmed == true;

                    // If "From Scratch" was selected, clear existing instructions
                    if (!generateWithoutDeleting)
                    {
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Clearing existing instructions and options");
                        CurrentComponent.Instructions.Clear();
                        CurrentComponent.Options.Clear();
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Cleared existing instructions for 'From Scratch' generation");
                    }
                    else
                    {
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Keeping existing instructions, will generate additional ones");
                    }
                }

                try
                {
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Setting auto-generate running flag");
                    _autoGenerateRunning = true;
                    UpdateAutoGenerateButtonState();

                    if (useModLinks == true)
                    {
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Calling GenerateInstructionsFromModLinks");
                        await GenerateInstructionsFromModLinks();
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] GenerateInstructionsFromModLinks completed");
                    }
                    else
                    {
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] Calling GenerateInstructionsFromArchive");
                        await GenerateInstructionsFromArchive();
                        await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] GenerateInstructionsFromArchive completed");
                    }
                }
                finally
                {
                    _autoGenerateRunning = false;
                    UpdateAutoGenerateButtonState();
                    await Logger.LogVerboseAsync("[AutoGenerateInstructions_Click] COMPLETED");
                }
            }
            catch (Exception exception)
            {
                _autoGenerateRunning = false;
                UpdateAutoGenerateButtonState();
                await Logger.LogExceptionAsync(exception, customMessage: "[AutoGenerateInstructions_Click] Exception occurred");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: $"[AutoGenerateInstructions_Click] An unexpected error occurred while generating instructions: {exception.Message}");
            }
        }

        private void UpdateAutoGenerateButtonState()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                UpdateAutoGenerateButtonStateCore();
            }
            else
            {
                Dispatcher.UIThread.Post(UpdateAutoGenerateButtonStateCore);
            }
        }

        private void UpdateAutoGenerateButtonStateCore()
        {
            try
            {
                Button autoGenerateButton = EditorTabControl?.FindControl<Button>("AutoGenerateButton");
                if (autoGenerateButton != null)
                {
                    autoGenerateButton.IsEnabled = !_autoGenerateRunning;
                    autoGenerateButton.Content = _autoGenerateRunning ? "⏳ Generating..." : "🤖 Auto-Generate";
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error updating auto-generate button state");
            }
        }

        /// <summary>
        /// Event handler for the AutofetchInstructionsRequested event from ModListSidebar
        /// </summary>
        [UsedImplicitly]
        private void AutoGenerateAllComponents_Click(object sender, RoutedEventArgs e) => _ = AutoGenerateAllComponentsAsync();

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async Task AutoGenerateAllComponentsAsync([NotNull] CancellationToken cancellationToken = default)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateAllComponents_Click] Cancellation requested");
                    return;
                }

                await Logger.LogVerboseAsync("[AutoGenerateAllComponents_Click] START");

                // Get all components that don't have instructions yet
                var componentsWithoutInstructions = MainConfig.AllComponents
                    .Where(c => c.Instructions.Count == 0)
                    .ToList();

                if (componentsWithoutInstructions.Count == 0)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        "All components already have instructions. No auto-generation needed.");
                    return;
                }

                // Show confirmation dialog
                bool? confirmed = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    $"This will attempt to auto-generate instructions for {componentsWithoutInstructions.Count} component(s) that don't have any instructions yet.\n\n" +
                    "This process will only work for components that have downloaded archives available.\n\n" +
                    "Would you like to proceed?",
                    "Yes, Generate Instructions",
                    "Cancel");

                if (confirmed != true)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateAllComponents_Click] User cancelled");
                    return;
                }

                // Show progress dialog
                var progressDialog = new ProgressDialog("Auto-Generating Instructions", "Processing components...");
                progressDialog.Show(this);

                try
                {
                    var results = new AutoGenerationResults
                    {
                        TotalProcessed = componentsWithoutInstructions.Count,
                        ComponentResults = new List<ComponentResult>(),
                    };

                    foreach (ModComponent component in componentsWithoutInstructions)
                    {
                        await progressDialog.UpdateProgressAsync(
                            $"Processing: {component.Name}",
                            results.ComponentResults.Count,
                            componentsWithoutInstructions.Count
                        );

                        // Try to generate instructions from local archive with detailed results
                        GenerationResult generationResult = AutoInstructionGenerator.TryGenerateInstructionsFromArchiveDetailed(component);

                        var componentResult = new ComponentResult
                        {
                            ComponentGuid = component.Guid,
                            ComponentName = component.Name,
                            Success = generationResult.Success,
                            InstructionsGenerated = generationResult.InstructionsGenerated,
                            SkipReason = generationResult.SkipReason,
                        };

                        results.ComponentResults.Add(componentResult);

                        if (generationResult.Success)
                        {
                            await Logger.LogAsync($"Auto-generated {generationResult.InstructionsGenerated} instruction(s) for '{component.Name}'");
                        }
                        else
                        {
                            await Logger.LogAsync($"Skipped '{component.Name}': {generationResult.SkipReason}");
                        }

                        // Update UI to show progress
                        await Task.Delay(100, cancellationToken); // Small delay to allow UI updates
                    }

                    results.SuccessfullyGenerated = results.ComponentResults.Count(r => r.Success);

                    progressDialog.Close();

                    // Show enhanced results dialog
                    AutoGenerationResultsDialog.ShowResultsDialog(this, results, JumpToComponent);

                    // Refresh the component list to show updated instructions
                    RefreshComponents_Click(null, null);
                }
                catch (Exception ex)
                {
                    progressDialog.Close();
                    await Logger.LogExceptionAsync(ex, customMessage: "[AutoGenerateAllComponents_Click] Exception occurred");
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: $"[AutoGenerateAllComponents_Click] An error occurred during auto-generation: {ex.Message}");
                }
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, customMessage: "[AutoGenerateAllComponents_Click] Exception occurred");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: $"[AutoGenerateAllComponents_Click] An unexpected error occurred: {exception.Message}");
            }
        }

        /// <summary>
        /// Event handler for the LockInstallOrderRequested event from ModListSidebar
        /// </summary>
        [UsedImplicitly]
        private void AutoGenerateDependencies_Click(object sender, RoutedEventArgs e) => _ = AutoGenerateDependenciesAsync();

        private async Task AutoGenerateDependenciesAsync([NotNull] CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await Logger.LogInfoAsync("[AutoGenerateDependencies_Click] Cancellation requested");
                return;
            }

            try
            {
                await Logger.LogVerboseAsync("[AutoGenerateDependencies_Click] START");

                var allComponents = MainConfig.AllComponents.ToList();
                if (allComponents.Count == 0)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "No components found to generate dependencies for.");
                    return;
                }

                // Show confirmation dialog
                bool? confirmed = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    $"This will generate InstallBefore/InstallAfter dependencies for all {allComponents.Count} components based on their current order.\n\n" +
                    "This will replace any existing dependency relationships.\n\n" +
                    "Would you like to proceed?",
                    "Yes, Generate Dependencies",
                    "Cancel");

                if (confirmed != true)
                {
                    await Logger.LogVerboseAsync("[AutoGenerateDependencies_Click] User cancelled");
                    return;
                }

                // Generate dependencies based on current order
                DependencyResolverService.GenerateDependenciesFromOrder(allComponents);

                // Update the main config with the new order
                MainConfig.AllComponents.Clear();
                MainConfig.AllComponents.AddRange(allComponents);

                await Logger.LogAsync($"Generated dependencies for {allComponents.Count} components based on current order");

                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"Successfully generated dependencies for all {allComponents.Count} components based on their current order.");

                // Refresh the component list to show updated order
                RefreshComponents_Click(null, null);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[AutoGenerateDependencies_Click] Exception occurred");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: $"An unexpected error occurred: {exception.Message}");
            }
        }

        /// <summary>
        /// Event handler for the RemoveAllDependenciesRequested event from ModListSidebar
        /// </summary>
        [UsedImplicitly]
        private void RemoveAllDependencies_Click(object sender, RoutedEventArgs e) => _ = RemoveAllDependenciesAsync();

        private async Task RemoveAllDependenciesAsync()
        {
            try
            {
                await Logger.LogVerboseAsync("[RemoveAllDependencies_Click] START");

                var allComponents = MainConfig.AllComponents.ToList();
                if (allComponents.Count == 0)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        "No components found to remove dependencies from.");
                    return;
                }

                // Show confirmation dialog
                bool? confirmed = await ConfirmationDialog.ShowConfirmationDialogAsync(
                    this,
                    ($"This will remove all InstallBefore/InstallAfter dependencies from all {allComponents.Count} components.\n\n" +
                    "This action cannot be undone.\n\n" +
                    "Would you like to proceed?"),
                    "Yes, Remove All Dependencies",
                    "Cancel");

                if (confirmed != true)
                {
                    await Logger.LogVerboseAsync("[RemoveAllDependencies_Click] User cancelled");
                    return;
                }

                // Remove all dependencies
                DependencyResolverService.ClearAllDependencies(allComponents);

                await Logger.LogAsync($"Removed all dependencies from {allComponents.Count} components");

                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"Successfully removed all dependencies from {allComponents.Count} components.");

                // Refresh the component list
                RefreshComponents_Click(null, null);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[RemoveAllDependencies_Click] Exception occurred");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    message: $"An unexpected error occurred: {exception.Message}");
            }
        }
        /// <summary>
        /// Resolves component dependencies and handles any errors through the GUI dialog.
        /// This method should be called after loading components to ensure proper ordering.
        /// </summary>
        public async Task<bool> ResolveDependenciesWithGuiHandling(
            [NotNull] List<ModComponent> components,
            [NotNull] CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await Logger.LogInfoAsync("[ResolveDependenciesWithGuiHandling] Cancellation requested");
                return false;
            }

            try
            {
                DependencyResolutionResult resolutionResult = DependencyResolverService.ResolveDependencies(components, ignoreErrors: false);

                if (resolutionResult.Success)
                {
                    // Update components with resolved order
                    MainConfig.AllComponents.Clear();
                    MainConfig.AllComponents.AddRange(resolutionResult.OrderedComponents);
                    return true;
                }
                else
                {
                    // Show error dialog
                    DependencyResolutionAction action = await DependencyResolutionErrorDialog.ShowErrorDialogAsync(
                        this,
                        resolutionResult.Errors,
                        components);

                    switch (action)
                    {
                        case DependencyResolutionAction.AutoFix:
                            DependencyResolverService.GenerateDependenciesFromOrder(components);
                            MainConfig.AllComponents.Clear();
                            MainConfig.AllComponents.AddRange(components);
                            return true;

                        case DependencyResolutionAction.ClearAll:
                            DependencyResolverService.ClearAllDependencies(components);
                            MainConfig.AllComponents.Clear();
                            MainConfig.AllComponents.AddRange(components);
                            return true;

                        case DependencyResolutionAction.IgnoreErrors:
                            DependencyResolutionResult ignoreResult = DependencyResolverService.ResolveDependencies(components, ignoreErrors: true);
                            MainConfig.AllComponents.Clear();
                            MainConfig.AllComponents.AddRange(ignoreResult.OrderedComponents);
                            return true;

                        case DependencyResolutionAction.Cancel:
                        default:
                            return false;
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[ResolveDependenciesWithGuiHandling] Exception occurred");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"An error occurred while resolving dependencies: {ex.Message}");
                return false;
            }
        }

        private async Task GenerateInstructionsFromModLinks()
        {
            if (CurrentComponent is null)
            {
                await Logger.LogVerboseAsync("[GenerateInstructionsFromModLinks] CurrentComponent is null");
                return;
            }
            int result = await _instructionGenerationService.GenerateInstructionsFromModLinksAsync(CurrentComponent);
            if (result > 0)
            {
                LoadComponentDetails(CurrentComponent);
            }
        }
        private async Task GenerateInstructionsFromArchive()
        {
            if (CurrentComponent is null)
            {
                await Logger.LogVerboseAsync("[GenerateInstructionsFromArchive] CurrentComponent is null");
                return;
            }

            string[] filePaths = null;
            bool success = await _instructionGenerationService.GenerateInstructionsFromArchiveAsync(
                CurrentComponent, async () =>
                {
                    filePaths = await _dialogService.ShowFileDialogAsync(
                        isFolderDialog: false,
                        allowMultiple: true,
                        startFolder: null,
                        windowName: "Select the mod archive to analyze for auto-generation"
                    );
                    return filePaths;
                });

            if (filePaths is null || filePaths.Length == 0)
            {
                await Logger.LogVerboseAsync("[GenerateInstructionsFromArchive] User cancelled file selection");
                return;
            }
            if (success)
            {
                LoadComponentDetails(CurrentComponent);
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void MoveInstructionUp_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }
                var thisInstruction = (Instruction)((Button)sender).Tag;
                int index = CurrentComponent.Instructions.IndexOf(thisInstruction);
                if (thisInstruction is null)
                {
                    await Logger.LogExceptionAsync(new InvalidOperationException(message: "The sender does not correspond to a instruction."), "[MoveInstructionUp_Click] Exception occurred");
                    return;
                }
                InstructionManagementService.MoveInstruction(CurrentComponent, thisInstruction, index - 1);
                LoadComponentDetails(CurrentComponent);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[MoveInstructionUp_Click] Exception occurred");
            }
        }
        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void MoveInstructionDown_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }
                var thisInstruction = (Instruction)((Button)sender).Tag;
                int index = CurrentComponent.Instructions.IndexOf(thisInstruction);
                if (thisInstruction is null)
                {
                    await Logger.LogErrorAsync($"Could not get instruction instance from button's tag: {((Button)sender).Content}");
                    return;
                }

                InstructionManagementService.MoveInstruction(CurrentComponent, thisInstruction, index + 1);
                LoadComponentDetails(CurrentComponent);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[MoveInstructionDown_Click] Exception occurred");
            }
        }
        [UsedImplicitly]
        private void OpenOutputWindow_Click([CanBeNull] object sender, [CanBeNull] RoutedEventArgs e)
        {
            Dispatcher.UIThread.Post((async () =>
            {
                if (_outputWindow?.IsVisible == true)
                {
                    _outputWindow.Close();
                }

                _outputWindow = new OutputWindow();
                _outputWindow.Show();
            }));
        }
        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void OpenSettings_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OpenSettings_Click(sender, e), DispatcherPriority.Normal);
                return;
            }

            try
            {
                var settingsDialog = new SettingsDialog();
                settingsDialog.InitializeFromMainWindow(this);
                bool result = await settingsDialog.ShowDialog<bool>(this);
                if (!result)
                {
                    return;
                }

                SaveSettings();

                // Update HoloPatcher version display in case a new version was downloaded
                UpdateHolopatcherVersionDisplay();
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[OpenSettings_Click] Error in OpenSettings_Click");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void OpenCheckpointManagement_Click([NotNull] object sender, [NotNull] RoutedEventArgs e, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await Logger.LogInfoAsync("[OpenCheckpointManagement_Click] Cancellation requested");
                return;
            }

            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OpenCheckpointManagement_Click(sender, e, cancellationToken), DispatcherPriority.Normal);
                return;
            }

            try
            {
                if (MainConfig.DestinationPath is null || !Directory.Exists(MainConfig.DestinationPath.FullName))
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        "Please set a KOTOR installation directory before managing checkpoints.",
                        "No Destination Path"
                    );
                    return;
                }

                var checkpointDialog = new CheckpointManagementDialog(MainConfig.DestinationPath.FullName);
                await checkpointDialog.ShowDialog(this);
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[OpenCheckpointManagement_Click] Error in OpenCheckpointManagement_Click");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async void RunHolopatcherButton_Click(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await Logger.LogInfoAsync("[RunHolopatcherButton_Click] Cancellation requested");
                return;
            }

            try
            {
                string engine = MainConfig.PatcherEngine ?? PatcherEngines.Holopatcher;
                if (string.Equals(engine, PatcherEngines.KPatcher, StringComparison.OrdinalIgnoreCase))
                {
                    await Logger.LogAsync("[RunHolopatcherButton_Click] Launching KPatcher (--help)...");
                    (string kPath, bool kFound) = await InstallationService.FindKPatcherExecutableAsync().ConfigureAwait(false);
                    if (!kFound)
                    {
                        await InformationDialog.ShowInformationDialogAsync(
                            this,
                            "KPatcher was selected in Settings but no executable was found. Set the path in Settings or add KPatcher to PATH.",
                            "KPatcher Not Found");
                        return;
                    }

                    string prefix = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? string.Empty
                        : "--console ";
                    (int exitCode, string stdout, string stderr) = await InstallationService.RunTslPatcherCliAsync(prefix + "--help").ConfigureAwait(false);

                    if (exitCode == 0)
                    {
                        await Logger.LogAsync("KPatcher CLI responded successfully.");
                    }
                    else
                    {
                        await Logger.LogErrorAsync($"KPatcher exited with code {exitCode}");
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            await InformationDialog.ShowInformationDialogAsync(
                                this,
                                $"KPatcher failed (exit code: {exitCode})\n\n{stderr}",
                                "Error Launching KPatcher");
                        }
                    }

                    return;
                }

                await Logger.LogAsync("[RunHolopatcherButton_Click] Launching HoloPatcher with no arguments...");

                bool preferPythonVersion = false;

                (int exitCode2, string stdout2, string stderr2) = await InstallationService.RunHolopatcherAsync(
                    args: "",
                    preferPythonVersion: preferPythonVersion
                );

                if (exitCode2 == 0)
                {
                    await Logger.LogAsync("HoloPatcher launched successfully.");
                }
                else
                {
                    await Logger.LogErrorAsync($"HoloPatcher exited with code {exitCode2}");
                    if (!string.IsNullOrEmpty(stderr2))
                    {
                        await Logger.LogErrorAsync($"Error: {stderr2}");

                        await InformationDialog.ShowInformationDialogAsync(
                            this,
                            $"HoloPatcher failed to launch (exit code: {exitCode2})\n\nFull Error Details:\n{stderr2}",
                            "Error Launching HoloPatcher"
                        );
                    }
                    else
                    {
                        await InformationDialog.ShowInformationDialogAsync(
                            this,
                            $"HoloPatcher failed to launch (exit code: {exitCode2})\n\nNo additional error details available.",
                            "Error Launching HoloPatcher"
                        );
                    }
                }
            }
            catch (FileNotFoundException ex)
            {
                await Logger.LogErrorAsync($"HoloPatcher not found: {ex.Message}");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"HoloPatcher could not be found in the Resources directory.\n\nPlease ensure the application is installed correctly.\n\n{ex.Message}",
                    "HoloPatcher Not Found"
                );
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[RunHolopatcherButton_Click] Error in RunHolopatcherButton_Click");
                await InformationDialog.ShowInformationDialogAsync(
                    this,
                    $"Failed to launch HoloPatcher:\n{ex.Message}\n\nFull Stack Trace:\n{ex.StackTrace}",
                    "Error Launching HoloPatcher"
                );
            }
        }

        [UsedImplicitly]
        private void CreateGithubIssue_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                UrlUtilities.OpenUrl("https://github.com/th3w1zard1/KOTORModSync/issues/new");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[CreateGithubIssue_Click] Failed to open GitHub issue creation page");
            }
        }

        [UsedImplicitly]
        private void OpenSponsorPage_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                UrlUtilities.OpenUrl("https://github.com/sponsors/th3w1zard1");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[OpenSponsorPage_Click] Failed to open GitHub Sponsors page");
            }
        }

        private static void ApplyTheme(string stylePath)
        {
            ThemeService.ApplyTheme(stylePath);
        }

        /// <summary>
        /// Converts a theme path to the corresponding TargetGame value.
        /// TargetGame and Theme are the same thing - this method provides the mapping.
        /// </summary>
        private static string GetTargetGameFromTheme(string themePath)
        {
            if (string.IsNullOrEmpty(themePath))
            {
                return null; // No game-specific theme
            }

            if (themePath.Contains("FluentLightStyle"))
            {
                return null;
            }

            if (themePath.IndexOf("Kotor2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TSL";
            }

            if (themePath.IndexOf("Kotor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "K1";
            }

            return null; // No game-specific theme
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void StyleComboBox_SelectionChanged(
            [NotNull] object sender,
            [NotNull] SelectionChangedEventArgs e
        )
        {
            try
            {
                if (_initialize)
                {
                    _initialize = false;
                    return;
                }
                if (!(sender is ComboBox comboBox))
                {
                    return;
                }

                var selectedItem = (ComboBoxItem)comboBox.SelectedItem;
                if (!(selectedItem?.Tag is string stylePath))
                {
                    await Logger.LogErrorAsync("stylePath cannot be rendered from tag, returning immediately");
                    return;
                }

                ThemeService.ApplyTheme(stylePath);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[StyleComboBox_SelectionChanged] Exception occurred");
            }
        }

        [UsedImplicitly]
        private void ToggleMaximizeButton_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
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
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void AddNewOption_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                await Logger.LogVerboseAsync($"[AddNewOption_Click] START - CurrentComponent={(CurrentComponent is null ? "null" : $"'{CurrentComponent.Name}'")}");
                if (CurrentComponent is null)
                {
                    await Logger.LogVerboseAsync("[AddNewOption_Click] CurrentComponent is null, showing dialog");
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }
                var addButton = (Button)sender;
                await Logger.LogVerboseAsync($"[AddNewOption_Click] Button tag type: {addButton.Tag?.GetType().Name ?? "null"}");
                var thisOption = addButton.Tag as Option;
                var thisComponent = addButton.Tag as ModComponent;
                if (thisOption is null && thisComponent is null)
                {
                    await Logger.LogErrorAsync("[AddNewOption_Click] Cannot find option or component instance from button tag");
                    throw new InvalidOperationException("Cannot find option instance from button.");
                }
                int index;
                if (thisOption is null)
                {
                    await Logger.LogVerboseAsync("[AddNewOption_Click] Tag is ModComponent, creating new option");
                    thisOption = new Option();
                    index = CurrentComponent.Options.Count;
                    await Logger.LogVerboseAsync($"[AddNewOption_Click] Creating option at index {index} (total options: {CurrentComponent.Options.Count})");
                }
                else
                {
                    await Logger.LogVerboseAsync($"[AddNewOption_Click] Tag is Option '{thisOption.Name}', getting index");
                    index = CurrentComponent.Options.IndexOf(thisOption);
                    await Logger.LogVerboseAsync($"[AddNewOption_Click] Creating option at index {index}");
                }
                CurrentComponent.CreateOption(index);
                await Logger.LogVerboseAsync($"[AddNewOption_Click] Option '{thisOption.Name}' created at index #{index} for component '{CurrentComponent.Name}'");
                await Logger.LogVerboseAsync("[AddNewOption_Click] Calling LoadComponentDetails");
                LoadComponentDetails(CurrentComponent);

                RefreshSingleComponentVisuals(CurrentComponent);
                await Logger.LogVerboseAsync("[AddNewOption_Click] COMPLETED");
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, customMessage: "[AddNewOption_Click] Exception occurred");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void DeleteOption_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }
                var thisOption = (Option)((Button)sender).Tag;
                int index = CurrentComponent.Options.IndexOf(thisOption);
                InstructionManagementService.DeleteOption(CurrentComponent, index);
                LoadComponentDetails(CurrentComponent);

                RefreshSingleComponentVisuals(CurrentComponent);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[DeleteOption_Click] Exception occurred");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void MoveOptionUp_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }
                var thisOption = (Option)((Button)sender).Tag;
                int index = CurrentComponent.Options.IndexOf(thisOption);
                if (thisOption is null)
                {
                    throw new InvalidOperationException($"Could not get option instance from button's tag: {((Button)sender).Content}");
                }

                InstructionManagementService.MoveOption(CurrentComponent, thisOption, index - 1);
                LoadComponentDetails(CurrentComponent);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[MoveOptionUp_Click] Exception occurred");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void MoveOptionDown_Click([NotNull] object sender, [NotNull] RoutedEventArgs e)
        {
            try
            {
                if (CurrentComponent is null)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        this,
                        message: "Please select a component from the list or create a new one before saving."
                    );
                    return;
                }
                var thisOption = (Option)((Button)sender).Tag;
                int index = CurrentComponent.Options.IndexOf(thisOption);
                if (thisOption is null)
                {
                    throw new InvalidOperationException($"Could not get option instance from button's tag: {((Button)sender).Content}");
                }

                InstructionManagementService.MoveOption(CurrentComponent, thisOption, index + 1);
                LoadComponentDetails(CurrentComponent);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[MoveOptionDown_Click] Exception occurred");
            }
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void CopyTextToClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard is null)
                {
                    throw new InvalidOperationException(nameof(Clipboard));
                }

                await Clipboard.SetTextAsync((string)((MenuItem)sender).DataContext);
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[CopyTextToClipboard_Click] Exception occurred");
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetCurrentModComponent(null);
                TabControl tabControl = this.FindControl<TabControl>("TabControl");
                TabItem initialTab = this.FindControl<TabItem>("InitialTab");
                if (tabControl != null && initialTab != null)
                {
                    SetTabInternal(tabControl, initialTab);
                }
            }
            catch (Exception exception)
            {
                Logger.LogException(exception, "[HomeButton_Click] Exception occurred");
            }
        }

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void Step1Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ShowSetDirectoriesDialog();
                await UpdateStepProgressAsync();
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[Step1Button_Click] Exception occurred");
            }
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void Step2Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadInstructionFile();
                await UpdateStepProgressAsync();
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[Step2Button_Click] Exception occurred");
            }
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void GettingStartedValidateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await RunValidation();
                await UpdateStepProgressAsync();

                ShowValidationResults();
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[GettingStartedValidateButton_Click] Exception occurred");
            }
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StartInstallation();
                await UpdateStepProgressAsync();
            }
            catch (Exception exception)
            {
                await Logger.LogExceptionAsync(exception, "[InstallButton_Click] Exception occurred");
            }
        }

        private void GettingStartedTab_AutoFixRequested(object sender, RoutedEventArgs e) => AutoFixButton_Click(sender, e);
        private void GettingStartedTab_CreateGithubIssueRequested(object sender, RoutedEventArgs e) => CreateGithubIssue_Click(sender, e);
        private void GettingStartedTab_DirectoryChangedRequested(object sender, DirectoryChangedEventArgs e) => OnDirectoryChanged(sender, e);
        private void GettingStartedTab_DownloadStatusRequested(object sender, RoutedEventArgs e) => DownloadStatusButton_Click(sender, e);
        private void GettingStartedTab_InstallRequested(object sender, RoutedEventArgs e) => InstallButton_Click(sender, e);
        private void GettingStartedTab_JumpToCurrentStepRequested(object sender, RoutedEventArgs e) => JumpToCurrentStep_Click(sender, e);
        private void GettingStartedTab_JumpToModRequested(object sender, RoutedEventArgs e) => JumpToModButton_Click(sender, e);
        private void GettingStartedTab_LoadInstructionFileRequested(object sender, RoutedEventArgs e) => Step2Button_Click(sender, e);
        private void GettingStartedTab_NextErrorRequested(object sender, RoutedEventArgs e) => NextErrorButton_Click(sender, e);
        private void GettingStartedTab_OpenModDirectoryRequested(object sender, RoutedEventArgs e) => OpenModDirectoryButton_Click(sender, e);
        private void GettingStartedTab_OpenOutputWindowRequested(object sender, RoutedEventArgs e) => OpenOutputWindow_Click(sender, e);
        private void GettingStartedTab_OpenSettingsRequested(object sender, RoutedEventArgs e) => OpenSettings_Click(sender, e);
        private void GettingStartedTab_OpenSponsorPageRequested(object sender, RoutedEventArgs e) => OpenSponsorPage_Click(sender, e);
        private void GettingStartedTab_PrevErrorRequested(object sender, RoutedEventArgs e) => PrevErrorButton_Click(sender, e);
        private void GettingStartedTab_ScrapeDownloadsRequested(object sender, RoutedEventArgs e) => ScrapeDownloadsButton_Click(sender, e);
        private void GettingStartedTab_StopDownloadsRequested(object sender, RoutedEventArgs e) => StopDownloadsButton_Click(sender, e);
        private void GettingStartedTab_ValidateRequested(object sender, RoutedEventArgs e) => GettingStartedValidateButton_Click(sender, e);

        private async Task ShowSetDirectoriesDialog([NotNull] CancellationToken cancellationToken = default)
        {

            Menu topMenu = this.FindControl<Menu>(name: "TopMenu");
            if (topMenu?.Items.Count > 0 && topMenu.Items[1] is MenuItem fileMenu && fileMenu.Items.Count > 1 && fileMenu.Items[1] is MenuItem setDirItem)
            {
                await Task.Delay(millisecondsDelay: 100, cancellationToken);
                SetDirectories_Click(setDirItem, new RoutedEventArgs(), cancellationToken);
            }
        }
        private async Task LoadInstructionFile([NotNull] CancellationToken cancellationToken = default)
        {

            Menu topMenu = this.FindControl<Menu>(name: "TopMenu");
            if (
                topMenu?.Items.Count > 0
                && topMenu.Items[1] is MenuItem fileMenu
                && fileMenu.Items.Count > 0
                && fileMenu.Items[0] is MenuItem loadFileItem)
            {
                await Task.Delay(millisecondsDelay: 100, cancellationToken);
                LoadFile_Click(loadFileItem, new RoutedEventArgs(), cancellationToken);
            }
        }
        private async Task RunValidation([NotNull] CancellationToken cancellationToken = default)
        {

            if (cancellationToken.IsCancellationRequested)
            {
                await Logger.LogVerboseAsync("[RunValidation] Cancellation requested");
                return;
            }

            await Task.Delay(millisecondsDelay: 100, cancellationToken);
            ValidateButton_Click(sender: null, new RoutedEventArgs());
        }
        private async Task StartInstallation([NotNull] CancellationToken cancellationToken = default)
        {

            await Task.Delay(millisecondsDelay: 100, cancellationToken);
            StartInstall_Click(sender: null, new RoutedEventArgs(), cancellationToken);
        }
        private void UpdateStepProgress()
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(UpdateStepProgress, DispatcherPriority.Normal);
                    return;
                }
                // Check if the control is loaded yet
                if (GettingStartedTabControl is null)
                {
                    return;
                }

                // Check if the service is initialized
                if (_uiStateService is null)
                {
                    return;
                }

                _uiStateService.UpdateStepProgress(
                    GettingStartedTabControl.FindControl<Border>("Step1Border"), GettingStartedTabControl.FindControl<Border>("Step1CompleteIndicator"), GettingStartedTabControl.FindControl<TextBlock>("Step1CompleteText"),
                    GettingStartedTabControl.FindControl<Border>("Step2Border"), GettingStartedTabControl.FindControl<Border>("Step2CompleteIndicator"), GettingStartedTabControl.FindControl<TextBlock>("Step2CompleteText"),
                    GettingStartedTabControl.FindControl<Border>("Step3Border"), GettingStartedTabControl.FindControl<Border>("Step3CompleteIndicator"), GettingStartedTabControl.FindControl<TextBlock>("Step3CompleteText"),
                    GettingStartedTabControl.FindControl<Border>("Step4Border"), GettingStartedTabControl.FindControl<Border>("Step4CompleteIndicator"), GettingStartedTabControl.FindControl<TextBlock>("Step4CompleteText"),
                    step5Border: null, step5Indicator: null, step5Text: null,
                    GettingStartedTabControl.FindControl<ProgressBar>("OverallProgressBar"), GettingStartedTabControl.FindControl<TextBlock>("ProgressText"),
                    GettingStartedTabControl.FindControl<CheckBox>("Step4Checkbox"),
                    EditorMode,
                    IsComponentValidForInstallation
                    );
            }
            catch (Exception exception)
            {
                Logger.LogException(exception, "[UpdateStepProgress] Exception occurred");
            }
        }

        private bool IsComponentValidForInstallation(ModComponent component) => _validationService.IsComponentValidForInstallation(component, EditorMode);

        private void OnThemeChanged(Uri newTheme)
        {
            // Refresh step indicators when theme changes to apply new colors
            Dispatcher.UIThread.Post(() =>
            {
                UpdateStepProgress();
            });
        }
        private void OnDirectoryChanged(object sender, DirectoryChangedEventArgs e)
        {
            try
            {
                switch (e?.PickerType)
                {
                    case DirectoryPickerType.ModDirectory:
                        MainConfigInstance.sourcePath = new DirectoryInfo(e.Path);
                        Logger.LogInfo($"[OnDirectoryChanged] Mod workspace directory set to: {e.Path}");

                        SyncDirectoryPickers(DirectoryPickerType.ModDirectory, e.Path);
                        break;
                    case DirectoryPickerType.KotorDirectory:
                        MainConfigInstance.destinationPath = new DirectoryInfo(e.Path);
                        Logger.LogInfo($"[OnDirectoryChanged] KOTOR installation directory set to: {e.Path}");

                        SyncDirectoryPickers(DirectoryPickerType.KotorDirectory, e.Path);
                        break;
                    default:
                        throw new InvalidOperationException($"[OnDirectoryChanged] Invalid DirectoryPickerType value in OnDirectoryChanged. PickerType={e?.PickerType}");
                }

                UpdateStepProgress();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[OnDirectoryChanged] Exception occurred");
            }
        }

        private void InitializeDirectoryPickers()
        {
            try
            {
                DirectoryPickerControl modPicker = this.FindControl<DirectoryPickerControl>("ModDirectoryPicker");
                DirectoryPickerControl kotorPicker = this.FindControl<DirectoryPickerControl>("KotorDirectoryPicker");
                DirectoryPickerControl step1ModPicker = GettingStartedTabControl.FindControl<DirectoryPickerControl>("Step1ModDirectoryPicker");
                DirectoryPickerControl step1KotorPicker = GettingStartedTabControl.FindControl<DirectoryPickerControl>("Step1KotorDirectoryPicker");
                if (modPicker != null && MainConfig.SourcePath != null)
                {
                    modPicker.SetCurrentPathFromSettings(MainConfig.SourcePath.FullName);
                }

                if (kotorPicker != null && MainConfig.DestinationPath != null)
                {
                    kotorPicker.SetCurrentPathFromSettings(MainConfig.DestinationPath.FullName);
                }
                // Use same pattern as SettingsDialog for GettingStartedTab pickers to prevent typing issues
                if (step1ModPicker != null && MainConfig.SourcePath != null)
                {
                    UpdateDirectoryPickerWithPath(step1ModPicker, MainConfig.SourcePath.FullName);
                }

                if (step1KotorPicker != null && MainConfig.DestinationPath != null)
                {
                    UpdateDirectoryPickerWithPath(step1KotorPicker, MainConfig.DestinationPath.FullName);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[InitializeDirectoryPickers] Exception occurred");
            }
        }

        public void SyncDirectoryPickers(DirectoryPickerType pickerType, string path)
        {
            try
            {
                DirectoryPickerControl mainPicker = null;
                DirectoryPickerControl step1Picker = null;

                if (pickerType == DirectoryPickerType.ModDirectory)
                {
                    mainPicker = this.FindControl<DirectoryPickerControl>("ModDirectoryPicker");
                    step1Picker = GettingStartedTabControl.FindControl<DirectoryPickerControl>("Step1ModDirectoryPicker");
                }
                else if (pickerType == DirectoryPickerType.KotorDirectory)
                {
                    mainPicker = this.FindControl<DirectoryPickerControl>("KotorDirectoryPicker");
                    step1Picker = GettingStartedTabControl.FindControl<DirectoryPickerControl>("Step1KotorDirectoryPicker");
                }

                // Update main picker with standard method
                if (mainPicker != null)
                {
                    mainPicker.SetCurrentPathFromSettings(path);
                }

                // Use same pattern as SettingsDialog for GettingStartedTab picker to prevent typing issues
                if (step1Picker != null)
                {
                    UpdateDirectoryPickerWithPath(step1Picker, path);
                }

                UpdateStepProgress();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[SyncDirectoryPickers] Exception occurred");
            }
        }

        private void InitializeModDirectoryWatcher()
        {
            try
            {
                if (MainConfig.SourcePath != null && Directory.Exists(MainConfig.SourcePath.FullName))
                {
                    SetupModDirectoryWatcher(MainConfig.SourcePath.FullName);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[InitializeModDirectoryWatcher] Failed to initialize mod workspace directory watcher");
            }
        }

        private void SetupModDirectoryWatcher(string path)
        {
            FileSystemService.SetupModDirectoryWatcher(path, UpdateDownloadStatus);

        }

        [SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private void UpdateDownloadStatus(string changedFilePath = null)
        {

            _ = Task.Run((async () =>
            {
                try
                {
                    if (MainConfig.SourcePath is null || !Directory.Exists(MainConfig.SourcePath.FullName))
                    {
                        return;
                    }

                    if (MainConfig.AllComponents.Count == 0)
                    {
                        return;
                    }

                    // Clear validation cache for components affected by the changed file
                    if (!string.IsNullOrEmpty(changedFilePath))
                    {
                        string fileName = Path.GetFileName(changedFilePath);
                        await Logger.LogVerboseAsync($"[FileValidation] File changed: {fileName}");

                        // Find components that reference this file in their instructions and clear their cache
                        foreach (ModComponent component in MainConfig.AllComponents)
                        {
                            bool componentAffected = false;

                            // Check if component references this file in instructions
                            if (component.Instructions != null)
                            {
                                foreach (Instruction instruction in component.Instructions)
                                {
                                    if (instruction.Source != null)
                                    {
                                        foreach (string source in instruction.Source)
                                        {
                                            if (source.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                componentAffected = true;
                                                break;
                                            }
                                        }
                                    }

                                    // Also check instruction Destination
                                    if (!componentAffected && instruction.Destination != null && instruction.Destination.Contains(fileName))
                                    {
                                        componentAffected = true;
                                    }

                                    if (componentAffected)
                                    {
                                        break;
                                    }
                                }
                            }

                            // Check if component's options reference this file
                            if (!componentAffected && component.Options != null)
                            {
                                foreach (Option option in component.Options)
                                {
                                    // Check option's instructions
                                    if (option.Instructions != null)
                                    {
                                        foreach (Instruction instruction in option.Instructions)
                                        {
                                            if (instruction.Source != null)
                                            {
                                                foreach (string source in instruction.Source)
                                                {
                                                    if (source.Contains(fileName))
                                                    {
                                                        componentAffected = true;
                                                        break;
                                                    }
                                                }
                                            }

                                            // Also check instruction Destination
                                            if (!componentAffected && instruction.Destination != null && instruction.Destination.Contains(fileName))
                                            {
                                                componentAffected = true;
                                            }

                                            if (componentAffected)
                                            {
                                                break;
                                            }
                                        }
                                    }

                                    if (componentAffected)
                                    {
                                        break;
                                    }
                                }
                            }

                            if (componentAffected)
                            {
                                Core.Services.ComponentValidationService.ClearValidationCacheForComponent(component.Guid.ToString());
                                await Logger.LogVerboseAsync($"[FileValidation] Cleared validation cache for component '{component.Name}' due to file change: {fileName}");
                            }
                        }
                    }

                    await Logger.LogVerboseAsync($"[FileValidation] Starting scan. Mod workspace: {MainConfig.SourcePath.FullName}");
                    int downloadedCount = 0;
                    int totalSelected = 0;
                    await Logger.LogVerboseAsync($"[FileValidation] Scanning {MainConfig.AllComponents.Count} components for download status");

                    foreach (ModComponent component in MainConfig.AllComponents)
                    {
                        if (!component.IsSelected)
                        {
                            continue;
                        }

                        totalSelected++;
                        await Logger.LogVerboseAsync($"[FileValidation] Checking component: {component.Name} (GUID: {component.Guid})");

                        bool allUrlsCached = true;
                        if (component.ResourceRegistry.Count > 0)
                        {
                            await Logger.LogVerboseAsync($"[FileValidation] ModComponent has {component.ResourceRegistry.Count} URLs:");
                            foreach (string url in component.ResourceRegistry.Keys)
                            {

                                bool isCached = DownloadCacheService.IsCached(url);
                                if (isCached)
                                {
                                    List<string> cachedArchive = Core.Services.DownloadCacheService.GetFileNames(url);
                                    await Logger.LogVerboseAsync($"[FileValidation]   URL: {url} - CACHED (Archive: {cachedArchive})");
                                }
                                else
                                {
                                    await Logger.LogVerboseAsync($"[FileValidation]   URL: {url} - NOT CACHED");
                                    allUrlsCached = false;
                                }
                            }
                        }
                        else
                        {
                            await Logger.LogVerboseAsync($"[FileValidation] ModComponent has no URLs");
                            allUrlsCached = false;
                        }

                        component.IsDownloaded = allUrlsCached;
                        await Logger.LogVerboseAsync($"[FileValidation] ModComponent '{component.Name}': {(allUrlsCached ? "DOWNLOADED" : "MISSING")}");
                        if (allUrlsCached)
                        {
                            downloadedCount++;
                        }
                    }

                    await Logger.LogVerboseAsync($"Download scan complete: {downloadedCount}/{totalSelected} mods ready");

                    Dispatcher.UIThread.Post(async () =>
                    {
                        await Logger.LogVerboseAsync("[UpdateDownloadStatus] Posting to dispatcher");
                        TextBlock statusText = GettingStartedTabControl.FindControl<TextBlock>("DownloadStatusText");
                        if (statusText != null)
                        {
                            if (totalSelected == 0)
                            {
                                statusText.Text = "No mods selected for installation.";
                                statusText.Foreground = Brushes.Gray;
                            }
                            else if (downloadedCount == totalSelected)
                            {
                                statusText.Text = $"✅ All {totalSelected} selected mod(s) are downloaded!";
                                statusText.Foreground = Brushes.Green;
                            }
                            else
                            {
                                statusText.Text = $"⚠️ {downloadedCount}/{totalSelected} selected mod(s) downloaded. {totalSelected - downloadedCount} missing.";
                                statusText.Foreground = Brushes.Orange;
                            }
                        }

                        await Logger.LogVerboseAsync("[UpdateDownloadStatus] Refreshing mod list items");
                        RefreshModListItems();

                        await Logger.LogVerboseAsync("[UpdateDownloadStatus] Updating step progress");
                        UpdateStepProgress();

                        await Logger.LogVerboseAsync("[UpdateDownloadStatus] Completed");
                    });
                }
                catch (Exception ex)
                {
                    await Logger.LogExceptionAsync(ex, "[UpdateDownloadStatus] Error scanning mod workspace for downloads");
                }
            }));
        }

        private void ResetDownloadStatusDisplay()
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(ResetDownloadStatusDisplay, DispatcherPriority.Normal);
                    return;
                }
                TextBlock statusText = GettingStartedTabControl.FindControl<TextBlock>("DownloadStatusText");
                if (statusText != null)
                {

                    statusText.Text = "Checking download status...";
                    statusText.Foreground = Brushes.Gray;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[ResetDownloadStatusDisplay] Error resetting download status display");
            }
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void ScrapeDownloadsButton_Click(object sender, RoutedEventArgs e)
        {
            await Logger.LogVerboseAsync("[ScrapeDownloadsButton_Click] Starting download session");
            HasFetchedDownloads = true;
            await _downloadOrchestrationService.StartDownloadSessionAsync(() => UpdateDownloadStatus(null));
        }

        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void DownloadStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await Logger.LogVerboseAsync("[DownloadStatusButton_Click] Showing download status");
            await _downloadOrchestrationService.ShowDownloadStatusAsync();
        }

        private void StopDownloadsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _downloadOrchestrationService.CancelAllDownloads(closeWindow: true);
                Logger.LogInfo("[StopDownloadsButton_Click] User requested to stop all downloads and close window");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[StopDownloadsButton_Click] Error stopping downloads");
            }
        }

        private void InitializeDownloadAnimationTimer()
        {
            _downloadAnimationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _downloadAnimationTimer.Tick += (sender, e) =>
            {
                TextBlock runningText = GettingStartedTabControl.FindControl<TextBlock>("DownloadRunningText");
                if (runningText != null && runningText.IsVisible)
                {
                    _downloadAnimationDots = (_downloadAnimationDots + 1) % 4;
                    string dots = new string('.', _downloadAnimationDots);
                    runningText.Text = $"Running{dots}";
                }
            };
        }

        private void OnDownloadStateChanged(object sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    UpdateDownloadIndicators();
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "[OnDownloadStateChanged] Error updating download indicators");
                }
            });
        }

        private void UpdateDownloadIndicators()
        {
            try
            {
                Avalonia.Controls.Shapes.Ellipse ledIndicator = GettingStartedTabControl.FindControl<Avalonia.Controls.Shapes.Ellipse>("DownloadLedIndicator");
                TextBlock runningText = GettingStartedTabControl.FindControl<TextBlock>("DownloadRunningText");
                Button stopButton = GettingStartedTabControl.FindControl<Button>("StopDownloadsButton");
                TextBlock progressText = GettingStartedTabControl.FindControl<TextBlock>("DownloadProgressText");

                bool isDownloadInProgress = _downloadOrchestrationService.IsDownloadInProgress;

                if (ledIndicator != null)
                {
                    ledIndicator.Fill = isDownloadInProgress
                        ? ThemeResourceHelper.DownloadLedActiveBrush
                        : ThemeResourceHelper.DownloadLedInactiveBrush;
                }

                if (runningText != null)
                {
                    runningText.IsVisible = isDownloadInProgress;
                }

                if (stopButton != null)
                {
                    stopButton.IsVisible = isDownloadInProgress;
                }

                if (progressText != null)
                {
                    progressText.IsVisible = isDownloadInProgress;
                    if (isDownloadInProgress)
                    {
                        int completed = _downloadOrchestrationService.CompletedComponents;
                        int total = _downloadOrchestrationService.TotalComponentsToDownload;
                        progressText.Text = $"Downloaded: {completed} / {total} mods";
                    }
                }

                if (isDownloadInProgress)
                {
                    if (!_downloadAnimationTimer.IsEnabled)
                    {
                        _downloadAnimationDots = 0;
                        _downloadAnimationTimer.Start();
                    }
                }
                else
                {
                    if (_downloadAnimationTimer.IsEnabled)
                    {
                        _downloadAnimationTimer.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[UpdateDownloadIndicators] Error in UpdateDownloadIndicators");
            }
        }
        private void OpenModDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainConfig.SourcePath is null || !Directory.Exists(MainConfig.SourcePath.FullName))
                {
                    Logger.LogWarning("[OpenModDirectoryButton_Click] Mod workspace directory is not set or does not exist.");
                    return;
                }

                if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
                {
                    _ = System.Diagnostics.Process.Start("explorer.exe", MainConfig.SourcePath.FullName);
                }
                else if (UtilityHelper.GetOperatingSystem() == OSPlatform.OSX)
                {
                    _ = System.Diagnostics.Process.Start("open", MainConfig.SourcePath.FullName);
                }
                else
                {
                    _ = System.Diagnostics.Process.Start("xdg-open", MainConfig.SourcePath.FullName);
                }

                Logger.Log($"Opened mod workspace: {MainConfig.SourcePath.FullName}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[OpenModDirectoryButton_Click] Error in OpenModDirectoryButton_Click: Failed to open mod workspace");
            }
        }

        private static async Task TryAutoGenerateInstructionsForComponents([NotNull][ItemNotNull] List<ModComponent> components)
        {
            await Logger.LogVerboseAsync("[TryAutoGenerateInstructionsForComponents] Starting auto-generation");
            await ComponentProcessingService.TryAutoGenerateInstructionsForComponentsAsync(components);
        }

        #region Selection Methods

        private void RefreshCategorySelectionControl()
        {
            try
            {
                Logger.LogVerbose($"[RefreshCategorySelectionControl] START - CurrentComponent={(CurrentComponent is null ? "null" : $"'{CurrentComponent.Name}'")}");
                Logger.LogVerbose($"[RefreshCategorySelectionControl] AllComponents count={MainConfig.AllComponents.Count}");

                CategorySelectionControl categoryControl = EditorTabControl?.FindControl<CategorySelectionControl>("CategorySelectionControl");
                Logger.LogVerbose($"[RefreshCategorySelectionControl] CategorySelectionControl found: {categoryControl != null}");
                if (categoryControl != null)
                {
                    Logger.LogVerbose($"[RefreshCategorySelectionControl] Calling RefreshCategories with {MainConfig.AllComponents.Count} components");
                    categoryControl.RefreshCategories(MainConfig.AllComponents);
                    Logger.LogVerbose("[RefreshCategorySelectionControl] RefreshCategories completed");

                    if (CurrentComponent != null)
                    {
                        Logger.LogVerbose($"[RefreshCategorySelectionControl] Setting SelectedCategories from CurrentComponent (count={CurrentComponent.Category.Count})");
                        if (CurrentComponent.Category.Count > 0)
                        {
                            Logger.LogVerbose($"[RefreshCategorySelectionControl] Categories to set: {string.Join(", ", CurrentComponent.Category)}");
                        }
                        categoryControl.SelectedCategories = (List<string>)CurrentComponent.Category;
                        Logger.LogVerbose("[RefreshCategorySelectionControl] SelectedCategories set");
                    }
                    else
                    {
                        Logger.LogVerbose("[RefreshCategorySelectionControl] CurrentComponent is null, not setting SelectedCategories");
                    }
                }
                else
                {
                    Logger.LogVerbose("[RefreshCategorySelectionControl] CategorySelectionControl is null, cannot refresh");
                }
                Logger.LogVerbose("[RefreshCategorySelectionControl] COMPLETED");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[RefreshCategorySelectionControl] Exception occurred");
            }
        }
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            _selectionService.SelectAll((component, visited) => ComponentCheckboxChecked(component, visited));
            UpdateModCounts();
            RefreshModListVisuals();
            UpdateStepProgress();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _suppressComponentCheckboxEvents = true;
                _selectionService.DeselectAll((component, visited) => ComponentCheckboxUnchecked(component, visited));

                UpdateModCounts();
                RefreshModListVisuals();
                UpdateStepProgress();
                ResetDownloadStatusDisplay();
            }
            finally
            {
                _suppressComponentCheckboxEvents = false;
            }
        }
        private void InitializeFilterUi(List<ModComponent> components)
        {
            _filterUiService.InitializeFilters(components, ModListSidebar?.TierSelectionComboBox, ModListSidebar?.CategorySelectionItemsControl);
        }
        private void CategoryItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Method unintentionally left empty.
        }
        private void SelectByTier_Click(object sender, RoutedEventArgs e)
        {
            var selectedTier = ModListSidebar?.TierSelectionComboBox?.SelectedItem as TierFilterItem;
            _filterUiService.SelectByTier(selectedTier, (c, visited) => ComponentCheckboxChecked(c, visited), () =>
            {
                UpdateModCounts();
                RefreshModListVisuals();
                UpdateStepProgress();
            });
        }
        private void ClearCategorySelection_Click(object sender, RoutedEventArgs e)
        {
            _filterUiService.ClearCategorySelections((item, handler) => item.PropertyChanged += handler);
        }
        private void ApplyCategorySelections_Click(object sender, RoutedEventArgs e)
        {
            _filterUiService.ApplyCategorySelections((c, visited) => ComponentCheckboxChecked(c, visited), () =>
            {
                UpdateModCounts();
                RefreshModListVisuals();
                UpdateStepProgress();
            });
        }
        private void EditorTab_ExpandAllSectionsRequested(object sender, RoutedEventArgs e)
        {
            Logger.LogVerbose("[EditorTab] ExpandAllSectionsRequested received by MainWindow (no additional action required).");
        }

        private void EditorTab_CollapseAllSectionsRequested(object sender, RoutedEventArgs e)
        {
            Logger.LogVerbose("[EditorTab] CollapseAllSectionsRequested received by MainWindow (no additional action required).");
        }
        private void EditorTab_AutoGenerateInstructionsRequested(object sender, RoutedEventArgs e) => AutoGenerateInstructions_Click(sender, e);
        private void EditorTab_AddNewInstructionRequested(object sender, RoutedEventArgs e) => AddNewInstruction_Click(sender, e);
        private void EditorTab_DeleteInstructionRequested(object sender, RoutedEventArgs e) => DeleteInstruction_Click(sender, e);
        private void EditorTab_BrowseDestinationRequested(object sender, RoutedEventArgs e) => BrowseDestination_Click(sender, e);
        private void EditorTab_BrowseSourceFilesRequested(object sender, RoutedEventArgs e) => BrowseSourceFiles_Click(sender, e);
        private void EditorTab_BrowseModFilesRequested(object sender, RoutedEventArgs e) => BrowseModFiles_Click(sender, e);
        private void EditorTab_MoveInstructionUpRequested(object sender, RoutedEventArgs e) => MoveInstructionUp_Click(sender, e);
        private void EditorTab_MoveInstructionDownRequested(object sender, RoutedEventArgs e) => MoveInstructionDown_Click(sender, e);
        private void EditorTab_AddNewOptionRequested(object sender, RoutedEventArgs e) => AddNewOption_Click(sender, e);
        private void EditorTab_DeleteOptionRequested(object sender, RoutedEventArgs e) => DeleteOption_Click(sender, e);
        private void EditorTab_MoveOptionUpRequested(object sender, RoutedEventArgs e) => MoveOptionUp_Click(sender, e);
        private void EditorTab_MoveOptionDownRequested(object sender, RoutedEventArgs e) => MoveOptionDown_Click(sender, e);

        private void RawTab_ApplyEditorChangesRequested(object sender, RoutedEventArgs e) => RawTabApply_Click(sender, e);
        private void RawTab_GenerateGuidRequested(object sender, RoutedEventArgs e) => GenerateGuidButton_Click(sender, e);

        private void SummaryTab_OpenLinkRequested(object sender, TappedEventArgs e) => OpenLink_Click(sender, e);
        private void SummaryTab_CopyTextToClipboardRequested(object sender, RoutedEventArgs e) => CopyTextToClipboard_Click(sender, e);
        private void SummaryTab_SummaryOptionPointerPressedRequested(object sender, PointerPressedEventArgs e) => SummaryOptionBorder_PointerPressed(sender, e);
        private void SummaryTab_CheckBoxChangedRequested(object sender, RoutedEventArgs e) => OnCheckBoxChanged(sender, e);
        private void SummaryTab_JumpToInstructionRequested(object sender, RoutedEventArgs e) => JumpToInstruction_Click(sender, e);

        [UsedImplicitly]
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void JumpToCurrentStep_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    ScrollViewer scrollViewer = GettingStartedTabControl.FindControl<ScrollViewer>("PART_ScrollViewer");
                    if (scrollViewer is null)
                    {
                        scrollViewer = GettingStartedTabControl.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                    }

                    if (scrollViewer is null)
                    {
                        return;
                    }

                    await _stepNavigationService.JumpToCurrentStepAsync(scrollViewer, name => GettingStartedTabControl.FindControl<Border>(name));
                });
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[JumpToCurrentStep_Click] Exception occurred");
            }
        }
        #region Validation Results Display

        private void ShowValidationResults()
        {
            _validationDisplayService.ShowValidationResults(
                GettingStartedTabControl.FindControl<Border>("ValidationResultsArea"),
                GettingStartedTabControl.FindControl<TextBlock>("ValidationSummaryText"),
                GettingStartedTabControl.FindControl<StackPanel>("ErrorNavigationArea"),
                GettingStartedTabControl.FindControl<Border>("ErrorDetailsArea"),
                GettingStartedTabControl.FindControl<Border>("ValidationSuccessArea"),
                IsComponentValidForInstallation
            );
        }

        private void PrevErrorButton_Click(object sender, RoutedEventArgs e)
        {
            _validationDisplayService.NavigateToPreviousError(
                GettingStartedTabControl.FindControl<TextBlock>("ErrorCounterText"),
                GettingStartedTabControl.FindControl<TextBlock>("ErrorModNameText"),
                GettingStartedTabControl.FindControl<TextBlock>("ErrorTypeText"),
                GettingStartedTabControl.FindControl<TextBlock>("ErrorDescriptionText"),
                GettingStartedTabControl.FindControl<Button>("AutoFixButton"),
                GettingStartedTabControl.FindControl<Button>("PrevErrorButton"),
                GettingStartedTabControl.FindControl<Button>("NextErrorButton")
            );
        }

        private void NextErrorButton_Click(object sender, RoutedEventArgs e)
        {
            _validationDisplayService.NavigateToNextError(
                GettingStartedTabControl.FindControl<TextBlock>("ErrorCounterText"),
                GettingStartedTabControl.FindControl<TextBlock>("ErrorModNameText"),
                GettingStartedTabControl.FindControl<TextBlock>("ErrorTypeText"),
                GettingStartedTabControl.FindControl<TextBlock>("ErrorDescriptionText"),
                GettingStartedTabControl.FindControl<Button>("AutoFixButton"),
                GettingStartedTabControl.FindControl<Button>("PrevErrorButton"),
                GettingStartedTabControl.FindControl<Button>("NextErrorButton")
            );
        }

        private void AutoFixButton_Click(object sender, RoutedEventArgs e)
        {
            if (_validationDisplayService.AutoFixCurrentError(RefreshSingleComponentVisuals))
            {
                ShowValidationResults();
            }
        }
        private void JumpToModButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ModComponent currentError = _validationDisplayService.GetCurrentError();
                if (currentError is null)
                {
                    return;
                }

                (string ErrorType, string Description, bool CanAutoFix) = _validationService.GetComponentErrorDetails(currentError);

                if (ModListBox?.ItemsSource != null)
                {
                    ModListBox.SelectedItem = currentError;
                    ModListBox.ScrollIntoView(currentError);
                }

                TabControl tabControl = this.FindControl<TabControl>("TabControl");
                if (tabControl != null)
                {

                    if (ErrorType.Contains("Invalid download URLs"))
                    {
                        TabItem guiEditTab = this.FindControl<TabItem>("GuiEditTabItem");
                        if (guiEditTab != null)
                        {
                            tabControl.SelectedItem = guiEditTab;
                        }
                    }
                    else
                    {

                        TabItem summaryTab = this.FindControl<TabItem>("SummaryTabItem");
                        if (summaryTab != null)
                        {
                            tabControl.SelectedItem = summaryTab;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[JumpToModButton_Click] Error in JumpToModButton_Click");
            }
        }
        #endregion
        #region URL Validation Helper Methods

        private bool _widescreenNotificationShown;

        private async Task<bool> ShowWidescreenNotificationAsync()
        {

            if (_widescreenNotificationShown)
            {
                return true;
            }

            try
            {
                var dialog = new WidescreenNotificationDialog(MainConfig.WidescreenWarningContent);
                bool? result = await dialog.ShowDialog<bool?>(this);

                if (dialog.DontShowAgain)
                {
                    _widescreenNotificationShown = true;
                }

                return result == true && !dialog.UserCancelled;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"[ShowWidescreenNotificationAsync] Error showing widescreen notification: {ex.Message}");
                return true;
            }
        }

        #endregion
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void JumpToInstruction_Click(
            [NotNull] object sender,
            [NotNull] RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Instruction instruction)
            {
                try
                {
                    EditorMode = true;

                    EditorTab editorTab = this.FindControl<EditorTab>("EditorTabControl");
                    if (editorTab is null)
                    {
                        await Logger.LogWarningAsync("[JumpToInstruction_Click] EditorTabControl not found");
                        return;
                    }

                    Expander instructionsExpander = editorTab.FindControl<Expander>("InstructionsExpander");
                    ScrollViewer scrollViewer = ScrollNavigationService.FindScrollViewer(GuiEditTabItem);

                    await ScrollNavigationService.NavigateToControlAsync(
                        tabItem: GuiEditTabItem,
                        expander: instructionsExpander,
                        scrollViewer: scrollViewer,
                        targetControl: FindInstructionEditorControl(instruction, editorTab)
                    );
                }
                catch (Exception ex)
                {
                    await Logger.LogExceptionAsync(ex, "[JumpToInstruction_Click] Failed to jump to instruction");
                }
            }
        }

        [CanBeNull]
        private static InstructionEditorControl FindInstructionEditorControl(Instruction targetInstruction, EditorTab editorTab)
        {
            ItemsRepeater instructionsRepeater = editorTab?.FindControl<ItemsRepeater>("InstructionsRepeater");
            if (!(instructionsRepeater is null))
            {
                return ScrollNavigationService.FindControlRecursive<InstructionEditorControl>(
                    instructionsRepeater.Parent as Control,
                    control =>
                    {
                        if (control.DataContext is Instruction instruction &&
                               ReferenceEquals(instruction, targetInstruction))
                        {
                            return true;
                        }

                        return false;
                    });
            }
            return null;
        }

        public void UpdateHolopatcherVersionDisplay()
        {
            try
            {
                TextBlock versionText = this.FindControl<TextBlock>("HolopatcherVersionText");
                if (versionText is null)
                {
                    return;
                }

                string version = GetCurrentPyKotorVersion();
                versionText.Text = version;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[UpdateHolopatcherVersionDisplay] Failed to update HoloPatcher version display");
            }
        }

        public async Task UpdateHolopatcherVersionDisplayWithRetryAsync(int maxRetries = 3)
        {
            var cts = new CancellationTokenSource();
            try
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        TextBlock versionText = this.FindControl<TextBlock>("HolopatcherVersionText");
                        if (versionText is null)
                        {
                            return;
                        }

                        string version = GetCurrentPyKotorVersion();
                        versionText.Text = version;

                        // If we got a successful version (not missing/incomplete), we're done
                        if (!version.Contains("missing") && !version.Contains("incomplete"))
                        {
                            await Logger.LogVerboseAsync($"[UpdateHolopatcherVersionDisplayWithRetryAsync] Success on attempt {i + 1}: {version}");
                            return;
                        }

                        // If this isn't the last attempt, wait and try again
                        if (i < maxRetries - 1)
                        {
                            await Logger.LogVerboseAsync($"[UpdateHolopatcherVersionDisplayWithRetryAsync] Attempt {i + 1} failed with: {version}, retrying in 1 second...");
                            await Task.Delay(1000, cts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogExceptionAsync(ex, $"[UpdateHolopatcherVersionDisplayWithRetryAsync] Failed to update HoloPatcher version display on attempt {i + 1}");
                        if (i < maxRetries - 1)
                        {
                            await Task.Delay(1000, cts.Token);
                        }
                    }
                }
            }
            finally
            {
                cts.Dispose();
            }
        }

        private static string GetCurrentPyKotorVersion()
        {
            try
            {
                string baseDir = UtilityHelper.GetBaseDirectory();
                string resourcesDir = UtilityHelper.GetResourcesDirectory(baseDir);
                string pyKotorPath = Path.Combine(resourcesDir, "PyKotor");
                string holopatcherPath = Path.Combine(pyKotorPath, "Tools", "HoloPatcher", "src", "holopatcher");

                if (!Directory.Exists(pyKotorPath))
                {
                    return "PyKotor: Not installed";
                }

                // Check if Tools directory exists
                string toolsPath = Path.Combine(pyKotorPath, "Tools");
                if (!Directory.Exists(toolsPath))
                {
                    return "PyKotor: Incomplete (Tools missing)";
                }

                // Check if HoloPatcher directory exists
                string holopatcherDir = Path.Combine(toolsPath, "HoloPatcher");
                if (!Directory.Exists(holopatcherDir))
                {
                    return "PyKotor: Incomplete (HoloPatcher missing)";
                }

                // Check if src directory exists
                string srcPath = Path.Combine(holopatcherDir, "src");
                if (!Directory.Exists(srcPath))
                {
                    return "PyKotor: Incomplete (HoloPatcher src missing)";
                }

                if (!Directory.Exists(holopatcherPath))
                {
                    return "PyKotor: Incomplete (HoloPatcher incomplete)";
                }

                // Try to read version from config.py
                string configPath = Path.Combine(holopatcherPath, "config.py");
                if (File.Exists(configPath))
                {
                    try
                    {
                        string configContent = File.ReadAllText(configPath);
                        Match versionMatch = Regex.Match(
                            configContent,
                            @"""currentVersion"":\s*""([^""]+)""",
                            RegexOptions.IgnoreCase,
                            TimeSpan.FromSeconds(3)
                        );
                        if (versionMatch.Success)
                        {
                            return $"PyKotor: v{versionMatch.Groups[1].Value}";
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "[GetCurrentPyKotorVersion] Error reading config.py");
                    }
                }

                return "PyKotor: Not installed";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[GetCurrentPyKotorVersion] Error getting PyKotor version");
                return "PyKotor: Error";
            }
        }

        /// <summary>
        /// Jumps to a specific component in the components list
        /// </summary>
        private void JumpToComponent(Guid componentGuid)
        {
            try
            {
                // Switch to editor mode if not already there
                if (!EditorMode)
                {
                    EditorMode = true;
                }

                // Find the component in the current mod directory
                ModComponent component = MainConfig.AllComponents.Find(c => c.Guid == componentGuid);
                if (component is null)
                {
                    Logger.LogWarning($"[JumpToComponent] Component with GUID {componentGuid} not found");
                    return;
                }

                // Set the current component and switch to the GUI edit tab
                SetCurrentModComponent(component);
                SetTabInternal(TabControl, GuiEditTabItem);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[JumpToComponent] Failed to jump to component");
            }
        }

        /// <summary>
        /// Handles the Add New Mod button click from the vertical toolbar
        /// </summary>
        private void AddNewMod_Click(object sender, RoutedEventArgs e)
        {
            ModComponent newMod = ModManagementService.CreateMod();
            if (newMod is null)
            {
                return;
            }

            SetCurrentModComponent(newMod);
            SetTabInternal(TabControl, GuiEditTabItem);
        }

        /// <summary>
        /// Handles the Show Mod Statistics button click from the vertical toolbar
        /// </summary>
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void ShowModStatistics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ModStatistics stats = ModManagementService.GetModStatistics();
                string statsText = "📊 Mod Statistics\n\n" +
                                   $"Total Mods: {stats.TotalMods}\n" +
                                   $"Selected: {stats.SelectedMods}\n" +
                                   $"Downloaded: {stats.DownloadedMods}\n\n" +
                                   $"Categories:\n{string.Join("\n", stats.Categories.Select(c => $"  • {c.Key}: {c.Value}"))}\n\n" +
                                   $"Tiers:\n{string.Join("\n", stats.Tiers.Select(t => $"  • {t.Key}: {t.Value}"))}\n\n" +
                                   $"Average Instructions/Mod: {stats.AverageInstructionsPerMod:F1}\n" +
                                   $"Average Options/Mod: {stats.AverageOptionsPerMod:F1}";
                await InformationDialog.ShowInformationDialogAsync(this, statsText);
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[ShowModStatistics_Click] Exception occurred");
                await InformationDialog.ShowInformationDialogAsync(this, $"An error occurred: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the Validate All Mods button click from the vertical toolbar
        /// </summary>
        [SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "<Pending>")]
        private async void ValidateAllMods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Dictionary<ModComponent, ModValidationResult> results = ModManagementService.ValidateAllMods();
                int errorCount = results.Count(r => !r.Value.IsValid);
                int warningCount = results.Sum(r => r.Value.Warnings.Count);
                await InformationDialog.ShowInformationDialogAsync(this,
                    "Validation complete!\n\n" +
                    $"Errors: {errorCount}\n" +
                    $"Warnings: {warningCount}\n\n" +
                    $"Valid mods: {results.Count(r => r.Value.IsValid)}/{results.Count}");
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[ValidateAllMods_Click] Exception occurred");
                await InformationDialog.ShowInformationDialogAsync(this, $"An error occurred: {ex.Message}");
            }
        }

        #region Wizard Mode Management

        /// <summary>
        /// Enters wizard mode, showing the wizard interface and hiding the main editor UI
        /// </summary>
        private void EnterWizardMode()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(EnterWizardMode);
                return;
            }

            if (_isWizardMode)
            {
                return;
            }

            _isWizardMode = true;

            // Initialize the wizard with current data and provide MainWindow reference
            if (this.FindControl<WizardHostControl>("WizardHost") is WizardHostControl wizardHost)
            {
                ModListSidebar modListSidebar = this.FindControl<ModListSidebar>("ModListSidebar");
                wizardHost.Initialize(MainConfigInstance, MainConfig.AllComponents, this, modListSidebar);

                // Subscribe to wizard events
                wizardHost.WizardCompleted += OnWizardCompleted;
                wizardHost.DownloadFetchRequested += OnWizardDownloadFetchRequested;
                wizardHost.DownloadStatusRequested += OnWizardDownloadStatusRequested;
                wizardHost.DownloadOpenFolderRequested += OnWizardDownloadOpenFolderRequested;
                wizardHost.DownloadStopRequested += OnWizardDownloadStopRequested;
            }

            _ = Logger.LogVerboseAsync("[MainWindow] Entered wizard mode");
            UpdateWorkflowSurfaces();
        }

        /// <summary>
        /// Exits wizard mode, showing the main editor UI and hiding the wizard
        /// </summary>
        private void ExitWizardMode()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ExitWizardMode);
                return;
            }

            if (!_isWizardMode)
            {
                return;
            }

            _isWizardMode = false;

            if (this.FindControl<WizardHostControl>("WizardHost") is WizardHostControl wizardHost)
            {
                // Unsubscribe from wizard events
                wizardHost.WizardCompleted -= OnWizardCompleted;
                wizardHost.DownloadFetchRequested -= OnWizardDownloadFetchRequested;
                wizardHost.DownloadStatusRequested -= OnWizardDownloadStatusRequested;
                wizardHost.DownloadOpenFolderRequested -= OnWizardDownloadOpenFolderRequested;
                wizardHost.DownloadStopRequested -= OnWizardDownloadStopRequested;

                // Cleanup wizard resources
                wizardHost.Cleanup();
            }

            _ = Logger.LogVerboseAsync("[MainWindow] Exited wizard mode");
            UpdateWorkflowSurfaces();
        }

        /// <summary>
        /// Called when the wizard is completed
        /// </summary>
        private void OnWizardCompleted(object sender, EventArgs e)
        {
            _ = Logger.LogVerboseAsync("[MainWindow] Wizard completed successfully");
            ExitWizardMode();
        }

        /// <summary>
        /// Called when the wizard is cancelled
        /// </summary>
        private void OnWizardCancelled(object sender, EventArgs e)
        {
            _ = Logger.LogVerboseAsync("[MainWindow] Wizard cancelled by user");
            ExitWizardMode();
        }

        /// <summary>
        /// Called when the wizard requests to fetch downloads
        /// </summary>
        private void OnWizardDownloadFetchRequested(object sender, EventArgs e)
        {
            ScrapeDownloadsButton_Click(sender, new RoutedEventArgs());
        }

        /// <summary>
        /// Called when the wizard requests to show download status
        /// </summary>
        private void OnWizardDownloadStatusRequested(object sender, EventArgs e)
        {
            DownloadStatusButton_Click(sender, new RoutedEventArgs());
        }

        /// <summary>
        /// Called when the wizard requests to open the mod directory
        /// </summary>
        private void OnWizardDownloadOpenFolderRequested(object sender, EventArgs e)
        {
            OpenModDirectoryButton_Click(sender, new RoutedEventArgs());
        }

        /// <summary>
        /// Called when the wizard requests to stop downloads
        /// </summary>
        private void OnWizardDownloadStopRequested(object sender, EventArgs e)
        {
            StopDownloadsButton_Click(sender, new RoutedEventArgs());
        }

        #endregion

        /// <summary>
        /// Checks if any components have download URLs that need filename resolution
        /// </summary>
        private static bool HasComponentsNeedingResolution([NotNull][ItemNotNull] IEnumerable<ModComponent> components)
        {
            if (components == null)
            {
                return false;
            }

            foreach (ModComponent component in components)
            {
                if (component?.ResourceRegistry == null)
                {
                    continue;
                }

                // Check if any URLs don't have files populated yet
                foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry)
                {
                    if (kvp.Value?.Files == null || kvp.Value.Files.Count == 0)
                    {
                        return true; // Found at least one URL that needs resolution
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Shows a user-friendly dialog asking if the user wants to resolve download filenames
        /// </summary>
        private async Task<bool?> AskToResolveFilenamesAsync([NotNull] string context)
        {
            string contextText = string.Equals(context, "save", StringComparison.Ordinal) ? "before saving" : "after loading";

            string dialogText = $"Would you like to look up the actual filenames for your download links {contextText}?\n\n" +
                               "Download links in your mod instructions point to files on websites. Looking up the filenames helps:\n" +
                               "• Ensure the correct files are downloaded\n" +
                               "• Verify files exist before installation\n" +
                               "• Improve auto-detection of mod files\n\n" +
                               "This will check the download pages online to find the exact filenames. You can skip this and resolve them later using the 'Resolve Filenames' button.";

            return await ConfirmationDialog.ShowConfirmationDialogAsync(
                this,
                confirmText: dialogText,
                yesButtonText: "Resolve Filenames",
                noButtonText: "Skip",
                yesButtonTooltip: "Look up filenames from download pages now",
                noButtonTooltip: "Continue without resolving filenames"
            );
        }

        /// <summary>
        /// Resolves filenames for all components that need it
        /// </summary>
        private async Task ResolveAllComponentFilenamesAsync([NotNull][ItemNotNull] IReadOnlyList<ModComponent> components)
        {
            if (components == null || components.Count == 0)
            {
                return;
            }

            int resolvedCount = 0;
            int totalUrls = 0;

            await Logger.LogVerboseAsync("[ResolveAllComponentFilenamesAsync] Starting filename resolution for all components");

            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
                foreach (ModComponent component in components)
                {
                    if (component?.ResourceRegistry == null || component.ResourceRegistry.Count == 0)
                    {
                        continue;
                    }

                    // Count URLs that need resolution
                    int urlsNeedingResolution = 0;
                    foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry)
                    {
                        if (kvp.Value?.Files == null || kvp.Value.Files.Count == 0)
                        {
                            urlsNeedingResolution++;
                        }
                    }

                    if (urlsNeedingResolution > 0)
                    {
                        totalUrls += urlsNeedingResolution;
                        await Logger.LogVerboseAsync($"[ResolveAllComponentFilenamesAsync] Resolving {urlsNeedingResolution} URL(s) for component '{component.Name}'");

                        try
                        {
                            await DownloadCacheService.PreResolveUrlsAsync(
                                component,
                                DownloadCacheService.DownloadManager,
                                sequential: false,
                                cts.Token
                            );
                            resolvedCount++;
                            await Logger.LogVerboseAsync($"[ResolveAllComponentFilenamesAsync] Successfully resolved filenames for component '{component.Name}'");
                        }
                        catch (OperationCanceledException)
                        {
                            await Logger.LogVerboseAsync($"[ResolveAllComponentFilenamesAsync] Resolution cancelled for component '{component.Name}'");
                            break;
                        }
                        catch (Exception ex)
                        {
                            await Logger.LogExceptionAsync(ex, $"[ResolveAllComponentFilenamesAsync] Error resolving filenames for component '{component.Name}'");
                        }
                    }
                }
            }

            await Logger.LogVerboseAsync($"[ResolveAllComponentFilenamesAsync] Completed: resolved filenames for {resolvedCount} component(s) with {totalUrls} total URL(s)");
        }
    }
}
#endregion
