// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using JetBrains.Annotations;
using KOTORModSync;
using KOTORModSync.Core;
using KOTORModSync.Dialogs;
using KOTORModSync.Dialogs.WizardPages;
using KOTORModSync.Services;

namespace KOTORModSync.Controls
{
    public partial class WizardHostControl : UserControl
    {
        private readonly List<IWizardPage> _pages = new List<IWizardPage>();
        private ModDirectoryPage _modDirectoryPage;
        private GameDirectoryPage _gameDirectoryPage;
        private int _currentPageIndex = 0;
        private MainConfig _mainConfig;
        private List<ModComponent> _allComponents;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _navigationSemaphore = new SemaphoreSlim(1, 1);
        private Window _parentWindow;
        private readonly Border _downloadStatusBar;
        private readonly Button _downloadStatusBarButton;
        private readonly TextBlock _downloadStatusBarText;
        private readonly TextBlock _downloadStatusBarIcon;
        private Control _downloadSidebar;
        private TextBlock _downloadStatusText;
        private TextBlock _downloadRunningText;
        private TextBlock _downloadProgressText;
        private Button _downloadStatusButton;
        private Button _downloadStopButton;
        private Button _downloadFetchButton;
        private Button _downloadOpenFolderButton;
        private Avalonia.Controls.Shapes.Ellipse _downloadLedIndicator;
        private bool _downloadSidebarEnabled;
        private int _downloadSidebarStartIndex = -1;
        private bool _latestDownloadInProgress;
        private int _latestCompletedDownloads;
        private int _latestTotalDownloads;

        // Installation state
        public bool InstallationCompleted { get; private set; }
        public bool InstallationCancelled { get; private set; }

        // Widescreen state
        private bool _hasWidescreenMods;
        private List<ModComponent> _widescreenMods;

        // Events
        public event EventHandler WizardCompleted;
        public event EventHandler DownloadStatusRequested;
        public event EventHandler DownloadFetchRequested;
        public event EventHandler DownloadOpenFolderRequested;
        public event EventHandler DownloadStopRequested;

        public WizardHostControl()
        {
            InitializeComponent();
            _downloadStatusBar = this.FindControl<Border>("DownloadStatusBar");
            _downloadStatusBarButton = this.FindControl<Button>("DownloadStatusBarButton");
            _downloadStatusBarText = this.FindControl<TextBlock>("DownloadStatusBarText");
            _downloadStatusBarIcon = this.FindControl<TextBlock>("DownloadStatusBarIcon");

            if (_downloadStatusBarButton != null)
            {
                _downloadStatusBarButton.Click += (_, __) => DownloadStatusRequested?.Invoke(this, EventArgs.Empty);
                ToolTip.SetTip(_downloadStatusBarButton, "View detailed download progress.");
            }
        }

        /// <summary>
        /// Initializes the wizard with the required data
        /// </summary>
        public void Initialize([NotNull] MainConfig mainConfig, [NotNull] List<ModComponent> allComponents, [NotNull] Window parentWindow, [CanBeNull] ModListSidebar modListSidebar = null)
        {
            _mainConfig = mainConfig ?? throw new ArgumentNullException(nameof(mainConfig));
            _allComponents = allComponents ?? throw new ArgumentNullException(nameof(allComponents));
            _parentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            InitializePages();
            NavigateToPage(0);
        }

        /// <summary>
        /// Resets the wizard state for a fresh start
        /// </summary>
        public void Reset()
        {
            _pages.Clear();
            _currentPageIndex = 0;
            InstallationCompleted = false;
            InstallationCancelled = false;
            _hasWidescreenMods = false;
            _widescreenMods?.Clear();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _downloadSidebarStartIndex = -1;
            _downloadSidebarEnabled = false;
            _modDirectoryPage = null;
            _gameDirectoryPage = null;
            ResetDownloadStatus();
        }

