// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace KOTORModSync.Core.Services.Download
{
    public sealed partial class DeadlyStreamDownloadHandler : IDownloadHandler
    {
        private readonly HttpClient _httpClient;
        private const long MaxBytesPerSecond = 7 * 1024 * 1024; // 7 MB/s (TODO: make 700 KB/s when releasing.)


        private readonly CookieContainer _cookieContainer;

        public DeadlyStreamDownloadHandler(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _cookieContainer = new CookieContainer();
            Logger.LogVerbose("[DeadlyStream] Initializing download handler with session cookie management");


            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
                Logger.LogVerbose($"[DeadlyStream] Added User-Agent header: {userAgent}");
            }
            else
            {
                Logger.LogVerbose($"[DeadlyStream] User-Agent header already present: {_httpClient.DefaultRequestHeaders.UserAgent}");
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
            {
                const string acceptHeader = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8";
                _httpClient.DefaultRequestHeaders.Add("Accept", acceptHeader);
                Logger.LogVerbose($"[DeadlyStream] Added Accept header: {acceptHeader}");
            }
            else
            {
                Logger.LogVerbose($"[DeadlyStream] Accept header already present: {string.Join(", ", _httpClient.DefaultRequestHeaders.Accept)}");
            }

            if (!_httpClient.DefaultRequestHeaders.Contains("Accept-Language"))
            {
                string acceptLanguage = "en-US,en;q=0.9";
                _httpClient.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
                Logger.LogVerbose($"[DeadlyStream] Added Accept-Language header: {acceptLanguage}");
            }
            else
            {
                Logger.LogVerbose($"[DeadlyStream] Accept-Language header already present: {string.Join(", ", _httpClient.DefaultRequestHeaders.AcceptLanguage)}");
            }


            _httpClient.DefaultRequestHeaders.Add("X-KOTORModSync-App", "Installer/1.0");
            _httpClient.DefaultRequestHeaders.Add("X-KOTORModSync-Repo", "https://github.com/KOTORModSync/KOTORModSync");
            _httpClient.DefaultRequestHeaders.Add("X-Accept-KOTORModSync", "true");
            Logger.LogVerbose("[DeadlyStream] Added custom identification headers: X-KOTORModSync-App, X-KOTORModSync-Repo, X-Accept-KOTORModSync");


            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "KOTOR_MODSYNC_PUBLIC");
            Logger.LogVerbose("[DeadlyStream] Added identification bearer token: KOTOR_MODSYNC_PUBLIC");

            Logger.LogVerbose("[DeadlyStream] Handler initialized with proper browser headers and identification markers");
            Logger.LogVerbose($"[DeadlyStream] Bandwidth throttling enabled: {MaxBytesPerSecond / 1024 / 1024} MB/s using ThrottledStream");
        }

        public bool CanHandle(string url)
        {
            bool canHandle = url != null && url.IndexOf("deadlystream.com", StringComparison.OrdinalIgnoreCase) >= 0;
            return canHandle;
        }

        private static string NormalizeDeadlyStreamUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            int queryIndex = url.IndexOf('?');
            int fragmentIndex = url.IndexOf('#');

            int cutIndex = -1;
            if (queryIndex >= 0 && fragmentIndex >= 0)
            {
                cutIndex = Math.Min(queryIndex, fragmentIndex);
            }
            else if (queryIndex >= 0)
            {
                cutIndex = queryIndex;
            }
            else if (fragmentIndex >= 0)
            {
                cutIndex = fragmentIndex;
            }

            if (cutIndex >= 0)
            {
                return url.Substring(0, cutIndex);
            }

            return url;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public async Task<List<string>> ResolveFilenamesAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Logger.LogVerboseAsync($"[DeadlyStream] Resolving filenames for URL: {url}").ConfigureAwait(false);

                url = NormalizeDeadlyStreamUrl(url);
                await Logger.LogVerboseAsync($"[DeadlyStream] Normalized URL: {url}").ConfigureAwait(false);

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri validatedUri))
                {
                    await Logger.LogWarningAsync($"[DeadlyStream] Invalid URL format: {url}").ConfigureAwait(false);
                    return new List<string>();
                }


                if (validatedUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    validatedUri = new UriBuilder(validatedUri) { Scheme = "https", Port = -1 }.Uri;
                    url = validatedUri.ToString();
                }

                await Logger.LogVerboseAsync($"[DeadlyStream] Requesting page: {url}").ConfigureAwait(false);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyCookiesToRequest(request, validatedUri);
                HttpResponseMessage pageResponse = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                _ = pageResponse.EnsureSuccessStatusCode();
                ExtractAndStoreCookies(pageResponse, validatedUri);
                await Logger.LogVerboseAsync($"[DeadlyStream] Page response received (StatusCode: {pageResponse.StatusCode})").ConfigureAwait(false);

                string html = await pageResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                pageResponse.Dispose();
                string csrfKey = ExtractCsrfKey(html);
                string downloadPageUrl = !string.IsNullOrEmpty(csrfKey)
                    ? $"{url}?do=download&csrfKey={csrfKey}"
                    : $"{url}?do=download";
                await Logger.LogVerboseAsync($"[DeadlyStream] Requesting download page: {downloadPageUrl}").ConfigureAwait(false);
                var downloadPageRequest = new HttpRequestMessage(HttpMethod.Get, downloadPageUrl);
                ApplyCookiesToRequest(downloadPageRequest, validatedUri);
                HttpResponseMessage downloadPageResponse = await _httpClient.SendAsync(downloadPageRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[DeadlyStream] Download page response received (StatusCode: {downloadPageResponse.StatusCode})").ConfigureAwait(false);

                var filenames = new List<string>();

                if (downloadPageResponse.IsSuccessStatusCode)
                {
                    string downloadPageHtml = await downloadPageResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    ExtractAndStoreCookies(downloadPageResponse, validatedUri);


                    if (downloadPageHtml.Contains("Download your files") || downloadPageHtml.Contains("data-action=\"download\""))
                    {
                        List<string> confirmedLinks = ExtractConfirmedDownloadLinks(downloadPageHtml, url);
                        foreach (string downloadLink in confirmedLinks)
                        {
                            string filename = await ResolveFilenameFromLink(downloadLink, cancellationToken).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(filename))
                            {
                                filenames.Add(filename);
                            }
                        }
                    }
                }

                downloadPageResponse.Dispose();
                if (filenames.Count == 0)
                {
                    List<string> downloadLinks = ExtractAllDownloadLinks(html, url);
                    foreach (string downloadLink in downloadLinks)
                    {
                        string filename = await ResolveFilenameFromLink(downloadLink, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(filename))
                        {
                            filenames.Add(filename);
                        }
                    }
                }

                await Logger.LogVerboseAsync($"[DeadlyStream] Resolved {filenames.Count} filename(s)").ConfigureAwait(false);
                return filenames;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, $"[DeadlyStream] Failed to resolve filenames").ConfigureAwait(false);
                return new List<string>();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async Task<string> ResolveFilenameFromLink(string downloadLink, CancellationToken cancellationToken)
        {
            try
            {
                await Logger.LogVerboseAsync($"[DeadlyStream] Resolving filename from link (HEAD): {downloadLink}").ConfigureAwait(false);
                var downloadUri = new Uri(downloadLink);
                var request = new HttpRequestMessage(HttpMethod.Head, downloadLink);
                ApplyCookiesToRequest(request, downloadUri);

                HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[DeadlyStream] HEAD response received (StatusCode: {response.StatusCode})").ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    response.Dispose();
                    await Logger.LogVerboseAsync($"[DeadlyStream] HEAD failed, trying GET request for filename resolution").ConfigureAwait(false);
                    var getReq = new HttpRequestMessage(HttpMethod.Get, downloadLink);
                    ApplyCookiesToRequest(getReq, downloadUri);
                    HttpResponseMessage getResp = await _httpClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    await Logger.LogVerboseAsync($"[DeadlyStream] GET response received (StatusCode: {getResp.StatusCode})").ConfigureAwait(false);
                    if (!getResp.IsSuccessStatusCode)
                    {
                        getResp.Dispose();
                        return null;
                    }
                    string ct = getResp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (ct.Contains("text/html"))
                    {
                        getResp.Dispose();
                        return null;
                    }

                    string nameFromGet = GetFileNameFromContentDisposition(getResp);
                    if (string.IsNullOrWhiteSpace(nameFromGet))
                    {
                        nameFromGet = Path.GetFileName(Uri.UnescapeDataString(downloadUri.AbsolutePath));
                    }

                    getResp.Dispose();
                    return string.IsNullOrWhiteSpace(nameFromGet) || nameFromGet.Contains("?") ? null : nameFromGet;
                }


                string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (contentType.Contains("text/html"))
                {
                    response.Dispose();
                    return null;
                }


                string fileName = GetFileNameFromContentDisposition(response);
                if (string.IsNullOrWhiteSpace(fileName))
                {

                    fileName = Path.GetFileName(Uri.UnescapeDataString(downloadUri.AbsolutePath));
                }

                if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("?"))
                {
                    fileName = null;
                }

                response.Dispose();
                return fileName;
            }
            catch (Exception ex)
            {
                await Logger.LogVerboseAsync($"[DeadlyStream] Could not resolve filename from link {downloadLink}: {ex.Message}").ConfigureAwait(false);
                return null;
            }
        }

        private void ExtractAndStoreCookies(HttpResponseMessage response, Uri uri)
        {
            try
            {

                if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookieHeaders))
                {
                    foreach (string cookieHeader in cookieHeaders)
                    {
                        try
                        {

                            _cookieContainer.SetCookies(uri, cookieHeader);
                            Logger.LogVerbose($"[DeadlyStream] Stored cookie from response: {cookieHeader.Substring(0, Math.Min(50, cookieHeader.Length))}...");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"[DeadlyStream] Failed to parse cookie: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[DeadlyStream] Failed to extract cookies: {ex.Message}");
            }
        }

        private void ApplyCookiesToRequest(HttpRequestMessage request, Uri uri)
        {
            try
            {
                string cookieHeader = _cookieContainer.GetCookieHeader(uri);
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    request.Headers.Add("Cookie", cookieHeader);

                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[DeadlyStream] Failed to apply cookies: {ex.Message}");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S6966:Awaitable method should be used", Justification = "Not supported in .NET Framework 4.8")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public async Task<DownloadResult> DownloadAsync(
            string url,
            string destinationDirectory,
            IProgress<DownloadProgress> progress = null,
            List<string> targetFilenames = null,
            CancellationToken cancellationToken = default
        )
        {
            await Logger.LogVerboseAsync($"[DeadlyStream] Starting download from URL: {url}").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[DeadlyStream] Destination directory: {destinationDirectory}").ConfigureAwait(false);

            url = NormalizeDeadlyStreamUrl(url);
            await Logger.LogVerboseAsync($"[DeadlyStream] Normalized URL: {url}").ConfigureAwait(false);

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri validatedUri))
            {
                string errorMsg = $"Invalid URL format: {url}";
                await Logger.LogErrorAsync($"[DeadlyStream] {errorMsg}").ConfigureAwait(false);
                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.Failed,
                    ErrorMessage = $"Invalid URL: {url}",
                    ProgressPercentage = 0,
                    EndTime = DateTime.Now,
                });
                return DownloadResult.Failed(errorMsg);
            }

            progress?.Report(new DownloadProgress
            {
                Status = DownloadStatus.InProgress,
                StatusMessage = "Extracting download links...",
                ProgressPercentage = 10,
            });

            try
            {

                await Logger.LogVerboseAsync($"[DeadlyStream] Fetching page to establish session: {url}").ConfigureAwait(false);

                if (validatedUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    validatedUri = new UriBuilder(validatedUri) { Scheme = "https", Port = -1 }.Uri;
                    url = validatedUri.ToString();
                    await Logger.LogVerboseAsync($"[DeadlyStream] Normalized URL to HTTPS: {url}").ConfigureAwait(false);
                }

                await Logger.LogVerboseAsync($"[DeadlyStream] Requesting page: {url}").ConfigureAwait(false);
                var request = new HttpRequestMessage(HttpMethod.Get, url);


                ApplyCookiesToRequest(request, validatedUri);

                HttpResponseMessage pageResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                _ = pageResponse.EnsureSuccessStatusCode();
                await Logger.LogVerboseAsync($"[DeadlyStream] Page response received (StatusCode: {pageResponse.StatusCode})").ConfigureAwait(false);


                ExtractAndStoreCookies(pageResponse, validatedUri);

                string html = await pageResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[DeadlyStream] Downloaded HTML content, length: {html.Length} characters").ConfigureAwait(false);


                string csrfKey = ExtractCsrfKey(html);
                if (string.IsNullOrEmpty(csrfKey))
                {
                    await Logger.LogWarningAsync("[DeadlyStream] Could not extract csrfKey from page, downloads may fail").ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogVerboseAsync($"[DeadlyStream] Extracted csrfKey: {csrfKey.Substring(0, Math.Min(8, csrfKey.Length))}...").ConfigureAwait(false);
                }


                string downloadPageUrl = !string.IsNullOrEmpty(csrfKey)
                    ? $"{url}?do=download&csrfKey={csrfKey}"
                    : $"{url}?do=download";

                await Logger.LogVerboseAsync($"[DeadlyStream] Checking for multi-file download at: {downloadPageUrl}").ConfigureAwait(false);
                var downloadPageRequest = new HttpRequestMessage(HttpMethod.Get, downloadPageUrl);
                ApplyCookiesToRequest(downloadPageRequest, validatedUri);

                HttpResponseMessage downloadPageResponse = await _httpClient.SendAsync(downloadPageRequest, cancellationToken).ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[DeadlyStream] Download page response received (StatusCode: {downloadPageResponse.StatusCode})").ConfigureAwait(false);

                if (downloadPageResponse.IsSuccessStatusCode)
                {
                    string downloadPageHtml = await downloadPageResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    ExtractAndStoreCookies(downloadPageResponse, validatedUri);

                    if (downloadPageHtml.IndexOf("Download your files", StringComparison.Ordinal) >= 0 || downloadPageHtml.IndexOf("data-action=\"download\"", StringComparison.Ordinal) >= 0)
                    {
                        await Logger.LogVerboseAsync("[DeadlyStream] Detected multi-file selection page").ConfigureAwait(false);
                        List<string> confirmedLinks = ExtractConfirmedDownloadLinks(downloadPageHtml, url);

                        if (!(confirmedLinks is null) && confirmedLinks.Count > 0)
                        {
                            await Logger.LogVerboseAsync($"[DeadlyStream] Found {confirmedLinks.Count} files to download").ConfigureAwait(false);
                            downloadPageResponse.Dispose();
                            pageResponse.Dispose();

                            var multiFileDownloads = new List<string>();
                            int multiFileIndex = 0;

                            foreach (string downloadLink in confirmedLinks)
                            {
                                multiFileIndex++;
                                double multiFileBaseProgress = 30 + (multiFileIndex - 1) * (60.0 / confirmedLinks.Count);
                                double multiFileProgressRange = 60.0 / confirmedLinks.Count;

                                progress?.Report(new DownloadProgress
                                {
                                    Status = DownloadStatus.InProgress,
                                    StatusMessage = $"Downloading file {multiFileIndex} of {confirmedLinks.Count}...",
                                    ProgressPercentage = multiFileBaseProgress,
                                });

                                string filePath = await DownloadSingleFile(
                                    downloadLink,
                                    destinationDirectory,
                                    progress,
                                    multiFileBaseProgress,
                                    multiFileProgressRange,
                                    cancellationToken
                                ).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(filePath))
                                {
                                    multiFileDownloads.Add(filePath);
                                }
                            }

                            if (multiFileDownloads.Count == 0)
                            {
                                string errorMsg = "Failed to download any files from multi-file selection page";
                                progress?.Report(new DownloadProgress
                                {
                                    Status = DownloadStatus.Failed,
                                    ErrorMessage = errorMsg,
                                    ProgressPercentage = 100,
                                    EndTime = DateTime.Now,
                                });
                                return DownloadResult.Failed(errorMsg);
                            }


                            string multiFileResultMessage = BuildCompletionMessage(multiFileDownloads);

                            progress?.Report(new DownloadProgress
                            {
                                Status = DownloadStatus.Completed,
                                StatusMessage = multiFileResultMessage,
                                ProgressPercentage = 100,
                                FilePath = multiFileDownloads[0],
                                EndTime = DateTime.Now,
                            });

                            return DownloadResult.Succeeded(multiFileDownloads[0], multiFileResultMessage);
                        }
                    }

                    downloadPageResponse.Dispose();
                }
                else
                {
                    await Logger.LogVerboseAsync(
                        $"[DeadlyStream] Download page request returned {downloadPageResponse.StatusCode}, trying direct download").ConfigureAwait(false);
                    downloadPageResponse.Dispose();
                }

                pageResponse.Dispose();


                List<string> downloadLinks = ExtractAllDownloadLinks(html, url);

                if (downloadLinks is null || downloadLinks.Count == 0)
                {
                    string debugPath = Path.Combine(destinationDirectory, "deadlystream_debug.html");
                    try
                    {
                        Directory.CreateDirectory(destinationDirectory);
                        File.WriteAllText(debugPath, html);
                        await Logger.LogVerboseAsync($"[DeadlyStream] Debug HTML saved to: {debugPath}").ConfigureAwait(false);
                    }
                    catch (Exception debugEx)
                    {
                        await Logger.LogWarningAsync($"[DeadlyStream] Failed to save debug HTML: {debugEx.Message}").ConfigureAwait(false);
                    }

                    string userMessage = "DeadlyStream download link could not be extracted.\n\n" +
                                         "This usually means:\n" +
                                         "• The page layout has changed\n" +
                                         "• The mod requires login to download\n" +
                                         "• The file has been removed\n\n" +
                                         $"Please try downloading manually from: {url}\n\n" +
                                         $"Debug HTML saved to: {debugPath}";

                    progress?.Report(new DownloadProgress
                    {
                        Status = DownloadStatus.Failed,
                        ErrorMessage = userMessage,
                        ProgressPercentage = 100,
                        EndTime = DateTime.Now,
                    });

                    pageResponse.Dispose();
                    return DownloadResult.Failed(userMessage);
                }

                var downloadedFiles = new List<string>();
                int fileIndex = 0;

                foreach (string downloadLink in downloadLinks)
                {
                    fileIndex++;
                    double baseProgress = 30 + (fileIndex - 1) * (60.0 / downloadLinks.Count);
                    double fileProgressRange = 60.0 / downloadLinks.Count;

                    progress?.Report(new DownloadProgress
                    {
                        Status = DownloadStatus.InProgress,
                        StatusMessage = string.Format(CultureInfo.InvariantCulture, "Downloading file {0} of {1}...", fileIndex, downloadLinks.Count),
                        ProgressPercentage = baseProgress,
                    });

                    string filePath = await DownloadSingleFile(
                        downloadLink,
                        destinationDirectory,
                        progress,
                        baseProgress,
                        fileProgressRange,
                        cancellationToken
                    ).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        downloadedFiles.Add(filePath);
                    }
                }

                if (downloadedFiles.Count == 0)
                {
                    string errorMsg = "Failed to download any files";
                    progress?.Report(new DownloadProgress
                    {
                        Status = DownloadStatus.Failed,
                        ErrorMessage = errorMsg,
                        ProgressPercentage = 100,
                        EndTime = DateTime.Now,
                    });
                    return DownloadResult.Failed(errorMsg);
                }


                string resultMessage = BuildCompletionMessage(downloadedFiles);

                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.Completed,
                    StatusMessage = resultMessage,
                    ProgressPercentage = 100,
                    FilePath = downloadedFiles[0],
                    EndTime = DateTime.Now,
                });

                return DownloadResult.Succeeded(downloadedFiles[0], resultMessage);
            }
            catch (HttpRequestException httpEx)
            {
                await Logger.LogErrorAsync($"[DeadlyStream] HTTP request failed for URL '{url}': {httpEx.Message}").ConfigureAwait(false);
                await Logger.LogExceptionAsync(httpEx).ConfigureAwait(false);

                string userMessage = "DeadlyStream download failed. This can happen when:\n\n" +
                                     "• The download page has changed its layout\n" +
                                     "• The mod file has been removed or made private\n" +
                                     "• The site is experiencing issues\n\n" +
                                     $"Please try downloading manually from: {url}\n\n" +
                                     $"Technical details: {httpEx.Message}";

                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.Failed,
                    ErrorMessage = userMessage,
                    Exception = httpEx,
                    ProgressPercentage = 100,
                    EndTime = DateTime.Now,
                });
                return DownloadResult.Failed(userMessage);
            }
            catch (TaskCanceledException tcEx)
            {
                await Logger.LogErrorAsync($"[DeadlyStream] Request timeout for URL '{url}': {tcEx.Message}").ConfigureAwait(false);
                await Logger.LogExceptionAsync(tcEx).ConfigureAwait(false);

                string userMessage = "DeadlyStream download timed out. This can happen when:\n\n" +
                                     "• The site is slow or experiencing high traffic\n" +
                                     "• Your internet connection is unstable\n" +
                                     "• The file is very large\n\n" +
                                     $"Please try downloading manually from: {url}\n\n" +
                                     $"Technical details: {tcEx.Message}";

                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.Failed,
                    ErrorMessage = userMessage,
                    Exception = tcEx,
                    ProgressPercentage = 100,
                    EndTime = DateTime.Now,
                });
                return DownloadResult.Failed(userMessage);
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"[DeadlyStream] Download failed for URL '{url}': {ex.Message}").ConfigureAwait(false);
                await Logger.LogExceptionAsync(ex).ConfigureAwait(false);

                string userMessage = "DeadlyStream download failed unexpectedly.\n\n" +
                                     $"Please try downloading manually from: {url}\n\n" +
                                     $"Technical details: {ex.Message}";

                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.Failed,
                    ErrorMessage = userMessage,
                    Exception = ex,
                    ProgressPercentage = 100,
                    EndTime = DateTime.Now,
                });
                return DownloadResult.Failed(userMessage);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async Task<string> DownloadSingleFile(
            string downloadLink,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            double baseProgress,
            double progressRange,
            CancellationToken cancellationToken = default)
        {
            try
            {

                cancellationToken.ThrowIfCancellationRequested();

                await Logger.LogVerboseAsync($"[DeadlyStream] Downloading from: {downloadLink}").ConfigureAwait(false);


                var downloadUri = new Uri(downloadLink);
                var fileRequest = new HttpRequestMessage(HttpMethod.Get, downloadLink);
                ApplyCookiesToRequest(fileRequest, downloadUri);
                HttpResponseMessage fileResponse = await _httpClient.SendAsync(fileRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);


                if (!fileResponse.IsSuccessStatusCode)
                {
                    await Logger.LogErrorAsync($"[DeadlyStream] File response status: {fileResponse.StatusCode}").ConfigureAwait(false);
                }

                _ = fileResponse.EnsureSuccessStatusCode();


                string contentType = fileResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (contentType.Contains("text/html"))
                {
                    await Logger.LogVerboseAsync("[DeadlyStream] Received HTML response - this appears to be a file selection page").ConfigureAwait(false);
                    string html = await fileResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    fileResponse.Dispose();


                    if (html.Contains("Download your files") || html.Contains("data-action=\"download\""))
                    {
                        await Logger.LogVerboseAsync("[DeadlyStream] Detected multi-file selection page, extracting actual download links").ConfigureAwait(false);
                        List<string> actualDownloadLinks = ExtractConfirmedDownloadLinks(html, downloadLink);

                        if (actualDownloadLinks.Count > 0)
                        {
                            await Logger.LogVerboseAsync($"[DeadlyStream] Found {actualDownloadLinks.Count} confirmed download link(s) on selection page").ConfigureAwait(false);


                            string lastDownloadedFile = null;
                            for (int i = 0; i < actualDownloadLinks.Count; i++)
                            {
                                double fileBaseProgress = baseProgress + (i * progressRange / actualDownloadLinks.Count);
                                double fileProgressRange = progressRange / actualDownloadLinks.Count;

                                progress?.Report(new DownloadProgress
                                {
                                    Status = DownloadStatus.InProgress,
                                    StatusMessage = $"Downloading file {i + 1} of {actualDownloadLinks.Count} from selection page...",
                                    ProgressPercentage = fileBaseProgress,
                                });

                                string downloadedFile = await DownloadSingleFile(actualDownloadLinks[i], destinationDirectory, progress, fileBaseProgress, fileProgressRange, cancellationToken).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(downloadedFile))
                                {
                                    lastDownloadedFile = downloadedFile;
                                }
                            }

                            return lastDownloadedFile;
                        }

                        await Logger.LogWarningAsync("[DeadlyStream] Multi-file selection page detected but no download links found").ConfigureAwait(false);
                    }


                    string errorMsg = "Received HTML instead of file - download link may be invalid or require authentication";
                    await Logger.LogErrorAsync($"[DeadlyStream] {errorMsg}").ConfigureAwait(false);
                    return null;
                }


                string fileName = GetFileNameFromContentDisposition(fileResponse);
                if (string.IsNullOrWhiteSpace(fileName))
                {

                    fileName = Path.GetFileName(Uri.UnescapeDataString(downloadUri.AbsolutePath));
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOf('?') >= 0)
                    {
                        fileName = $"deadlystream_download_{Guid.NewGuid():N}.zip";
                    }
                    await Logger.LogWarningAsync($"[DeadlyStream] Could not determine filename, using: {fileName}").ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogVerboseAsync($"[DeadlyStream] Filename: {fileName}").ConfigureAwait(false);
                }


                _ = Directory.CreateDirectory(destinationDirectory);
                string finalPath = Path.Combine(destinationDirectory, fileName);
                string tempPath = DownloadHelper.GetTempFilePath(finalPath);

                long totalBytes = fileResponse.Content.Headers.ContentLength ?? 0;
                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.InProgress,
                    StatusMessage = $"Downloading {fileName}...",
                    ProgressPercentage = baseProgress + (progressRange * 0.1),
                    TotalBytes = totalBytes,
                });

                using (Stream contentStream = await fileResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var throttledStream = new ThrottledStream(contentStream, MaxBytesPerSecond))
                {

                    await DownloadHelper.DownloadWithProgressAsync(
                        throttledStream,
                        tempPath,
                        totalBytes,
                        fileName,
                        downloadLink,
                        progress,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                long fileSize = new FileInfo(tempPath).Length;
                await Logger.LogVerboseAsync(string.Format(CultureInfo.InvariantCulture, "[DeadlyStream] File downloaded successfully: {0} ({1} bytes)", tempPath, fileSize)).ConfigureAwait(false);

                // Atomically move to final destination
                try
                {
                    DownloadHelper.MoveToFinalDestination(tempPath, finalPath);
                    await Logger.LogVerboseAsync(string.Format(CultureInfo.InvariantCulture, "[DeadlyStream] Moved temporary file to final destination: {0}", finalPath)).ConfigureAwait(false);
                }
                catch (Exception moveEx)
                {
                    await Logger.LogErrorAsync(string.Format(CultureInfo.InvariantCulture, "[DeadlyStream] Failed to move temporary file to final destination: {0}", moveEx.Message)).ConfigureAwait(false);
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception deleteEx)
                    {
                        await Logger.LogExceptionAsync(deleteEx, $"[DeadlyStream] Failed to delete temporary file: {tempPath}").ConfigureAwait(false);
                    }
                    throw;
                }

                fileResponse.Dispose();
                return finalPath;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"[DeadlyStream] Failed to download file from {downloadLink}: {ex.Message}").ConfigureAwait(false);
                await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                return null;
            }
        }

        private static string ExtractCsrfKey(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }

            Match jsMatch = Regex.Match(
                html,
                @"csrfKey:\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(5)
            );
            if (jsMatch.Success)
            {
                Logger.LogVerbose($"[DeadlyStream] Extracted csrfKey from JavaScript: {jsMatch.Groups[1].Value}");
                return jsMatch.Groups[1].Value;
            }


            Match linkMatch = Regex.Match(
                html,
                @"csrfKey=([^&""'<>\s]+)",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(5)
            );
            if (linkMatch.Success)
            {
                Logger.LogVerbose($"[DeadlyStream] Extracted csrfKey from link: {linkMatch.Groups[1].Value}");
                return linkMatch.Groups[1].Value;
            }

            Logger.LogWarning("[DeadlyStream] Could not extract csrfKey from page");
            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static List<string> ExtractConfirmedDownloadLinks(string html, string baseUrl)
        {
            Logger.LogVerbose($"[DeadlyStream] ExtractConfirmedDownloadLinks called with HTML length: {html?.Length ?? 0}, baseUrl: {baseUrl}");

            if (string.IsNullOrEmpty(html))
            {
                Logger.LogWarning("[DeadlyStream] HTML content is null or empty");
                return new List<string>();
            }

            var document = new HtmlDocument();
            document.LoadHtml(html);
            Logger.LogVerbose("[DeadlyStream] HTML document loaded successfully");
            string[] selectors = new[]
            {
                "//a[@data-action='download' and contains(@href,'?do=download')]",
                "//a[contains(@href,'?do=download') and contains(@href,'&confirm=1')]",
                "//a[contains(@class,'ipsButton') and contains(@href,'?do=download') and contains(@href,'&r=')]",
            };

            var downloadLinks = new List<string>();

            foreach (string selector in selectors)
            {
                Logger.LogVerbose($"[DeadlyStream] Trying selector: {selector}");
                HtmlNodeCollection nodes = document.DocumentNode.SelectNodes(selector);

                if (nodes != null && nodes.Count > 0)
                {
                    Logger.LogVerbose($"[DeadlyStream] Found {nodes.Count} matching nodes");

                    foreach (HtmlNode node in nodes)
                    {
                        string href = node.GetAttributeValue("href", string.Empty);
                        if (string.IsNullOrWhiteSpace(href))
                        {
                            continue;
                        }

                        if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                            !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseUri = new Uri(baseUrl);
                            var absoluteUri = new Uri(baseUri, href);
                            href = absoluteUri.ToString();
                        }

                        href = WebUtility.HtmlDecode(href);
                        if (!downloadLinks.Contains(href, StringComparer.OrdinalIgnoreCase))
                        {
                            downloadLinks.Add(href);
                            Logger.LogVerbose($"[DeadlyStream] Added confirmed download link #{downloadLinks.Count}: {href}");
                        }
                    }
                }
                if (downloadLinks.Count > 0)
                {
                    break;
                }
            }

            if (downloadLinks.Count > 0)
            {
                Logger.LogVerbose($"[DeadlyStream] Successfully extracted {downloadLinks.Count} confirmed download link(s) from selection page");
            }
            else
            {
                Logger.LogWarning("[DeadlyStream] No confirmed download links found in selection page HTML");
            }

            return downloadLinks;
        }

        private static string BuildCompletionMessage(IReadOnlyList<string> filePaths)
        {
            if (filePaths is null || filePaths.Count == 0)
            {
                return "Downloaded from DeadlyStream";
            }

            var fileNames = filePaths
                .Select(path => Path.GetFileName(path ?? string.Empty))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fileNames.Count == 0)
            {
                return $"Downloaded {filePaths.Count} files from DeadlyStream";
            }

            if (fileNames.Count == 1)
            {
                return $"Downloaded {fileNames[0]} from DeadlyStream";
            }

            const int maxDisplay = 3;
            string summary = string.Join(", ", fileNames.Take(maxDisplay));
            if (fileNames.Count > maxDisplay)
            {
                summary += $", +{fileNames.Count - maxDisplay} more";
            }

            return $"Downloaded {fileNames.Count} files from DeadlyStream: {summary}";
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static List<string> ExtractAllDownloadLinks(string html, string baseUrl)
        {
            Logger.LogVerbose($"[DeadlyStream] ExtractAllDownloadLinks called with HTML length: {html?.Length ?? 0}, baseUrl: {baseUrl}");

            if (string.IsNullOrEmpty(html))
            {
                Logger.LogWarning("[DeadlyStream] HTML content is null or empty");
                return new List<string>();
            }

            var document = new HtmlDocument();
            document.LoadHtml(html);
            Logger.LogVerbose("[DeadlyStream] HTML document loaded successfully");


            string selector = "//a[contains(@href,'?do=download')]";
            Logger.LogVerbose($"[DeadlyStream] Using XPath selector: {selector}");

            HtmlNodeCollection nodes = document.DocumentNode.SelectNodes(selector);
            if (nodes is null || nodes.Count == 0)
            {
                bool isTopicUrl = baseUrl.IndexOf("/topic/", StringComparison.OrdinalIgnoreCase) >= 0;
                string urlType = isTopicUrl ? "forum topic" : "page";
                Logger.LogWarning($"[DeadlyStream] No file attachments/download links found on {urlType}: {baseUrl}. If this is a forum topic with attachments, they may be in a different format or the page structure may have changed.");
                return new List<string>();
            }

            Logger.LogVerbose($"[DeadlyStream] Found {nodes.Count} potential download links");

            var downloadLinks = new List<string>();
            foreach (HtmlNode node in nodes)
            {
                string href = node.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var baseUri = new Uri(baseUrl);
                    var absoluteUri = new Uri(baseUri, href);
                    href = absoluteUri.ToString();
                }

                href = WebUtility.HtmlDecode(href);
                if (!downloadLinks.Contains(href, StringComparer.OrdinalIgnoreCase))
                {
                    downloadLinks.Add(href);
                    Logger.LogVerbose($"[DeadlyStream] Added download link #{downloadLinks.Count}: {href}");
                }
            }

            if (downloadLinks.Count > 1)
            {
                Logger.LogVerbose($"[DeadlyStream] Multi-file download detected - {downloadLinks.Count} files will be downloaded");
            }

            return downloadLinks;
        }

        private static string GetFileNameFromContentDisposition(HttpResponseMessage response)
        {
            Logger.LogVerbose("[DeadlyStream] GetFileNameFromContentDisposition called");

            if (response is null || response.Content is null || response.Content.Headers.ContentDisposition is null)
            {
                Logger.LogVerbose("[DeadlyStream] Response, Content, or ContentDisposition is null, cannot extract filename");
                return null;
            }

            Logger.LogVerbose($"[DeadlyStream] ContentDisposition header: {response.Content.Headers.ContentDisposition}");

            string fileName = response.Content.Headers.ContentDisposition.FileNameStar;
            Logger.LogVerbose($"[DeadlyStream] FileNameStar: '{fileName}'");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = response.Content.Headers.ContentDisposition.FileName;
                Logger.LogVerbose($"[DeadlyStream] FileName: '{fileName}'");
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                Logger.LogVerbose("[DeadlyStream] No filename found in ContentDisposition header");
                return null;
            }

            string trimmed = SurroundingQuotesRegex.Replace(fileName, string.Empty);
            string unescaped = Uri.UnescapeDataString(trimmed);

            if (!trimmed.Equals(unescaped, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogVerbose($"[DeadlyStream] Extracted filename: '{trimmed}' -> '{unescaped}'");
            }
            else
            {
                Logger.LogVerbose($"[DeadlyStream] Extracted filename: '{unescaped}'");
            }

            return unescaped;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public async Task<Dictionary<string, object>> GetFileMetadataAsync(string url, CancellationToken cancellationToken = default)
        {
            var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string normalizedUrl = NormalizeDeadlyStreamUrl(url);
                await Logger.LogVerboseAsync($"[DeadlyStream] Getting metadata for URL: {normalizedUrl}").ConfigureAwait(false);

                // Parse URL to extract IDs
                Match match = FilePageUrlRegex.Match(normalizedUrl);
                if (!match.Success)
                {
                    await Logger.LogWarningAsync($"[DeadlyStream] Could not parse file page ID from URL: {normalizedUrl}").ConfigureAwait(false);
                    return metadata;
                }

                string filePageId = match.Groups[1].Value;
                string changelogId = match.Groups[2].Success ? match.Groups[2].Value : "0";

                metadata["filePageId"] = filePageId;
                metadata["changelogId"] = changelogId;

                // Fetch the page to extract additional metadata
                HttpResponseMessage response = await _httpClient.GetAsync(normalizedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    await Logger.LogWarningAsync($"[DeadlyStream] Failed to fetch page for metadata: {response.StatusCode}").ConfigureAwait(false);
                    return metadata;
                }

                string htmlContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                // Extract fileId from download link
                HtmlNode downloadNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href, '/files/download/')]");
                if (downloadNode != null)
                {
                    string downloadHref = downloadNode.GetAttributeValue("href", "");
                    Match fileIdMatch = Regex.Match(downloadHref, @"/files/download/(\d+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
                    if (fileIdMatch.Success)
                    {
                        metadata["fileId"] = fileIdMatch.Groups[1].Value;
                    }
                }

                // Extract version
                HtmlNode versionNode = doc.DocumentNode.SelectSingleNode("//span[@itemprop='version']");
                if (versionNode != null)
                {
                    metadata["version"] = versionNode.InnerText.Trim();
                }

                // Extract updated date
                HtmlNode dateNode = doc.DocumentNode.SelectSingleNode("//time[@datetime]");
                if (dateNode != null)
                {
                    string dateStr = dateNode.GetAttributeValue("datetime", "");
                    if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                    {
                        metadata["updated"] = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                }

                // Extract file size
                HtmlNode sizeNode = doc.DocumentNode.SelectSingleNode("//li[contains(text(), 'Size')]");
                if (sizeNode != null)
                {
                    string sizeText = sizeNode.InnerText;
                    Match sizeMatch = FileSizeWithUnitRegex.Match(sizeText);
                    if (sizeMatch.Success)
                    {
                        double size = double.Parse(sizeMatch.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture);
                        string unit = sizeMatch.Groups[2].Value.ToUpperInvariant();

                        long bytes = 0;
                        if (unit.Equals("KB", StringComparison.OrdinalIgnoreCase))
                        {
                            bytes = (long)(size * 1024);
                        }
                        else if (unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
                        {
                            bytes = (long)(size * 1024 * 1024);
                        }
                        else if (unit.Equals("GB", StringComparison.OrdinalIgnoreCase))
                        {
                            bytes = (long)(size * 1024 * 1024 * 1024);
                        }

                        metadata["size"] = bytes;
                    }
                }

                await Logger.LogVerboseAsync($"[DeadlyStream] Extracted metadata: {string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"))}").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogWarningAsync($"[DeadlyStream] Failed to extract metadata: {ex.Message}").ConfigureAwait(false);
            }

            return NormalizeMetadata(metadata);
        }

        public string GetProviderKey() => "deadlystream";

        private Dictionary<string, object> NormalizeMetadata(Dictionary<string, object> raw)
        {
            var normalized = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            string[] whitelist = new[] { "provider", "filePageId", "changelogId", "fileId", "version", "updated", "size" };

            // Always add provider
            normalized["provider"] = GetProviderKey();

            foreach (string field in whitelist)
            {
                if (field.Equals("provider", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Already added
                }

                if (!raw.ContainsKey(field))
                {
                    continue;
                }

                object value = raw[field];

                // Type-specific normalization
                if (field.Equals("size", StringComparison.OrdinalIgnoreCase))
                {
                    normalized[field] = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                }
                else if (field.Equals("updated", StringComparison.OrdinalIgnoreCase) && value != null)
                {
                    // Ensure YYYY-MM-DD format
                    string dateStr = value.ToString();
                    if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    {
                        normalized[field] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        normalized[field] = dateStr;
                    }
                }
                else
                {
                    normalized[field] = value?.ToString() ?? "";
                }
            }

            return normalized;
        }

        private static readonly Regex SurroundingQuotesRegex = new Regex("^\"|\"$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));
        private static readonly Regex FilePageUrlRegex = new Regex(@"files/file/(\d+)-[^/]*/?(?:\?r=(\d+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));
        private static readonly Regex FileSizeWithUnitRegex = new Regex(@"([\d,.]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));
    }
}
