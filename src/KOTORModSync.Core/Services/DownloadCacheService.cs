// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KOTORModSync.Core.Services.Download;
using KOTORModSync.Core.Services.FileSystem;
using KOTORModSync.Core.Utility;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;

namespace KOTORModSync.Core.Services
{

    public sealed class DownloadCacheService
    {
        public DownloadManager DownloadManager { get; set; }
        private readonly ComponentValidationService _validationService;

        private readonly Dictionary<string, DownloadFailureInfo> _failedDownloads = new Dictionary<string, DownloadFailureInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly object _failureLock = new object();

        // Phase 5: Resource Index (dual index structure for content-addressable storage)
        private static readonly Dictionary<string, ResourceMetadata> s_resourceByMetadataHash = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> s_metadataHashToContentId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ResourceMetadata> s_resourceByContentId = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_resourceIndexLock = new object();
        private static volatile bool s_resourceIndexLoaded = false;

        public DownloadCacheService()
        {
            _validationService = new ComponentValidationService();

            Logger.LogVerbose("[DownloadCacheService] Initialized");
        }

        /// <summary>
        /// Resolution filtering must reflect current <see cref="MainConfig.FilterDownloadsByResolution"/> on every operation.
        /// The GUI constructs this service before settings load; caching a single <see cref="ResolutionFilterService"/> would ignore later changes.
        /// </summary>
        internal static ResolutionFilterService CreateResolutionFilterFromMainConfig() =>
            new ResolutionFilterService(MainConfig.FilterDownloadsByResolution);

        public void SetDownloadManager(DownloadManager downloadManager = null)
        {
            if (downloadManager is null)
            {
                DownloadManager = Download.DownloadHandlerFactory.CreateDownloadManager();
            }
            else
            {
                DownloadManager = downloadManager;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public async Task<IReadOnlyDictionary<string, List<string>>> PreResolveUrlsAsync(
            ModComponent component,
            DownloadManager downloadManager = null,
            bool sequential = false,
            CancellationToken cancellationToken = default)
        {
            // Ensure the in-memory resource index is hydrated before doing any cache lookups or saves
            if (!s_resourceIndexLoaded)
            {
                try { await LoadResourceIndexAsync().ConfigureAwait(false); }
                catch { /* non-fatal */ }
            }
            await Logger.LogVerboseAsync("[DownloadCacheService] ===== PreResolveUrlsAsync START =====").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[DownloadCacheService] Component: {component?.Name ?? "NULL"}").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[DownloadCacheService] Sequential: {sequential}").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[DownloadCacheService] CancellationToken: {cancellationToken}").ConfigureAwait(false);

            if (component is null)
            {

                throw new ArgumentNullException(nameof(component));
            }


            downloadManager = downloadManager ?? DownloadManager;
            if (downloadManager is null)
            {

                throw new InvalidOperationException("DownloadManager is not set. Call SetDownloadManager() first.");
            }

            ResolutionFilterService resolutionFilter = CreateResolutionFilterFromMainConfig();

            await Logger.LogVerboseAsync($"[DownloadCacheService] Pre-resolving URLs for component: {component.Name}").ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[DownloadCacheService] Component.ResourceRegistry count: {component.ResourceRegistry?.Count ?? 0} URLs").ConfigureAwait(false);

            if (component.ResourceRegistry.Count > 0)
            {
                foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry)
                {
                    await Logger.LogVerboseAsync($"[DownloadCacheService]   URL: {kvp.Key}").ConfigureAwait(false);
                    int filenameCount = kvp.Value?.Files?.Count ?? 0;
                    await Logger.LogVerboseAsync($"[DownloadCacheService]   ResourceRegistry entry: {filenameCount} file(s)").ConfigureAwait(false);

                    // Check if we have a resource-index entry for this URL
                    ResourceMetadata cachedMetaForUrl = TryGetResourceMetadataByUrl(kvp.Key);
                    if (cachedMetaForUrl != null && cachedMetaForUrl.Files != null && cachedMetaForUrl.Files.Count > 0)
                    {
                        string firstFilename = cachedMetaForUrl.Files.Keys.First();
                        if (filenameCount == 0)
                        {
                            await Logger.LogVerboseAsync($"[DownloadCacheService]     (Note: Resource-index has {cachedMetaForUrl.Files.Count} filename(s) including '{firstFilename}' but ResourceRegistry entry has no files - will be populated during resolution)").ConfigureAwait(false);
                        }
                        else
                        {
                            await Logger.LogVerboseAsync($"[DownloadCacheService]     (Resource-index entry: {cachedMetaForUrl.Files.Count} file(s) including '{firstFilename}')").ConfigureAwait(false);
                        }
                    }

                    if (kvp.Value?.Files != null && filenameCount > 0)
                    {
                        foreach (KeyValuePair<string, bool?> filenameKvp in kvp.Value.Files)
                        {
                            await Logger.LogVerboseAsync($"[DownloadCacheService]     Filename: {filenameKvp.Key} -> {filenameKvp.Value}").ConfigureAwait(false);
                        }
                    }
                }
            }

            string modSourceDirectory = MainConfig.SourcePath?.FullName;
            if (string.IsNullOrEmpty(modSourceDirectory))
            {
                await Logger.LogWarningAsync("[DownloadCacheService] MainConfig.SourcePath is not set, cannot verify file existence").ConfigureAwait(false);
                modSourceDirectory = null;
            }

            if (!string.IsNullOrEmpty(modSourceDirectory))
            {
                await InitializeVirtualFileSystemAsync(modSourceDirectory).ConfigureAwait(false);
            }

            var urls = component.ResourceRegistry.Keys.Where(url => !string.IsNullOrWhiteSpace(url)).ToList();
            await Logger.LogVerboseAsync($"[DownloadCacheService] Extracted {urls.Count} URLs from ResourceRegistry").ConfigureAwait(false);
            foreach (string url in urls)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService]   URL: {url}").ConfigureAwait(false);
            }

            if (urls.Count == 0)
            {
                await Logger.LogVerboseAsync("[DownloadCacheService] No URLs to resolve").ConfigureAwait(false);
                return new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            // First, check which URLs actually need downloads based on file existence
            List<string> urlsNeedingDownloads = new List<string>();
            if (!string.IsNullOrEmpty(modSourceDirectory))
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] Analyzing download necessity for {urls.Count} URLs").ConfigureAwait(false);
                (List<string> urlsNeedingAnalysis, bool _initialSimulationFailed) = await AnalyzeDownloadNecessityWithStatusAsync(component, modSourceDirectory, cancellationToken).ConfigureAwait(false);