        private void InitializePages()
        {
            _pages.Clear();

            // NOTE: LoadInstructionPage removed - it should be shown as LandingPageView outside wizard
            // The wizard should only start after an instruction file is loaded

            // 1. Welcome
            _pages.Add(new WelcomePage());

            // 2. Preamble (conditional)
            if (!string.IsNullOrWhiteSpace(_mainConfig.preambleContent))
            {
                _pages.Add(new PreamblePage(_mainConfig.preambleContent));
            }

            // 3. Mod workspace directory
            _modDirectoryPage = new ModDirectoryPage(_mainConfig);
            _pages.Add(_modDirectoryPage);

            // 4. Game directory
            _gameDirectoryPage = new GameDirectoryPage(_mainConfig);
            _pages.Add(_gameDirectoryPage);

            // 6. AspyrNotice (conditional)
            if (
                (
                    string.Equals(MainConfig.TargetGame, "KOTOR2", StringComparison.Ordinal)
                    || string.Equals(MainConfig.TargetGame, "TSL", StringComparison.Ordinal)
                )
                && !string.IsNullOrWhiteSpace(_mainConfig.aspyrExclusiveWarningContent)
            )
            {
                _pages.Add(new AspyrNoticePage(_mainConfig.aspyrExclusiveWarningContent));
            }

            // 7. ModSelection
            _pages.Add(new ModSelectionPage(_allComponents, _parentWindow as MainWindow));

            // 8. DownloadsExplain
            _pages.Add(new DownloadsExplainPage());

            // 9. Validate
            _pages.Add(new ValidatePage(_allComponents, _mainConfig));

            // 10. InstallStart
            _pages.Add(new InstallStartPage(_allComponents));

            // 11. Installing (progress page)
            _pages.Add(new InstallingPage(_allComponents, _mainConfig, _cancellationTokenSource));

            // 12. BaseInstallComplete
            _pages.Add(new BaseInstallCompletePage(0, TimeSpan.Zero, 0, 0));

            // Note: Widescreen-specific pages will be inserted dynamically after the base install if needed

            // Finished
            _pages.Add(new FinishedPage());
            _downloadSidebarStartIndex = _pages.FindIndex(page => page is DownloadsExplainPage);
            // NOTE: LoadInstructionPage removed - instruction file should already be loaded before wizard starts
        }

        private void AddWidescreenPages()
        {
            // Detect widescreen mods
            _widescreenMods = _allComponents.Where(c => c.WidescreenOnly).ToList();
            _hasWidescreenMods = _widescreenMods.Any();

            if (!_hasWidescreenMods)
            {
                return;
            }

            // Find the index to insert before FinishedPage
            int finishedPageIndex = _pages.Count - 1;

            // 11. WidescreenNotice
            if (!string.IsNullOrWhiteSpace(_mainConfig.widescreenWarningContent))
            {
                _pages.Insert(finishedPageIndex, new WidescreenNoticePage(_mainConfig.widescreenWarningContent));
                finishedPageIndex++;
            }

            // 12. WidescreenModSelection
            _pages.Insert(finishedPageIndex, new WidescreenModSelectionPage(_widescreenMods));
            finishedPageIndex++;

            // 13. WidescreenInstalling
            _pages.Insert(finishedPageIndex, new WidescreenInstallingPage(_widescreenMods, _mainConfig, _cancellationTokenSource));
            finishedPageIndex++;

            // 14. WidescreenComplete
            _pages.Insert(finishedPageIndex, new WidescreenCompletePage());
        }

