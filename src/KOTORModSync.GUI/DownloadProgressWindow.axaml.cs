// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetBrains.Annotations;
using KOTORModSync.Core;
using KOTORModSync.Core.Services.Download;
using KOTORModSync.Core.Utility;
using KOTORModSync.Dialogs;

namespace KOTORModSync
{
    public partial class DownloadProgressWindow : Window
    {
        private readonly List<DownloadProgress> _allDownloadItems = new List<DownloadProgress>();
        private readonly ObservableCollection<DownloadProgress> _activeDownloads = new ObservableCollection<DownloadProgress>();
        private readonly ObservableCollection<DownloadProgress> _pendingDownloads = new ObservableCollection<DownloadProgress>();
        private readonly ObservableCollection<DownloadProgress> _completedDownloads = new ObservableCollection<DownloadProgress>();

        // Filtered collections for tabs
        private readonly ObservableCollection<DownloadProgress> _activeDirectDownloads = new ObservableCollection<DownloadProgress>();
        private readonly ObservableCollection<DownloadProgress> _completedDirectDownloads = new ObservableCollection<DownloadProgress>();
        private readonly ObservableCollection<DownloadProgress> _activeOptimizedDownloads = new ObservableCollection<DownloadProgress>();
        private readonly ObservableCollection<DownloadProgress> _completedOptimizedDownloads = new ObservableCollection<DownloadProgress>();

        private CancellationTokenSource _cancellationTokenSource;
        private bool _isCompleted;
        private bool _mouseDownForWindowMoving;
        private PointerPoint _originalPoint;
        private int _downloadTimeoutMinutes = 10080; // 7 days
        private bool _forceCloseRequested;


        public event EventHandler<DownloadControlEventArgs> DownloadControlRequested;
        public event EventHandler WindowHidden;

        public void UpdateCheckpointProgress(string message, int current, int total)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StackPanel checkpointPanel = this.FindControl<StackPanel>("CheckpointProgressPanel");
                TextBlock checkpointText = this.FindControl<TextBlock>("CheckpointProgressText");
                ProgressBar checkpointBar = this.FindControl<ProgressBar>("CheckpointProgressBar");

                if (checkpointPanel != null)
                {
                    checkpointPanel.IsVisible = true;
                }

                if (checkpointText != null)
                {
                    checkpointText.Text = message;
                }

