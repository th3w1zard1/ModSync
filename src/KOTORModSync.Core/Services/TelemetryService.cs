// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using KOTORModSync.Core.Utility;

using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
#if !NETSTANDARD2_0
#endif

namespace KOTORModSync.Core.Services
{

    public sealed class TelemetryService : IDisposable
    {
        private static readonly Lazy<TelemetryService> s_instance = new Lazy<TelemetryService>(() => new TelemetryService());

        private TelemetryConfiguration _config;
        private TelemetryAuthenticator _authenticator;
        private TracerProvider _tracerProvider;
        private MeterProvider _meterProvider;
        private ActivitySource _activitySource;
        private Meter _meter;

        private Counter<long> _eventCounter;
        private Counter<long> _errorCounter;
        private Histogram<double> _operationDuration;
        private Counter<long> _modInstallCounter;
        private Counter<long> _modValidationCounter;
        private Counter<long> _downloadCounter;
        private Histogram<long> _downloadSize;

        // Cache telemetry metrics
        private Counter<long> _cacheHitCounter;
        private Counter<long> _cacheMissCounter;
        private Counter<long> _cacheSizeCounter;
        private Counter<long> _integrityVerificationCounter;
        private Histogram<double> _cacheOperationDuration;

        private bool _isInitialized;
        private bool _disposed;

        public static TelemetryService Instance => s_instance.Value;

        public bool IsEnabled => _config?.IsEnabled ?? false;

        private TelemetryService() => _config = TelemetryConfiguration.Load();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public void Initialize()
        {
            if (_isInitialized || !_config.IsEnabled || !_config.UserConsented)
            {
                return;
            }

            try
            {
                _authenticator = new TelemetryAuthenticator(_config.SigningSecret, _config.SessionId);

                if (!_authenticator.HasValidSecret())
                {
                    Logger.LogWarning("[Telemetry] No signing secret available - telemetry will be disabled");
                    _config.IsEnabled = false;
                    return;
                }

                ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: "KOTORModSync",
                        serviceVersion: typeof(TelemetryService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                        serviceInstanceId: _config.SessionId)
                    .AddAttributes(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["user.id"] = _config.AnonymousUserId,
                        ["session.id"] = _config.SessionId,
                        ["environment"] = _config.Environment,
                        ["platform"] = Environment.OSVersion.Platform.ToString(),
                    });

                _activitySource = new ActivitySource("KOTORModSync", "1.0.0");

                _meter = new Meter("KOTORModSync", "1.0.0");

                _eventCounter = _meter.CreateCounter<long>("kotormodsync.events", "events", "Number of events recorded");
                _errorCounter = _meter.CreateCounter<long>("kotormodsync.errors", "errors", "Number of errors recorded");
                _operationDuration = _meter.CreateHistogram<double>("kotormodsync.operation.duration", "ms", "Duration of operations");
                _modInstallCounter = _meter.CreateCounter<long>("kotormodsync.mods.installed", "mods", "Number of mods installed");
                _modValidationCounter = _meter.CreateCounter<long>("kotormodsync.mods.validated", "mods", "Number of mods validated");
                _downloadCounter = _meter.CreateCounter<long>("kotormodsync.downloads", "downloads", "Number of downloads");
                _downloadSize = _meter.CreateHistogram<long>("kotormodsync.download.size", "bytes", "Size of downloads");

                // Initialize cache telemetry metrics
                _cacheHitCounter = _meter.CreateCounter<long>("kotormodsync.cache.hits", "hits", "Number of cache hits");
                _cacheMissCounter = _meter.CreateCounter<long>("kotormodsync.cache.misses", "misses", "Number of cache misses");
                _cacheSizeCounter = _meter.CreateCounter<long>("kotormodsync.cache.size", "bytes", "Total cache size in bytes");
                _integrityVerificationCounter = _meter.CreateCounter<long>("kotormodsync.cache.integrity.verified", "verifications", "Number of integrity verifications");
                _cacheOperationDuration = _meter.CreateHistogram<double>("kotormodsync.cache.operation.duration", "ms", "Duration of cache operations");

                TracerProviderBuilder tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource("KOTORModSync");

                if (_config.EnableConsoleExporter)
                {
                    tracerProviderBuilder.AddConsoleExporter();
                }

                if (_config.EnableFileExporter && !string.IsNullOrEmpty(_config.LocalLogPath))
                {

                    Logger.LogVerbose("[Telemetry] File exporter requested but not yet implemented");
                }

                if (_config.EnableOtlpExporter && !string.IsNullOrEmpty(_config.OtlpEndpoint))
                {
                    tracerProviderBuilder.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(_config.OtlpEndpoint);
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.Headers = GetAuthHeaders("/v1/traces");
                    });
                }