        private void EnsureDownloadSidebarCreated()
        {
            if (_downloadSidebar != null)
            {
                return;
            }

            var panelBorder = new Border
            {
                Width = 320,
                Margin = new Thickness(24, 0, 0, 0),
                Padding = new Thickness(16),
            };

            var panelStack = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panelBorder.Child = panelStack;

            panelStack.Children.Add(new TextBlock
            {
                Text = "Download Center",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
            });

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            panelStack.Children.Add(buttonRow);

            _downloadFetchButton = new Button
            {
                Content = "📥 Fetch Downloads",
                Padding = new Thickness(12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            _downloadFetchButton.Click += (_, __) => DownloadFetchRequested?.Invoke(this, EventArgs.Empty);
            ToolTip.SetTip(_downloadFetchButton, "Try to download all missing mod archives automatically.");
            buttonRow.Children.Add(_downloadFetchButton);

            _downloadOpenFolderButton = new Button
            {
                Content = "📁 Open Workspace",
                Padding = new Thickness(12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            _downloadOpenFolderButton.Click += (_, __) => DownloadOpenFolderRequested?.Invoke(this, EventArgs.Empty);
            ToolTip.SetTip(_downloadOpenFolderButton, "Open your configured mod workspace folder.");
            buttonRow.Children.Add(_downloadOpenFolderButton);

            panelStack.Children.Add(new Separator());

            _downloadStatusButton = new Button
            {
                Padding = new Thickness(12),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            _downloadStatusButton.Click += (_, __) => DownloadStatusRequested?.Invoke(this, EventArgs.Empty);
            ToolTip.SetTip(_downloadStatusButton, "View detailed download progress.");
            panelStack.Children.Add(_downloadStatusButton);

            var statusGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            };
            _downloadStatusButton.Content = statusGrid;

            _downloadLedIndicator = new Ellipse
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = ThemeResourceHelper.DownloadLedInactiveBrush,
            };
            statusGrid.Children.Add(_downloadLedIndicator);

            var statusStack = new StackPanel
            {
                Spacing = 4,
            };
            Grid.SetColumn(statusStack, 1);
            statusGrid.Children.Add(statusStack);

            _downloadStatusText = new TextBlock
            {
                Text = "Downloads idle",
                FontSize = 14,
            };
            statusStack.Children.Add(_downloadStatusText);

            _downloadRunningText = new TextBlock
            {
                Text = "Running…",
                FontSize = 12,
                FontStyle = FontStyle.Italic,
                Opacity = 0.8,
                IsVisible = false,
            };
            statusStack.Children.Add(_downloadRunningText);

            _downloadProgressText = new TextBlock
            {
                Text = "No downloads queued.",
                FontSize = 12,
                Opacity = 0.8,
            };
            panelStack.Children.Add(_downloadProgressText);

            _downloadStopButton = new Button
            {
                Content = "⏹ Stop Downloads",
                Padding = new Thickness(12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsVisible = false,
            };
            _downloadStopButton.Click += (_, __) => DownloadStopRequested?.Invoke(this, EventArgs.Empty);
            ToolTip.SetTip(_downloadStopButton, "Cancel all active downloads.");
            panelStack.Children.Add(_downloadStopButton);

            panelStack.Children.Add(new TextBlock
            {
                Text = "Tip: Downloads continue in the background while you proceed.",
                FontSize = 12,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap,
            });

            _downloadSidebar = panelBorder;
            ApplyLatestDownloadState();
        }

        private void AttachSidebar(Control content)
        {
            Logger.LogVerbose($"[AttachSidebar] START: content={content?.GetType().Name}, hasParent={content?.Parent != null}");

            EnsureDownloadSidebarCreated();

            if (_downloadSidebar is null)
            {
                Logger.LogVerbose($"[AttachSidebar] No sidebar, setting content directly");
                PageContent.Content = content;
                Logger.LogVerbose($"[AttachSidebar] Content parent is now: {content?.Parent?.GetType().Name}");
                return;
            }

            Logger.LogVerbose($"[AttachSidebar] Detaching content from existing parent...");
            // Detach controls from their current parents
            DetachVisual(content);
            Logger.LogVerbose($"[AttachSidebar] Detaching sidebar from existing parent...");
            DetachVisual(_downloadSidebar);

            Logger.LogVerbose($"[AttachSidebar] Creating Grid wrapper...");
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };

            Logger.LogVerbose($"[AttachSidebar] Adding content to Grid column 0...");
            Grid.SetColumn(content, 0);
            grid.Children.Add(content);
            Logger.LogVerbose($"[AttachSidebar] Content added. Parent is now: {content?.Parent?.GetType().Name}");

            Logger.LogVerbose($"[AttachSidebar] Adding sidebar to Grid column 1...");
            Grid.SetColumn(_downloadSidebar, 1);
            grid.Children.Add(_downloadSidebar);
            _downloadSidebar.IsVisible = true;
            Logger.LogVerbose($"[AttachSidebar] Sidebar added. Grid has {grid.Children.Count} children");

            Logger.LogVerbose($"[AttachSidebar] Setting PageContent.Content to Grid...");
            PageContent.Content = grid;
            Logger.LogVerbose($"[AttachSidebar] COMPLETE: PageContent.Content is now: {PageContent.Content?.GetType().Name}");
        }

        private void DetachSidebar(Control content)
        {
            Logger.LogVerbose($"[DetachSidebar] START: content={content?.GetType().Name}, hasParent={content?.Parent != null}");

            Logger.LogVerbose($"[DetachSidebar] Detaching content...");
            DetachVisual(content);

            Logger.LogVerbose($"[DetachSidebar] Detaching sidebar...");
            DetachVisual(_downloadSidebar);
            if (_downloadSidebar != null)
            {
                _downloadSidebar.IsVisible = false;
                Logger.LogVerbose($"[DetachSidebar] Sidebar hidden");
            }

            Logger.LogVerbose($"[DetachSidebar] Setting PageContent.Content directly to content...");
            PageContent.Content = content;
            Logger.LogVerbose($"[DetachSidebar] COMPLETE: Content parent is now: {content?.Parent?.GetType().Name}");
        }

        private void ClearPageContent()
        {
            Logger.LogVerbose($"[ClearPageContent] START: PageContent.Content type={PageContent?.Content?.GetType().Name}");

            if (PageContent?.Content is Control existingContent)
            {
                Logger.LogVerbose($"[ClearPageContent] Existing content: {existingContent.GetType().Name}");

                if (existingContent is ContentControl existingContentControl)
                {
                    if (existingContentControl.Content is Control innerControl)
                    {
                        Logger.LogVerbose($"[ClearPageContent] Clearing ContentControl inner control: {innerControl.GetType().Name}");
                        existingContentControl.Content = null;

                        if (innerControl.Parent != null)
                        {
                            Logger.LogWarning($"[ClearPageContent] Inner control still has parent {innerControl.Parent.GetType().Name} after clearing ContentControl. Detaching manually.");
                            DetachVisual(innerControl);
                        }
                    }
                    else if (existingContentControl.Content != null)
                    {
                        Logger.LogVerbose("[ClearPageContent] Clearing ContentControl content (non-Control)");
                        existingContentControl.Content = null;
                    }
                }

                // If it's a Grid wrapper (from AttachSidebar), detach all its children first
                if (existingContent is Grid grid)
                {
                    Logger.LogVerbose($"[ClearPageContent] Existing content is Grid with {grid.Children.Count} children");

                    Control[] childrenCopy = grid.Children.ToArray();
                    foreach (Control child in childrenCopy)
                    {
                        Logger.LogVerbose($"[ClearPageContent] Removing Grid child: {child.GetType().Name}");
                        grid.Children.Remove(child);

                        if (child is Control childControl)
                        {
                            if (childControl.Parent != null)
                            {
                                Logger.LogWarning($"[ClearPageContent] Grid child still has parent {childControl.Parent.GetType().Name} after removal. Detaching manually.");
                            }
                            DetachVisual(childControl);
                        }
                    }
                }

                // Now detach the content itself
                Logger.LogVerbose("[ClearPageContent] Detaching existing content from PageContent...");
                DetachVisual(existingContent);
            }

            // Clear the PageContent
            if (PageContent != null)
            {
                Logger.LogVerbose("[ClearPageContent] Setting PageContent.Content = null");
                PageContent.Content = null;
            }

            Logger.LogVerbose("[ClearPageContent] COMPLETE");
        }

        private static void DetachScrollViewerContent(ScrollViewer scrollViewer)
        {
            if (scrollViewer?.Content is Control innerContent)
            {
                Logger.LogVerbose($"[DetachScrollViewerContent] Detaching ScrollViewer.Content: {innerContent.GetType().Name}");

                // Recursively detach nested controls if it's a panel
                if (innerContent is Panel panel)
                {
                    Logger.LogVerbose($"[DetachScrollViewerContent] Inner content is Panel with {panel.Children.Count} children");
                    Control[] childrenCopy = panel.Children.ToArray();
                    panel.Children.Clear();
                    Logger.LogVerbose($"[DetachScrollViewerContent] Panel children cleared");

                    foreach (Control child in childrenCopy)
                    {
                        if (child is Control childControl)
                        {
                            Logger.LogVerbose($"[DetachScrollViewerContent] Detaching child: {childControl.GetType().Name}");
                            DetachVisual(childControl);
                        }
                    }
                }

                // Detach the ScrollViewer's content itself
                scrollViewer.Content = null;
                Logger.LogVerbose($"[DetachScrollViewerContent] ScrollViewer.Content set to null");
            }
        }

        private static void DetachVisual(Control control)
        {
            if (control is null)
            {
                Logger.LogVerbose("[DetachVisual] Control is null, nothing to detach");
                return;
            }

            // Store the parent reference before attempting to detach
            StyledElement parent = control.Parent;

            if (parent is null)
            {
                Logger.LogVerbose($"[DetachVisual] Control {control.GetType().Name} has no parent, nothing to detach");
                return;
            }

            Logger.LogVerbose($"[DetachVisual] Detaching {control.GetType().Name} from parent {parent.GetType().Name}");

            switch (parent)
            {
                case ContentControl contentControl when contentControl.Content == control:
                    contentControl.Content = null;
                    Logger.LogVerbose($"[DetachVisual] Detached from ContentControl");
                    break;
                case ContentPresenter contentPresenter when contentPresenter.Content == control:
                    contentPresenter.Content = null;
                    Logger.LogVerbose($"[DetachVisual] Detached from ContentPresenter");
                    break;
                case Decorator decorator when decorator.Child == control:
                    decorator.Child = null;
                    Logger.LogVerbose($"[DetachVisual] Detached from Decorator");
                    break;
                case Panel panel:
                    if (panel.Children.Contains(control))
                    {
                        panel.Children.Remove(control);
                        Logger.LogVerbose($"[DetachVisual] Removed from Panel with {panel.Children.Count} remaining children");
                    }
                    else
                    {
                        Logger.LogWarning($"[DetachVisual] Control claims Panel parent but is not in Panel's children collection");
                    }
                    break;
                default:
                    Logger.LogWarning($"[DetachVisual] Unknown parent type: {parent.GetType().Name}, cannot detach");
                    break;
            }

            if (control.Parent != null)
            {
                Logger.LogError($"[DetachVisual] FAILED: Control still has parent {control.Parent.GetType().Name} after detach attempt");
            }
            else
            {
                Logger.LogVerbose($"[DetachVisual] SUCCESS: Control parent is now null");
            }
        }

        private void UpdateDownloadSidebarUI()
        {
            if (_downloadSidebar is null || !_downloadSidebarEnabled)
            {
                return;
            }

            if (_downloadLedIndicator != null)
            {
                _downloadLedIndicator.Fill = _latestDownloadInProgress
                    ? ThemeResourceHelper.DownloadLedActiveBrush
                    : ThemeResourceHelper.DownloadLedInactiveBrush;
            }

            if (_downloadStatusText != null)
            {
                _downloadStatusText.Text = _latestDownloadInProgress
                    ? "Downloads in progress"
                    : "Downloads idle";
            }

            if (_downloadRunningText != null)
            {
                _downloadRunningText.IsVisible = _latestDownloadInProgress;
            }

            if (_downloadProgressText != null)
            {
                if (_latestTotalDownloads > 0)
                {
                    _downloadProgressText.IsVisible = true;
                    _downloadProgressText.Text = $"Downloaded: {_latestCompletedDownloads} / {_latestTotalDownloads} mods";
                }
                else
                {
                    _downloadProgressText.IsVisible = true;
                    _downloadProgressText.Text = _latestDownloadInProgress
                        ? "Preparing downloads…"
                        : "No downloads queued.";
                }
            }

            if (_downloadStopButton != null)
            {
                _downloadStopButton.IsVisible = _latestDownloadInProgress;
            }
        }

        private void UpdateDownloadStatusBarUI()
        {
            if (_downloadStatusBar is null)
            {
                return;
            }

            if (!_downloadSidebarEnabled)
            {
                _downloadStatusBar.IsVisible = false;
                return;
            }

            _downloadStatusBar.IsVisible = true;

            if (_downloadStatusBarText != null)
            {
                if (_latestTotalDownloads > 0)
                {
                    _downloadStatusBarText.Text = $"Downloads: {_latestCompletedDownloads}/{_latestTotalDownloads} complete";
                }
                else if (_latestDownloadInProgress)
                {
                    _downloadStatusBarText.Text = "Downloads running…";
                }
                else
                {
                    _downloadStatusBarText.Text = "Downloads ready";
                }
            }

            if (_downloadStatusBarIcon != null)
            {
                _downloadStatusBarIcon.Text = _latestDownloadInProgress ? "⬇️" : "✅";
            }
        }

        private void ApplyLatestDownloadState()
        {
            UpdateDownloadSidebarUI();
            UpdateDownloadStatusBarUI();
        }

        public void UpdateDownloadUI(bool isInProgress, int completed, int total)
        {
            _latestDownloadInProgress = isInProgress;
            _latestCompletedDownloads = completed;
            _latestTotalDownloads = total;
            ApplyLatestDownloadState();
        }

        public void ResetDownloadStatus()
        {
            _latestDownloadInProgress = false;
            _latestCompletedDownloads = 0;
            _latestTotalDownloads = 0;
            ApplyLatestDownloadState();
        }

        private void NavigateToPage(int pageIndex) => _ = NavigateToPageInternalAsync(pageIndex);

        private async Task NavigateToPageInternalAsync(int pageIndex)
        {
            await _navigationSemaphore.WaitAsync();
            try
            {
                await Logger.LogVerboseAsync($"[NavigateToPage] START: pageIndex={pageIndex}, _pages.Count={_pages.Count}");

                if (pageIndex < 0 || pageIndex >= _pages.Count)
                {
                    await Logger.LogErrorAsync($"[NavigateToPage] ABORT: Invalid page index {pageIndex}");
                    return;
                }

                _currentPageIndex = pageIndex;
                IWizardPage page = _pages[pageIndex];

                if (page is null)
                {
                    await Logger.LogErrorAsync($"[NavigateToPage] Page at index {pageIndex} is null. Aborting navigation.");
                    return;
                }

                IWizardPage activePage = page;

                await Logger.LogVerboseAsync($"[NavigateToPage] Page type: {activePage.GetType().Name}, Title: {activePage.Title}");
                await Logger.LogVerboseAsync($"[NavigateToPage] Page.Content type: {activePage.Content?.GetType().Name}, HasParent: {activePage.Content?.Parent != null}");
                if (activePage.Content?.Parent != null)
                {
                    await Logger.LogWarningAsync($"[NavigateToPage] WARNING: Page content already has parent: {activePage.Content.Parent.GetType().Name}");
                }

                bool showDownloads = _downloadSidebarStartIndex >= 0 && pageIndex >= _downloadSidebarStartIndex;
                _downloadSidebarEnabled = showDownloads;
                await Logger.LogVerboseAsync($"[NavigateToPage] showDownloads={showDownloads}");

                if (_downloadStatusBar != null)
                {
                    _downloadStatusBar.IsVisible = showDownloads;
                }
                ApplyLatestDownloadState();

                // Update header
                PageTitleText.Text = activePage.Title;
                PageSubtitleText.Text = activePage.Subtitle;
                ProgressStepText.Text = $"Step {pageIndex + 1} of {_pages.Count}";
                WizardProgress.Maximum = _pages.Count;
                WizardProgress.Value = pageIndex + 1;

                await Logger.LogVerboseAsync($"[NavigateToPage] About to clear PageContent. Current content type: {PageContent?.Content?.GetType().Name}");

                // CRITICAL: Clear existing content first to ensure clean detachment
                ClearPageContent();

                // Wait for Avalonia to process visual tree detachment of nested controls
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);

                await Logger.LogVerboseAsync($"[NavigateToPage] PageContent cleared. Page.Content parent now: {activePage.Content?.Parent?.GetType().Name ?? "null"}");

                // Detach the page content from any existing parent
                if (activePage.Content?.Parent != null)
                {
                    await Logger.LogWarningAsync($"[NavigateToPage] Page content still has parent after clear: {activePage.Content.Parent.GetType().Name}. Attempting detach...");
                    DetachVisual(activePage.Content);
                    await Logger.LogVerboseAsync($"[NavigateToPage] After DetachVisual, parent is: {activePage.Content?.Parent?.GetType().Name ?? "null"}");
                }

                // If the page content is a ScrollViewer, recursively detach its nested content
                if (activePage.Content is ScrollViewer scrollViewer)
                {
                    await Logger.LogVerboseAsync($"[NavigateToPage] Page content is ScrollViewer, detaching nested content...");
                    DetachScrollViewerContent(scrollViewer);
                    await Logger.LogVerboseAsync($"[NavigateToPage] ScrollViewer nested content detached");
                }

                await Logger.LogVerboseAsync($"[NavigateToPage] Creating fresh ContentControl wrapper...");

                // Create a fresh wrapper container each time to avoid visual tree conflicts
                var pageContainer = new ContentControl
                {
                    Content = activePage.Content,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                };

                await Logger.LogVerboseAsync($"[NavigateToPage] Wrapper created. Page.Content parent now: {activePage.Content?.Parent?.GetType().Name}");

                // Update content
                if (showDownloads)
                {
                    await Logger.LogVerboseAsync($"[NavigateToPage] Attaching sidebar...");
                    EnsureDownloadSidebarCreated();
                    AttachSidebar(pageContainer);
                    await Logger.LogVerboseAsync($"[NavigateToPage] Sidebar attached. PageContent.Content type: {PageContent?.Content?.GetType().Name}");
                }
                else
                {
                    await Logger.LogVerboseAsync($"[NavigateToPage] Detaching sidebar...");
                    DetachSidebar(pageContainer);
                    await Logger.LogVerboseAsync($"[NavigateToPage] Sidebar detached. PageContent.Content type: {PageContent?.Content?.GetType().Name}");
                }

                // Update navigation buttons
                BackButton.IsEnabled = pageIndex > 0 && activePage.CanNavigateBack;
                NextButton.IsEnabled = activePage.CanNavigateForward;
                NextButton.IsVisible = pageIndex < _pages.Count - 1;
                FinishButton.IsVisible = pageIndex == _pages.Count - 1;

                // Update button text
                if (activePage is InstallingPage || activePage is WidescreenInstallingPage)
                {
                    NextButton.Content = "Continue";
                    BackButton.IsEnabled = false;
                }
                else
                {
                    NextButton.Content = "Next →";
                }

                // Call page activation
                await Logger.LogVerboseAsync($"[NavigateToPage] Calling OnNavigatedToAsync...");
                try
                {
                    await activePage.OnNavigatedToAsync(_cancellationTokenSource.Token);
                    await Logger.LogVerboseAsync($"[NavigateToPage] OnNavigatedToAsync completed successfully");
                }
                catch (Exception ex)
                {
                    await Logger.LogExceptionAsync(ex, "[NavigateToPage] Error in OnNavigatedToAsync");
                }

                await Logger.LogVerboseAsync($"[NavigateToPage] COMPLETE: Successfully navigated to page {pageIndex}");
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"[NavigateToPage] FATAL ERROR during navigation to page {pageIndex}");
                throw;
            }
            finally
            {
                _navigationSemaphore.Release();
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IWizardPage currentPage = _pages[_currentPageIndex];

                // Validate current page before proceeding
                (bool isValid, string errorMessage) = await currentPage.ValidateAsync(_cancellationTokenSource.Token);

                if (!isValid)
                {
                    await InformationDialog.ShowInformationDialogAsync(
                        _parentWindow,
                        errorMessage ?? "Please complete all required fields before continuing."
                    );
                    return;
                }

                // Call page deactivation
                await currentPage.OnNavigatingFromAsync(_cancellationTokenSource.Token);

                // Special handling for certain pages
                if (currentPage is BaseInstallCompletePage && !_hasWidescreenMods)
                {
                    // Check if widescreen pages need to be added
                    AddWidescreenPages();
                }

                // Navigate to next page
                if (_currentPageIndex < _pages.Count - 1)
                {
                    NavigateToPage(_currentPageIndex + 1);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "Error navigating to next page");
            }
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IWizardPage currentPage = _pages[_currentPageIndex];
                await currentPage.OnNavigatingFromAsync(_cancellationTokenSource.Token);

                if (_currentPageIndex > 0)
                {
                    NavigateToPage(_currentPageIndex - 1);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "Error navigating to previous page");
            }
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            InstallationCompleted = true;
            WizardCompleted?.Invoke(this, EventArgs.Empty);
        }