                if (checkpointBar != null && total > 0)
                {
                    checkpointBar.IsIndeterminate = false;
                    checkpointBar.Value = (double)current / total * 100;
                }
                else if (checkpointBar != null)
                {
                    checkpointBar.IsIndeterminate = true;
                }
            });
        }

        public void HideCheckpointProgress()
        {
            Dispatcher.UIThread.Post(() =>
            {
                StackPanel checkpointPanel = this.FindControl<StackPanel>("CheckpointProgressPanel");
                if (checkpointPanel != null)
                {
                    checkpointPanel.IsVisible = false;
                }
            });
        }

        public void ResetCancellationToken()
        {
            _cancellationTokenSource?.Dispose();
            int timeoutMinutes = DownloadTimeoutMinutes;
            _cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
        }

        public int DownloadTimeoutMinutes => _downloadTimeoutMinutes;

        public DownloadProgressWindow()
        {
            InitializeComponent();
            _cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(180));

            // Initialize and track timeout value without cross-thread UI access
            NumericUpDown timeoutControl = this.FindControl<NumericUpDown>("TimeoutNumericUpDown");
            if (timeoutControl != null)
            {
                if (timeoutControl.Value.HasValue)
                {
                    _downloadTimeoutMinutes = (int)timeoutControl.Value.Value;
                }

                // Update cached value whenever the control changes
                timeoutControl.PropertyChanged += (s, e) =>
                {
                    if (e.Property == NumericUpDown.ValueProperty && timeoutControl.Value.HasValue)
                    {
                        _downloadTimeoutMinutes = (int)timeoutControl.Value.Value;
                    }
                };
            }


            ItemsControl activeControl = this.FindControl<ItemsControl>("ActiveDownloadsControl");
            if (activeControl != null)
            {
                activeControl.ItemsSource = _activeDownloads;
            }

            ItemsControl pendingControl = this.FindControl<ItemsControl>("PendingDownloadsControl");
            if (pendingControl != null)
            {
                pendingControl.ItemsSource = _pendingDownloads;
            }

            ItemsControl completedControl = this.FindControl<ItemsControl>("CompletedDownloadsControl");
            if (completedControl != null)
            {
                completedControl.ItemsSource = _completedDownloads;
            }

            // Bind filtered collections for tabs
            ItemsControl activeDirectControl = this.FindControl<ItemsControl>("ActiveDirectDownloadsControl");
            if (activeDirectControl != null)
            {
                activeDirectControl.ItemsSource = _activeDirectDownloads;
            }

            ItemsControl completedDirectControl = this.FindControl<ItemsControl>("CompletedDirectDownloadsControl");
            if (completedDirectControl != null)
            {
                completedDirectControl.ItemsSource = _completedDirectDownloads;
            }

            ItemsControl activeOptimizedControl = this.FindControl<ItemsControl>("ActiveOptimizedDownloadsControl");
            if (activeOptimizedControl != null)
            {
                activeOptimizedControl.ItemsSource = _activeOptimizedDownloads;
            }

            ItemsControl completedOptimizedControl = this.FindControl<ItemsControl>("CompletedOptimizedDownloadsControl");
            if (completedOptimizedControl != null)
            {
                completedOptimizedControl.ItemsSource = _completedOptimizedDownloads;
            }

            Button closeButton = this.FindControl<Button>("CloseButton");
            if (closeButton != null)
            {
                closeButton.Click += CloseButton_Click;
            }

            Button cancelButton = this.FindControl<Button>("CancelButton");
            if (cancelButton != null)
            {
                cancelButton.Click += CancelButton_Click;
            }

            PointerPressed += InputElement_OnPointerPressed;
            PointerMoved += InputElement_OnPointerMoved;
            PointerReleased += InputElement_OnPointerReleased;
            PointerExited += InputElement_OnPointerReleased;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void DownloadItem_PointerPressed(object sender, PointerPressedEventArgs e)
        {

            if (!(sender is Border border) || !(border.DataContext is DownloadProgress progress))
            {
                return;
            }

            // Check if spoiler-free mode is enabled from owner window
            bool spoilerFreeMode = false;
            if (Owner is MainWindow mainWindow)
            {
                spoilerFreeMode = mainWindow.SpoilerFreeMode;
            }

            // Update context menu visibility based on spoiler-free mode when right-clicking
            if (e.GetCurrentPoint(border).Properties.IsRightButtonPressed && border.ContextMenu != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var item in border.ContextMenu.Items)
                    {
                        if (item is MenuItem menuItem && menuItem.Header?.ToString() == "Copy Download URL")
                        {
                            menuItem.IsVisible = !spoilerFreeMode;
                        }
                    }
                });
            }

            if (e.ClickCount != 2)
            {
                return;
            }

            try
            {
                var detailsDialog = new ModDownloadDetailsDialog(progress, RetryDownload);
                _ = detailsDialog.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to show download details: {ex.Message}");
            }
        }

        private void OpenFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is DownloadProgress progress)
            {
                if (string.IsNullOrEmpty(progress.FilePath) || !File.Exists(progress.FilePath))
                {
                    return;
                }

                try
                {
                    string directory = Path.GetDirectoryName(progress.FilePath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
                        {
                            _ = Process.Start("explorer.exe", directory);
                        }
                        else if (UtilityHelper.GetOperatingSystem() == OSPlatform.OSX)
                        {
                            _ = Process.Start("open", directory);
                        }
                        else
                        {
                            _ = Process.Start("xdg-open", directory);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to open download folder: {ex.Message}");
                }
            }
        }

        private async void CopyUrlMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if spoiler-free mode is enabled from owner window
                bool spoilerFreeMode = false;
                if (Owner is MainWindow mainWindow)
                {
                    spoilerFreeMode = mainWindow.SpoilerFreeMode;
                }

                // Don't copy URL in spoiler-free mode
                if (spoilerFreeMode)
                {
                    return;
                }

                if (sender is MenuItem menuItem && menuItem.DataContext is DownloadProgress progress)
                {
                    if (string.IsNullOrEmpty(progress.Url))
                    {
                        return;
                    }

                    try
                    {
                        if (Clipboard != null)
                        {
                            await Clipboard.SetTextAsync(progress.Url);
                            await Logger.LogVerboseAsync($"Copied URL to clipboard: {progress.Url}");
                        }
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogErrorAsync($"Failed to copy URL to clipboard: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Failed to copy URL to clipboard: {ex.Message}");
            }
        }

        private void ViewDetailsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is MenuItem menuItem) || !(menuItem.DataContext is DownloadProgress progress))
            {
                return;
            }

            try
            {
                var detailsDialog = new ModDownloadDetailsDialog(progress, RetryDownload);
                _ = detailsDialog.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to show download details: {ex.Message}");
            }
        }

        public void AddDownload(DownloadProgress progress)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _allDownloadItems.Add(progress);


                CategorizeDownload(progress);


                progress.PropertyChanged += DownloadProgress_PropertyChanged;


                if (progress.IsGrouped)
                {
                    foreach (DownloadProgress child in progress.ChildDownloads)
                    {
                        child.PropertyChanged += (sender, e) =>
                        {



                            Dispatcher.UIThread.Post(UpdateSummary);
                        };
                    }
                }
            });
        }

        private void CategorizeDownload(DownloadProgress progress)
        {
            // Remove from all collections
            _activeDownloads.Remove(progress);
            _pendingDownloads.Remove(progress);
            _completedDownloads.Remove(progress);
            _activeDirectDownloads.Remove(progress);
            _completedDirectDownloads.Remove(progress);
            _activeOptimizedDownloads.Remove(progress);
            _completedOptimizedDownloads.Remove(progress);

            // Categorize by status
            switch (progress.Status)
            {
                case DownloadStatus.InProgress:
                    InsertSorted(_activeDownloads, progress, p => p.StartTime);
                    // Also add to filtered collections based on source
                    if (progress.DownloadSource == DownloadSource.Direct)
                    {
                        InsertSorted(_activeDirectDownloads, progress, p => p.StartTime);
                    }
                    else if (progress.DownloadSource == DownloadSource.Optimized)
                    {
                        InsertSorted(_activeOptimizedDownloads, progress, p => p.StartTime);
                    }
                    break;
                case DownloadStatus.Pending:
                    InsertSorted(_pendingDownloads, progress, p => p.StartTime);
                    break;
                case DownloadStatus.Completed:
                case DownloadStatus.Failed:
                case DownloadStatus.Skipped:
                    InsertSorted(_completedDownloads, progress, p => p.EndTime ?? p.StartTime);
                    // Also add to filtered collections based on source
                    if (progress.DownloadSource == DownloadSource.Direct)
                    {
                        InsertSorted(_completedDirectDownloads, progress, p => p.EndTime ?? p.StartTime);
                    }
                    else if (progress.DownloadSource == DownloadSource.Optimized)
                    {
                        InsertSorted(_completedOptimizedDownloads, progress, p => p.EndTime ?? p.StartTime);
                    }
                    break;
            }

            UpdateSummary();
        }

        private static void InsertSorted(ObservableCollection<DownloadProgress> collection, DownloadProgress item, Func<DownloadProgress, DateTime> timestampSelector)
        {
            DateTime itemTimestamp = timestampSelector(item);


            int insertIndex = 0;
            for (int i = 0; i < collection.Count; i++)
            {
                DateTime existingTimestamp = timestampSelector(collection[i]);
                if (itemTimestamp > existingTimestamp)
                {

                    insertIndex = i;
                    break;
                }
                insertIndex = i + 1;
            }

            collection.Insert(insertIndex, item);
        }

        public void UpdateDownloadProgress(DownloadProgress progress)
        {
            Dispatcher.UIThread.Post(() =>
            {
                DownloadProgress existing = _allDownloadItems.Find(p => string.Equals(p.Url, progress.Url, StringComparison.Ordinal));
                if (existing != null)
                {

                    existing.PropertyChanged -= DownloadProgress_PropertyChanged;


                    existing.Status = progress.Status;
                    existing.StatusMessage = progress.StatusMessage;
                    existing.ProgressPercentage = progress.ProgressPercentage;
                    existing.BytesDownloaded = progress.BytesDownloaded;
                    existing.TotalBytes = progress.TotalBytes;
                    existing.FilePath = progress.FilePath;
                    existing.CompletedFromCache = progress.CompletedFromCache;
                    existing.StartTime = progress.StartTime;
                    existing.EndTime = progress.EndTime;
                    existing.ErrorMessage = progress.ErrorMessage;
                    existing.Exception = progress.Exception;


                    existing.PropertyChanged += DownloadProgress_PropertyChanged;


                    CategorizeDownload(existing);
                }
                else
                {
                    AddDownload(progress);
                }
            });
        }

        public void MarkCompleted()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isCompleted = true;

                Button closeButton = this.FindControl<Button>("CloseButton");
                if (closeButton != null)
                {
                    closeButton.IsEnabled = true;
                }

                Button cancelButton = this.FindControl<Button>("CancelButton");
                if (cancelButton != null)
                {
                    cancelButton.IsEnabled = false;
                }

                UpdateSummary();
            });
        }

        private void UpdateSummary()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(UpdateSummary, DispatcherPriority.Normal);
                return;
            }
            TextBlock summaryText = this.FindControl<TextBlock>("SummaryText");
            TextBlock overallProgressText = this.FindControl<TextBlock>("OverallProgressText");
            ProgressBar overallProgressBar = this.FindControl<ProgressBar>("OverallProgressBar");


            TextBlock activeHeader = this.FindControl<TextBlock>("ActiveSectionHeader");
            TextBlock pendingHeader = this.FindControl<TextBlock>("PendingSectionHeader");
            TextBlock completedHeader = this.FindControl<TextBlock>("CompletedSectionHeader");

            if (activeHeader != null)
            {
                activeHeader.Text = $"🔄 Active Downloads ({_activeDownloads.Count})";
            }

            if (pendingHeader != null)
            {
                pendingHeader.Text = $"⏳ Pending Downloads ({_pendingDownloads.Count})";
            }

            if (completedHeader != null)
            {
                completedHeader.Text = $"✅ Completed Downloads ({_completedDownloads.Count})";
            }

            // Update filtered tab headers
            TextBlock activeDirectHeader = this.FindControl<TextBlock>("ActiveDirectSectionHeader");
            if (activeDirectHeader != null)
            {
                activeDirectHeader.Text = $"🌐 Active Direct Downloads ({_activeDirectDownloads.Count})";
            }

            TextBlock completedDirectHeader = this.FindControl<TextBlock>("CompletedDirectSectionHeader");
            if (completedDirectHeader != null)
            {
                completedDirectHeader.Text = $"🌐 Completed Direct Downloads ({_completedDirectDownloads.Count})";
            }

            TextBlock activeOptimizedHeader = this.FindControl<TextBlock>("ActiveOptimizedSectionHeader");
            if (activeOptimizedHeader != null)
            {
                activeOptimizedHeader.Text = $"⚡ Active Network Cache Downloads ({_activeOptimizedDownloads.Count})";
            }

            TextBlock completedOptimizedHeader = this.FindControl<TextBlock>("CompletedOptimizedSectionHeader");
            if (completedOptimizedHeader != null)
            {
                completedOptimizedHeader.Text = $"⚡ Completed Network Cache Downloads ({_completedOptimizedDownloads.Count})";
            }

            if (summaryText is null)
            {
                return;
            }

            int inProgress = _activeDownloads.Count;
            int pending = _pendingDownloads.Count;


            int completedCount = _completedDownloads.Count(x => x.Status == DownloadStatus.Completed);
            int cachedCount = _completedDownloads.Count(x => x.Status == DownloadStatus.Completed && x.CompletedFromCache);
            int downloadedCount = completedCount - cachedCount;
            int skippedCount = _completedDownloads.Count(x => x.Status == DownloadStatus.Skipped);
            int failedCount = _completedDownloads.Count(x => x.Status == DownloadStatus.Failed);
            int totalFinished = completedCount + skippedCount + failedCount;


            double overallProgress = _allDownloadItems.Count > 0 ? (double)totalFinished / _allDownloadItems.Count * 100 : 0;


            if (overallProgressText != null)
            {
                string progressText = $"Overall Progress: {totalFinished} / {_allDownloadItems.Count} URLs";
                if (completedCount > 0 || skippedCount > 0 || failedCount > 0)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    if (downloadedCount > 0)
                    {
                        parts.Add($"{downloadedCount} downloaded");
                    }

                    if (cachedCount > 0)
                    {
                        parts.Add($"{cachedCount} cached");
                    }

                    if (skippedCount > 0)
                    {
                        parts.Add($"{skippedCount} skipped");
                    }

                    if (failedCount > 0)
                    {
                        parts.Add($"{failedCount} failed");
                    }

                    progressText += $" ({string.Join(", ", parts)})";
                }
                overallProgressText.Text = progressText;
            }

            if (overallProgressBar != null)
            {
                overallProgressBar.Value = overallProgress;
            }

            if (_isCompleted)
            {
                // Check if there are pending downloads and auto-start them
                if (pending > 0)
                {
                    Logger.LogVerbose($"Auto-starting {pending} pending downloads after initial completion");
                    StartAllPendingDownloads();

                    // Don't show completion message yet - let the pending downloads start first
                    summaryText.Text = $"Starting {pending} remaining downloads...";
                    return;
                }

                var messageParts = new System.Collections.Generic.List<string>();
                if (downloadedCount > 0)
                {
                    messageParts.Add($"{downloadedCount} downloaded");
                }

                if (cachedCount > 0)
                {
                    messageParts.Add($"{cachedCount} cached");
                }

                if (skippedCount > 0)
                {
                    messageParts.Add($"{skippedCount} skipped");
                }

                if (failedCount > 0)
                {
                    messageParts.Add($"{failedCount} failed");
                }

                string message = messageParts.Count > 0
                    ? $"Download complete! {string.Join(", ", messageParts)}"
                    : "Download complete!";
                summaryText.Text = message;
            }
            else
            {
                if (inProgress > 0)
                {
                    summaryText.Text = $"Downloading {inProgress} URL(s)... {downloadedCount + cachedCount + skippedCount}/{_allDownloadItems.Count} complete";
                }
                else if (pending > 0)
                {
                    summaryText.Text = $"Preparing downloads... {pending} URL(s) pending";
                }
                else
                {
                    summaryText.Text = "Initializing downloads...";
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        [UsedImplicitly]
        private void MinimizeButton_Click([NotNull] object sender, [NotNull] RoutedEventArgs e) => WindowState = WindowState.Minimized;

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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDownloads();
        }

        public void CancelDownloads()
        {
            try
            {
                Logger.LogVerbose("[DownloadProgressWindow] CancelDownloads() called");

                _cancellationTokenSource?.Cancel();


                Dispatcher.UIThread.Post(() =>
                {
                    Button cancelButton = this.FindControl<Button>("CancelButton");
                    if (cancelButton != null)
                    {
                        cancelButton.IsEnabled = false;
                        cancelButton.Content = "Cancelling...";
                    }


                    foreach (DownloadProgress download in _allDownloadItems.Where(d => d.Status == DownloadStatus.InProgress))
                    {
                        download.Status = DownloadStatus.Failed;
                        download.StatusMessage = "Download cancelled by user";
                        download.ErrorMessage = "Download was cancelled by user";
                    }

                    UpdateSummary();
                });

                Logger.LogVerbose("[DownloadProgressWindow] Cooperative cancellation initiated - downloads will stop gracefully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DownloadProgressWindow] Failed to cancel downloads: {ex.Message}");
            }
        }

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        protected override void OnClosing(WindowClosingEventArgs e)
        {

            if (!_forceCloseRequested && !_isCompleted && HasActiveOrPendingDownloads())
            {
                if (ShouldEmbedIntoWizardMode())
                {
                    e.Cancel = true;
                    Hide();
                    WindowHidden?.Invoke(this, EventArgs.Empty);
                    return;
                }

                e.Cancel = true;
                Hide();
                WindowHidden?.Invoke(this, EventArgs.Empty);
                return;
            }

            base.OnClosing(e);
        }

        private void DownloadProgress_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(DownloadProgress.ErrorMessage), StringComparison.Ordinal) && sender is DownloadProgress progress)
            {
                Dispatcher.UIThread.Post(() => UpdateErrorMessageWithLinks(progress));
            }

            if (string.Equals(e.PropertyName, nameof(DownloadProgress.Status), StringComparison.Ordinal) && sender is DownloadProgress progressItem)
            {
                Dispatcher.UIThread.Post(() => CategorizeDownload(progressItem));
            }
        }

        private void UpdateErrorMessageWithLinks(DownloadProgress progress)
        {
            if (string.IsNullOrEmpty(progress.ErrorMessage))
            {
                return;
            }

            ItemsControl itemsControl = null;
            if (progress.Status == DownloadStatus.InProgress)
            {
                itemsControl = this.FindControl<ItemsControl>("ActiveDownloadsControl");
            }
            else if (progress.Status == DownloadStatus.Pending)
            {
                itemsControl = this.FindControl<ItemsControl>("PendingDownloadsControl");
            }
            else
            {
                itemsControl = this.FindControl<ItemsControl>("CompletedDownloadsControl");
            }

            Control container = itemsControl?.ContainerFromItem(progress);
            if (container is null)
            {
                return;
            }

            System.Collections.Generic.IEnumerable<TextBlock> allTextBlocks = container.GetVisualDescendants().OfType<TextBlock>();
            TextBlock textBlock = allTextBlocks.FirstOrDefault(tb => string.Equals(tb.Name, "ErrorMessageBlock", StringComparison.Ordinal));

            if (textBlock is null)
            {
                return;
            }

            textBlock.Inlines = ParseTextWithUrls(progress.ErrorMessage);
        }

        private static InlineCollection ParseTextWithUrls(string text)
        {
            var inlines = new InlineCollection();


            string urlPattern = @"(https?://[^\s<>""{}|\\^`\[\]]+)";
            var regex = new Regex(urlPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));

            int lastIndex = 0;
            foreach (Match match in regex.Matches(text))
            {

                if (match.Index > lastIndex)
                {
                    string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                    inlines.Add(new Run(beforeText));
                }

                string url = match.Value;
                var button = new Button
                {
                    Content = url,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Foreground = Brushes.LightBlue,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = 13,
                };

                button.Classes.Add("link-button");
                button.Click += (sender, e) =>
                {
                    try
                    {
                        UrlUtilities.OpenUrl(url);
                        Logger.LogVerbose($"Opened URL: {url}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to open URL: {ex.Message}");
                    }
                };

                inlines.Add(new InlineUIContainer { Child = button });
                lastIndex = match.Index + match.Length;
            }


            if (lastIndex < text.Length)
            {
                string afterText = text.Substring(lastIndex);
                inlines.Add(new Run(afterText));
            }

            if (inlines.Count == 0)
            {
                inlines.Add(new Run(text));
            }

            return inlines;
        }

        private void ControlButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.DataContext is DownloadProgress progress))
            {
                return;
            }

            try
            {
                DownloadControlAction action;
                switch (progress.Status)
                {
                    case DownloadStatus.InProgress:
                        action = DownloadControlAction.Stop;
                        break;
                    case DownloadStatus.Completed:
                    case DownloadStatus.Skipped:
                    case DownloadStatus.Failed:
                        action = DownloadControlAction.Retry;
                        break;
                    default:
                        action = DownloadControlAction.Start;
                        break;
                }

                Logger.LogVerbose($"Download control requested: {action} for {progress.ModName}");
                DownloadControlRequested?.Invoke(this, new DownloadControlEventArgs(progress, action));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to handle control button click: {ex.Message}");
            }
        }

        private void StartAllPendingButton_Click(object sender, RoutedEventArgs e)
        {
            StartAllPendingDownloads();
        }

        private void StartAllPendingDownloads()
        {
            try
            {
                Logger.LogVerbose($"Start all pending downloads requested - {_pendingDownloads.Count} items");

                // Start all pending downloads
                foreach (DownloadProgress progress in _pendingDownloads.ToList())
                {
                    Logger.LogVerbose($"Starting download: {progress.ModName}");
                    DownloadControlRequested?.Invoke(this, new DownloadControlEventArgs(progress, DownloadControlAction.Start));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to start all pending downloads: {ex.Message}");
            }
        }

        private void RetryDownload(DownloadProgress progress)
        {
            try
            {
                Logger.LogVerbose($"Retry requested for: {progress.ModName} ({progress.Url})");
                DownloadControlRequested?.Invoke(this, new DownloadControlEventArgs(progress, DownloadControlAction.Retry));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to trigger retry: {ex.Message}");
            }
        }

        private void InputElement_OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (!_mouseDownForWindowMoving)
            {
                return;
            }

            PointerPoint currentPoint = e.GetCurrentPoint(this);
            Position = new PixelPoint(
                Position.X + (int)(currentPoint.Position.X - _originalPoint.Position.X),
                Position.Y + (int)(currentPoint.Position.Y - _originalPoint.Position.Y)
            );
        }

        private void InputElement_OnPointerPressed(object sender, PointerPressedEventArgs e)
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
                    case ProgressBar _:
                    case ScrollViewer _:

                    case Control control when control.ContextMenu?.IsOpen == true:
                        return true;
                    case Control control when control.ContextFlyout?.IsOpen == true:
                        return true;
                    default:
                        current = current.GetVisualParent();
                        break;
                }
            }

            return false;
        }

        public void AllowClose()
        {
            _forceCloseRequested = true;
        }

        private bool HasActiveOrPendingDownloads()
        {
            return _allDownloadItems.Exists(
                download => download.Status == DownloadStatus.InProgress
                            || download.Status == DownloadStatus.Pending);
        }

        /// <summary>
        /// Determines whether the download progress window should render inside the main wizard surface, mirroring the wizard visibility rules from <see cref="MainWindow"/>.
        /// By querying the owning window (or, as a fallback, the application lifetime) we ensure the download UI only embeds when the wizard is guiding the installation, preventing wizard-only chrome from leaking into editor sessions.
        /// This check keeps the windowing behaviour consistent regardless of where the dialog was launched, aligning auxiliary UI with the current high-level mode so players remain in a single cohesive workflow.
        /// </summary>
        private bool ShouldEmbedIntoWizardMode()
        {
            if (Owner is MainWindow mainWindow)
            {
                return mainWindow.WizardMode;
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime &&
                desktopLifetime.MainWindow is MainWindow lifetimeMainWindow)
            {
                return lifetimeMainWindow.WizardMode;
            }

            return false;
        }
    }

    public enum DownloadControlAction
    {
        Start,
        Stop,
        Resume,
        Retry,
    }

    public class DownloadControlEventArgs : EventArgs
    {
        public DownloadProgress Progress { get; }
        public DownloadControlAction Action { get; }

        public DownloadControlEventArgs(DownloadProgress progress, DownloadControlAction action)
        {
            Progress = progress;
            Action = action;
        }
    }
}
