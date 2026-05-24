// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KOTORModSync.Core.Services.Download
{
    public sealed class DownloadManager : IDisposable
    {
        private readonly List<IDownloadHandler> _handlers;
        private readonly Dictionary<string, DateTime> _lastProgressLogTime = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, DownloadStatus> _lastLoggedStatus = new Dictionary<string, DownloadStatus>(StringComparer.Ordinal);
        private readonly object _logThrottleLock = new object();
        private const int LogThrottleSeconds = 30;

        private CancellationTokenSource _globalCancellationTokenSource;
        private bool _disposed;

        public DownloadManager(IEnumerable<IDownloadHandler> handlers)
        {
            _handlers = new List<IDownloadHandler>(handlers);
            _globalCancellationTokenSource = new CancellationTokenSource();
            Logger.LogVerbose($"[DownloadManager] Initialized with {_handlers.Count} download handlers");
            for (int i = 0; i < _handlers.Count; i++)
            {
                Logger.LogVerbose($"[DownloadManager] Handler {i + 1}: {_handlers[i].GetType().Name}");
            }
        }

        public IDownloadHandler GetHandlerForUrl(string url)
        {
            return _handlers.Find(h => h.CanHandle(url));
        }

        public async Task<Dictionary<string, List<string>>> ResolveUrlsToFilenamesAsync(
            IEnumerable<string> urls,
            CancellationToken cancellationToken = default,
            bool sequential = false)
        {
            var urlList = urls.ToList();
            string mode = sequential ? "sequential" : "concurrent";

            await Logger.LogVerboseAsync($"[DownloadManager] Resolving {urlList.Count} URLs to filenames ({mode})").ConfigureAwait(false);

            // CRITICAL: Combine the global cancellation token with the parameter token
            // This ensures that CancelAll() will properly cancel ongoing resolution tasks
            // Use 'using' to properly dispose the linked token source
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _globalCancellationTokenSource.Token,
                cancellationToken))
            {
                CancellationToken combinedCancellationToken = linkedCts.Token;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    var results = new Dictionary<string, List<string>>(StringComparer.Ordinal);

                    if (sequential)
                    {
                        // Process URLs sequentially
                        foreach (string url in urlList)

                        {
                            (string resolvedUrl, List<string> filenames) = await ResolveUrlToFilenamesInternalAsync(url, combinedCancellationToken).ConfigureAwait(false);
                            results[resolvedUrl] = filenames;
                        }

                        sw.Stop();
                        TelemetryService.Instance.RecordFileOperation(
                            operationType: "resolve_urls",
                            success: true,
                            fileCount: urlList.Count,
                            durationMs: sw.Elapsed.TotalMilliseconds
                        );

                        return results;
                    }
                    else
                    {
                        // Process URLs concurrently
                        var resolutionTasks = urlList.Select(url => ResolveUrlToFilenamesInternalAsync(url, combinedCancellationToken)).ToList();

                        (string url, List<string> filenames)[] resolvedItems = await Task.WhenAll(resolutionTasks).ConfigureAwait(false);

                        foreach ((string url, List<string> filenames) item in resolvedItems)
                        {
                            string url = item.url;
                            List<string> filenames = item.filenames;
                            results[url] = filenames;
                        }

                        sw.Stop();
                        TelemetryService.Instance.RecordFileOperation(
                            operationType: "resolve_urls",
                            success: true,
                            fileCount: urlList.Count,
                            durationMs: sw.Elapsed.TotalMilliseconds
                        );

                        return results;
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    TelemetryService.Instance.RecordFileOperation(
                        operationType: "resolve_urls",
                        success: false,
                        fileCount: urlList.Count,
                        durationMs: sw.Elapsed.TotalMilliseconds,
                        errorMessage: ex.Message
                    );
                    throw;
                }
            }
        }

        private async Task<(string url, List<string> filenames)> ResolveUrlToFilenamesInternalAsync(
            string url,
            CancellationToken cancellationToken)
        {
            IDownloadHandler handler = _handlers.Find(h => h.CanHandle(url));
            if (handler is null)

            {
                await Logger.LogWarningAsync($"[DownloadManager] No handler for URL: {url}").ConfigureAwait(false);
                return (url, filenames: new List<string>());
            }

            try
            {
                await Logger.LogVerboseAsync($"[DownloadManager] Resolving URL with {handler.GetType().Name}: {url}").ConfigureAwait(false);
                List<string> filenames = await handler.ResolveFilenamesAsync(url, cancellationToken).ConfigureAwait(false);

                if (filenames is null || filenames.Count == 0)

                {
                    await Logger.LogWarningAsync($"[DownloadManager] No filenames resolved for URL: {url} (Handler: {handler.GetType().Name}). The URL may be incorrect, the page structure may have changed, or the file may no longer be available.").ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogVerboseAsync($"[DownloadManager] Resolved {filenames.Count} filename(s) for URL: {url}").ConfigureAwait(false);
                }

                return (url, filenames: filenames ?? new List<string>());
            }
            catch (Exception ex)

            {
                await Logger.LogExceptionAsync(ex, $"[DownloadManager] Failed to resolve URL: {url}").ConfigureAwait(false);
                return (url, filenames: new List<string>());
            }
        }

        public async Task CancelAllAsync()
        {
            if (_disposed)
            {
                await Logger.LogWarningAsync("[DownloadManager] CancelAll called on disposed instance").ConfigureAwait(false);
                return;
            }

            try
            {
                await Logger.LogVerboseAsync("[DownloadManager] CancelAll() called - using cooperative cancellation").ConfigureAwait(false);
#if NET8_0_OR_GREATER
                await _globalCancellationTokenSource.CancelAsync().ConfigureAwait(false);
#else
                _globalCancellationTokenSource?.Cancel();
                await Task.CompletedTask.ConfigureAwait(false);
#endif

                await Logger.LogVerboseAsync("[DownloadManager] Cooperative cancellation signal sent - downloads will stop gracefully").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"[DownloadManager] Failed to cancel downloads: {ex.Message}").ConfigureAwait(false);
            }
        }

        public void CancelAll()
        {
            if (_disposed)
            {
                Logger.LogWarning("[DownloadManager] CancelAll called on disposed instance");
                return;
            }

            try
            {
                Logger.LogVerbose("[DownloadManager] CancelAll() called - using cooperative cancellation");
                _globalCancellationTokenSource?.Cancel();

                Logger.LogVerbose("[DownloadManager] Cooperative cancellation signal sent - downloads will stop gracefully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DownloadManager] Failed to cancel downloads: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the global cancellation token so new downloads can start after a cancellation.
        /// </summary>
        public void ResetCancellation()
        {
            if (_disposed)
            {
                Logger.LogWarning("[DownloadManager] ResetCancellation called on disposed instance");
                return;
            }

            try
            {
                if (_globalCancellationTokenSource == null)
                {
                    _globalCancellationTokenSource = new CancellationTokenSource();
                    Logger.LogVerbose("[DownloadManager] Created global cancellation token");
                    return;
                }

                if (_globalCancellationTokenSource.IsCancellationRequested)
                {
                    CancellationTokenSource oldCts = _globalCancellationTokenSource;
                    _globalCancellationTokenSource = new CancellationTokenSource();

                    // Dispose the old CTS asynchronously to avoid blocking
                    // Don't pass a cancellation token - we always want to dispose the old CTS
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            oldCts?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            await Logger.LogWarningAsync($"[DownloadManager] Error disposing old CTS: {ex.Message}").ConfigureAwait(false);
                        }
                    });

                    Logger.LogVerbose("[DownloadManager] Reset global cancellation token");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[DownloadManager] Failed to reset cancellation token: {ex.Message}");
            }
        }

        public async Task<List<DownloadResult>> DownloadAllWithProgressAsync(
                Dictionary<string, DownloadProgress> urlToProgressMap,
                string destinationDirectory,
                IProgress<DownloadProgress> progressReporter = null,
                CancellationToken cancellationToken = default)
        {
            var urlList = urlToProgressMap.Keys.ToList();

            await Logger.LogVerboseAsync($"[DownloadManager] Starting concurrent batch download with progress reporting for {urlList.Count} URLs").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[DownloadManager] Destination directory: {destinationDirectory}").ConfigureAwait(false);

            // CRITICAL: Combine the global cancellation token with the parameter token
            // Use 'using' to properly dispose the linked token source
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _globalCancellationTokenSource.Token,
                cancellationToken))
            {
                CancellationToken combinedCancellationToken = linkedCts.Token;

                var results = new List<DownloadResult>();
                var tasks = new List<Task<DownloadResult>>();

                foreach (string url in urlList)
                {
                    DownloadProgress progressItem = urlToProgressMap[url];

                    await Logger.LogVerboseAsync($"[DownloadManager] Creating download task for URL: {url}, Mod: {progressItem.ModName}").ConfigureAwait(false);
                    tasks.Add(DownloadSingleWithConcurrencyLimit(url, progressItem, destinationDirectory, progressReporter, combinedCancellationToken));
                }

                DownloadResult[] downloadResults = await Task.WhenAll(tasks).ConfigureAwait(false);
                results.AddRange(downloadResults);

                int successCount = results.Count(r => r.Success);
                int failCount = results.Count(r => !r.Success);

                await Logger.LogVerboseAsync($"[DownloadManager] Concurrent batch download completed. Success: {successCount}, Failed: {failCount}").ConfigureAwait(false);
                return results;
            }
        }

        private async Task<DownloadResult> DownloadSingleWithConcurrencyLimit(
            string url,
            DownloadProgress progressItem,
            string destinationDirectory,
            IProgress<DownloadProgress> progressReporter,
            CancellationToken combinedCancellationToken)
        {

            if (combinedCancellationToken.IsCancellationRequested)
            {
                await Logger.LogVerboseAsync("[DownloadManager] Download cancelled by user").ConfigureAwait(false);
                progressItem.Status = DownloadStatus.Failed;
                progressItem.ErrorMessage = "Download cancelled by user";
                return DownloadResult.Failed("Download cancelled by user");
            }

            IDownloadHandler handler = _handlers.Find(h => h.CanHandle(url));
            if (handler is null)
            {
                await Logger.LogErrorAsync($"[DownloadManager] No handler configured for URL: {url}").ConfigureAwait(false);
                progressItem.Status = DownloadStatus.Failed;
                progressItem.ErrorMessage = "No handler configured for this URL";
                return DownloadResult.Failed("No handler configured for URL: " + url);
            }

            await Logger.LogVerboseAsync($"[DownloadManager] Using handler: {handler.GetType().Name} for URL: {url}").ConfigureAwait(false);
            progressItem.AddLog($"Using handler: {handler.GetType().Name}");

            try
            {
                await Logger.LogVerboseAsync($"[DownloadManager] Starting download: {url}").ConfigureAwait(false);

                progressItem.Status = DownloadStatus.InProgress;
                progressItem.StatusMessage = "Starting download...";
                progressItem.StartTime = DateTime.Now;

                var internalProgressReporter = new Progress<DownloadProgress>(update =>
                {
                    progressItem.Status = update.Status;
                    progressItem.ProgressPercentage = update.ProgressPercentage;
                    progressItem.BytesDownloaded = update.BytesDownloaded;
                    progressItem.TotalBytes = update.TotalBytes;
                    progressItem.StatusMessage = update.StatusMessage;
                    progressItem.ErrorMessage = update.ErrorMessage;
                    progressItem.Exception = update.Exception;
                    progressItem.FilePath = update.FilePath;

                    if (update.StartTime != default)
                    {
                        progressItem.StartTime = update.StartTime;
                    }

                    if (update.EndTime != null)
                    {
                        progressItem.EndTime = update.EndTime;
                    }

                    bool shouldLog = false;
                    DateTime now = DateTime.Now;

                    lock (_logThrottleLock)
                    {
                        bool isFirstLog = !_lastProgressLogTime.ContainsKey(url);
                        bool statusChanged = !_lastLoggedStatus.ContainsKey(url) || _lastLoggedStatus[url] != update.Status;
                        bool isTerminalStatus = update.Status == DownloadStatus.Completed ||
                                                update.Status == DownloadStatus.Failed ||
                                                update.Status == DownloadStatus.Skipped;
                        bool hasError = !string.IsNullOrEmpty(update.ErrorMessage);
                        DateTime lastLogTime;
                        _lastProgressLogTime.TryGetValue(url, out lastLogTime);
                        bool throttleExpired = !isFirstLog &&
                                               (now - lastLogTime).TotalSeconds >= LogThrottleSeconds;

                        shouldLog = isFirstLog || statusChanged || isTerminalStatus || hasError || throttleExpired;

                        if (shouldLog)
                        {
                            _lastProgressLogTime[url] = now;
                            _lastLoggedStatus[url] = update.Status;
                        }
                    }

                    if (shouldLog)
                    {
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                if (update.Status == DownloadStatus.Pending ||
                                     update.Status == DownloadStatus.Completed ||
                                     update.Status == DownloadStatus.Skipped ||
                                     update.Status == DownloadStatus.Failed)
                                {
                                    Logger.Log($"[Download] {update.Status}: {Path.GetFileName(update.FilePath ?? url)}");
                                    if (!string.IsNullOrEmpty(update.StatusMessage) && update.Status != DownloadStatus.InProgress)
                                    {
                                        Logger.LogVerbose($"  {update.StatusMessage}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogException(ex, $"[DownloadManager] Unexpected exception during download of '{url}': {ex.Message}");
                            }
                        }, combinedCancellationToken);
                    }

                    progressReporter?.Report(progressItem);
                });

                DownloadResult result;
                try
                {
                    result = await handler.DownloadAsync(url, destinationDirectory, internalProgressReporter, progressItem.TargetFilenames, combinedCancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await Logger.LogAsync($"[DownloadManager] Download cancelled by user: {url}").ConfigureAwait(false);
                    progressItem.AddLog("[CANCELLED] Download cancelled by user");

                    result = DownloadResult.Failed("Download cancelled by user");
                    progressItem.Status = DownloadStatus.Failed;
                    progressItem.StatusMessage = "Cancelled";
                    progressItem.ErrorMessage = "Download cancelled by user";
                    progressItem.EndTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    await Logger.LogErrorAsync($"[DownloadManager] Unexpected exception during download of '{url}': {ex.Message}").ConfigureAwait(false);
                    progressItem.AddLog($"[UNEXPECTED EXCEPTION] {ex.GetType().Name}: {ex.Message}");

                    result = DownloadResult.Failed($"Unexpected error: {ex.Message}");
                    progressItem.Status = DownloadStatus.Failed;
                    progressItem.StatusMessage = "Download failed due to unexpected error";
                    progressItem.ErrorMessage = ex.Message;
                    progressItem.Exception = ex;
                    progressItem.EndTime = DateTime.Now;
                }

                if (result.Success)
                {
                    await Logger.LogVerboseAsync($"[DownloadManager] Successfully downloaded: {result.FilePath}").ConfigureAwait(false);
                    progressItem.AddLog($"Download completed successfully: {result.FilePath}");

                    TimeSpan downloadDuration = (progressItem.EndTime ?? DateTime.Now) - progressItem.StartTime;
                    TelemetryService.Instance.RecordDownload(
                        modName: progressItem.ModName ?? Path.GetFileName(result.FilePath),
                        success: true,
                        durationMs: downloadDuration.TotalMilliseconds,
                        bytesDownloaded: progressItem.BytesDownloaded,
                        downloadSource: handler?.GetType().Name,
                        errorMessage: null
                    );

                    if (result.WasSkipped)
                    {
                        progressItem.AddLog("File was skipped (already exists)");

                        progressItem.Status = DownloadStatus.Skipped;
                        progressItem.StatusMessage = "File already exists";
                        progressItem.ProgressPercentage = 100;
                        progressItem.FilePath = result.FilePath;
                        progressItem.EndTime = DateTime.Now;
                        if (progressItem.StartTime == default)
                        {
                            progressItem.StartTime = DateTime.Now;
                        }

                        if (!string.IsNullOrEmpty(result.FilePath) && File.Exists(result.FilePath))
                        {
                            try
                            {
                                long fileSize = new FileInfo(result.FilePath).Length;
                                progressItem.BytesDownloaded = fileSize;
                                progressItem.TotalBytes = fileSize;
                                await Logger.LogVerboseAsync($"[DownloadManager] File already exists ({fileSize} bytes): {result.FilePath}").ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                await Logger.LogWarningAsync($"[DownloadManager] Could not get file size for skipped file: {ex.Message}").ConfigureAwait(false);
                            }
                        }
                    }
                }
                else
                {
                    await Logger.LogErrorAsync($"[DownloadManager] Failed to download URL '{url}': {result.Message}").ConfigureAwait(false);
                    progressItem.AddLog($"Download failed: {result.Message}");

                    TimeSpan downloadDuration = (progressItem.EndTime ?? DateTime.Now) - progressItem.StartTime;
                    TelemetryService.Instance.RecordDownload(
                        modName: progressItem.ModName ?? url,
                        success: false,
                        durationMs: downloadDuration.TotalMilliseconds,
                        bytesDownloaded: progressItem.BytesDownloaded,
                        downloadSource: handler?.GetType().Name,
                        errorMessage: result.Message
                    );

                    progressItem.Status = DownloadStatus.Failed;
                    progressItem.StatusMessage = "Download failed";
                    progressItem.ErrorMessage = result.Message;
                    progressItem.EndTime = DateTime.Now;
                }

                return result;
            }
            finally
            {

                lock (_logThrottleLock)
                {
                    _lastProgressLogTime.Remove(url);
                    _lastLoggedStatus.Remove(url);
                }
            }
        }

        /// <summary>
        /// Downloads a single file with progress reporting
        /// </summary>
        /// <param name="url">The URL to download</param>
        /// <param name="progress">Progress object to update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the download operation</returns>
        public async Task DownloadFileAsync(
            string url,
            DownloadProgress progress,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Logger.LogVerboseAsync($"[DownloadManager] Starting single file download: {url}").ConfigureAwait(false);

                var urlToProgressMap = new Dictionary<string, DownloadProgress>(StringComparer.Ordinal)
                {
                    { url, progress },
                };

                var progressReporter = new Progress<DownloadProgress>(update =>
                {
                    // Update the original progress object
                    progress.Status = update.Status;
                    progress.StatusMessage = update.StatusMessage;
                    progress.ProgressPercentage = update.ProgressPercentage;
                    progress.BytesDownloaded = update.BytesDownloaded;
                    progress.TotalBytes = update.TotalBytes;
                    progress.FilePath = update.FilePath;
                    progress.StartTime = update.StartTime;
                    progress.EndTime = update.EndTime;
                    progress.ErrorMessage = update.ErrorMessage;
                    progress.Exception = update.Exception;
                });

                List<DownloadResult> results = await DownloadAllWithProgressAsync(
                    urlToProgressMap,
                    "", // No specific destination directory for single file
                    progressReporter,
                    cancellationToken).ConfigureAwait(false);

                if (results.Count > 0 && !results[0].Success)
                {
                    await Logger.LogErrorAsync($"[DownloadManager] Single file download failed: {results[0].Message}").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"[DownloadManager] Exception during single file download: {url}").ConfigureAwait(false);
                progress.Status = DownloadStatus.Failed;
                progress.ErrorMessage = ex.Message;
                progress.Exception = ex;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _globalCancellationTokenSource?.Dispose();
                Logger.LogVerbose("[DownloadManager] Disposed");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[DownloadManager] Error during disposal: {ex.Message}");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