        [UsedImplicitly]
        private void SwitchToLightTheme_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("[ThemeSwitch] User clicked Light theme button");
            ApplyThemeAndRefresh(ThemeType.Light);
        }

        [UsedImplicitly]
        private void SwitchToK1Theme_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("[ThemeSwitch] User clicked K1 theme button");
            ApplyThemeAndRefresh(ThemeType.KOTOR);
        }

        [UsedImplicitly]
        private void SwitchToTslTheme_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("[ThemeSwitch] User clicked K2 theme button");
            ApplyThemeAndRefresh(ThemeType.KOTOR2);
        }

        private void ApplyThemeAndRefresh(ThemeType theme)
        {
            try
            {
                Logger.Log($"[ApplyThemeAndRefresh] START: Applying theme {theme}, current page index={_currentPageIndex}");

                // Apply the theme without tearing down the current visual tree
                Logger.LogVerbose($"[ApplyThemeAndRefresh] Applying theme {theme}...");
                ThemeService.ApplyTheme(theme);
                Logger.LogVerbose("[ApplyThemeAndRefresh] Theme applied");

                // Force a refresh of the current page visuals so theme resources take effect
                if (PageContent?.Content is Control currentContent)
                {
                    Logger.LogVerbose("[ApplyThemeAndRefresh] Invalidating current content visuals for theme update");
                    currentContent.InvalidateVisual();
                    currentContent.InvalidateMeasure();
                    currentContent.InvalidateArrange();
                }

                Logger.Log("[ApplyThemeAndRefresh] COMPLETE: Theme applied without reloading page content");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[ApplyThemeAndRefresh] FATAL ERROR during theme change");
                throw;
            }
        }

        public void Cleanup()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _downloadSidebarEnabled = false;

            // Properly clean up sidebar
            if (_downloadSidebar != null)
            {
                DetachVisual(_downloadSidebar);
                _downloadSidebar = null;
            }

            // Clear page content
            ClearPageContent();

            if (_downloadStatusBar != null)
            {
                _downloadStatusBar.IsVisible = false;
            }
            ResetDownloadStatus();
            _modDirectoryPage = null;
            _gameDirectoryPage = null;
        }
    }
}