                _tracerProvider = tracerProviderBuilder.Build();

                MeterProviderBuilder meterProviderBuilder = Sdk.CreateMeterProviderBuilder()
                    .SetResourceBuilder(resourceBuilder)
                    .AddMeter("KOTORModSync");

                if (_config.EnableConsoleExporter)
                {
                    meterProviderBuilder.AddConsoleExporter();
                }

                if (_config.EnableOtlpExporter && !string.IsNullOrEmpty(_config.OtlpEndpoint))
                {
                    meterProviderBuilder.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(_config.OtlpEndpoint);
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.Headers = GetAuthHeaders("/v1/metrics");
                    });
                }

                if (_config.EnablePrometheusExporter)
                {
                    try
                    {
#if NETSTANDARD2_0
						Logger.LogWarning("[Telemetry] Prometheus HTTP listener requires .NET Core 3.1+ or .NET 5+. Use OTLP exporter for authenticated remote telemetry.");
#else
                        meterProviderBuilder.AddPrometheusHttpListener(options =>
                        {
						    options.UriPrefixes = new[] { BuildPrometheusUriPrefix(_config.PrometheusPort) };
                        });
                        Logger.Log($"[Telemetry] Prometheus HTTP listener started on http://localhost:{_config.PrometheusPort}/metrics (LOCAL ONLY - for development/testing)");
                        Logger.Log("[Telemetry] Note: For authenticated remote telemetry, use OTLP exporter (enabled by default)");
#endif
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "[Telemetry] Failed to start Prometheus HTTP listener");
                    }
                }

                _meterProvider = meterProviderBuilder.Build();

                _isInitialized = true;
                Logger.Log("[Telemetry] Telemetry service initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to initialize telemetry service");
            }
        }

        public void UpdateConfiguration(TelemetryConfiguration newConfig)
        {
            if (newConfig is null)
            {
                return;
            }

            bool wasEnabled = _config?.IsEnabled ?? false;
            bool isNowEnabled = newConfig.IsEnabled;

            _config = newConfig;

            if (!wasEnabled && isNowEnabled && !_isInitialized)
            {
                Initialize();
            }
            else if (wasEnabled && !isNowEnabled && _isInitialized)
            {
                Dispose();
            }
        }

        public void RecordEvent(string eventName, Dictionary<string, object> tags = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                _eventCounter?.Add(1, CreateTagList(eventName, tags));
                Logger.LogVerbose($"[Telemetry] Event: {eventName}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"[Telemetry] Failed to record event: {eventName}");
            }
        }

        public Activity StartActivity(string activityName, IReadOnlyDictionary<string, object> tags = null)
        {
            if (!IsEnabled || !_config.CollectPerformanceMetrics)
            {
                return null;
            }

            try
            {
                Activity activity = _activitySource?.StartActivity(activityName);
                if (activity != null && tags != null)
                {
                    foreach (KeyValuePair<string, object> tag in tags)
                    {
                        activity.SetTag(tag.Key, tag.Value);
                    }
                }
                return activity;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"[Telemetry] Failed to start activity: {activityName}");
                return null;
            }
        }

        public void RecordModInstallation(string modName, bool success, double durationMs, string errorMessage = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["mod.name.hash"] = HashString(modName),
                    ["success"] = success,
                };

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    tags["error.type"] = HashString(errorMessage);
                }

                _modInstallCounter?.Add(1, CreateTagList("mod.install", tags));

                if (_config.CollectPerformanceMetrics)
                {
                    _operationDuration?.Record(durationMs, CreateTagList("mod.install", tags));
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record mod installation");
            }
        }

        public void RecordUiInteraction(string elementName, string action)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["element"] = elementName,
                    ["action"] = action,
                };

                _eventCounter?.Add(1, CreateTagList("ui.interaction", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record UI interaction");
            }
        }

        public void RecordDownload(
            string modName,
            bool success,
            double durationMs,
            long bytesDownloaded,
            string downloadSource = null,
            string errorMessage = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["mod.name.hash"] = HashString(modName),
                    ["success"] = success,
                    ["download.source"] = downloadSource ?? "unknown",
                };

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    tags["error.type"] = HashString(errorMessage);
                }

                _downloadCounter?.Add(1, CreateTagList("download", tags));

                if (bytesDownloaded > 0)
                {
                    _downloadSize?.Record(bytesDownloaded, CreateTagList("download.size", tags));
                }

                if (_config.CollectPerformanceMetrics)
                {
                    _operationDuration?.Record(durationMs, CreateTagList("download", tags));
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record download");
            }
        }

        public void RecordValidation(string validationType, bool success, int issueCount, double durationMs)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["validation.type"] = validationType,
                    ["success"] = success,
                    ["issue.count"] = issueCount,
                };

                _modValidationCounter?.Add(1, CreateTagList("validation", tags));

                if (_config.CollectPerformanceMetrics)
                {
                    _operationDuration?.Record(durationMs, CreateTagList("validation", tags));
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record validation");
            }
        }

        public void RecordFileOperation(string operationType, bool success, int fileCount, double durationMs, string errorMessage = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["operation.type"] = operationType,
                    ["success"] = success,
                    ["file.count"] = fileCount,
                };

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    tags["error.type"] = HashString(errorMessage);
                }

                _eventCounter?.Add(1, CreateTagList("file.operation", tags));

                if (_config.CollectPerformanceMetrics)
                {
                    _operationDuration?.Record(durationMs, CreateTagList("file.operation", tags));
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record file operation");
            }
        }

        public void RecordComponentExecution(string componentName, bool success, int instructionCount, double durationMs, string errorMessage = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["component.name.hash"] = HashString(componentName),
                    ["success"] = success,
                    ["instruction.count"] = instructionCount,
                };

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    tags["error.type"] = HashString(errorMessage);
                }

                _eventCounter?.Add(1, CreateTagList("component.execution", tags));

                if (_config.CollectPerformanceMetrics)
                {
                    _operationDuration?.Record(durationMs, CreateTagList("component.execution", tags));
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record component execution");
            }
        }

        public void RecordParsingOperation(string fileType, bool success, int componentCount, double durationMs, string errorMessage = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["file.type"] = fileType,
                    ["success"] = success,
                    ["component.count"] = componentCount,
                };

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    tags["error.type"] = HashString(errorMessage);
                }

                _eventCounter?.Add(1, CreateTagList("parsing", tags));

                if (_config.CollectPerformanceMetrics)
                {
                    _operationDuration?.Record(durationMs, CreateTagList("parsing", tags));
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record parsing operation");
            }
        }

        public void RecordSessionStart(int componentCount, int selectedCount)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["component.total"] = componentCount,
                    ["component.selected"] = selectedCount,
                };

                _eventCounter?.Add(1, CreateTagList("session.start", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record session start");
            }
        }

        public void RecordSessionEnd(double durationMs, bool completed)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["completed"] = completed,
                };

                _eventCounter?.Add(1, CreateTagList("session.end", tags));

                if (_config.CollectPerformanceMetrics)
                {
                    _operationDuration?.Record(durationMs, CreateTagList("session", tags));
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record session end");
            }
        }

        public void RecordError(string errorType, string errorMessage, string stackTrace = null)
        {
            if (!IsEnabled || !_config.CollectCrashReports)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["error.type"] = errorType,
                    ["error.message.hash"] = HashString(errorMessage),
                };

                if (!string.IsNullOrEmpty(stackTrace))
                {
                    tags["error.stacktrace.hash"] = HashString(stackTrace);
                }

                _errorCounter?.Add(1, CreateTagList("error", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record error");
            }
        }

        #region Cache Telemetry

        public void RecordCacheHit(string provider, string contentType = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["provider"] = provider ?? "unknown",
                };

                if (!string.IsNullOrEmpty(contentType))
                {
                    tags["content.type"] = contentType;
                }

                _cacheHitCounter?.Add(1, CreateTagList("cache.hit", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record cache hit");
            }
        }

        public void RecordCacheMiss(string provider, string reason = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["provider"] = provider ?? "unknown",
                };

                if (!string.IsNullOrEmpty(reason))
                {
                    tags["reason"] = reason;
                }

                _cacheMissCounter?.Add(1, CreateTagList("cache.miss", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record cache miss");
            }
        }

        public void RecordCacheSize(long sizeBytes, string provider = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(provider))
                {
                    tags["provider"] = provider;
                }

                _cacheSizeCounter?.Add(sizeBytes, CreateTagList("cache.size", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record cache size");
            }
        }

        public void RecordIntegrityVerification(bool success, string verificationType = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["success"] = success,
                };

                if (!string.IsNullOrEmpty(verificationType))
                {
                    tags["type"] = verificationType;
                }

                _integrityVerificationCounter?.Add(1, CreateTagList("integrity.verification", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record integrity verification");
            }
        }

        public void RecordCacheOperation(
            string operationType,
            bool success,
            double durationMs,
            string provider = null,
            string errorMessage = null)
        {
            if (!IsEnabled || !_config.CollectUsageData)
            {
                return;
            }

            try
            {
                var tags = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["operation.type"] = operationType,
                    ["success"] = success,
                };

                if (!string.IsNullOrEmpty(provider))
                {
                    tags["provider"] = provider;
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    tags["error"] = errorMessage;
                }

                _cacheOperationDuration?.Record(durationMs, CreateTagList("cache.operation", tags));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to record cache operation");
            }
        }

        #endregion

        public void Flush()
        {
            try
            {
                _tracerProvider?.ForceFlush();
                _meterProvider?.ForceFlush();
                Logger.LogVerbose("[Telemetry] Telemetry data flushed");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to flush telemetry data");
            }
        }

        private static KeyValuePair<string, object>[] CreateTagList(string eventName, Dictionary<string, object> tags)
        {
            var tagList = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("event.name", eventName),
            };

            if (tags != null)
            {
                foreach (KeyValuePair<string, object> tag in tags)
                {
                    tagList.Add(new KeyValuePair<string, object>(tag.Key, tag.Value));
                }
            }

            return tagList.ToArray();
        }

        private static string HashString(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "empty";
            }

            try
            {
#if NET8_0_OR_GREATER
                byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
                return Convert.ToHexString(hashBytes).Substring(0, 16);
#elif NETSTANDARD2_0 || NET48
                byte[] hashBytes = NetFrameworkCompatibility.HashDataSHA256(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
#else
				using ( var sha256 = SHA256.Create() )
				{
					byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
					return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
				}
#endif
            }
            catch
            {
                return "hash_error";
            }
        }

        internal static string BuildPrometheusUriPrefix(int port) => $"http://localhost:{port}/";

        internal string GetAuthHeaders(string requestPath)
        {
            if (_authenticator is null || !_authenticator.HasValidSecret())
            {
                return string.Empty;
            }

            try
            {
                long timestamp = TelemetryAuthenticator.GetUnixTimestamp();
                string signature = _authenticator.ComputeSignature(requestPath, timestamp);

                if (string.IsNullOrEmpty(signature))
                {
                    return string.Empty;
                }

                string version = typeof(TelemetryService).Assembly.GetName().Version?.ToString() ?? "1.0.0";

                return $"X-KMS-Signature={signature}," +
                       $"X-KMS-Timestamp={timestamp}," +
                       $"X-KMS-Session-ID={_config.SessionId}," +
                       $"X-KMS-Client-Version={version}";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to generate authentication headers");
                return string.Empty;
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
                Flush();
                _tracerProvider?.Dispose();
                _meterProvider?.Dispose();
                _activitySource?.Dispose();
                _meter?.Dispose();
                _isInitialized = false;
                _disposed = true;
                Logger.LogVerbose("[Telemetry] Telemetry service disposed");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "[Telemetry] Failed to dispose telemetry service");
            }
        }
    }
}