                if (urlsNeedingAnalysis.Count > 0)
                {
                    await Logger.LogVerboseAsync($"[DownloadCacheService] {urlsNeedingAnalysis.Count} URL(s) need cache/disk existence check for component '{component.Name}'").ConfigureAwait(false);
                    foreach (string url in urlsNeedingAnalysis)
                    {
                        await Logger.LogVerboseAsync($"  • URL to check: {url}").ConfigureAwait(false);
                    }
                    urlsNeedingDownloads = urlsNeedingAnalysis;
                }
                else
                {
                    await Logger.LogVerboseAsync($"[DownloadCacheService] All files already exist on disk for component '{component.Name}', no downloads needed").ConfigureAwait(false);
                }
            }

            List<string> filteredUrls = resolutionFilter.FilterByResolution(urls);
            if (filteredUrls.Count < urls.Count)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] Resolution filter reduced URLs from {urls.Count} to {filteredUrls.Count}").ConfigureAwait(false);
            }

            if (filteredUrls.Count == 0)
            {
                await Logger.LogVerboseAsync("[DownloadCacheService] All URLs filtered out by resolution filter").ConfigureAwait(false);
                return new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            // CRITICAL: Pre-populate ResourceRegistry from cache for ALL filteredUrls BEFORE any network calls
            // This ensures we skip network requests for URLs that are already cached
            await Logger.LogVerboseAsync($"[DownloadCacheService] Checking cache for all {filteredUrls.Count} URL(s) before making network requests...").ConfigureAwait(false);
            int cacheHitsBeforeNetwork = 0;
            foreach (string url in filteredUrls)
            {
                ResourceMetadata cachedMeta = TryGetResourceMetadataByUrl(url);
                if (cachedMeta != null && cachedMeta.Files != null && cachedMeta.Files.Count > 0)
                {
                    // Check if ResourceRegistry already has this URL with complete data
                    if (component.ResourceRegistry.TryGetValue(url, out ResourceMetadata existingMeta))
                    {
                        // Merge files if existing entry is incomplete
                        if (existingMeta.Files == null || existingMeta.Files.Count == 0)
                        {
                            existingMeta.Files = new Dictionary<string, bool?>(cachedMeta.Files, StringComparer.OrdinalIgnoreCase);
                            existingMeta.ContentKey = cachedMeta.ContentKey;
                            existingMeta.ContentId = cachedMeta.ContentId;
                            existingMeta.ContentHashSHA256 = cachedMeta.ContentHashSHA256;
                            existingMeta.MetadataHash = cachedMeta.MetadataHash;
                            existingMeta.FileSize = cachedMeta.FileSize;
                            existingMeta.PieceLength = cachedMeta.PieceLength;
                            existingMeta.PieceHashes = cachedMeta.PieceHashes;
                            existingMeta.TrustLevel = cachedMeta.TrustLevel;
                            existingMeta.FirstSeen = cachedMeta.FirstSeen;
                            existingMeta.LastVerified = cachedMeta.LastVerified;
                            if (cachedMeta.HandlerMetadata != null)
                            {
                                existingMeta.HandlerMetadata = new Dictionary<string, object>(cachedMeta.HandlerMetadata, StringComparer.Ordinal);
                            }
                            if (!existingMeta.HandlerMetadata.ContainsKey("PrimaryUrl"))
                            {
                                existingMeta.HandlerMetadata["PrimaryUrl"] = url;
                            }
                            cacheHitsBeforeNetwork++;
                            await Logger.LogVerboseAsync($"[DownloadCacheService] ✓ Populated incomplete ResourceRegistry entry from cache: {url} ({cachedMeta.Files.Count} file(s))").ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // Create new ResourceRegistry entry from cache
                        var newMeta = new ResourceMetadata
                        {
                            ContentKey = cachedMeta.ContentKey,
                            ContentId = cachedMeta.ContentId,
                            ContentHashSHA256 = cachedMeta.ContentHashSHA256,
                            MetadataHash = cachedMeta.MetadataHash,
                            HandlerMetadata = new Dictionary<string, object>(cachedMeta.HandlerMetadata ?? new Dictionary<string, object>(StringComparer.Ordinal), StringComparer.Ordinal),
                            Files = new Dictionary<string, bool?>(cachedMeta.Files, StringComparer.OrdinalIgnoreCase),
                            FileSize = cachedMeta.FileSize,
                            PieceLength = cachedMeta.PieceLength,
                            PieceHashes = cachedMeta.PieceHashes,
                            TrustLevel = cachedMeta.TrustLevel,
                            FirstSeen = cachedMeta.FirstSeen ?? DateTime.UtcNow,
                            LastVerified = cachedMeta.LastVerified,
                        };
                        if (!newMeta.HandlerMetadata.ContainsKey("PrimaryUrl"))
                        {
                            newMeta.HandlerMetadata["PrimaryUrl"] = url;
                        }
                        component.ResourceRegistry[url] = newMeta;
                        cacheHitsBeforeNetwork++;
                        await Logger.LogVerboseAsync($"[DownloadCacheService] ✓ Created new ResourceRegistry entry from cache: {url} ({cachedMeta.Files.Count} file(s))").ConfigureAwait(false);
                    }
                }
            }
            await Logger.LogVerboseAsync($"[DownloadCacheService] Pre-populated {cacheHitsBeforeNetwork}/{filteredUrls.Count} URL(s) from cache BEFORE network phase").ConfigureAwait(false);

            // Check if ResourceRegistry already has files for all URLs before extracting metadata
            // This prevents unnecessary network calls when files are already known
            bool allUrlsHaveFiles = true;
            foreach (string url in filteredUrls)
            {
                if (component.ResourceRegistry.TryGetValue(url, out ResourceMetadata existingMeta))
                {
                    if (existingMeta?.Files == null || existingMeta.Files.Count == 0)
                    {
                        allUrlsHaveFiles = false;
                        break;
                    }
                }
                else
                {
                    allUrlsHaveFiles = false;
                    break;
                }
            }

            List<string> urlsToResolve = new List<string>();
            Dictionary<string, List<string>> results = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<(string url, string filename)> missingFiles = new List<(string, string)>();

            // If all URLs already have files, populate results directly from ResourceRegistry and return early
            if (allUrlsHaveFiles)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] ResourceRegistry already has files for all {filteredUrls.Count} URL(s), skipping network calls").ConfigureAwait(false);
                foreach (string url in filteredUrls)
                {
                    if (component.ResourceRegistry.TryGetValue(url, out ResourceMetadata meta) && meta?.Files != null && meta.Files.Count > 0)
                    {
                        results[url] = meta.Files.Keys.ToList();
                    }
                }

                Dictionary<string, List<string>> filteredResolvedResults = resolutionFilter.FilterResolvedUrls(results);
                await Logger.LogVerboseAsync($"[DownloadCacheService] Final filtered results count: {filteredResolvedResults.Count}").ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[DownloadCacheService] ✓ Pre-resolved {filteredResolvedResults.Count} URL(s) ENTIRELY from cache (0 network requests), all files exist on disk").ConfigureAwait(false);
                await Logger.LogVerboseAsync("[DownloadCacheService] ===== PreResolveUrlsAsync END =====").ConfigureAwait(false);
                return filteredResolvedResults;
            }

            // Only extract metadata for URLs that don't already have files in ResourceRegistry
            // AND only if files don't exist on disk (i.e., urlsNeedingDownloads)
            // This prevents unnecessary network calls when files already exist
            var urlsNeedingMetadata = filteredUrls.Where(url =>
            {
                // If we checked file existence and this URL doesn't need downloads, skip metadata extraction
                if (urlsNeedingDownloads.Count > 0 && !urlsNeedingDownloads.Contains(url, StringComparer.Ordinal))
                {
                    return false;
                }

                // If ResourceRegistry already has files for this URL, skip metadata extraction
                if (component.ResourceRegistry.TryGetValue(url, out ResourceMetadata existingMeta) &&
                    existingMeta?.Files != null && existingMeta.Files.Count > 0)
                {
                    return false;
                }

                return true;
            }).ToList();

            if (urlsNeedingMetadata.Count > 0)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] Extracting metadata for {urlsNeedingMetadata.Count} URL(s) that need downloads").ConfigureAwait(false);
            }
            else
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] No URLs need metadata extraction - all files exist on disk or already cached").ConfigureAwait(false);
            }

            foreach (string url in urlsNeedingMetadata)
            {
                try
                {
                    IDownloadHandler handler = downloadManager.GetHandlerForUrl(url);
                    if (handler != null)
                    {
                        Dictionary<string, object> metadata = await handler.GetFileMetadataAsync(url, cancellationToken).ConfigureAwait(false);
                        if (metadata != null && metadata.Count > 0)
                        {
                            // Ensure provider is set
                            if (!metadata.ContainsKey("provider"))
                            {
                                metadata["provider"] = handler.GetProviderKey();
                            }

                            // Normalize URL if present
                            if (metadata.ContainsKey("url"))
                            {
                                metadata["url"] = UrlNormalizer.Normalize(metadata["url"].ToString());
                            }

                            // Compute metadataHash
                            string metadataHash = CanonicalJson.ComputeHash(metadata);
                            await Logger.LogVerboseAsync($"[Cache] Computed MetadataHash for {url}: {metadataHash}...").ConfigureAwait(false);

                            // Compute ContentId from metadata (PRE-DOWNLOAD)
                            string contentId = null;
                            try
                            {
                                contentId = DownloadCacheOptimizer.ComputeContentIdFromMetadata(metadata, url);
                                await Logger.LogVerboseAsync($"[Cache] Computed ContentId from metadata: {contentId}...").ConfigureAwait(false);

                                // Record telemetry
                                TelemetryService.Instance.RecordContentIdGenerated(handler.GetProviderKey(), fromMetadata: true);
                            }
                            catch (Exception ex)
                            {
                                await Logger.LogWarningAsync($"[Cache] Failed to compute ContentId for {url}: {ex.Message}").ConfigureAwait(false);
                            }

                            // Check if we already have a ContentId mapping
                            string existingContentId = null;
                            string logMessage = null;
                            lock (s_resourceIndexLock)
                            {
                                if (s_metadataHashToContentId.TryGetValue(metadataHash, out existingContentId))
                                {
                                    logMessage = $"[Cache] Found existing ContentId mapping: {existingContentId}...";
                                }
                            }

                            if (logMessage != null)
                            {
                                await Logger.LogVerboseAsync(logMessage).ConfigureAwait(false);
                                // Record cache hit telemetry
                                TelemetryService.Instance.RecordCacheHit(handler.GetProviderKey(), "metadata");
                            }
                            else
                            {
                                // Record cache miss telemetry
                                TelemetryService.Instance.RecordCacheMiss(handler.GetProviderKey(), "no_existing_mapping");
                            }

                            // Create or update ResourceMetadata
                            long fileSize = 0;
                            if (metadata.TryGetValue("size", out object sizeValue))
                            {
                                fileSize = Convert.ToInt64(sizeValue, System.Globalization.CultureInfo.InvariantCulture);
                            }
                            else if (metadata.TryGetValue("contentLength", out object contentLengthValue))
                            {
                                fileSize = Convert.ToInt64(contentLengthValue, System.Globalization.CultureInfo.InvariantCulture);
                            }

                            // Check resource-index for existing entry with Files to avoid unnecessary network queries
                            ResourceMetadata cachedMetaWithFiles = null;
                            lock (s_resourceIndexLock)
                            {
                                cachedMetaWithFiles = TryGetResourceMetadataByUrl(url);
                                if (cachedMetaWithFiles != null && (cachedMetaWithFiles.Files == null || cachedMetaWithFiles.Files.Count == 0))
                                {
                                    cachedMetaWithFiles = null; // Only use if it has Files
                                }
                            }

                            // Ensure metadata dictionary has PrimaryUrl for global index lookup compatibility
                            if (!metadata.ContainsKey("PrimaryUrl"))
                            {
                                metadata["PrimaryUrl"] = url;
                            }

                            var resourceMeta = new ResourceMetadata
                            {
                                ContentKey = contentId ?? metadataHash, // Use ContentId if available, otherwise metadataHash
                                ContentId = contentId, // Store the pre-computed ContentId
                                MetadataHash = metadataHash,
                                HandlerMetadata = metadata,
                                FileSize = fileSize,
                                FirstSeen = DateTime.UtcNow,
                                TrustLevel = MappingTrustLevel.Unverified,
                                Files = cachedMetaWithFiles?.Files != null
                                    ? new Dictionary<string, bool?>(cachedMetaWithFiles.Files, StringComparer.OrdinalIgnoreCase)
                                    : new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                            };

                            if (cachedMetaWithFiles != null && cachedMetaWithFiles.Files != null && cachedMetaWithFiles.Files.Count > 0)
                            {
                                await Logger.LogVerboseAsync($"[DownloadCacheService] Copied {cachedMetaWithFiles.Files.Count} filename(s) from resource-index to new ResourceMetadata").ConfigureAwait(false);
                            }

                            // Update component's ResourceRegistry - use URL as key
                            // Check if there's already an entry for this URL and merge Files if needed
                            if (component.ResourceRegistry.TryGetValue(url, out ResourceMetadata existingByUrl) &&
                                existingByUrl.Files != null && existingByUrl.Files.Count > 0)
                            {
                                if (resourceMeta.Files == null || resourceMeta.Files.Count == 0)
                                {
                                    resourceMeta.Files = new Dictionary<string, bool?>(existingByUrl.Files, StringComparer.OrdinalIgnoreCase);
                                }
                                else
                                {
                                    // Merge both - prefer existing entry's values
                                    foreach (KeyValuePair<string, bool?> fileEntry in existingByUrl.Files)
                                    {
                                        if (!resourceMeta.Files.ContainsKey(fileEntry.Key))
                                        {
                                            resourceMeta.Files[fileEntry.Key] = fileEntry.Value;
                                        }
                                    }
                                }
                                await Logger.LogVerboseAsync($"[DownloadCacheService] Merging ResourceRegistry entry: copied {existingByUrl.Files.Count} filename(s) from existing entry to new metadata entry").ConfigureAwait(false);
                            }

                            // Always update to ensure latest metadata (ContentId, MetadataHash, HandlerMetadata) is saved
                            component.ResourceRegistry[url] = resourceMeta;
                            await Logger.LogVerboseAsync($"[DownloadCacheService] Updated/Added ResourceRegistry entry for URL: {url} (MetadataHash: {metadataHash.Substring(0, Math.Min(16, metadataHash.Length))}..., ContentId: {contentId?.Substring(0, Math.Min(16, contentId?.Length ?? 0)) ?? "null"}...)").ConfigureAwait(false);

                            // Update global index
                            lock (s_resourceIndexLock)
                            {
                                // Preserve Files from existing entry if updating
                                if (s_resourceByMetadataHash.TryGetValue(metadataHash, out ResourceMetadata existingByMetadataHash) &&
                                    existingByMetadataHash.Files != null && existingByMetadataHash.Files.Count > 0)
                                {
                                    // Merge Files from existing entry
                                    if (resourceMeta.Files == null || resourceMeta.Files.Count == 0)
                                    {
                                        resourceMeta.Files = new Dictionary<string, bool?>(existingByMetadataHash.Files, StringComparer.OrdinalIgnoreCase);
                                    }
                                    else
                                    {
                                        // Merge both dictionaries
                                        foreach (KeyValuePair<string, bool?> fileEntry in existingByMetadataHash.Files)
                                        {
                                            if (!resourceMeta.Files.ContainsKey(fileEntry.Key))
                                            {
                                                resourceMeta.Files[fileEntry.Key] = fileEntry.Value;
                                            }
                                        }
                                    }
                                }

                                s_resourceByMetadataHash[metadataHash] = resourceMeta;

                                // Store by ContentId if we computed one
                                if (contentId != null &&
                                    s_resourceByContentId.TryGetValue(contentId, out ResourceMetadata existingByContentId) &&
                                    existingByContentId.Files != null && existingByContentId.Files.Count > 0)
                                {
                                    if (resourceMeta.Files == null || resourceMeta.Files.Count == 0)
                                    {
                                        resourceMeta.Files = new Dictionary<string, bool?>(existingByContentId.Files, StringComparer.OrdinalIgnoreCase);
                                    }
                                    else
                                    {
                                        foreach (KeyValuePair<string, bool?> fileEntry in existingByContentId.Files)
                                        {
                                            if (!resourceMeta.Files.ContainsKey(fileEntry.Key))
                                            {
                                                resourceMeta.Files[fileEntry.Key] = fileEntry.Value;
                                            }
                                        }
                                    }
                                }
                                if (contentId != null)
                                {
                                    s_resourceByContentId[contentId] = resourceMeta;
                                    // Also store the mapping
                                    if (!s_metadataHashToContentId.ContainsKey(metadataHash))
                                    {
                                        s_metadataHashToContentId[metadataHash] = contentId;
                                    }
                                }

                                if (existingContentId != null && !s_resourceByContentId.ContainsKey(existingContentId))
                                {
                                    s_resourceByContentId[existingContentId] = resourceMeta;
                                }
                            }

                            await Logger.LogVerboseAsync($"[Cache] Updated ResourceRegistry for URL: {url}").ConfigureAwait(false);
                            await Logger.LogVerboseAsync($"[Cache] ResourceRegistry now has {component.ResourceRegistry.Count} entry(ies) total").ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogVerboseAsync($"[Cache] Failed to extract metadata for {url}: {ex.Message}").ConfigureAwait(false);
                }
            }

            // Save updated resource index
            try
            {
                await SaveResourceIndexAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogWarningAsync($"[Cache] Failed to save resource index: {ex.Message}").ConfigureAwait(false);
            }

            foreach (string url in filteredUrls)
            {
                ResourceMetadata cachedMeta = TryGetResourceMetadataByUrl(url);
                component.ResourceRegistry.TryGetValue(url, out ResourceMetadata existingResource);

                // Check if we already have complete ResourceMetadata with all filenames for this URL
                if (existingResource != null && existingResource.Files != null && existingResource.Files.Count > 0)
                {
                    // We have ResourceMetadata with Files populated - can skip re-resolution
                    await Logger.LogVerboseAsync($"[DownloadCacheService] ResourceMetadata has {existingResource.Files.Count} file(s) for URL: {url}, skipping re-resolution").ConfigureAwait(false);
                }
                else if (cachedMeta != null && cachedMeta.Files != null && cachedMeta.Files.Count > 0)
                {
                    // We have resource-index entry but ResourceMetadata.Files is empty or missing
                    // Populate ResourceMetadata.Files from resource-index
                    if (!component.ResourceRegistry.TryGetValue(url, out ResourceMetadata resourceMeta))
                    {
                        // Create new ResourceMetadata entry
                        resourceMeta = new ResourceMetadata
                        {
                            Files = new Dictionary<string, bool?>(cachedMeta.Files, StringComparer.OrdinalIgnoreCase),
                            HandlerMetadata = new Dictionary<string, object>(cachedMeta.HandlerMetadata ?? new Dictionary<string, object>(StringComparer.Ordinal), StringComparer.Ordinal),
                        };
                        if (!resourceMeta.HandlerMetadata.ContainsKey("PrimaryUrl"))
                        {
                            resourceMeta.HandlerMetadata["PrimaryUrl"] = url;
                        }
                        component.ResourceRegistry[url] = resourceMeta;
                    }
                    else
                    {
                        // Ensure Files dictionary exists
                        if (resourceMeta.Files == null)
                        {
                            resourceMeta.Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
                        }
                        // Populate Files from cached metadata
                        foreach (KeyValuePair<string, bool?> fileEntry in cachedMeta.Files)
                        {
                            if (!resourceMeta.Files.ContainsKey(fileEntry.Key))
                            {
                                resourceMeta.Files[fileEntry.Key] = fileEntry.Value;
                                await Logger.LogVerboseAsync($"[DownloadCacheService] Populated ResourceMetadata.Files from resource-index: {url} -> {fileEntry.Key}").ConfigureAwait(false);
                            }
                        }
                    }

                    // Resource-index has filenames - skip re-resolution since ResourceMetadata.Files now has them
                    await Logger.LogVerboseAsync($"[DownloadCacheService] Resource-index has {cachedMeta.Files.Count} file(s) for URL: {url}, skipping re-resolution").ConfigureAwait(false);
                }
                else
                {
                    // No resource-index entry - will be resolved from network
                    await Logger.LogVerboseAsync($"[DownloadCacheService] Resource-index miss for URL: {url} - will resolve from network").ConfigureAwait(false);
                    urlsToResolve.Add(url);
                }
            }

            // Note: We only re-resolve URLs when we don't have complete ResourceMetadata
            // with all filenames already populated from a previous resolution

            if (urlsToResolve.Count > 0)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] Resolving {urlsToResolve.Count} URL(s) via network (including cached URLs to get all files)").ConfigureAwait(false);
                await Logger.LogVerboseAsync("[DownloadCacheService] URLs to resolve:").ConfigureAwait(false);
                foreach (string url in urlsToResolve)
                {
                    await Logger.LogVerboseAsync($"[DownloadCacheService]   {url}").ConfigureAwait(false);
                }

                Dictionary<string, List<string>> resolvedResults = await downloadManager.ResolveUrlsToFilenamesAsync(urlsToResolve, cancellationToken, sequential: false).ConfigureAwait(false);

                await Logger.LogVerboseAsync($"[DownloadCacheService] Received {resolvedResults.Count} resolved results from download manager").ConfigureAwait(false);
                foreach (KeyValuePair<string, List<string>> kvp in resolvedResults)
                {
                    await Logger.LogVerboseAsync($"[DownloadCacheService]   URL: {kvp.Key}").ConfigureAwait(false);
                    await Logger.LogVerboseAsync($"[DownloadCacheService]   Filenames: {string.Join(", ", kvp.Value)}").ConfigureAwait(false);
                }

                if (sequential)
                {
                    await Logger.LogVerboseAsync("[DownloadCacheService] Processing resolved URLs sequentially").ConfigureAwait(false);
                    foreach (KeyValuePair<string, List<string>> kvp in resolvedResults)
                    {
                        await ProcessResolvedUrlForPreResolveAsync(component, kvp, results, modSourceDirectory, missingFiles, resolutionFilter).ConfigureAwait(false);
                    }
                }
                else
                {
                    await Logger.LogVerboseAsync("[DownloadCacheService] Processing resolved URLs in parallel").ConfigureAwait(false);
                    var processingTasks = resolvedResults.Select(kvp =>
                        ProcessResolvedUrlForPreResolveAsync(component, kvp, results, modSourceDirectory, missingFiles, resolutionFilter)
                    ).ToList();
                    await Task.WhenAll(processingTasks).ConfigureAwait(false);
                }
            }

            Dictionary<string, List<string>> filteredResults = resolutionFilter.FilterResolvedUrls(results);

            await Logger.LogVerboseAsync($"[DownloadCacheService] Final filtered results count: {filteredResults.Count}").ConfigureAwait(false);
            foreach (KeyValuePair<string, List<string>> kvp in filteredResults)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService]   Final URL: {kvp.Key}").ConfigureAwait(false);
                await Logger.LogVerboseAsync($"[DownloadCacheService]   Final Filenames: {string.Join(", ", kvp.Value)}").ConfigureAwait(false);
            }

            if (missingFiles.Count > 0)
            {
                await Logger.LogWarningAsync($"[DownloadCacheService] Pre-resolve summary for '{component.Name}': {filteredResults.Count} URLs resolved, {missingFiles.Count} files missing on disk").ConfigureAwait(false);
                await Logger.LogWarningAsync("[DownloadCacheService] Missing files that need to be downloaded:").ConfigureAwait(false);
                foreach ((string url, string filename) item in missingFiles)
                {
                    string url = item.url;
                    string filename = item.filename;
                    await Logger.LogWarningAsync($"  • {filename} (from {url})").ConfigureAwait(false);
                    RecordFailure(url, component.Name, filename, "File does not exist on disk", DownloadFailureInfo.FailureType.FileNotFound);
                }
            }
            else
            {
                int totalFromCache = cacheHitsBeforeNetwork + (filteredResults.Count - urlsToResolve.Count);
                await Logger.LogVerboseAsync($"[DownloadCacheService] Pre-resolved {filteredResults.Count} URLs ({totalFromCache} from cache, {urlsToResolve.Count} from network), all files exist on disk").ConfigureAwait(false);
            }

            await Logger.LogVerboseAsync("[DownloadCacheService] ===== PreResolveUrlsAsync END =====").ConfigureAwait(false);
            return filteredResults;
        }

        private async Task ProcessResolvedUrlForPreResolveAsync(
            ModComponent component,
            KeyValuePair<string, List<string>> kvp,
            Dictionary<string, List<string>> results,
            string modSourceDirectory,
            List<(string url, string filename)> missingFiles,
            ResolutionFilterService resolutionFilter)
        {
            results[kvp.Key] = kvp.Value;

            if (kvp.Value != null && kvp.Value.Count > 0)
            {
                List<string> filteredFilenames = resolutionFilter.FilterByResolution(kvp.Value);

                // Always populate ModLinkFilenames with resolved filenames, even if modSourceDirectory is null
                // This ensures filenames are persisted to serialized component file even when directory isn't configured
                await PopulateModLinkFilenamesForUrlAsync(component, kvp.Key, filteredFilenames, modSourceDirectory).ConfigureAwait(false);

                string bestMatchFilename = await _validationService.FindBestMatchingFilenameAsync(component, kvp.Key, filteredFilenames).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(bestMatchFilename))
                {
                    bestMatchFilename = filteredFilenames.Count > 0 ? filteredFilenames[0] : kvp.Value[0];
                    if (filteredFilenames.Count > 1)
                    {
                        await Logger.LogWarningAsync($"[DownloadCacheService] No instruction pattern match for URL {kvp.Key}, using first of {filteredFilenames.Count} files: {bestMatchFilename}").ConfigureAwait(false);
                    }
                }
                else if (filteredFilenames.Count > 1)
                {
                    await Logger.LogAsync($"[DownloadCacheService] ✓ Pattern-matched filename for '{component.Name}': {bestMatchFilename} (from {filteredFilenames.Count} options)").ConfigureAwait(false);
                }

                // Update ResourceMetadata with resolved filenames
                await UpdateResourceMetadataWithFilenamesAsync(component, kvp.Key, filteredFilenames).ConfigureAwait(false);

                await Logger.LogVerboseAsync($"[DownloadCacheService] Resolved filename: {kvp.Key} -> {bestMatchFilename}").ConfigureAwait(false);

                if (!string.IsNullOrEmpty(modSourceDirectory) && !string.IsNullOrEmpty(bestMatchFilename))
                {
                    string filePath = Path.Combine(modSourceDirectory, bestMatchFilename);
                    if (!File.Exists(filePath))
                    {
                        missingFiles.Add((kvp.Key, bestMatchFilename));
                        await Logger.LogWarningAsync($"[DownloadCacheService] Resolved file does not exist on disk: {bestMatchFilename}").ConfigureAwait(false);
                    }
                }
            }
            else
            {
                RecordFailure(kvp.Key, component.Name, expectedFileName: null, "Failed to resolve filename from URL", DownloadFailureInfo.FailureType.ResolutionFailed);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async Task<(string url, string fileName, bool needsDownload)> CheckCacheAndFileExistenceAsync(
            string url,
            ModComponent component,
            string modSourceDirectory,
            string destinationDirectory,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken,
            ResolutionFilterService resolutionFilter)
        {
            // Ensure ResourceRegistry has an entry for this URL with Files dictionary
            if (!component.ResourceRegistry.TryGetValue(url, out ResourceMetadata resourceMeta))
            {
                resourceMeta = new ResourceMetadata
                {
                    Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                    HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal),
                };
                resourceMeta.HandlerMetadata["PrimaryUrl"] = url;
                component.ResourceRegistry[url] = resourceMeta;
            }
            if (resourceMeta.Files == null)
            {
                resourceMeta.Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            }

            // Check resource-index for cached filenames - check ALL files exist
            ResourceMetadata cachedMeta = TryGetResourceMetadataByUrl(url);
            if (cachedMeta != null && cachedMeta.Files != null && cachedMeta.Files.Count > 0)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] Checking {cachedMeta.Files.Count} file(s) from resource-index for URL: {url}").ConfigureAwait(false);

                // Check if ALL files from resource-index exist and update ResourceMetadata.Files
                bool allFilesExist = true;
                string firstExistingFile = null;
                foreach (string cachedFileName in cachedMeta.Files.Keys)
                {
                    if (string.IsNullOrWhiteSpace(cachedFileName))
                    {
                        continue;
                    }


                    string existingFilePath = ResolveSourceFilePath(cachedFileName, modSourceDirectory, destinationDirectory);

                    // Update ResourceMetadata.Files based on file existence
                    if (File.Exists(existingFilePath))
                    {
                        // File exists - set to true
                        resourceMeta.Files[cachedFileName] = true;
                        if (firstExistingFile == null)
                        {
                            firstExistingFile = cachedFileName;
                        }
                        await Logger.LogVerboseAsync($"[DownloadCacheService]   ✓ File exists: {cachedFileName}").ConfigureAwait(false);
                    }
                    else
                    {
                        // File doesn't exist - leave as null (don't override explicit false)
                        if (!resourceMeta.Files.ContainsKey(cachedFileName))
                        {
                            resourceMeta.Files[cachedFileName] = null;
                        }

                        await Logger.LogVerboseAsync($"[DownloadCacheService]   ✗ File not found: {cachedFileName} (will need to download)").ConfigureAwait(false);
                        allFilesExist = false;
                        break;
                    }
                }

                // Only skip download if ALL files exist
                if (allFilesExist && firstExistingFile != null)
                {
                    string existingFilePath = ResolveSourceFilePath(firstExistingFile, modSourceDirectory, destinationDirectory);

                    // VERIFY INTEGRITY: Check if existing file matches cached integrity data
                    bool integrityValid = true;
                    if (cachedMeta.ContentHashSHA256 != null || (cachedMeta.PieceHashes != null && cachedMeta.PieceLength > 0))
                    {
                        integrityValid = await DownloadCacheOptimizer.VerifyContentIntegrity(existingFilePath, cachedMeta).ConfigureAwait(false);
                        if (!integrityValid)
                        {
                            await Logger.LogWarningAsync($"[DownloadCacheService] Existing file failed integrity check: {firstExistingFile}").ConfigureAwait(false);
                            await Logger.LogWarningAsync($"[DownloadCacheService] File may be corrupted - will re-download").ConfigureAwait(false);
                            // Force re-download by returning needsDownload: true
                            return (url, fileName: firstExistingFile, needsDownload: true);
                        }
                    }

                    bool shouldValidate = MainConfig.ValidateAndReplaceInvalidArchives;

                    if (shouldValidate && ArchiveHelper.IsArchive(existingFilePath))
                    {
                        await Logger.LogVerboseAsync($"[DownloadCacheService] Validating archive integrity: {existingFilePath}").ConfigureAwait(false);
                    }

                    string fileType = ArchiveHelper.IsArchive(existingFilePath) ? "archive" : "non-archive";
                    string reason = shouldValidate ? $"{fileType} file(s) exist" : "file(s) exist (validation disabled)";
                    await Logger.LogVerboseAsync($"[DownloadCacheService] {char.ToUpper(reason[0], System.Globalization.CultureInfo.InvariantCulture)}{reason.Substring(1)}, skipping download: all {cachedMeta.Files.Count} file(s) from {url} exist").ConfigureAwait(false);

                    progress?.Report(new DownloadProgress
                    {
                        ModName = component.Name,
                        Url = url,
                        Status = DownloadStatus.Skipped,
                        StatusMessage = $"All {cachedMeta.Files.Count} file(s) already exist, skipping download",
                        ProgressPercentage = 100,
                        FilePath = existingFilePath,
                        TotalBytes = new FileInfo(existingFilePath).Length,
                        BytesDownloaded = new FileInfo(existingFilePath).Length,
                    });

                    return (url, fileName: firstExistingFile, needsDownload: false);
                }
            }

            Dictionary<string, List<string>> resolved = await DownloadManager.ResolveUrlsToFilenamesAsync(new List<string> { url }, cancellationToken).ConfigureAwait(false);
            if (resolved.TryGetValue(url, out List<string> value) && value.Count > 0)
            {
                List<string> filteredFilenames = resolutionFilter.FilterByResolution(value);

                await PopulateModLinkFilenamesWithSimulationAsync(component, url, filteredFilenames, modSourceDirectory).ConfigureAwait(false);

                if (filteredFilenames.Count > 0)
                {
                    // Check ALL resolved filenames and update ModLinkFilenames
                    bool allResolvedFilesExist = true;
                    string firstExistingResolvedFile = null;
                    foreach (string resolvedFileName in filteredFilenames)
                    {
                        if (string.IsNullOrWhiteSpace(resolvedFileName))
                        {
                            continue;
                        }


                        string expectedFilePath = ResolveSourceFilePath(resolvedFileName, modSourceDirectory, destinationDirectory);

                        // Update ModLinkFilenames based on file existence
                        if (File.Exists(expectedFilePath))
                        {
                            // File exists - set to true
                            resourceMeta.Files[resolvedFileName] = true;
                            if (firstExistingResolvedFile == null)
                            {
                                firstExistingResolvedFile = resolvedFileName;
                            }

                        }
                        else
                        {
                            // File doesn't exist - leave as null (don't override explicit false)
                            if (!resourceMeta.Files.ContainsKey(resolvedFileName))
                            {
                                resourceMeta.Files[resolvedFileName] = null;
                            }


                            allResolvedFilesExist = false;
                        }
                    }

                    // Only skip download if ALL resolved files exist
                    if (allResolvedFilesExist && firstExistingResolvedFile != null)
                    {
                        string existingFilePath = ResolveSourceFilePath(firstExistingResolvedFile, modSourceDirectory, destinationDirectory);

                        bool shouldValidate = MainConfig.ValidateAndReplaceInvalidArchives;

                        if (shouldValidate && ArchiveHelper.IsArchive(existingFilePath))
                        {
                            await Logger.LogVerboseAsync($"[DownloadCacheService] Validating existing archive: {existingFilePath}").ConfigureAwait(false);
                        }

                        await Logger.LogVerboseAsync($"[DownloadCacheService] Found existing file(s): all {filteredFilenames.Count} file(s) from {url} exist").ConfigureAwait(false);

                        progress?.Report(new DownloadProgress
                        {
                            ModName = component.Name,
                            Url = url,
                            Status = DownloadStatus.Completed,
                            CompletedFromCache = true,
                            StatusMessage = $"Using cached file(s) ({filteredFilenames.Count})",
                            ProgressPercentage = 100,
                            FilePath = existingFilePath,
                            TotalBytes = new FileInfo(existingFilePath).Length,
                            BytesDownloaded = new FileInfo(existingFilePath).Length,
                        });

                        return (url, fileName: firstExistingResolvedFile, needsDownload: false);
                    }
                }
            }

            return (url, fileName: null, needsDownload: true);
        }

        public async Task InitializeVirtualFileSystemAsync(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                return;
            }


            VirtualFileSystemProvider vfs = _validationService.GetVirtualFileSystem();
            await vfs.InitializeFromRealFileSystemAsync(rootPath).ConfigureAwait(false);
            await Logger.LogVerboseAsync($"[DownloadCacheService] VirtualFileSystem initialized for: {rootPath}").ConfigureAwait(false);
        }

        /// <summary>
        /// Looks up ResourceMetadata by URL from the resource-index.
        /// </summary>
        public static ResourceMetadata TryGetResourceMetadataByUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            // Normalize incoming URL to avoid hit/miss due to trailing slash or provider-specific variants

            string normalizedLookup = UrlNormalizer.Normalize(url);
            string trimmedLookup = normalizedLookup.TrimEnd('/');

            lock (s_resourceIndexLock)
            {
                // Search through both indices to find ResourceMetadata with matching URL (stored in HandlerMetadata["PrimaryUrl"])
                foreach (ResourceMetadata meta in s_resourceByMetadataHash.Values)
                {
                    string metaUrl = meta.HandlerMetadata != null && meta.HandlerMetadata.TryGetValue("PrimaryUrl", out object urlObj)
                        ? urlObj?.ToString() ?? string.Empty
                        : string.Empty;
                    string metaUrlNorm = UrlNormalizer.Normalize(metaUrl);
                    if (string.Equals(metaUrlNorm, normalizedLookup, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(metaUrlNorm.TrimEnd('/'), trimmedLookup, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogVerbose($"[DownloadCacheService] Resource index hit for URL: {url}");
                        return meta;
                    }
                }

                foreach (ResourceMetadata meta in s_resourceByContentId.Values)
                {
                    string metaUrl = meta.HandlerMetadata != null && meta.HandlerMetadata.TryGetValue("PrimaryUrl", out object urlObj)
                        ? urlObj?.ToString() ?? string.Empty
                        : string.Empty;
                    string metaUrlNorm = UrlNormalizer.Normalize(metaUrl);
                    if (string.Equals(metaUrlNorm, normalizedLookup, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(metaUrlNorm.TrimEnd('/'), trimmedLookup, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogVerbose($"[DownloadCacheService] Resource index hit for URL: {url}");
                        return meta;
                    }
                }
            }

            Logger.LogVerbose($"[DownloadCacheService] Resource index miss for URL: {url}");
            return null;
        }

        /// <summary>
        /// Gets the first filename for a URL from resource-index, for backward compatibility.
        /// </summary>
        public static string GetFileName(string url)
        {
            ResourceMetadata meta = TryGetResourceMetadataByUrl(url);
            if (meta != null && meta.Files != null && meta.Files.Count > 0)
            {
                // Return the first filename (original cache only stored one)
                return meta.Files.Keys.First();
            }

            return null;
        }

        /// <summary>
        /// Gets the file path for the first filename of a URL from resource-index, for backward compatibility.
        /// </summary>
        public static string GetFilePath(string url)
        {
            ResourceMetadata meta = TryGetResourceMetadataByUrl(url);
            if (meta != null && meta.Files != null && meta.Files.Count > 0)
            {
                string fileName = meta.Files.Keys.First();
                if (string.IsNullOrEmpty(fileName))
                {
                    return null;
                }


                if (MainConfig.SourcePath is null)
                {

                    return fileName;
                }


                return Path.Combine(MainConfig.SourcePath.FullName, fileName);
            }

            return null;
        }

        /// <summary>
        /// Checks if a URL has cached ResourceMetadata in the resource-index.
        /// </summary>
        public static bool IsCached(string url)
        {
            return TryGetResourceMetadataByUrl(url) != null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public async Task<IReadOnlyList<DownloadCacheEntry>> ResolveOrDownloadAsync(
                ModComponent component,
                string destinationDirectory,
                IProgress<DownloadProgress> progress = null,
                bool sequential = false,
                CancellationToken cancellationToken = default)
        {
            if (component is null)
            {

                throw new ArgumentNullException(nameof(component));
            }


            await Logger.LogVerboseAsync($"[DownloadCacheService] Processing component: {component.Name} ({(component.ResourceRegistry?.Count ?? 0)} URL(s))").ConfigureAwait(false);

            ResolutionFilterService resolutionFilter = CreateResolutionFilterFromMainConfig();

            string modSourceDirectory = MainConfig.SourcePath?.FullName;
            if (string.IsNullOrEmpty(modSourceDirectory))
            {
                await Logger.LogWarningAsync("[DownloadCacheService] MainConfig.SourcePath is not set, cannot analyze download necessity").ConfigureAwait(false);
                modSourceDirectory = destinationDirectory;
            }

            await InitializeVirtualFileSystemAsync(modSourceDirectory).ConfigureAwait(false);

            await Logger.LogVerboseAsync($"[DownloadCacheService] Analyzing download necessity for {(component.ResourceRegistry?.Count ?? 0)} URLs").ConfigureAwait(false);
            (List<string> urlsNeedingAnalysis, bool initialSimulationFailed) = await AnalyzeDownloadNecessityWithStatusAsync(component, modSourceDirectory, cancellationToken).ConfigureAwait(false);

            var urlsToProcess = new List<string>();
            foreach (string url in urlsNeedingAnalysis)
            {
                if (!resolutionFilter.ShouldDownload(url))
                {
                    await Logger.LogVerboseAsync($"[DownloadCacheService] Skipping URL due to resolution filter: {url}").ConfigureAwait(false);
                    continue;
                }
                urlsToProcess.Add(url);
            }

            if (urlsToProcess.Count == 0)
            {
                await Logger.LogVerboseAsync("[DownloadCacheService] No URLs need processing after analysis").ConfigureAwait(false);
                List<string> allAnalyzedUrls = component.ResourceRegistry?.Keys.Where(url => !string.IsNullOrWhiteSpace(url)).ToList() ?? new List<string>();
                var cacheEntries = new List<DownloadCacheEntry>();

                foreach (string url in allAnalyzedUrls)
                {
                    ResourceMetadata cachedMeta = TryGetResourceMetadataByUrl(url);
                    if (cachedMeta != null && cachedMeta.Files != null && cachedMeta.Files.Count > 0)
                    {
                        // Create entries for ALL filenames from resource-index (URL can have multiple files)
                        foreach (string fileName in cachedMeta.Files.Keys)
                        {
                            if (string.IsNullOrWhiteSpace(fileName))
                            {
                                continue;
                            }


                            var entry = new DownloadCacheEntry
                            {
                                Url = url,
                                FileName = fileName,
                                IsArchiveFile = ArchiveHelper.IsArchive(fileName),
                            };
                            cacheEntries.Add(entry);
                        }
                    }
                    else
                    {
                        Dictionary<string, List<string>> resolved = await DownloadManager.ResolveUrlsToFilenamesAsync(new List<string> { url }, cancellationToken).ConfigureAwait(false);
                        if (resolved.TryGetValue(url, out List<string> filenames) && filenames.Count > 0)
                        {
                            // Create entries for ALL resolved filenames (URL can have multiple files)
                            foreach (string fileName in filenames)
                            {
                                if (string.IsNullOrWhiteSpace(fileName))
                                {
                                    await Logger.LogWarningAsync($"[DownloadCacheService] Skipping empty filename from URL: {url}").ConfigureAwait(false);
                                    RecordFailure(url, component.Name, expectedFileName: null, "Resolved filename is empty", DownloadFailureInfo.FailureType.ResolutionFailed);
                                    continue;
                                }

                                var entry = new DownloadCacheEntry
                                {
                                    Url = url,
                                    FileName = fileName,
                                    IsArchiveFile = ArchiveHelper.IsArchive(fileName),
                                };
                                cacheEntries.Add(entry);
                            }
                        }
                        else
                        {
                            RecordFailure(url, component.Name, expectedFileName: null, "Failed to resolve filename", DownloadFailureInfo.FailureType.ResolutionFailed);
                        }
                    }
                }

                return cacheEntries;
            }

            await Logger.LogVerboseAsync($"[DownloadCacheService] Checking cache and file existence for {urlsToProcess.Count} URL(s)").ConfigureAwait(false);
            var cachedResults = new List<DownloadCacheEntry>();
            var urlsNeedingDownload = new List<string>();

            List<(string, string, bool)> cacheCheckResults;

            if (sequential)
            {
                cacheCheckResults = new List<(string, string, bool)>();
                foreach (string url in urlsToProcess)
                {
                    (string url, string fileName, bool needsDownload) result = await CheckCacheAndFileExistenceAsync(url, component, modSourceDirectory, destinationDirectory, progress, cancellationToken, resolutionFilter).ConfigureAwait(false);
                    cacheCheckResults.Add(result);
                }
            }
            else
            {
                var cacheCheckTasks = urlsToProcess.Select(url =>
                    CheckCacheAndFileExistenceAsync(url, component, modSourceDirectory, destinationDirectory, progress, cancellationToken, resolutionFilter)
                ).ToList();

                cacheCheckResults = (await Task.WhenAll(cacheCheckTasks).ConfigureAwait(false)).ToList();
            }

            foreach ((string, string, bool) result in cacheCheckResults)
            {
                string url = result.Item1;
                string fileName = result.Item2;
                bool needsDownload = result.Item3;

                if (!string.IsNullOrEmpty(fileName))
                {
                    // Create a DownloadCacheEntry for backward compatibility with existing code
                    var entry = new DownloadCacheEntry
                    {
                        Url = url,
                        FileName = fileName,
                        IsArchiveFile = ArchiveHelper.IsArchive(fileName),
                    };
                    cachedResults.Add(entry);
                    await Logger.LogVerboseAsync($"[DownloadCacheService] File already cached/exists: {fileName}").ConfigureAwait(false);
                }
                else if (needsDownload)
                {
                    if (ShouldDownloadUrl(component, url))
                    {
                        urlsNeedingDownload.Add(url);
                        await Logger.LogVerboseAsync($"[DownloadCacheService] Marked for download: {url}").ConfigureAwait(false);
                    }
                    else
                    {
                        await Logger.LogWarningAsync($"[DownloadCacheService] Skipping URL (all filenames disabled in ModLinkFilenames): {url}").ConfigureAwait(false);
                    }
                }
            }

            await Logger.LogAsync($"[DownloadCacheService] Component '{component.Name}': {cachedResults.Count} file(s) exist, {urlsNeedingDownload.Count} URL(s) to download").ConfigureAwait(false);

            if (urlsNeedingDownload.Count > 0)
            {
                await Logger.LogAsync($"[DownloadCacheService] Starting download of {urlsNeedingDownload.Count} missing file(s) for component '{component.Name}'...").ConfigureAwait(false);

                var urlToProgressMap = new Dictionary<string, DownloadProgress>(StringComparer.Ordinal);

                foreach (string url in urlsNeedingDownload)
                {
                    var progressState = new DownloadProgress
                    {
                        ModName = component.Name,
                        Url = url,
                        Status = DownloadStatus.Pending,
                        StatusMessage = "Waiting to start...",
                        ProgressPercentage = 0,
                    };

                    ResourceMetadata cachedMeta = TryGetResourceMetadataByUrl(url);
                    if (cachedMeta != null && cachedMeta.Files != null && cachedMeta.Files.Count > 0)
                    {
                        // Use all filenames from resource-index
                        var cachedFilenames = cachedMeta.Files.Keys.ToList();
                        progressState.TargetFilenames = cachedFilenames;
                        await Logger.LogVerboseAsync($"[DownloadCacheService] Set target filename(s) from resource-index for {url}: {string.Join(", ", cachedFilenames)}").ConfigureAwait(false);
                    }
                    else
                    {
                        Dictionary<string, List<string>> resolved = await DownloadManager.ResolveUrlsToFilenamesAsync(new List<string> { url }, cancellationToken).ConfigureAwait(false);
                        if (resolved.TryGetValue(url, out List<string> allFilenames) && allFilenames.Count > 0)
                        {
                            List<string> filteredFilenames = resolutionFilter.FilterByResolution(allFilenames);

                            IReadOnlyList<string> targetFiles = GetFilenamesForDownload(component, url, filteredFilenames);

                            if (targetFiles.Count > 0)
                            {
                                progressState.TargetFilenames = targetFiles.ToList();
                                await Logger.LogVerboseAsync($"[DownloadCacheService] Set target filename(s) for {url}: {string.Join(", ", targetFiles)} (filtered by ModLinkFilenames)").ConfigureAwait(false);
                            }
                            else
                            {
                                string bestMatch = await _validationService.FindBestMatchingFilenameAsync(component, url, filteredFilenames).ConfigureAwait(false);

                                if (!string.IsNullOrWhiteSpace(bestMatch))
                                {
                                    progressState.TargetFilenames = new List<string> { bestMatch };
                                    await Logger.LogVerboseAsync($"[DownloadCacheService] Set target filename for {url}: {bestMatch} (pattern matched)").ConfigureAwait(false);
                                }
                                else if (filteredFilenames.Count > 0)
                                {
                                    progressState.TargetFilenames = filteredFilenames;
                                    await Logger.LogVerboseAsync($"[DownloadCacheService] No ModLinkFilenames or pattern match, will use all {filteredFilenames.Count} file(s) for {url}").ConfigureAwait(false);
                                }
                            }
                        }
                    }

                    urlToProgressMap[url] = progressState;
                }

                var progressForwarder = new Progress<DownloadProgress>(p =>
                {
                    if (urlToProgressMap.TryGetValue(p.Url, out DownloadProgress progressSnapshot))
                    {
                        progressSnapshot.Status = p.Status;
                        progressSnapshot.StatusMessage = p.StatusMessage;
                        progressSnapshot.ProgressPercentage = p.ProgressPercentage;
                        progressSnapshot.BytesDownloaded = p.BytesDownloaded;
                        progressSnapshot.TotalBytes = p.TotalBytes;
                        progressSnapshot.FilePath = p.FilePath;
                        progressSnapshot.StartTime = p.StartTime;
                        progressSnapshot.EndTime = p.EndTime;
                        progressSnapshot.ErrorMessage = p.ErrorMessage;
                        progressSnapshot.Exception = p.Exception;

                        if (progressSnapshot.Status == DownloadStatus.Pending ||
                             progressSnapshot.Status == DownloadStatus.Completed ||
                             progressSnapshot.Status == DownloadStatus.Failed)
                        {
                            Logger.LogVerbose($"[DownloadCache] {progressSnapshot.Status}: {progressSnapshot.StatusMessage}");
                        }
                        progress?.Report(progressSnapshot);
                    }
                });

                // Try optimized download with distributed cache first, then fall back to traditional download
                var downloadResults = new List<DownloadResult>();
                foreach (KeyValuePair<string, DownloadProgress> urlProgress in urlToProgressMap)
                {
                    string url = urlProgress.Key;
                    DownloadProgress progressEntry = urlProgress.Value;

                    // Look up ContentId from ResourceRegistry (computed during PreResolve)
                    string contentId = null;
                    // ResourceRegistry is now keyed by URL, so we can look it up directly
                    component.ResourceRegistry.TryGetValue(url, out ResourceMetadata resourceMeta);
                    if (resourceMeta != null && !string.IsNullOrEmpty(resourceMeta.ContentId))
                    {
                        contentId = resourceMeta.ContentId;
                        await Logger.LogVerboseAsync($"[DownloadCacheService] Using ContentId for optimized download: {contentId.Substring(0, Math.Min(16, contentId.Length))}...").ConfigureAwait(false);
                    }
                    else
                    {
                        await Logger.LogVerboseAsync($"[DownloadCacheService] No ContentId available for URL, optimizer will use URL hash").ConfigureAwait(false);
                    }

                    // Create traditional download function as fallback
                    Func<Task<DownloadResult>> traditionalDownload = async () =>
                    {
                        var singleUrlMap = new Dictionary<string, DownloadProgress>(StringComparer.Ordinal)
                        {
                            [url] = progressEntry,
                        };
                        List<DownloadResult> results = await DownloadManager.DownloadAllWithProgressAsync(
                            singleUrlMap,
                            destinationDirectory,
                            progressForwarder,
                            cancellationToken).ConfigureAwait(false);
                        return results.FirstOrDefault() ?? DownloadResult.Failed("No result returned");
                    };

                    // Try optimized download (uses distributed cache if available, falls back to traditional)
                    DownloadResult result = await DownloadCacheOptimizer.TryOptimizedDownload(
                        url,
                        destinationDirectory,
                        traditionalDownload,
                        progressForwarder,
                        cancellationToken,
                        contentId).ConfigureAwait(false);

                    // Set DownloadSource on the progress entry
                    if (result.Success && progressEntry != null)
                    {
                        progressEntry.DownloadSource = result.DownloadSource;
                    }

                    downloadResults.Add(result);
                }

                int successCount = 0;
                int failCount = 0;

                for (int i = 0; i < downloadResults.Count && i < urlsNeedingDownload.Count; i++)
                {
                    DownloadResult result = downloadResults[i];
                    string originalUrl = urlsNeedingDownload[i];

                    if (!result.Success)
                    {
                        failCount++;
                        await Logger.LogErrorAsync($"[DownloadCacheService] Download FAILED for component '{component.Name}': {originalUrl}").ConfigureAwait(false);
                        await Logger.LogErrorAsync($"  Error: {result.Message ?? "Unknown error"}").ConfigureAwait(false);

                        string errorMsg = result.Message
                                          ?? "Unknown error";
                        RecordFailure(originalUrl, component.Name, expectedFileName: null, errorMsg, DownloadFailureInfo.FailureType.DownloadFailed);

                        continue;
                    }

                    string fileName = !string.IsNullOrEmpty(result.FilePath) ? Path.GetFileName(result.FilePath) : string.Empty;
                    if (string.IsNullOrEmpty(fileName))
                    {
                        failCount++;
                        await Logger.LogErrorAsync($"[DownloadCacheService] Download result has empty filename for component '{component.Name}': {originalUrl}").ConfigureAwait(false);
                        RecordFailure(originalUrl, component.Name, expectedFileName: null, "Download returned empty filename", DownloadFailureInfo.FailureType.DownloadFailed);
                        continue;
                    }

                    await Logger.LogVerboseAsync($"[DownloadCacheService] Downloaded file: {fileName}").ConfigureAwait(false);

                    bool isArchive = ArchiveHelper.IsArchive(fileName);

                    if (MainConfig.ValidateAndReplaceInvalidArchives && isArchive && !string.IsNullOrEmpty(result.FilePath))
                    {
                        await Logger.LogVerboseAsync($"[DownloadCacheService] Validating downloaded archive: {result.FilePath}").ConfigureAwait(false);
                        bool isValid = true;

                        if (!isValid)
                        {
                            failCount++;
                            await Logger.LogWarningAsync($"[DownloadCacheService] Downloaded archive is corrupt: {result.FilePath}").ConfigureAwait(false);
                            try
                            {
                                File.Delete(result.FilePath);
                                await Logger.LogVerboseAsync($"[DownloadCacheService] Deleted invalid download: {result.FilePath}").ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                await Logger.LogErrorAsync($"[DownloadCacheService] Error deleting invalid download: {ex.Message}").ConfigureAwait(false);
                            }
                            continue;
                        }
                    }

                    var newEntry = new DownloadCacheEntry
                    {
                        Url = originalUrl,
                        FileName = fileName,
                        IsArchiveFile = isArchive,
                    };

                    cachedResults.Add(newEntry);
                    successCount++;

                    // Post-download: Compute INTEGRITY hashes (ContentId already exists from PreResolve!)
                    try
                    {
                        if (File.Exists(result.FilePath))
                        {
                            await Logger.LogVerboseAsync($"[Cache] Computing file integrity data for: {result.FilePath}").ConfigureAwait(false);

                            (string contentHashSHA256, int pieceLength, string pieceHashes) =
                                await DownloadCacheOptimizer.ComputeFileIntegrityData(result.FilePath).ConfigureAwait(false);

                            await Logger.LogVerboseAsync($"[Cache] ContentHashSHA256: {contentHashSHA256}...").ConfigureAwait(false);
                            await Logger.LogVerboseAsync($"[Cache] PieceLength: {pieceLength}, Pieces: {pieceHashes.Length / 40}").ConfigureAwait(false);

                            // Find existing ResourceMetadata by URL (should already have ContentId from PreResolve)
                            ResourceMetadata resourceMeta = null;
                            string metadataHash = null;

                            // Try to find metadata from component's ResourceRegistry
                            foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry)
                            {
                                if (string.Equals(kvp.Key, originalUrl, StringComparison.Ordinal))
                                {
                                    resourceMeta = kvp.Value;
                                    metadataHash = kvp.Key;
                                    // Update with integrity data (ContentId should already exist!)
                                    resourceMeta.ContentHashSHA256 = contentHashSHA256;
                                    resourceMeta.PieceLength = pieceLength;
                                    resourceMeta.PieceHashes = pieceHashes;
                                    resourceMeta.FileSize = new FileInfo(result.FilePath).Length;
                                    resourceMeta.LastVerified = DateTime.UtcNow;

                                    // VERIFY INTEGRITY: Actually use the integrity data we computed!
                                    bool integrityValid = await DownloadCacheOptimizer.VerifyContentIntegrity(result.FilePath, resourceMeta).ConfigureAwait(false);
                                    if (!integrityValid)
                                    {
                                        await Logger.LogErrorAsync($"[Cache] INTEGRITY VERIFICATION FAILED for downloaded file: {fileName}").ConfigureAwait(false);
                                        await Logger.LogErrorAsync($"[Cache] File may be corrupted - download may need to be retried").ConfigureAwait(false);
                                        // Continue anyway - the file exists and might be usable, but log the warning
                                    }
                                    else
                                    {
                                        await Logger.LogVerboseAsync($"[Cache] ✓ Integrity verification passed for: {fileName}").ConfigureAwait(false);
                                    }

                                    // Add filename to Files dictionary
                                    if (!resourceMeta.Files.ContainsKey(fileName))
                                    {
                                        resourceMeta.Files[fileName] = true;
                                    }

                                    // Log ContentId if available
                                    if (!string.IsNullOrEmpty(resourceMeta.ContentId))
                                    {
                                        await Logger.LogVerboseAsync($"[Cache] ContentId: {resourceMeta.ContentId}...").ConfigureAwait(false);

                                        // Update mapping with verification (ContentId should already exist)
                                        bool updated = await UpdateMappingWithVerification(metadataHash, resourceMeta.ContentId, resourceMeta).ConfigureAwait(false);

                                        if (updated)
                                        {
                                            await Logger.LogVerboseAsync($"[Cache] Updated mapping: {metadataHash}... → {resourceMeta.ContentId}...").ConfigureAwait(false);

                                            // Save updated resource index
                                            await SaveResourceIndexAsync().ConfigureAwait(false);
                                            await Logger.LogVerboseAsync("[Cache] Saved updated resource index").ConfigureAwait(false);
                                        }
                                    }
                                    else
                                    {
                                        await Logger.LogWarningAsync($"[Cache] ContentId missing for URL (PreResolve may have failed): {originalUrl}").ConfigureAwait(false);
                                    }
                                }
                            }
                        }

                        await Logger.LogAsync($"[DownloadCacheService] ✓ Downloaded successfully: {fileName}").ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogWarningAsync($"[Cache] Failed to compute file integrity data: {ex.Message}").ConfigureAwait(false);
                    }
                }

                await Logger.LogAsync($"[DownloadCacheService] Download results for '{component.Name}': {successCount} succeeded, {failCount} failed").ConfigureAwait(false);
            }

            // Note: VFS re-simulation removed per architecture requirements.
            // Downloads are validated through wildcard pattern matching in AnalyzeDownloadNecessityAsync instead.

            await EnsureInstructionsExist(component, cachedResults, cancellationToken).ConfigureAwait(false);
            return cachedResults;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private async Task EnsureInstructionsExist(
            ModComponent component,
            List<DownloadCacheEntry> entries,
            CancellationToken cancellationToken
        )
        {
            await Logger.LogVerboseAsync($"[DownloadCacheService] Ensuring instructions exist for {entries.Count} cached entries").ConfigureAwait(false);

            foreach (DownloadCacheEntry entry in entries)
            {
                string archiveFullPath = $@"<<modDirectory>>\{entry.FileName}";
                Instruction existingInstruction = null;
                Instruction mismatchedInstruction = null;

                foreach (Instruction instruction in component.Instructions)
                {
                    if (instruction.Source != null && instruction.Source.Any(src =>
                        {
                            if (src.IndexOf(entry.FileName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {

                                return true;
                            }


                            try
                            {
                                if (FileSystemUtils.PathHelper.WildcardPathMatch(archiveFullPath, src))
                                {

                                    return true;
                                }

                            }
                            catch (Exception ex)
                            {
                                Logger.LogException(ex);
                            }

                            return _validationService.GetVirtualFileSystem().FileExists(ResolveInstructionSource(src, entry.FileName));
                        }))
                    {
                        existingInstruction = instruction;
                        break;
                    }

                    // Check for Extract instructions with mismatched archive names
                    if (instruction.Action == Instruction.ActionType.Extract &&
                         instruction.Source != null && instruction.Source.Count > 0)
                    {
                        string instructionArchiveName = ExtractFilenameFromSource(instruction.Source[0]);
                        if (!string.IsNullOrEmpty(instructionArchiveName) &&
                             !instructionArchiveName.Equals(entry.FileName, StringComparison.OrdinalIgnoreCase) &&
                             entry.IsArchiveFile)
                        {
                            // Check if this could be the same mod with different filename
                            string instructionBaseName = Path.GetFileNameWithoutExtension(instructionArchiveName);
                            string entryBaseName = Path.GetFileNameWithoutExtension(entry.FileName);

                            // Enhanced similarity check with multiple strategies
                            double similarityScore = CalculateArchiveNameSimilarity(instructionBaseName, entryBaseName);
                            const double SIMILARITY_THRESHOLD = 0.7; // 70% similarity required

                            if (similarityScore >= SIMILARITY_THRESHOLD)
                            {
                                mismatchedInstruction = instruction;
                                await Logger.LogVerboseAsync($"[DownloadCacheService] Detected archive name mismatch (similarity: {similarityScore:P0}): instruction expects '{instructionArchiveName}' but downloaded '{entry.FileName}'").ConfigureAwait(false);
                                break;
                            }
                        }
                    }
                }

                // Handle mismatched archive name
                if (mismatchedInstruction != null && existingInstruction is null)
                {
                    await Logger.LogAsync($"[DownloadCacheService] Attempting to fix archive name mismatch for component '{component.Name}'").ConfigureAwait(false);
                    bool fixSuccess = await TryFixArchiveNameMismatchAsync(component, mismatchedInstruction, entry, cancellationToken).ConfigureAwait(false);

                    if (fixSuccess)
                    {
                        existingInstruction = mismatchedInstruction;
                        await Logger.LogAsync($"[DownloadCacheService] ✓ Successfully updated instructions to use '{entry.FileName}'").ConfigureAwait(false);
                    }
                    else
                    {
                        await Logger.LogWarningAsync("[DownloadCacheService] Failed to fix archive name mismatch, will create new instruction").ConfigureAwait(false);
                    }
                }

                if (existingInstruction != null)
                {
                    await Logger.LogVerboseAsync($"[DownloadCacheService] Found existing instruction for {entry.FileName} (GUID: {existingInstruction.ActionString + "|" + existingInstruction.Destination})").ConfigureAwait(false);
                }
                else
                {
                    if (!MainConfig.EditorMode)
                    {
                        await Logger.LogVerboseAsync($"[DownloadCacheService] EditorMode disabled; skipping auto instruction creation for '{entry.FileName}'").ConfigureAwait(false);
                        continue;
                    }

                    int initialInstructionCount = component.Instructions.Count;

                    string entryFilePath = !string.IsNullOrEmpty(entry.FileName) && MainConfig.SourcePath != null
                        ? Path.Combine(MainConfig.SourcePath.FullName, entry.FileName)
                        : entry.FileName;

                    if (entry.IsArchiveFile && !string.IsNullOrEmpty(entryFilePath) && File.Exists(entryFilePath))
                    {
                        // Only auto-generate instructions in EditorMode
                        bool generated = false;
                        if (MainConfig.EditorMode)
                        {
                            generated = AutoInstructionGenerator.GenerateInstructions(component, entryFilePath);
                        }

                        if (!File.Exists(entryFilePath))
                        {
                            await Logger.LogWarningAsync($"[DownloadCacheService] Archive was deleted (likely corrupted): {entryFilePath}").ConfigureAwait(false);
                            await Logger.LogWarningAsync("[DownloadCacheService] Creating placeholder instruction").ConfigureAwait(false);

                            CreateSimpleInstructionForEntry(component, entry);
                            continue;
                        }

                        if (generated)
                        {
                            await Logger.LogVerboseAsync($"[DownloadCacheService] Auto-generated comprehensive instructions for {entry.FileName}").ConfigureAwait(false);

                            Instruction newInstruction = null;
                            for (int j = initialInstructionCount; j < component.Instructions.Count; j++)
                            {
                                Instruction instr = component.Instructions[j];
                                if (instr.Source != null && instr.Source.Count > 0)
                                {
                                    foreach (string s in instr.Source)
                                    {
                                        if (s.IndexOf(entry.FileName, StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            await Logger.LogAsync($"[DownloadCacheService] Found exact match existing instruction for {entry.FileName}: {instr.ActionString + "|" + instr.Destination}").ConfigureAwait(false);
                                            newInstruction = instr;
                                            break;
                                        }
                                        try
                                        {
                                            List<string> resolvedFiles = FileSystemUtils.PathHelper.EnumerateFilesWithWildcards(
                                                new List<string> { s },
                                                _validationService.GetVirtualFileSystem()
                                            );
                                            if (resolvedFiles.Exists(f =>
                                                 string.Equals(
                                                    Path.GetFileName(f),
                                                    entry.FileName,
                                                    StringComparison.OrdinalIgnoreCase)))
                                            {
                                                await Logger.LogAsync($"[DownloadCacheService] EnumerateFilesWithWildcards found matching instruction for {entry.FileName}: {instr.ActionString + "|" + instr.Destination}").ConfigureAwait(false);
                                                newInstruction = instr;
                                                break;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                                        }
                                    }
                                }
                                if (newInstruction != null)
                                {
                                    break;
                                }
                            }

                            if (newInstruction is null)
                            {

                                throw new InvalidOperationException($"No existing instruction found for {entry.FileName}");
                            }


                            await Logger.LogVerboseAsync($"[DownloadCacheService] Found existing instruction for {entry.FileName} {newInstruction.ActionString + "|" + newInstruction.Destination}").ConfigureAwait(false);
                        }
                        else
                        {
                            CreateSimpleInstructionForEntry(component, entry);
                        }
                    }
                    else
                    {
                        CreateSimpleInstructionForEntry(component, entry);
                    }
                }

            }
        }

        private static void CreateSimpleInstructionForEntry(ModComponent component, DownloadCacheEntry entry)
        {
            var newInstruction = new Instruction
            {
                Action = entry.IsArchiveFile
                     ? Instruction.ActionType.Extract
                     : Instruction.ActionType.Move,
                Source = new List<string> { $@"<<modDirectory>>\{entry.FileName}" },
                Destination = entry.IsArchiveFile
                          ? string.Empty
                          : @"<<kotorDirectory>>\Override",
                Overwrite = true,
            };
            newInstruction.SetParentComponent(component);
            component.Instructions.Add(newInstruction);

            Logger.LogWarning($"[DownloadCacheService] Created placeholder {newInstruction.Action} instruction for {entry.FileName} due to file not being downloaded yet.");
        }

        private static string ResolveInstructionSource(string sourcePath, string archiveName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath;
            }


            string resolved = sourcePath.Replace("<<modDirectory>>", "");

            if (resolved.Contains(archiveName))
            {

                return resolved;
            }


            return Path.Combine(resolved.TrimStart(new[] { '\\' }), archiveName);
        }

        /// <summary>
        /// Gets the cache directory path.
        /// </summary>
        private static string GetCacheDirectory()
        {
            string appDataPath;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else
            {
                string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                appDataPath = Path.Combine(homeDir, ".local", "share");
            }

            string cacheDir = Path.Combine(appDataPath, "KOTORModSync");

            if (!Directory.Exists(cacheDir))
            {
                try
                {
                    Directory.CreateDirectory(cacheDir);
                    Logger.LogVerbose($"[DownloadCacheService] Created cache directory: {cacheDir}");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[DownloadCacheService] Failed to create cache directory: {ex.Message}");
                }
            }

            return cacheDir;
        }

        private async Task<(List<string> urls, bool simulationFailed)> AnalyzeDownloadNecessityWithStatusAsync(
            ModComponent component,
            string modSourceDirectory,
            CancellationToken cancellationToken)
        {
            (List<string> urls, bool simulationFailed) = await _validationService.AnalyzeDownloadNecessityAsync(component, modSourceDirectory, cancellationToken).ConfigureAwait(false);
            return (urls, simulationFailed);
        }

        private static string ResolveSourceFilePath(string fileName, string modSourceDirectory, string destinationDirectory)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }

            string normalizedName = fileName.Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(normalizedName))
            {
                return normalizedName;
            }

            string baseDirectory = MainConfig.SourcePath?.FullName;
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = modSourceDirectory;
            }

            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = destinationDirectory;
            }

            if (string.IsNullOrEmpty(baseDirectory))
            {
                return Path.GetFullPath(normalizedName);
            }

            return Path.Combine(baseDirectory, normalizedName);
        }

        private static string ExtractFilenameFromSource(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {

                return string.Empty;
            }


            string cleanedPath = sourcePath.Replace("<<modDirectory>>\\", "").Replace("<<modDirectory>>/", "");

            string filename = Path.GetFileName(cleanedPath);

            return filename;
        }

        private static string NormalizeModName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {

                return string.Empty;
            }

            // Remove common version patterns, special characters, normalize spacing (safe expressions)

            string normalized = name.ToLowerInvariant();

            // Define a short timeout to prevent DoS via catastrophic regex backtracking
            var regexTimeout = TimeSpan.FromMilliseconds(100);

            // Replace underscores, hyphens, and multiple spaces (no catastrophic backtracking possible here)
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"[_\-\s]+",
                " ",
                System.Text.RegularExpressions.RegexOptions.Compiled,
                regexTimeout
            );

            // Remove "v" prefix if directly followed by a number at word boundaries to avoid catastrophic regex on odd input
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"\bv\d+(\.\d+)*",
                "",
                System.Text.RegularExpressions.RegexOptions.Compiled,
                regexTimeout
            );

            // Remove standalone version numbers (we do not attempt to match extremely complex version patterns to avoid catastrophic backtracking)
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"\b\d+(\.\d+)*\b",
                "",
                System.Text.RegularExpressions.RegexOptions.Compiled,
                regexTimeout
            );

            // Remove all non-word, non-space characters (safe pattern)
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"[^\w\s]",
                "",
                System.Text.RegularExpressions.RegexOptions.Compiled,
                regexTimeout
            );

            normalized = normalized.Trim();

            return normalized;
        }

        /// <summary>
        /// Calculates similarity score between two archive names using multiple strategies.
        /// Returns a value between 0.0 (completely different) and 1.0 (identical).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static double CalculateArchiveNameSimilarity(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
            {
                return 0.0;
            }

            // Strategy 1: Exact match

            if (name1.Equals(name2, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            // Strategy 2: Substring containment (one contains the other)

            string lower1 = name1.ToLowerInvariant();
            string lower2 = name2.ToLowerInvariant();
            if (lower1.Contains(lower2) || lower2.Contains(lower1))
            {
                return 0.95;
            }

            // Strategy 3: Normalized name comparison (removes versions, special chars)

            string normalized1 = NormalizeModName(name1);
            string normalized2 = NormalizeModName(name2);
            if (!string.IsNullOrEmpty(normalized1) && normalized1.Equals(normalized2, StringComparison.OrdinalIgnoreCase))
            {

                return 0.90;
            }

            // Strategy 4: Token-based similarity (split on common delimiters)

            var tokens1 = new HashSet<string>(
                System.Text.RegularExpressions.Regex.Split(
                    lower1,
                    @"[\s\-_\.]+",
                    System.Text.RegularExpressions.RegexOptions.Compiled,
                    TimeSpan.FromMilliseconds(250)
                )
                    .Where(t => t.Length > 2), // Ignore very short tokens
                StringComparer.OrdinalIgnoreCase
            );
            var tokens2 = new HashSet<string>(
                System.Text.RegularExpressions.Regex.Split(
                    lower2,
                    @"[\s\-_\.]+",
                    System.Text.RegularExpressions.RegexOptions.Compiled,
                    TimeSpan.FromMilliseconds(250)
                )
                    .Where(t => t.Length > 2),
                StringComparer.OrdinalIgnoreCase
            );

            if (tokens1.Count > 0 && tokens2.Count > 0)
            {
                int commonTokens = tokens1.Intersect(tokens2, StringComparer.Ordinal).Count();
                int totalTokens = tokens1.Union(tokens2, StringComparer.Ordinal).Count();
                double tokenSimilarity = (double)commonTokens / totalTokens;

                if (tokenSimilarity >= 0.5) // At least 50% tokens in common
                {

                    return 0.75 + (tokenSimilarity * 0.15); // 0.75-0.90 range
                }

            }

            // Strategy 5: Levenshtein distance ratio for fuzzy matching
            int distance = CalculateLevenshteinDistance(normalized1, normalized2);
            int maxLength = Math.Max(normalized1?.Length ?? 0, normalized2?.Length ?? 0);
            if (maxLength > 0)
            {
                double distanceRatio = 1.0 - ((double)distance / maxLength);
                if (distanceRatio >= 0.7)
                {

                    return distanceRatio * 0.85; // Scale down slightly as it's less reliable
                }

            }

            // Strategy 6: Longest common substring ratio
            int lcsLength = CalculateLongestCommonSubstringLength(lower1, lower2);
            int minLength = Math.Min(lower1.Length, lower2.Length);
            if (minLength > 0)
            {
                double lcsRatio = (double)lcsLength / minLength;
                if (lcsRatio >= 0.6)
                {

                    return lcsRatio * 0.8; // Scale to 0.48-0.80 range
                }

            }

            // No good match found
            return 0.0;
        }

        /// <summary>
        /// Calculates the Levenshtein distance (edit distance) between two strings.
        /// Returns the minimum number of single-character edits required to change one string into the other.
        /// </summary>
        private static int CalculateLevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1))
            {
                return s2?.Length ?? 0;
            }


            if (string.IsNullOrEmpty(s2))
            {

                return s1.Length;
            }


            int len1 = s1.Length;
            int len2 = s2.Length;
            int[,] matrix = new int[len1 + 1, len2 + 1];

            // Initialize first column and row
            for (int i = 0; i <= len1; i++)
            {
                matrix[i, 0] = i;
            }

            for (int j = 0; j <= len2; j++)
            {
                matrix[0, j] = j;
            }

            // Calculate distances

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost
                    );
                }
            }

            return matrix[len1, len2];
        }

        /// <summary>
        /// Calculates the length of the longest common substring between two strings.
        /// Used to find the largest contiguous matching sequence.
        /// </summary>
        private static int CalculateLongestCommonSubstringLength(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            {
                return 0;
            }


            int maxLength = 0;
            int[,] lengths = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    if (char.ToLowerInvariant(s1[i - 1]) == char.ToLowerInvariant(s2[j - 1]))
                    {
                        lengths[i, j] = lengths[i - 1, j - 1] + 1;
                        maxLength = Math.Max(maxLength, lengths[i, j]);
                    }
                }
            }

            return maxLength;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task<bool> TryFixArchiveNameMismatchAsync(
            ModComponent component,
            Instruction extractInstruction,
            DownloadCacheEntry entry,
            CancellationToken cancellationToken)
        {
            if (extractInstruction is null || entry is null || !entry.IsArchiveFile)
            {
                return false;
            }


            string oldArchiveName = ExtractFilenameFromSource(extractInstruction.Source[0]);
            string newArchiveName = entry.FileName;

            if (string.IsNullOrEmpty(oldArchiveName) || string.IsNullOrEmpty(newArchiveName))
            {

                return false;
            }


            await Logger.LogVerboseAsync($"[DownloadCacheService] Attempting to update archive name from '{oldArchiveName}' to '{newArchiveName}'").ConfigureAwait(false);

            // Store original instruction state for rollback (using indices since instructions don't have GUIDs)
            var originalInstructions =
                new List<(Instruction instruction, List<string> source, string destination)>();

            try
            {
                // Step 1: Backup all instruction states (component + options)
                foreach (Instruction instruction in component.Instructions)
                {
                    originalInstructions.Add((
                        instruction,
                        new List<string>(instruction.Source),
                        instruction.Destination
                    ));
                }

                foreach (Option option in component.Options)
                {
                    foreach (Instruction instruction in option.Instructions)
                    {
                        originalInstructions.Add((
                            instruction,
                            new List<string>(instruction.Source),
                            instruction.Destination
                        ));
                    }
                }

                // Step 2: Update Extract instruction
                string oldExtractedFolderName = Path.GetFileNameWithoutExtension(oldArchiveName);
                string newExtractedFolderName = Path.GetFileNameWithoutExtension(newArchiveName);

                // Instead of assigning to Source[0] (IReadOnlyList), assign a new List with the updated value
                var newExtractSource = new List<string>(extractInstruction.Source);
                if (newExtractSource.Count > 0)
                {
                    newExtractSource[0] = $@"<<modDirectory>>\{newArchiveName}";
                }
                else
                {
                    newExtractSource.Add($@"<<modDirectory>>\{newArchiveName}");
                }


                extractInstruction.Source = newExtractSource;

                await Logger.LogVerboseAsync($"[DownloadCacheService] Updated Extract instruction source to: {extractInstruction.Source[0]}").ConfigureAwait(false);

                // Step 3: Update subsequent instructions that reference the old extracted folder
                int updatedInstructionCount = 0;

                // Update component instructions
                foreach (Instruction instruction in component.Instructions)
                {
                    if (ReferenceEquals(instruction, extractInstruction))
                    {
                        continue;
                    }


                    if (instruction.Source.Count == 0)
                    {
                        continue;
                    }


                    bool updated = false;
                    var newSource = new List<string>(instruction.Source);

                    for (int i = 0; i < newSource.Count; i++)
                    {
                        string src = newSource[i];
                        if (src.IndexOf(oldExtractedFolderName, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        // Replace the old folder name with the new one
                        // Replace using a fast, safe, non-regex approach to prevent ReDoS

                        string updatedSrc = src;
                        int index = updatedSrc.IndexOf(oldExtractedFolderName, StringComparison.OrdinalIgnoreCase);
                        while (index >= 0)
                        {
                            updatedSrc = updatedSrc.Substring(0, index) + newExtractedFolderName + updatedSrc.Substring(index + oldExtractedFolderName.Length);
                            index = updatedSrc.IndexOf(oldExtractedFolderName, index + newExtractedFolderName.Length, StringComparison.OrdinalIgnoreCase);
                        }

                        newSource[i] = updatedSrc;
                        updated = true;
                        await Logger.LogVerboseAsync($"[DownloadCacheService] Updated instruction source: {src} -> {updatedSrc}").ConfigureAwait(false);
                    }
                    if (updated)
                    {
                        instruction.Source = newSource;
                        updatedInstructionCount++;
                    }
                }

                // Update option instructions
                foreach (Option option in component.Options)
                {
                    foreach (Instruction instruction in option.Instructions)
                    {
                        if (instruction.Source.Count == 0)
                        {
                            continue;
                        }


                        bool updated = false;
                        var newSource = new List<string>(instruction.Source);
                        for (int i = 0; i < newSource.Count; i++)
                        {
                            string src = newSource[i];
                            if (src.IndexOf(oldExtractedFolderName, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }

                            // Safe, fast, non-regex, case-insensitive string replacement (prevents ReDoS)

                            string updatedSrc = src;
                            int index = updatedSrc.IndexOf(oldExtractedFolderName, StringComparison.OrdinalIgnoreCase);
                            while (index >= 0)
                            {
                                updatedSrc = updatedSrc.Substring(0, index) + newExtractedFolderName + updatedSrc.Substring(index + oldExtractedFolderName.Length);
                                index = updatedSrc.IndexOf(oldExtractedFolderName, index + newExtractedFolderName.Length, StringComparison.OrdinalIgnoreCase);
                            }

                            newSource[i] = updatedSrc;
                            updated = true;
                            await Logger.LogVerboseAsync($"[DownloadCacheService] Updated option instruction source: {src} -> {updatedSrc}").ConfigureAwait(false);
                        }
                        if (updated)
                        {
                            instruction.Source = newSource;
                            updatedInstructionCount++;
                        }
                    }
                }

                await Logger.LogVerboseAsync($"[DownloadCacheService] Updated {updatedInstructionCount} instruction(s) to reference new archive name").ConfigureAwait(false);

                // Step 4: Validate changes with VFS simulation
                string modSourceDirectory = MainConfig.SourcePath?.FullName;
                if (!string.IsNullOrEmpty(modSourceDirectory))
                {
                    await Logger.LogVerboseAsync("[DownloadCacheService] Validating instruction changes via VFS simulation...").ConfigureAwait(false);

                    var vfs = new VirtualFileSystemProvider();
                    await vfs.InitializeFromRealFileSystemAsync(modSourceDirectory).ConfigureAwait(false);

                    try
                    {
                        ModComponent.InstallExitCode exitCode = await component.ExecuteInstructionsAsync(
                            component.Instructions,
                            new List<ModComponent>(),
                            cancellationToken,
                            vfs,
                            skipDependencyCheck: true
                        ).ConfigureAwait(false);

                        List<ValidationIssue> issues = vfs.GetValidationIssues();
                        var criticalIssues = issues.Where(i =>
                            i.Severity == ValidationSeverity.Error ||
                            i.Severity == ValidationSeverity.Critical
                        ).ToList();

                        if (exitCode == ModComponent.InstallExitCode.Success && criticalIssues.Count == 0)
                        {
                            await Logger.LogVerboseAsync("[DownloadCacheService] ✓ VFS simulation passed - changes are valid").ConfigureAwait(false);
                            return true;
                        }

                        await Logger.LogWarningAsync($"[DownloadCacheService] VFS simulation failed with {criticalIssues.Count} critical issue(s), exit code: {exitCode}").ConfigureAwait(false);

                        // Log a few issues for debugging
                        foreach (ValidationIssue issue in criticalIssues.Take(3))
                        {
                            await Logger.LogVerboseAsync($"  • {issue.Category}: {issue.Message}").ConfigureAwait(false);
                        }

                        // Rollback changes
                        await RollbackInstructionChanges(originalInstructions, cancellationToken).ConfigureAwait(false);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        await Logger.LogWarningAsync($"[DownloadCacheService] VFS simulation threw exception: {ex.Message}").ConfigureAwait(false);

                        // Rollback changes
                        await RollbackInstructionChanges(originalInstructions, cancellationToken).ConfigureAwait(false);
                        return false;
                    }
                }

                // No VFS available, accept changes optimistically
                await Logger.LogVerboseAsync("[DownloadCacheService] No mod directory configured, accepting changes without validation").ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex, "[DownloadCacheService] Error during archive name mismatch fix").ConfigureAwait(false);

                // Rollback changes
                await RollbackInstructionChanges(originalInstructions, cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        private static async Task RollbackInstructionChanges(
            List<(Instruction instruction, List<string> source, string destination)> originalState,
            CancellationToken cancellationToken = default)
        {
            await Logger.LogVerboseAsync("[DownloadCacheService] Rolling back instruction changes...").ConfigureAwait(false);

            int rollbackCount = 0;

            // Rollback instructions by reference
            foreach ((Instruction instruction, List<string> source, string destination) in originalState)
            {
                instruction.Source = new List<string>(source);
                instruction.Destination = destination;
                rollbackCount++;
                await Logger.LogVerboseAsync($"[DownloadCacheService] Rolled back instruction: {instruction.Action} {instruction.Source[0]} -> {instruction.Destination}").ConfigureAwait(false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            await Logger.LogVerboseAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[DownloadCacheService] Rolled back {0} instruction(s) to original state", rollbackCount)).ConfigureAwait(false);
        }

        public sealed class DownloadCacheEntry
        {
            public string Url { get; set; }

            public string FileName { get; set; }

            public bool IsArchiveFile { get; set; }

            public override string ToString() =>
                $"DownloadCacheEntry[FileName={FileName}, IsArchive={IsArchiveFile}]";
        }

        public sealed class DownloadFailureInfo
        {
            public string Url { get; set; }
            public string ComponentName { get; set; }
            public string ExpectedFileName { get; set; }
            public string ErrorMessage { get; set; }
            public FailureType Type { get; set; }
            public List<string> LogContext { get; set; }

            public enum FailureType
            {
                DownloadFailed,
                FileNotFound,
                ResolutionFailed,
            }
        }

        public IReadOnlyList<DownloadFailureInfo> GetFailures()
        {
            lock (_failureLock)
            {
                return _failedDownloads.Values.ToList();
            }

        }

        private void RecordFailure(
            string url,
            string componentName,
            string expectedFileName,
            string errorMessage, DownloadFailureInfo.FailureType type)
        {
            lock (_failureLock)
            {
                if (_failedDownloads.ContainsKey(url))
                {
                    return;
                }
                // Capture recent log messages for context (last 30 messages)

                List<string> logContext = Logger.GetRecentLogMessages(30);

                _failedDownloads[url] = new DownloadFailureInfo
                {
                    Url = url,
                    ComponentName = componentName,
                    ExpectedFileName = expectedFileName,
                    ErrorMessage = errorMessage,
                    Type = type,
                    LogContext = logContext,
                };
            }
        }

        private async Task PopulateModLinkFilenamesWithSimulationAsync(
            ModComponent component,
            string url,
            List<string> allFilenames,
            string modSourceDirectory)
        {
            if (component is null || string.IsNullOrWhiteSpace(url) || allFilenames is null || allFilenames.Count == 0)
            {
                return;
            }

            // Ensure ResourceRegistry has an entry for this URL with Files dictionary
            if (!component.ResourceRegistry.TryGetValue(url, out ResourceMetadata resourceMeta))
            {
                resourceMeta = new ResourceMetadata
                {
                    Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                    HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal),
                };
                resourceMeta.HandlerMetadata["PrimaryUrl"] = url;
                component.ResourceRegistry[url] = resourceMeta;
            }
            if (resourceMeta.Files == null)
            {
                resourceMeta.Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (string filename in allFilenames.Where(filename => !string.IsNullOrWhiteSpace(filename)))
            {
                // If filename already exists with explicit value, don't override
                if (resourceMeta.Files.TryGetValue(filename, out bool? shouldDownload) && shouldDownload.HasValue)
                {
                    continue;
                }

                // If filename exists but is null, test if needed by instructions using VFS
                // Only do VFS simulation if modSourceDirectory is available

                bool isNeeded = false;
                if (!string.IsNullOrEmpty(modSourceDirectory))
                {
                    isNeeded = await _validationService.TestFilenameNeededByInstructionsAsync(component, filename, modSourceDirectory).ConfigureAwait(false);
                }

                // null = default/auto-discover (will be set to true after instruction generation)
                // If instructions are generated for this file, set to true, otherwise leave as null
                if (isNeeded)
                {
                    resourceMeta.Files[filename] = true;
                    await Logger.LogVerboseAsync($"[DownloadCacheService] Added filename '{filename}' for URL '{url}' (shouldDownload=true, matched instructions)").ConfigureAwait(false);
                }
                else
                {
                    // Set to null to indicate "not yet tested" or "no instructions yet"
                    // Always add the filename to ResourceMetadata.Files so it's persisted to serialized component file
                    resourceMeta.Files[filename] = null;
                    await Logger.LogVerboseAsync($"[DownloadCacheService] Added filename '{filename}' for URL '{url}' (shouldDownload=null, no matching instructions or VFS not available)").ConfigureAwait(false);
                }
            }

            int enabledCount = resourceMeta.Files.Count(f => f.Value == true);
            int nullCount = resourceMeta.Files.Count(f => !f.Value.HasValue);
            await Logger.LogVerboseAsync($"[DownloadCacheService] Populated ResourceMetadata.Files for '{component.Name}': {url} -> {resourceMeta.Files.Count} file(s), {enabledCount} enabled, {nullCount} auto-discover").ConfigureAwait(false);
        }

        /// <summary>
        /// Populates ModLinkFilenames for a URL, ensuring all resolved filenames are included
        /// even when modSourceDirectory is not available.
        /// </summary>
        private async Task PopulateModLinkFilenamesForUrlAsync(
            ModComponent component,
            string url,
            List<string> allFilenames,
            string modSourceDirectory)
        {
            if (component is null || string.IsNullOrWhiteSpace(url) || allFilenames is null || allFilenames.Count == 0)
            {
                return;
            }

            // Always populate ResourceMetadata.Files - use VFS simulation if available, otherwise just add filenames

            if (!string.IsNullOrEmpty(modSourceDirectory))
            {
                await PopulateModLinkFilenamesWithSimulationAsync(component, url, allFilenames, modSourceDirectory).ConfigureAwait(false);
            }
            else
            {
                // No source directory configured - just add all filenames with null (auto-discover)
                // This handles components with loose files or when SourcePath isn't set
                if (!component.ResourceRegistry.TryGetValue(url, out ResourceMetadata resourceMeta))
                {
                    resourceMeta = new ResourceMetadata
                    {
                        Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                        HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal),
                    };
                    resourceMeta.HandlerMetadata["PrimaryUrl"] = url;
                    component.ResourceRegistry[url] = resourceMeta;
                }

                foreach (string filename in allFilenames.Where(filename => !string.IsNullOrWhiteSpace(filename)))
                {
                    // Only add if not already present with explicit value
                    if (!resourceMeta.Files.ContainsKey(filename))
                    {
                        resourceMeta.Files[filename] = null; // null = auto-discover
                        await Logger.LogVerboseAsync($"[DownloadCacheService] Added filename '{filename}' for URL '{url}' (shouldDownload=null, source directory not configured)").ConfigureAwait(false);
                    }
                }

                await Logger.LogVerboseAsync($"[DownloadCacheService] Populated ModLinkFilenames for '{component.Name}': {url} -> {resourceMeta.Files.Count} file(s) (source directory not configured)").ConfigureAwait(false);
            }

            // CRITICAL: Also update ResourceRegistry to keep it in sync with ModLinkFilenames
            await UpdateResourceMetadataWithFilenamesAsync(component, url, allFilenames).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates ResourceMetadata with resolved filenames during PreResolve phase.
        /// Creates ResourceMetadata entry if it doesn't exist (e.g., if metadata extraction failed but URL resolution succeeded).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2589:Boolean expressions should not be gratuitous", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static async Task UpdateResourceMetadataWithFilenamesAsync(
            ModComponent component,
            string url,
            List<string> resolvedFilenames)
        {
            if (component is null || string.IsNullOrWhiteSpace(url) || resolvedFilenames is null || resolvedFilenames.Count == 0)
            {
                return;
            }

            // Find ResourceMetadata for this URL

            ResourceMetadata resourceMeta = null;
            string metadataHash = null;

            // ResourceRegistry is now keyed by URL directly
            component.ResourceRegistry.TryGetValue(url, out resourceMeta);
            metadataHash = resourceMeta?.MetadataHash;

            // If ResourceMetadata doesn't exist, create a minimal entry so filenames can be tracked
            if (resourceMeta is null)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] No ResourceMetadata found for URL '{url}', creating minimal entry from resolved filenames").ConfigureAwait(false);

                // Create a minimal ResourceMetadata entry keyed by URL directly
                // Use SHA1 hash of normalized URL as temporary ContentKey/MetadataHash
                string normalizedUrl = UrlNormalizer.Normalize(url);
                byte[] urlHashBytes;
#if NET48
				using (var sha1 = System.Security.Cryptography.SHA1.Create())
				{
					urlHashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalizedUrl));
				}
#else
                urlHashBytes = NetFrameworkCompatibility.HashDataSHA1(System.Text.Encoding.UTF8.GetBytes(normalizedUrl));
#endif
                metadataHash = BitConverter.ToString(urlHashBytes).Replace("-", string.Empty).ToLowerInvariant();

                resourceMeta = new ResourceMetadata
                {
                    ContentKey = metadataHash, // Will be updated with ContentId if available later
                    ContentId = null, // Not computed yet
                    MetadataHash = metadataHash, // Temporary, will be updated when metadata is extracted
                    HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal),
                    Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                    FileSize = 0,
                    TrustLevel = MappingTrustLevel.Unverified,
                    FirstSeen = DateTime.UtcNow,
                };
                // Store URL in HandlerMetadata for global index lookup compatibility
                resourceMeta.HandlerMetadata["PrimaryUrl"] = url;

                component.ResourceRegistry[url] = resourceMeta; // Key by URL directly
                await Logger.LogVerboseAsync($"[DownloadCacheService] Created minimal ResourceMetadata entry for URL: {url} (temp MetadataHash: {metadataHash?.Substring(0, Math.Min(16, metadataHash.Length))}...)").ConfigureAwait(false);
            }

            // Add all resolved filenames to ResourceMetadata.Files dictionary
            int addedCount = 0;
            foreach (string filename in resolvedFilenames.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                if (!resourceMeta.Files.ContainsKey(filename))
                {
                    resourceMeta.Files[filename] = null; // null = not yet verified/needed
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                await Logger.LogVerboseAsync($"[DownloadCacheService] Added {addedCount} filename(s) to ResourceMetadata.Files for URL '{url}' (total: {resourceMeta.Files.Count})").ConfigureAwait(false);
            }

            await Logger.LogVerboseAsync($"[DownloadCacheService] ResourceMetadata for '{component.Name}': {url} -> MetadataHash: {resourceMeta.MetadataHash?.Substring(0, Math.Min(16, resourceMeta.MetadataHash?.Length ?? 0))}..., ContentId: {resourceMeta.ContentId?.Substring(0, Math.Min(16, resourceMeta.ContentId?.Length ?? 0)) ?? "null"}..., Files: {resourceMeta.Files.Count}").ConfigureAwait(false);

            // Persist filenames to the global resource index and save to disk
            lock (s_resourceIndexLock)
            {
                if (!string.IsNullOrEmpty(resourceMeta.MetadataHash))
                {
                    s_resourceByMetadataHash[resourceMeta.MetadataHash] = resourceMeta;
                }

                if (!string.IsNullOrEmpty(resourceMeta.ContentId))
                {
                    s_resourceByContentId[resourceMeta.ContentId] = resourceMeta;
                }

            }

            try
            {
                await SaveResourceIndexAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogWarningAsync($"[Cache] Failed to save resource index after filename update: {ex.Message}").ConfigureAwait(false);
            }
        }


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static IReadOnlyList<string> GetFilenamesForDownload(ModComponent component, string url, List<string> allFilenames)
        {
            if (component is null || string.IsNullOrWhiteSpace(url) || allFilenames is null || allFilenames.Count == 0)
            {

                return new List<string>();
            }


            if (component.ResourceRegistry.TryGetValue(url, out ResourceMetadata resourceMeta))
            {
                // If dictionary is empty (auto-discover mode), use VFS-based pattern filtering
                if (resourceMeta.Files.Count == 0)
                {
                    Logger.LogVerbose($"[DownloadCacheService] Auto-discover mode for '{url}', filtering {allFilenames.Count} files by unsatisfied Extract patterns...");

                    string modSourceDirectoryForAutoDiscover = MainConfig.SourcePath?.FullName;
                    if (string.IsNullOrEmpty(modSourceDirectoryForAutoDiscover))
                    {
                        Logger.LogWarning("[DownloadCacheService] MainConfig.SourcePath not set, downloading all files as fallback");
                        return allFilenames;
                    }

                    var validationService2 = new ComponentValidationService();
                    List<string> matchedFiles = validationService2.FilterFilenamesByUnsatisfiedPatterns(
                        component,
                        allFilenames,
                        modSourceDirectoryForAutoDiscover
                    );

                    if (matchedFiles.Count > 0)
                    {
                        Logger.LogVerbose($"[DownloadCacheService] Auto-discover matched {matchedFiles.Count}/{allFilenames.Count} files needed to satisfy Extract patterns");
                        return matchedFiles;
                    }

                    Logger.LogWarning($"[DownloadCacheService] Auto-discover mode but no files matched unsatisfied Extract patterns for '{url}'. Available files:");
                    foreach (string fn in allFilenames)
                    {
                        Logger.LogWarning($"  • {fn}");
                    }
                    return new List<string>();
                }

                // Dictionary has entries - use explicit filtering
                // null = auto-discover/test with VFS
                // true = explicitly enabled
                // false = explicitly disabled (skip)

                string modDir = MainConfig.SourcePath?.FullName
                                ?? throw new InvalidOperationException("MainConfig.SourcePath not set");
                ComponentValidationService validationSvc = string.IsNullOrEmpty(modDir) ? null : new ComponentValidationService();

                var filesToDownload = allFilenames
                    .Where(filename =>
                    {
                        if (resourceMeta.Files.TryGetValue(filename, out bool? shouldDownload))
                        {
                            return shouldDownload != false; // null or true = download, false = skip
                        }
                        // File not in dict - use VFS to check if it satisfies any unsatisfied pattern

                        if (validationSvc is null)
                        {

                            return false; // If no VFS available, don't download unknown files
                        }


                        List<string> testResult = validationSvc.FilterFilenamesByUnsatisfiedPatterns(component, new List<string> { filename }, modDir);
                        return testResult.Count > 0;

                    })
                    .ToList();

                if (filesToDownload.Count > 0)
                {
                    int explicitlyEnabled = allFilenames.Count(fn => resourceMeta.Files.TryGetValue(fn, out bool? val) && val == true);
                    int autoDiscover = allFilenames.Count(fn => resourceMeta.Files.TryGetValue(fn, out bool? val) && val is null);
                    Logger.LogVerbose($"[DownloadCacheService] Filtered download list for '{url}': {filesToDownload.Count}/{allFilenames.Count} files ({explicitlyEnabled} enabled, {autoDiscover} auto-discover)");
                    return filesToDownload;
                }

                Logger.LogWarning($"[DownloadCacheService] All files disabled for URL '{url}' (0/{allFilenames.Count} enabled)");
                return new List<string>();
            }

            Logger.LogVerbose($"[DownloadCacheService] No ModLinkFilenames entry for '{url}', filtering {allFilenames.Count} files by unsatisfied Extract patterns...");

            string modSourceDirectory = MainConfig.SourcePath?.FullName;
            if (string.IsNullOrEmpty(modSourceDirectory))
            {
                Logger.LogWarning("[DownloadCacheService] MainConfig.SourcePath not set, downloading all files as fallback");
                return allFilenames;
            }

            var validationService = new ComponentValidationService();
            List<string> filteredFiles = validationService.FilterFilenamesByUnsatisfiedPatterns(component, allFilenames, modSourceDirectory);

            if (filteredFiles.Count > 0)
            {
                Logger.LogVerbose($"[DownloadCacheService] Matched {filteredFiles.Count}/{allFilenames.Count} files needed to satisfy Extract patterns");
                return filteredFiles;
            }

            // No patterns matched - all Extract patterns already satisfied
            Logger.LogVerbose("[DownloadCacheService] All Extract patterns already satisfied, no downloads needed");
            return new List<string>();
        }

        private static bool ShouldDownloadUrl(ModComponent component, string url)
        {
            if (component is null || string.IsNullOrWhiteSpace(url))
            {
                return true;
            }


            if (!component.ResourceRegistry.TryGetValue(url, out ResourceMetadata resourceMeta) ||
                 resourceMeta.Files.Count <= 0)
            {
                return true;
            }
            // null = auto-discover (treat as enabled)
            // true = explicitly enabled
            // false = explicitly disabled

            bool hasEnabledFile = resourceMeta.Files.Values.Any(shouldDownload => shouldDownload != false);
            if (hasEnabledFile)
            {

                return true;
            }


            Logger.LogVerbose($"[DownloadCacheService] URL has all filenames explicitly disabled, skipping: {url}");
            return false;

        }

        #region Phase 5: Resource Index Management

        private static string GetResourceIndexPath()
        {
            string cacheDir = GetCacheDirectory();
            return Path.Combine(cacheDir, "resource-index.json");
        }

        private static string GetResourceIndexLockPath()
        {
            return GetResourceIndexPath() + ".lock";
        }

        /// <summary>
        /// Saves the resource index atomically with cross-process file locking.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task SaveResourceIndexAsync()
        {
            string path = GetResourceIndexPath();
            string temp = path + ".tmp";
            string backup = path + ".bak";
            string lockPath = GetResourceIndexLockPath();

            // Lazy-hydrate from disk before saving to avoid overwriting with an empty in-memory index
            if (!s_resourceIndexLoaded && File.Exists(path))
            {
                try { await LoadResourceIndexAsync().ConfigureAwait(false); }
                catch { /* non-fatal */ }
            }

            // Ensure directory exists
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }


            try
            {
                // Cross-process file lock using cross-platform approach
                using (var fileLock = new CrossPlatformFileLock(lockPath))
                {
                    // Lock the entire file
                    await fileLock.LockAsync().ConfigureAwait(false);

                    // RELOAD from disk one more time after acquiring the lock to prevent race conditions
                    // where another process may have cleared and reloaded the in-memory index between
                    // the initial lazy-hydration above and acquiring the lock here.
                    if (File.Exists(path))
                    {
                        try
                        {
                            await LoadResourceIndexAsync().ConfigureAwait(false);
                            await Logger.LogVerboseAsync("[Cache] Reloaded resource index after acquiring save lock to prevent race condition").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            await Logger.LogWarningAsync($"[Cache] Failed to reload resource index before save (non-fatal): {ex.Message}").ConfigureAwait(false);
                        }
                    }

                    try
                    {
                        var indexData = new
                        {
                            schemaVersion = 1,
                            lastSaved = DateTime.UtcNow.ToString("O"),
                            entries = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase),
                            mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        };

                        lock (s_resourceIndexLock)
                        {
                            // Merge all resource metadata
                            foreach (KeyValuePair<string, ResourceMetadata> kvp in s_resourceByMetadataHash)
                            {
                                (indexData.entries)[kvp.Key] = kvp.Value;
                            }


                            foreach (KeyValuePair<string, ResourceMetadata> kvp in s_resourceByContentId)
                            {
                                if (!(indexData.entries).ContainsKey(kvp.Key))
                                {
                                    (indexData.entries)[kvp.Key] = kvp.Value;
                                }

                            }

                            // Copy mappings
                            foreach (KeyValuePair<string, string> kvp in s_metadataHashToContentId)
                            {
                                (indexData.mappings)[kvp.Key] = kvp.Value;
                            }

                        }

                        string json = JsonConvert.SerializeObject(indexData, Formatting.Indented);
                        await Task.Run(() => File.WriteAllText(temp, json)).ConfigureAwait(false);

                        // Platform-specific atomic replace
                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            if (File.Exists(path))
                            {
                                File.Replace(temp, path, backup);
                            }
                            else
                            {
                                File.Move(temp, path);
                            }


                            if (File.Exists(backup))
                            {
                                File.Delete(backup);
                            }

                        }
                        else
                        {
                            // POSIX: rename is atomic
                            if (File.Exists(path))
                            {
                                File.Move(path, backup);
                            }


                            File.Move(temp, path);

                            if (File.Exists(backup))
                            {
                                File.Delete(backup);
                            }

                        }

                        await Logger.LogVerboseAsync($"[Cache] Saved resource index: {path}").ConfigureAwait(false);
                    }
                    finally
                    {
                        await fileLock.UnlockAsync().ConfigureAwait(false);
                    }
                }

                // Clean up lock file if empty/stale
                if (File.Exists(lockPath))
                {
                    var lockInfo = new FileInfo(lockPath);
                    if (lockInfo.Length == 0)
                    {
                        try { File.Delete(lockPath); }
                        catch (Exception ex)
                        {
                            await Logger.LogExceptionAsync(ex, $"[Cache] Failed to delete lock file: {ex.Message}").ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"[Cache] Failed to save resource index: {ex.Message}").ConfigureAwait(false);

                // Clean up temp file
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch (Exception ex2)
                    {
                        await Logger.LogExceptionAsync(ex2, $"[Cache] Failed to delete temp file: {ex.Message}").ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Loads the resource index from disk with file locking.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static async Task LoadResourceIndexAsync()
        {
            string path = GetResourceIndexPath();
            string lockPath = GetResourceIndexLockPath();

            if (!File.Exists(path))
            {
                await Logger.LogVerboseAsync("[Cache] No resource index found, starting fresh").ConfigureAwait(false);
                return;
            }

            try
            {
                using (var fileLock = new CrossPlatformFileLock(lockPath))
                {
                    await fileLock.LockAsync().ConfigureAwait(false);

                    try
                    {
                        string json = await Task.Run(() => File.ReadAllText(path)).ConfigureAwait(false);
                        dynamic indexData = JsonConvert.DeserializeObject(json);

                        if (indexData is null)
                        {
                            await Logger.LogWarningAsync("[Cache] Resource index file was empty or invalid").ConfigureAwait(false);
                            return;
                        }

                        lock (s_resourceIndexLock)
                        {
                            s_resourceByMetadataHash.Clear();
                            s_metadataHashToContentId.Clear();
                            s_resourceByContentId.Clear();

                            // Load entries
                            if (indexData.entries != null)
                            {
                                var entries = (Newtonsoft.Json.Linq.JObject)indexData.entries;
                                foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> kvp in entries)
                                {
                                    Newtonsoft.Json.Linq.JToken metaToken = kvp.Value;
                                    var meta = new ResourceMetadata
                                    {
                                        ContentKey = metaToken["ContentKey"]?.ToString(),
                                        ContentId = metaToken["ContentId"]?.ToString(),
                                        ContentHashSHA256 = metaToken["ContentHashSHA256"]?.ToString(),
                                        MetadataHash = metaToken["MetadataHash"]?.ToString(),
                                        FileSize = metaToken["FileSize"]?.ToObject<long>() ?? 0,
                                        PieceLength = metaToken["PieceLength"]?.ToObject<int>() ?? 0,
                                        PieceHashes = metaToken["PieceHashes"]?.ToString(),
                                    };

                                    if (metaToken["HandlerMetadata"] != null)
                                    {
                                        meta.HandlerMetadata = metaToken["HandlerMetadata"].ToObject<Dictionary<string, object>>();
                                    }

                                    if (metaToken["Files"] != null)
                                    {
                                        meta.Files = metaToken["Files"].ToObject<Dictionary<string, bool?>>();
                                    }

                                    if (Enum.TryParse(metaToken["TrustLevel"]?.ToString(), out MappingTrustLevel trustLevel))
                                    {
                                        meta.TrustLevel = trustLevel;
                                    }

                                    if (DateTime.TryParse(metaToken["FirstSeen"]?.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime firstSeen))
                                    {
                                        meta.FirstSeen = firstSeen;
                                    }

                                    if (DateTime.TryParse(metaToken["LastVerified"]?.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime lastVerified))
                                    {
                                        meta.LastVerified = lastVerified;
                                    }

                                    // Store in appropriate index

                                    if (!string.IsNullOrEmpty(meta.MetadataHash))
                                    {
                                        s_resourceByMetadataHash[meta.MetadataHash] = meta;
                                    }

                                    if (!string.IsNullOrEmpty(meta.ContentId))
                                    {
                                        s_resourceByContentId[meta.ContentId] = meta;
                                    }

                                }
                            }

                            // Load mappings
                            if (indexData.mappings != null)
                            {
                                var mappings = (Newtonsoft.Json.Linq.JObject)indexData.mappings;
                                foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> kvp in mappings)
                                {
                                    string key = kvp.Key;
                                    Newtonsoft.Json.Linq.JToken mapping = kvp.Value;
                                    s_metadataHashToContentId[key] = mapping.ToString();
                                }
                            }
                        }

                        await Logger.LogVerboseAsync($"[Cache] Loaded resource index: {s_resourceByMetadataHash.Count} metadata entries, {s_resourceByContentId.Count} content entries, {s_metadataHashToContentId.Count} mappings").ConfigureAwait(false);
                    }
                    finally
                    {
                        await fileLock.UnlockAsync().ConfigureAwait(false);
                    }
                }

                s_resourceIndexLoaded = true;
            }
            catch (Exception ex)
            {
                await Logger.LogWarningAsync($"[Cache] Failed to load resource index: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates a MetadataHash → ContentId mapping with trust elevation logic.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task<bool> UpdateMappingWithVerification(
            string metadataHash,
            string contentId,
            ResourceMetadata meta
        )
        {
            bool updated = false;
            string logMessage = null;
            bool hasConflict = false;
            bool keepExisting = false;

            lock (s_resourceIndexLock)
            {
                // Check existing mapping
                if (s_metadataHashToContentId.TryGetValue(metadataHash, out string existingContentId))
                {
                    if (string.Equals(existingContentId, contentId, StringComparison.Ordinal))
                    {
                        // Same mapping, elevate trust
                        if (meta.TrustLevel == MappingTrustLevel.Unverified)
                        {
                            meta.TrustLevel = MappingTrustLevel.ObservedOnce;
                            updated = true;
                            logMessage = $"[Cache] Trust elevated: {metadataHash}... → ObservedOnce";
                        }
                        else if (meta.TrustLevel == MappingTrustLevel.ObservedOnce)
                        {
                            meta.TrustLevel = MappingTrustLevel.Verified;
                            updated = true;
                            logMessage = $"[Cache] Trust elevated: {metadataHash}... → Verified";
                        }
                    }
                    else
                    {
                        // CONFLICT: Different ContentId for same MetadataHash
                        hasConflict = true;

                        // Keep existing if Verified
                        if (s_resourceByContentId.TryGetValue(existingContentId, out ResourceMetadata existingMeta) &&
                            existingMeta.TrustLevel == MappingTrustLevel.Verified)
                        {
                            keepExisting = true;
                        }
                        else
                        {
                            // Replace with new mapping
                            s_metadataHashToContentId[metadataHash] = contentId;
                            s_resourceByContentId[contentId] = meta;
                            meta.TrustLevel = MappingTrustLevel.ObservedOnce;
                            updated = true;
                        }
                    }
                }
                else
                {
                    // New mapping
                    s_metadataHashToContentId[metadataHash] = contentId;
                    s_resourceByContentId[contentId] = meta;
                    meta.TrustLevel = MappingTrustLevel.ObservedOnce;
                    updated = true;
                    logMessage = $"[Cache] New mapping: {metadataHash}... → {contentId}...";
                }

                // Always update metadata hash index
                s_resourceByMetadataHash[metadataHash] = meta;
            }

            // Log outside of lock
            if (hasConflict)
            {
                await Logger.LogWarningAsync($"[Cache] Mapping conflict detected:").ConfigureAwait(false);
                await Logger.LogWarningAsync($"  MetadataHash: {metadataHash}...").ConfigureAwait(false);
                await Logger.LogWarningAsync($"  New ContentId: {contentId}...").ConfigureAwait(false);

                if (keepExisting)
                {
                    await Logger.LogWarningAsync($"  Keeping existing (Verified)").ConfigureAwait(false);
                    return false;
                }

                await Logger.LogWarningAsync($"  Replacing with new mapping").ConfigureAwait(false);
            }
            else if (logMessage != null)
            {
                if (meta.TrustLevel == MappingTrustLevel.Verified)
                {
                    await Logger.LogAsync(logMessage).ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogVerboseAsync(logMessage).ConfigureAwait(false);
                }

            }

            return updated;
        }

        /// <summary>
        /// Garbage collects stale entries and downgrades trust levels.
        /// </summary>
        public static void GarbageCollectResourceIndex()
        {
            DateTime now = DateTime.UtcNow;
            var toRemove = new List<string>();

            lock (s_resourceIndexLock)
            {
                foreach (KeyValuePair<string, ResourceMetadata> kvp in s_resourceByContentId)
                {
                    ResourceMetadata meta = kvp.Value;

                    // Rule 1: Old and file doesn't exist
                    if (meta.LastVerified.HasValue && (now - meta.LastVerified.Value).TotalDays > 90)
                    {
                        string expectedFile = Path.Combine(MainConfig.SourcePath?.FullName ?? "", meta.Files.Keys.FirstOrDefault() ?? "");
                        if (!File.Exists(expectedFile))
                        {
                            toRemove.Add(kvp.Key);
                            continue;
                        }
                    }

                    // Rule 2: Never used and very old
                    if (!meta.LastVerified.HasValue && meta.FirstSeen.HasValue && (now - meta.FirstSeen.Value).TotalDays > 365)
                    {
                        toRemove.Add(kvp.Key);
                    }

                    // Rule 3: Downgrade trust if not re-verified
                    if (meta.LastVerified.HasValue && (now - meta.LastVerified.Value).TotalDays > 30)
                    {
                        if (meta.TrustLevel == MappingTrustLevel.Verified)
                        {
                            meta.TrustLevel = MappingTrustLevel.ObservedOnce;
                        }

                        else if (meta.TrustLevel == MappingTrustLevel.ObservedOnce)
                        {
                            meta.TrustLevel = MappingTrustLevel.Unverified;
                        }

                    }
                }

                foreach (string key in toRemove)
                {
                    s_resourceByContentId.Remove(key);
                    KeyValuePair<string, string> metaMapping = s_metadataHashToContentId.FirstOrDefault(
                        m => string.Equals(m.Value, key, StringComparison.Ordinal)
                    );
                    if (!string.IsNullOrEmpty(metaMapping.Key))
                    {
                        s_metadataHashToContentId.Remove(metaMapping.Key);
                    }

                }
            }

            Logger.LogVerbose($"[Cache] GC removed {toRemove.Count} stale entries");
        }

        /// <summary>
        /// Enforces disk quota using LRU eviction.
        /// </summary>
        public static void EnforceDiskQuota(long maxSizeBytes)
        {
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KOTORModSync",
                "Cache",
                "Network"
            );

            if (!Directory.Exists(cacheDir))
            {
                return;
            }


            string[] datFiles = Directory.GetFiles(cacheDir, "*.dat");
            long totalSize = datFiles.Sum(f => new FileInfo(f).Length);

            if (totalSize <= maxSizeBytes)
            {
                return;
            }

            // Sort by LastVerified (oldest first)

            lock (s_resourceIndexLock)
            {
                var entries = s_resourceByContentId.OrderBy(
                    e => e.Value.LastVerified ?? e.Value.FirstSeen
                ).ToList();

                foreach (string key in entries.Select(entry => entry.Key))
                {
                    // This requires access to GetCachePath from DownloadCacheOptimizer
                    // TODO - STUB: For now, construct path manually
                    string datPath = Path.Combine(cacheDir, $"{key}.dat");

                    if (File.Exists(datPath))
                    {
                        long fileSize = new FileInfo(datPath).Length;
                        try
                        {
                            File.Delete(datPath);
                            totalSize -= fileSize;

                            s_resourceByContentId.Remove(key);
                            Logger.LogVerbose($"[Cache] Evicted: {key}... ({fileSize / 1024} KB)");

                            if (totalSize <= maxSizeBytes)
                            {
                                break;
                            }

                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"[Cache] Failed to delete cache file: {ex.Message}");
                        }
                    }
                }

            }

            Logger.LogVerbose($"[Cache] Quota enforcement: pruned to {totalSize / (1024 * 1024)} MB");
        }

        #endregion

        #region CLI Management Methods

        /// <summary>
        /// Gets the total number of resources in the cache.
        /// </summary>
        public static int GetResourceCount()
        {
            lock (s_resourceIndexLock)
            {
                return s_resourceByContentId.Count;
            }
        }

        /// <summary>
        /// Gets the total cache size in bytes.
        /// </summary>
        public static long GetTotalCacheSize()
        {
            lock (s_resourceIndexLock)
            {
                return s_resourceByContentId.Values.Sum(r => r.FileSize);
            }
        }

        /// <summary>
        /// Gets statistics by provider.
        /// </summary>
        public static IReadOnlyDictionary<string, int> GetProviderStats()
        {
            lock (s_resourceIndexLock)
            {
                return s_resourceByContentId.Values
                    .GroupBy(r => r.HandlerMetadata?.ContainsKey("provider") == true ? r.HandlerMetadata["provider"].ToString() : "unknown", StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// Gets the count of blocked ContentIds.
        /// </summary>
        public static int GetBlockedContentIdCount()
        {
            return DownloadCacheOptimizer.GetBlockedContentIdCount();
        }

        /// <summary>
        /// Gets the last index update time.
        /// </summary>
        public static DateTime GetLastIndexUpdate()
        {
            lock (s_resourceIndexLock)
            {
                return s_resourceByContentId.Values
                    .Where(r => r.LastVerified.HasValue)
                    .DefaultIfEmpty(new ResourceMetadata { LastVerified = DateTime.MinValue })
                    .Max(r => r.LastVerified ?? DateTime.MinValue);
            }
        }

        /// <summary>
        /// Clears the cache, optionally for a specific provider.
        /// </summary>
        public static async Task ClearCacheAsync(string provider = null)
        {
            lock (s_resourceIndexLock)
            {
                if (string.IsNullOrEmpty(provider))
                {
                    // Clear all
                    s_resourceByContentId.Clear();
                    s_resourceByMetadataHash.Clear();
                    s_metadataHashToContentId.Clear();
                }
                else
                {
                    // Clear specific provider
                    var toRemove = s_resourceByContentId
                        .Where(kvp => kvp.Value.HandlerMetadata?.ContainsKey("provider") == true &&
                               string.Equals(kvp.Value.HandlerMetadata["provider"].ToString(), provider, StringComparison.Ordinal))
                        .ToList();

                    foreach (KeyValuePair<string, ResourceMetadata> kvp in toRemove)
                    {
                        s_resourceByContentId.Remove(kvp.Key);
                        s_resourceByMetadataHash.Remove(kvp.Value.MetadataHash);
                        s_metadataHashToContentId.Remove(kvp.Value.MetadataHash);
                    }
                }
            }

            await SaveResourceIndexAsync().ConfigureAwait(false);
        }

        public static List<string> GetFileNames(string url)
        {
            ResourceMetadata cachedMeta = TryGetResourceMetadataByUrl(url);
            if (cachedMeta != null && cachedMeta.Files != null && cachedMeta.Files.Count > 0)
            {
                return cachedMeta.Files.Keys.ToList();
            }
            return new List<string>();
        }

        #endregion
    }

    /// <summary>
    /// Cross-platform file locking implementation that works on Windows, Linux, and macOS.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0048:File name must match type name", Justification = "<Pending>")]
    internal sealed partial class CrossPlatformFileLock : IDisposable
    {
        private readonly string _lockPath;
        private FileStream _fileStream;
        private bool _isLocked;
        private bool _disposed;

        public CrossPlatformFileLock(string lockPath)
        {
            _lockPath = lockPath ?? throw new ArgumentNullException(nameof(lockPath));
        }

        public async Task LockAsync()
        {
            if (_disposed)
            {

                throw new ObjectDisposedException(nameof(CrossPlatformFileLock));
            }

            if (_isLocked)
            {
                return;
            }

            // Ensure directory exists

            string directory = Path.GetDirectoryName(_lockPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }


            _fileStream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                // Windows: Use FileStream.Lock/Unlock
#pragma warning disable CA1416 // This call site is reachable on all platforms. 'FileStream.Lock(long, long)' is unsupported on: 'macOS/OSX'.
                _fileStream.Lock(0, 0);
#pragma warning restore CA1416
            }
            else
            {
                // Unix systems (Linux/macOS): Use flock via P/Invoke
                await LockUnixFileAsync().ConfigureAwait(false);
            }

            _isLocked = true;
        }

        public async Task UnlockAsync()
        {
            if (_disposed || !_isLocked)
            {
                return;
            }


            try
            {
                if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
                {
                    // Windows: Use FileStream.Unlock
#pragma warning disable CA1416 // This call site is reachable on all platforms. 'FileStream.Unlock(long, long)' is unsupported on: 'macOS/OSX'.
                    _fileStream?.Unlock(0, 0);
#pragma warning restore CA1416
                }
                else
                {
                    // Unix systems: Use flock via P/Invoke
                    await UnlockUnixFileAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _isLocked = false;
            }
        }

        private async Task LockUnixFileAsync()
        {
            await Task.Run(() =>
            {
                int fd = GetFileDescriptor(_fileStream);
                if (fd == -1)
                {

                    throw new InvalidOperationException("Failed to get file descriptor");
                }


                int result = InvokeFlock(fd, LOCK_EX | LOCK_NB);
                if (result != 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new IOException($"Failed to acquire file lock: {error}");
                }
            }).ConfigureAwait(false);
        }

        private async Task UnlockUnixFileAsync()
        {
            await Task.Run(() =>
            {
                int fd = GetFileDescriptor(_fileStream);
                if (fd != -1)
                {
                    InvokeFlock(fd, LOCK_UN);
                }
            }).ConfigureAwait(false);
        }

        private static int GetFileDescriptor(FileStream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (UtilityHelper.GetOperatingSystem() == OSPlatform.Windows)
            {
                Logger.LogWarning("Attempted to obtain a Unix file descriptor on Windows.");
                return -1;
            }

            SafeFileHandle safeHandle = stream.SafeFileHandle;
            bool addedRef = false;
            try
            {
                safeHandle.DangerousAddRef(ref addedRef);
                IntPtr handle = safeHandle.DangerousGetHandle();
                if (handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to get file descriptor from FileStream.");
                }

                return handle.ToInt32();
            }
            finally
            {
                if (addedRef)
                {
                    safeHandle.DangerousRelease();
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    UnlockAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "(ignorable) error unlocking file during disposal");
                }
                finally
                {
                    _fileStream?.Dispose();
                    _disposed = true;
                }
            }
        }

        // P/Invoke declarations for Unix file locking
        private const int LOCK_EX = 2;    // Exclusive lock
        private const int LOCK_NB = 4;    // Non-blocking
        private const int LOCK_UN = 8;    // Unlock

        [DllImport("libc", SetLastError = true, EntryPoint = "flock")]
        private static extern int FlockNative(int fd, int operation);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0051:Remove unused private members", Justification = "Used when running on Unix-based platforms for file locking.")]
        private static int InvokeFlock(int fd, int operation)
        {
            OSPlatform os = UtilityHelper.GetOperatingSystem();
            if (os == OSPlatform.Windows)
            {
                Logger.LogWarning("Attempted to use flock on Windows; operation not supported.");
                return -1;
            }

            return FlockNative(fd, operation);
        }
    }
}
