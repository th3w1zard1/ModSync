// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CommandLine;

using KOTORModSync.Core.Services;
using KOTORModSync.Core.Services.Download;
using KOTORModSync.Core.Utility;

using Newtonsoft.Json;

namespace KOTORModSync.Core.CLI
{
    public class ErrorCollector
    {
        private readonly List<ErrorInfo> _errors = new List<ErrorInfo>();
        private readonly object _errorLock = new object();

        public enum ErrorCategory
        {
            Download,
            FileOperation,
            Extraction,
            Validation,
            Installation,
            TslPatcher,
            General,
        }

        public class ErrorInfo
        {
            public ErrorCategory Category { get; set; }
            public string ComponentName { get; set; }
            public string Message { get; set; }
            public string Details { get; set; }
            public Exception Exception { get; set; }
            public DateTime Timestamp { get; set; }
            public IReadOnlyList<string> LogContext { get; set; }
        }

        public void RecordError(
            ErrorCategory category,
            string componentName,
            string message,
            string details = null,
            Exception exception = null)
        {
            lock (_errorLock)
            {
                // Capture recent log messages for context (last 30 messages)
                List<string> logContext = Logger.GetRecentLogMessages(30);

                _errors.Add(new ErrorInfo
                {
                    Category = category,
                    ComponentName = componentName,
                    Message = message,
                    Details = details,
                    Exception = exception,
                    Timestamp = DateTime.Now,
                    LogContext = logContext,
                });
            }
        }

        public IReadOnlyList<ErrorInfo> GetErrors()
        {
            lock (_errorLock)
            {
                return _errors.ToList();
            }
        }

        public int GetErrorCount()
        {
            lock (_errorLock)
            {
                return _errors.Count;
            }
        }

        public void Clear()
        {
            lock (_errorLock)
            {
                _errors.Clear();
            }
        }

        public IReadOnlyDictionary<ErrorCategory, List<ErrorInfo>> GetErrorsByCategory()
        {
            lock (_errorLock)
            {
                return _errors.GroupBy(e => e.Category)
                    .ToDictionary(g => g.Key, g => g.ToList());
            }
        }
    }

    public static class ModBuildConverter
    {
        private static MainConfig s_config;
        private static ConsoleProgressDisplay s_progressDisplay;
        private static DownloadCacheService s_globalDownloadCache;
        private static ErrorCollector s_errorCollector;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static void EnsureConfigInitialized()
        {
            if (s_config is null)
            {
                s_config = new MainConfig();
                Logger.LogVerbose("MainConfig initialized");

                try
                {
                    string settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "KOTORModSync",
                        "settings.json"
                    );

                    if (File.Exists(settingsPath))
                    {
                        Logger.LogVerbose($"Loading settings from: {settingsPath}");
                        string json = File.ReadAllText(settingsPath);
                        SettingsData settings = JsonConvert.DeserializeObject<SettingsData>(json);

                        if (settings != null)
                        {
                            if (!string.IsNullOrWhiteSpace(settings.NexusModsApiKey))
                            {
                                s_config.nexusModsApiKey = settings.NexusModsApiKey;
                                Logger.LogVerbose("Loaded Nexus Mods API key from settings.json");
                            }

                            if (!string.IsNullOrEmpty(settings.SourcePath) && Directory.Exists(settings.SourcePath))
                            {
                                s_config.sourcePath = new DirectoryInfo(settings.SourcePath);
                                Logger.LogVerbose($"Loaded source path from settings: {settings.SourcePath}");
                            }

                            if (!string.IsNullOrEmpty(settings.DestinationPath) && Directory.Exists(settings.DestinationPath))
                            {
                                s_config.destinationPath = new DirectoryInfo(settings.DestinationPath);
                                Logger.LogVerbose($"Loaded destination path from settings: {settings.DestinationPath}");
                            }

                            s_config.debugLogging = settings.DebugLogging;
                            s_config.attemptFixes = settings.AttemptFixes;
                            s_config.noAdmin = settings.NoAdmin;
                            s_config.caseInsensitivePathing = settings.CaseInsensitivePathing;
                            s_config.archiveDeepCheck = settings.ArchiveDeepCheck;
                            s_config.useMultiThreadedIO = settings.UseMultiThreadedIO;
                            s_config.useCopyForMoveActions = settings.UseCopyForMoveActions;
                            s_config.validateAndReplaceInvalidArchives = settings.ValidateAndReplaceInvalidArchives;
                            s_config.filterDownloadsByResolution = settings.FilterDownloadsByResolution;
                            if (!string.IsNullOrWhiteSpace(settings.PatcherEngine))
                            {
                                s_config.patcherEngine = settings.PatcherEngine;
                            }

                            if (!string.IsNullOrWhiteSpace(settings.KPatcherExecutablePath))
                            {
                                s_config.kpatcherExecutablePath = settings.KPatcherExecutablePath;
                            }

                            Logger.LogVerbose("Settings loaded successfully from settings.json");
                        }
                    }
                    else
                    {
                        Logger.LogVerbose("No settings.json found, using defaults");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to load settings.json: {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(s_config.nexusModsApiKey))
                {
                    try
                    {
                        string configDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "KOTORModSync"
                        );
                        string legacyConfigFile = Path.Combine(configDir, "nexusmods.config");

                        if (File.Exists(legacyConfigFile))
                        {
                            string apiKey = File.ReadAllText(legacyConfigFile).Trim();
                            if (!string.IsNullOrWhiteSpace(apiKey))
                            {
                                s_config.nexusModsApiKey = apiKey;
                                Logger.LogVerbose($"Loaded Nexus Mods API key from legacy config: {legacyConfigFile}");

                                SaveSettings();
                                Logger.LogVerbose("Migrated API key to settings.json");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to load legacy nexusmods.config: {ex.Message}");
                    }
                }
            }
        }

        private static void SaveSettings()
        {
            try
            {
                string configDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KOTORModSync"
                );
                Directory.CreateDirectory(configDir);
                string settingsPath = Path.Combine(configDir, "settings.json");

                SettingsData settings;
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(settingsPath);
                        settings = JsonConvert.DeserializeObject<SettingsData>(existingJson) ?? new SettingsData();
                        Logger.LogVerbose("Loaded existing settings for merge");
                    }
                    catch
                    {
                        settings = new SettingsData();
                        Logger.LogVerbose("Failed to load existing settings, creating new");
                    }
                }
                else
                {
                    settings = new SettingsData();
                }

                if (string.IsNullOrEmpty(settings.Theme))
                {
                    settings.Theme = "SukiUI.Light";
                }

                settings.SourcePath = s_config.sourcePathFullName;
                settings.DestinationPath = s_config.destinationPathFullName;
                settings.DebugLogging = s_config.debugLogging;
                settings.AttemptFixes = s_config.attemptFixes;
                settings.NoAdmin = s_config.noAdmin;
                settings.CaseInsensitivePathing = s_config.caseInsensitivePathing;
                settings.ArchiveDeepCheck = s_config.archiveDeepCheck;
                settings.UseMultiThreadedIO = s_config.useMultiThreadedIO;
                settings.UseCopyForMoveActions = s_config.useCopyForMoveActions;
                settings.LastOutputDirectory = s_config.lastOutputDirectory?.FullName;
                settings.ValidateAndReplaceInvalidArchives = s_config.validateAndReplaceInvalidArchives;
                settings.FilterDownloadsByResolution = s_config.filterDownloadsByResolution;
                settings.NexusModsApiKey = s_config.nexusModsApiKey;
                settings.PatcherEngine = s_config.patcherEngine;
                settings.KPatcherExecutablePath = s_config.kpatcherExecutablePath;

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(settingsPath, json);

                Logger.LogVerbose($"Settings saved to: {settingsPath}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to save settings");
            }
        }

        private sealed class SettingsData
        {
            [JsonProperty("theme")]
            public string Theme { get; set; }

            [JsonProperty("sourcePath")]
            public string SourcePath { get; set; }

            [JsonProperty("destinationPath")]
            public string DestinationPath { get; set; }

            [JsonProperty("debugLogging")]
            public bool DebugLogging { get; set; }

            [JsonProperty("attemptFixes")]
            public bool AttemptFixes { get; set; } = true;

            [JsonProperty("noAdmin")]
            public bool NoAdmin { get; set; }

            [JsonProperty("caseInsensitivePathing")]
            public bool CaseInsensitivePathing { get; set; } = true;

            [JsonProperty("archiveDeepCheck")]
            public bool ArchiveDeepCheck { get; set; }

            [JsonProperty("useMultiThreadedIO")]
            public bool UseMultiThreadedIO { get; set; }

            [JsonProperty("useCopyForMoveActions")]
            public bool UseCopyForMoveActions { get; set; }

            [JsonProperty("lastOutputDirectory")]
            public string LastOutputDirectory { get; set; }

            [JsonProperty("validateAndReplaceInvalidArchives")]
            public bool ValidateAndReplaceInvalidArchives { get; set; } = true;

            [JsonProperty("filterDownloadsByResolution")]
            public bool FilterDownloadsByResolution { get; set; } = false;

            [JsonProperty("nexusModsApiKey")]
            public string NexusModsApiKey { get; set; }

            [JsonProperty("patcherEngine")]
            public string PatcherEngine { get; set; }

            [JsonProperty("kpatcherExecutablePath")]
            public string KPatcherExecutablePath { get; set; }
        }

        public class BaseOptions
        {
            [Option('v', "verbose", Required = false, HelpText = "Enable verbose output for debugging.")]
            public bool Verbose { get; set; }

            [Option("plaintext", Required = false, HelpText = "Use plaintext output instead of fancy ANSI progress display.")]
            public bool PlainText { get; set; }
        }

        [Verb("convert", HelpText = "Convert between formats or merge instruction sets, output to stdout or file")]
        public class ConvertOptions : BaseOptions
        {
            [Option('i', "input", Required = false, HelpText = "Input file path (for single file conversion)")]
            public string InputPath { get; set; }

            [Option('o', "output", Required = false, HelpText = "Output file path (if not specified, writes to stdout)")]
            public string OutputPath { get; set; }

            [Option('f', "format", Required = false, Default = "toml", HelpText = "Output format (toml, yaml, json, xml, ini, markdown)")]
            public string Format { get; set; }

            [Option('a', "auto", Required = false, HelpText = "Autogenerate instructions by pre-resolving URLs (does not download files)")]
            public bool AutoGenerate { get; set; }

            [Option('d', "download", Required = false, HelpText = "Download all mod files to source-path before processing (requires --source-path)")]
            public bool Download { get; set; }

            [Option('s', "select", Required = false, HelpText = "Select components by category or tier (format: 'category:Name' or 'tier:Name'). Can be specified multiple times.")]
            public IEnumerable<string> Select { get; set; }

            [Option("source-path", Required = false, HelpText = "Path to source directory containing downloaded mod files")]
            public string SourcePath { get; set; }

            [Option("nexus-mods-api-key", Required = false, HelpText = "Nexus Mods API key (overrides stored key from settings.json)")]
            public string NexusModsApiKey { get; set; }

            [Option('m', "merge", Required = false, HelpText = "Merge mode: merge two instruction sets (requires --existing and --incoming)")]
            public bool Merge { get; set; }

            [Option('e', "existing", Required = false, HelpText = "Existing instruction set file path (for merge mode)")]
            public string ExistingPath { get; set; }

            [Option('n', "incoming", Required = false, HelpText = "Incoming instruction set file path (for merge mode)")]
            public string IncomingPath { get; set; }

            [Option("exclude-existing-only", Required = false, HelpText = "[Merge] Remove components that exist only in EXISTING")]
            public bool ExcludeExistingOnly { get; set; }

            [Option("exclude-incoming-only", Required = false, HelpText = "[Merge] Remove components that exist only in INCOMING")]
            public bool ExcludeIncomingOnly { get; set; }

            [Option("use-existing-order", Required = false, HelpText = "[Merge] Use EXISTING component order (default: INCOMING order)")]
            public bool UseExistingOrder { get; set; }

            [Option("prefer-existing-fields", Required = false, HelpText = "[Merge] Prefer EXISTING values for ALL fields when both exist (default: prefer INCOMING)")]
            public bool PreferExistingFields { get; set; }

            [Option("prefer-incoming-fields", Required = false, HelpText = "[Merge] Prefer INCOMING values for ALL fields when both exist (default behavior)")]
            public bool PreferIncomingFields { get; set; }

            [Option("prefer-existing-name", Required = false, HelpText = "[Merge] Prefer EXISTING name when both exist")]
            public bool PreferExistingName { get; set; }

            [Option("prefer-existing-author", Required = false, HelpText = "[Merge] Prefer EXISTING author when both exist")]
            public bool PreferExistingAuthor { get; set; }

            [Option("prefer-existing-description", Required = false, HelpText = "[Merge] Prefer EXISTING description when both exist")]
            public bool PreferExistingDescription { get; set; }

            [Option("prefer-existing-directions", Required = false, HelpText = "[Merge] Prefer EXISTING directions when both exist")]
            public bool PreferExistingDirections { get; set; }

            [Option("prefer-existing-category", Required = false, HelpText = "[Merge] Prefer EXISTING category when both exist")]
            public bool PreferExistingCategory { get; set; }

            [Option("prefer-existing-tier", Required = false, HelpText = "[Merge] Prefer EXISTING tier when both exist")]
            public bool PreferExistingTier { get; set; }

            [Option("prefer-existing-installation-method", Required = false, HelpText = "[Merge] Prefer EXISTING installation method when both exist")]
            public bool PreferExistingInstallationMethod { get; set; }

            [Option("prefer-existing-instructions", Required = false, HelpText = "[Merge] Prefer EXISTING instructions when both exist")]
            public bool PreferExistingInstructions { get; set; }

            [Option("prefer-existing-options", Required = false, HelpText = "[Merge] Prefer EXISTING options when both exist")]
            public bool PreferExistingOptions { get; set; }

            [Option("prefer-existing-modlinks", Required = false, HelpText = "[Merge] Prefer EXISTING mod link filenames when both exist")]
            public bool PreferExistingModLinks { get; set; }

            [Option("concurrent", Required = false, HelpText = "Process downloads concurrently/in parallel instead of sequentially (faster but harder to debug) (default: false, sequential)")]
            public bool Concurrent { get; set; }

            [Option("ignore-errors", Required = false, HelpText = "Ignore dependency resolution errors and attempt to load components in the best possible order")]
            public bool IgnoreErrors { get; set; }

            [Option("spoiler-free", Required = false, HelpText = "Path to spoiler-free markdown file to populate spoiler-free content fields")]
            public string SpoilerFreePath { get; set; }
        }

        [Verb("merge", HelpText = "Merge two instruction sets together")]
        public class MergeOptions : BaseOptions
        {
            [Option('e', "existing", Required = true, HelpText = "Existing instruction set file path")]
            public string ExistingPath { get; set; }

            [Option('n', "incoming", Required = true, HelpText = "Incoming instruction set file path")]
            public string IncomingPath { get; set; }

            [Option('o', "output", Required = false, HelpText = "Output file path (if not specified, writes to stdout)")]
            public string OutputPath { get; set; }

            [Option('f', "format", Required = false, Default = "toml", HelpText = "Output format (toml, yaml, json, xml, ini, markdown)")]
            public string Format { get; set; }

            [Option('d', "download", Required = false, HelpText = "Download all mod files to source-path before processing (requires --source-path)")]
            public bool Download { get; set; }

            [Option('s', "select", Required = false, HelpText = "Select components by category or tier (format: 'category:Name' or 'tier:Name'). Can be specified multiple times.")]
            public IEnumerable<string> Select { get; set; }

            [Option("source-path", Required = false, HelpText = "Path to source directory containing downloaded mod files")]
            public string SourcePath { get; set; }

            [Option("nexus-mods-api-key", Required = false, HelpText = "Nexus Mods API key (overrides stored key from settings.json)")]
            public string NexusModsApiKey { get; set; }

            [Option("exclude-existing-only", Required = false, HelpText = "Remove components that exist only in EXISTING")]
            public bool ExcludeExistingOnly { get; set; }

            [Option("exclude-incoming-only", Required = false, HelpText = "Remove components that exist only in INCOMING")]
            public bool ExcludeIncomingOnly { get; set; }

            [Option("use-existing-order", Required = false, HelpText = "Use EXISTING component order (default: INCOMING order)")]
            public bool UseExistingOrder { get; set; }

            [Option("prefer-existing-fields", Required = false, HelpText = "Prefer EXISTING values for ALL fields when both exist (default: prefer INCOMING)")]
            public bool PreferExistingFields { get; set; }

            [Option("prefer-incoming-fields", Required = false, HelpText = "Prefer INCOMING values for ALL fields when both exist (default behavior)")]
            public bool PreferIncomingFields { get; set; }

            [Option("prefer-existing-name", Required = false, HelpText = "Prefer EXISTING name when both exist")]
            public bool PreferExistingName { get; set; }

            [Option("prefer-existing-author", Required = false, HelpText = "Prefer EXISTING author when both exist")]
            public bool PreferExistingAuthor { get; set; }

            [Option("prefer-existing-description", Required = false, HelpText = "Prefer EXISTING description when both exist")]
            public bool PreferExistingDescription { get; set; }

            [Option("prefer-existing-directions", Required = false, HelpText = "Prefer EXISTING directions when both exist")]
            public bool PreferExistingDirections { get; set; }

            [Option("prefer-existing-category", Required = false, HelpText = "Prefer EXISTING category when both exist")]
            public bool PreferExistingCategory { get; set; }

            [Option("prefer-existing-tier", Required = false, HelpText = "Prefer EXISTING tier when both exist")]
            public bool PreferExistingTier { get; set; }

            [Option("prefer-existing-installation-method", Required = false, HelpText = "Prefer EXISTING installation method when both exist")]
            public bool PreferExistingInstallationMethod { get; set; }

            [Option("prefer-existing-instructions", Required = false, HelpText = "Prefer EXISTING instructions when both exist")]
            public bool PreferExistingInstructions { get; set; }

            [Option("prefer-existing-options", Required = false, HelpText = "Prefer EXISTING options when both exist")]
            public bool PreferExistingOptions { get; set; }

            [Option("prefer-existing-modlinks", Required = false, HelpText = "Prefer EXISTING mod link filenames when both exist")]
            public bool PreferExistingModLinks { get; set; }

            [Option("concurrent", Required = false, HelpText = "Process downloads concurrently/in parallel instead of sequentially (faster but harder to debug) (default: false, sequential)")]
            public bool Concurrent { get; set; }

            [Option("ignore-errors", Required = false, HelpText = "Ignore dependency resolution errors and attempt to load components in the best possible order")]
            public bool IgnoreErrors { get; set; }

            [Option("spoiler-free", Required = false, HelpText = "Path to spoiler-free markdown file to populate spoiler-free content fields")]
            public string SpoilerFreePath { get; set; }
        }

        [Verb("validate", HelpText = "Validate instruction files for errors")]
        public class ValidateOptions : BaseOptions
        {
            [Option('i', "input", Required = true, HelpText = "Input file path to validate")]
            public string InputPath { get; set; }

            [Option('g', "game-dir", Required = false, HelpText = "Game installation directory (for full validation)")]
            public string GameDirectory { get; set; }

            [Option('s', "source-dir", Required = false, HelpText = "Source directory containing mod files (for file existence checks)")]
            public string SourceDirectory { get; set; }

            [Option("select", Required = false, HelpText = "Select components to validate (format: 'category:Name' or 'tier:Name'). Can be specified multiple times.")]
            public IEnumerable<string> Select { get; set; }

            [Option("full", Required = false, Default = false, HelpText = "Perform full validation including environment checks (requires --game-dir and --source-dir)")]
            public bool FullValidation { get; set; }

            [Option("errors-only", Required = false, Default = false, HelpText = "Only show errors, suppress warnings and info messages")]
            public bool ErrorsOnly { get; set; }

            [Option("ignore-errors", Required = false, HelpText = "Ignore dependency resolution errors and attempt to load components in the best possible order")]
            public bool IgnoreErrors { get; set; }
        }

        [Verb("install", HelpText = "Install mods from an instruction file")]
        public class InstallOptions : BaseOptions
        {
            [Option('i', "input", Required = true, HelpText = "Instruction file path")]
            public string InputPath { get; set; }

            [Option('g', "game-dir", Required = true, HelpText = "Game installation directory")]
            public string GameDirectory { get; set; }

            [Option('s', "source-dir", Required = false, HelpText = "Source directory containing mod files (defaults to input file directory)")]
            public string SourceDirectory { get; set; }

            [Option("select", Required = false, HelpText = "Select components to install (format: 'category:Name' or 'tier:Name'). Can be specified multiple times. If not specified, all selected mods in the file will be installed.")]
            public IEnumerable<string> Select { get; set; }

            [Option("no-checkpoint", Required = false, Default = false, HelpText = "Disable checkpoint system (not recommended)")]
            public bool NoCheckpoint { get; set; }

            [Option("skip-validation", Required = false, Default = false, HelpText = "Skip pre-installation validation (not recommended)")]
            public bool SkipValidation { get; set; }

            [Option('y', "yes", Required = false, Default = false, HelpText = "Automatically answer 'yes' to all prompts")]
            public bool AutoConfirm { get; set; }

            [Option("ignore-errors", Required = false, Default = false, HelpText = "Ignore dependency resolution errors and attempt to load components in the best possible order")]
            public bool IgnoreErrors { get; set; }
        }

        [Verb("set-nexus-api-key", HelpText = "Set and validate your Nexus Mods API key")]
        public class SetNexusApiKeyOptions : BaseOptions
        {
            [Value(0, Required = true, MetaName = "api-key", HelpText = "Your Nexus Mods API key")]
            public string ApiKey { get; set; }

            [Option("skip-validation", Required = false, Default = false, HelpText = "Skip API key validation")]
            public bool SkipValidation { get; set; }
        }

        [Verb("install-python-deps", HelpText = "Install Python dependencies for HoloPatcher at build time")]
        public class InstallPythonDepsOptions : BaseOptions
        {
            [Option("force", Required = false, Default = false, HelpText = "Force reinstall even if dependencies are already installed")]
            public bool Force { get; set; }
        }

        [Verb("holopatcher", HelpText = "Run HoloPatcher with optional arguments")]
        public class HolopatcherOptions : BaseOptions
        {
            [Option('a', "args", Required = false, Default = "", HelpText = "Arguments to pass to HoloPatcher")]
            public string Arguments { get; set; }
        }

        [Verb("cache-stats", HelpText = "Show distributed cache statistics")]
        public class CacheStatsOptions : BaseOptions
        {
            [Option('j', "json", Required = false, Default = false, HelpText = "Output in JSON format")]
            public bool JsonOutput { get; set; }
        }

        [Verb("cache-clear", HelpText = "Clear distributed cache")]
        public class CacheClearOptions : BaseOptions
        {
            [Option('f', "force", Required = false, Default = false, HelpText = "Force clear without confirmation")]
            public bool Force { get; set; }

            [Option('p', "provider", Required = false, HelpText = "Clear cache for specific provider only")]
            public string Provider { get; set; }
        }

        [Verb("cache-block", HelpText = "Block a ContentId from being used in distributed cache")]
        public class CacheBlockOptions : BaseOptions
        {
            [Value(0, Required = true, MetaName = "content-id", HelpText = "ContentId to block")]
            public string ContentId { get; set; }

            [Option('r', "reason", Required = false, HelpText = "Reason for blocking")]
            public string Reason { get; set; }
        }

        [Verb("cache-test", HelpText = "Run distributed cache integration tests")]
        public class CacheTestOptions : BaseOptions
        {
            [Option('c', "category", Required = false, HelpText = "Test category to run (ContentId, Seeding, Port, Engine, Integration, All)")]
            public string Category { get; set; } = "All";

            [Option('d', "docker", Required = false, Default = false, HelpText = "Include Docker-based tests (requires Docker/Podman)")]
            public bool IncludeDocker { get; set; }

            [Option("timeout", Required = false, Default = 300, HelpText = "Test timeout in seconds")]
            public int TimeoutSeconds { get; set; }
        }

        [Verb("cache-seed", HelpText = "Start long-running seeding operation for distributed cache")]
        public class CacheSeedOptions : BaseOptions
        {
            [Option('t', "toml", Required = true, HelpText = "Path to TOML file to seed")]
            public string TomlPath { get; set; }

            [Option('d', "duration", Required = false, Default = 21600, HelpText = "Seeding duration in seconds (default: 6 hours)")]
            public int DurationSeconds { get; set; }

            [Option('l', "limit", Required = false, Default = 10, HelpText = "Maximum number of concurrent seeds")]
            public int ConcurrentLimit { get; set; }

            [Option('s', "source-path", Required = true, HelpText = "Path to directory containing downloaded mod files")]
            public string SourcePath { get; set; }
        }

        public static int Run(string[] args)
        {
            // Disable keyring BEFORE any Python initialization to prevent pip hanging
            // This must be set at the process level before Python.Included initializes
            Environment.SetEnvironmentVariable("PYTHON_KEYRING_BACKEND", "keyring.backends.null.Keyring");
            Environment.SetEnvironmentVariable("DISPLAY", "");  // Also disable X11 display waiting

            Logger.Initialize();

            var parser = new Parser(with => with.HelpWriter = Console.Out);

            return parser.ParseArguments<ConvertOptions, MergeOptions, ValidateOptions, InstallOptions, SetNexusApiKeyOptions, InstallPythonDepsOptions, HolopatcherOptions, CacheStatsOptions, CacheClearOptions, CacheBlockOptions, CacheTestOptions, CacheSeedOptions>(args)
            .MapResult(
                (ConvertOptions opts) => RunConvertAsync(opts).GetAwaiter().GetResult(),
                (MergeOptions opts) => RunMergeAsync(opts).GetAwaiter().GetResult(),
                (ValidateOptions opts) => RunValidateAsync(opts).GetAwaiter().GetResult(),
                (InstallOptions opts) => RunInstallAsync(opts).GetAwaiter().GetResult(),
                (SetNexusApiKeyOptions opts) => RunSetNexusApiKeyAsync(opts).GetAwaiter().GetResult(),
                (InstallPythonDepsOptions opts) => RunInstallPythonDepsAsync(opts).GetAwaiter().GetResult(),
                (HolopatcherOptions opts) => RunHolopatcherAsync(opts).GetAwaiter().GetResult(),
                (CacheStatsOptions opts) => RunCacheStatsAsync(opts).GetAwaiter().GetResult(),
                (CacheClearOptions opts) => RunCacheClearAsync(opts).GetAwaiter().GetResult(),
                (CacheBlockOptions opts) => RunCacheBlockAsync(opts).GetAwaiter().GetResult(),
                (CacheTestOptions opts) => RunCacheTestAsync(opts).GetAwaiter().GetResult(),
                (CacheSeedOptions opts) => RunCacheSeedAsync(opts).GetAwaiter().GetResult(),
                errs => 1);
        }

        private static void SetVerboseMode(bool verbose)
        {
            _ = new MainConfig { debugLogging = verbose };
        }

        /// <summary>
        /// Handles dependency resolution errors in CLI mode.
        /// If ignoreErrors is true, attempts to resolve with errors ignored.
        /// Otherwise, prints comprehensive error information and fails.
        /// </summary>
        private static IReadOnlyList<ModComponent> HandleDependencyResolutionErrors(
            List<ModComponent> components,
            bool ignoreErrors,
            string operationContext)
        {
            try
            {
                DependencyResolutionResult resolutionResult = DependencyResolverService.ResolveDependencies(components, ignoreErrors);

                if (resolutionResult.Success)
                {
                    Logger.LogVerbose($"Successfully resolved dependencies for {resolutionResult.OrderedComponents.Count} components");
                    return resolutionResult.OrderedComponents;
                }

                if (ignoreErrors)
                {
                    Logger.LogWarning($"Dependency resolution failed with {resolutionResult.Errors.Count} errors, but --ignore-errors flag was specified. Attempting to load in best possible order.");
                    return resolutionResult.OrderedComponents;
                }

                Logger.LogError($"Dependency resolution failed with {resolutionResult.Errors.Count} errors:");
                Logger.LogError("");

                foreach (DependencyError error in resolutionResult.Errors)
                {
                    Logger.LogError($"❌ {error.ComponentName}: {error.Message}");
                    if (error.AffectedComponents.Count > 0)
                    {
                        Logger.LogError($"   Affected components: {string.Join(", ", error.AffectedComponents)}");
                    }
                }

                Logger.LogError("");
                Logger.LogError("To resolve these issues, you can:");
                Logger.LogError("1. Fix the dependency relationships manually in your instruction file");
                Logger.LogError("2. Use the --ignore-errors flag to attempt loading in the best possible order");
                Logger.LogError("3. Use the GUI to auto-fix dependencies or remove all dependencies");
                Logger.LogError("");
                Logger.LogError($"Operation '{operationContext}' failed due to dependency resolution errors.");

                throw new InvalidOperationException($"Dependency resolution failed with {resolutionResult.Errors.Count} errors. Use --ignore-errors flag to attempt loading in best possible order.");
            }
            catch (Exception ex)
            {
                if (ignoreErrors)
                {
                    Logger.LogWarning($"Dependency resolution failed with exception: {ex.Message}. Continuing with original order due to --ignore-errors flag.");
                    return components;
                }

                Logger.LogError($"Dependency resolution failed with exception: {ex.Message}");
                throw;
            }
        }

        private static void LogAllErrors(DownloadCacheService downloadCache, bool forceConsoleOutput = false)
        {
            bool hasDownloadFailures = downloadCache?.GetFailures().Count > 0;
            bool hasOtherErrors = s_errorCollector?.GetErrorCount() > 0;

            if (!hasDownloadFailures && !hasOtherErrors)
            {
                if (forceConsoleOutput)
                {
                    Console.WriteLine("No errors to report.");
                    Console.Out.Flush();
                }
                return;
            }

            void WriteOutput(string message)
            {
                if (forceConsoleOutput)
                {
                    Console.WriteLine(message);
                    Console.Out.Flush();
                }
                else if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog(message);
                }
                else
                {
                    Logger.Log(message);
                }
            }

            WriteOutput("");
            WriteOutput(new string('=', 80));
            WriteOutput("ERROR AND FAILURE SUMMARY");
            WriteOutput(new string('=', 80));

            // Display errors by category
            if (hasOtherErrors)
            {
                IReadOnlyDictionary<ErrorCollector.ErrorCategory, List<ErrorCollector.ErrorInfo>> errorsByCategory = s_errorCollector.GetErrorsByCategory();

                foreach (KeyValuePair<ErrorCollector.ErrorCategory, List<ErrorCollector.ErrorInfo>> categoryGroup in errorsByCategory.OrderBy(kvp => kvp.Key.ToString(), StringComparer.Ordinal))
                {
                    WriteOutput("");
                    WriteOutput($"▼ {categoryGroup.Key} Errors ({categoryGroup.Value.Count}):");
                    WriteOutput(new string('-', 80));

                    foreach (ErrorCollector.ErrorInfo error in categoryGroup.Value)
                    {
                        string componentPrefix = !string.IsNullOrWhiteSpace(error.ComponentName)
                            ? $"[{error.ComponentName}] "
                            : "";

                        WriteOutput($"  ✗ {componentPrefix}{error.Message}");

                        if (!string.IsNullOrWhiteSpace(error.Details))
                        {
                            WriteOutput($"    Details: {error.Details}");
                        }

                        // Display full log context leading up to the error
                        if (error.LogContext != null && error.LogContext.Count > 0)
                        {
                            WriteOutput("");
                            WriteOutput("    ═══ Log Context (leading up to error) ═══");
                            foreach (string logLine in error.LogContext)
                            {
                                WriteOutput($"    {logLine}");
                            }
                            WriteOutput("    ═══════════════════════════════════════");
                        }

                        if (error.Exception != null)
                        {
                            WriteOutput("");
                            WriteOutput($"    Exception: {error.Exception.GetType().Name} - {error.Exception.Message}");
                            WriteOutput($"    Stack trace:");
                            if (!string.IsNullOrWhiteSpace(error.Exception.StackTrace))
                            {
                                // Split stack trace by lines and indent each line
                                string[] stackLines = error.Exception.StackTrace.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                                foreach (string line in stackLines)
                                {
                                    WriteOutput($"      {line}");
                                }
                            }
                        }

                        WriteOutput("");
                    }
                }
            }

            // Display download failures
            if (hasDownloadFailures)
            {
                IReadOnlyList<DownloadCacheService.DownloadFailureInfo> failures = downloadCache.GetFailures();
                var failuresWithBoth = new List<DownloadCacheService.DownloadFailureInfo>();
                var failuresUrlOnly = new List<DownloadCacheService.DownloadFailureInfo>();
                var failuresFileOnly = new List<DownloadCacheService.DownloadFailureInfo>();

                foreach (DownloadCacheService.DownloadFailureInfo failure in failures)
                {
                    bool hasUrl = !string.IsNullOrWhiteSpace(failure.Url);
                    bool hasFile = !string.IsNullOrWhiteSpace(failure.ExpectedFileName);

                    if (hasUrl && hasFile)
                    {
                        failuresWithBoth.Add(failure);
                    }
                    else if (hasUrl)
                    {
                        failuresUrlOnly.Add(failure);
                    }
                    else if (hasFile)
                    {
                        failuresFileOnly.Add(failure);
                    }
                }

                WriteOutput("");
                WriteOutput($"▼ Download and File Failures ({failures.Count}):");
                WriteOutput(new string('-', 80));

                if (failuresWithBoth.Count > 0)
                {
                    WriteOutput("");
                    WriteOutput($"  Failed Downloads with Expected Filenames ({failuresWithBoth.Count}):");

                    foreach (DownloadCacheService.DownloadFailureInfo failure in failuresWithBoth)
                    {
                        WriteOutput($"    [{failure.ComponentName}] {failure.Url} → {failure.ExpectedFileName}");
                        if (!string.IsNullOrWhiteSpace(failure.ErrorMessage))
                        {
                            WriteOutput($"      Error: {failure.ErrorMessage}");
                        }

                        // Display full log context leading up to the failure
                        if (failure.LogContext != null && failure.LogContext.Count > 0)
                        {
                            WriteOutput("");
                            WriteOutput("      ═══ Log Context (leading up to failure) ═══");
                            foreach (string logLine in failure.LogContext)
                            {
                                WriteOutput($"      {logLine}");
                            }
                            WriteOutput("      ════════════════════════════════════════════");
                            WriteOutput("");
                        }
                    }
                }

                if (failuresUrlOnly.Count > 0)
                {
                    WriteOutput("");
                    WriteOutput($"  Failed URLs (No Filename Resolved) ({failuresUrlOnly.Count}):");

                    foreach (DownloadCacheService.DownloadFailureInfo failure in failuresUrlOnly)
                    {
                        WriteOutput($"    [{failure.ComponentName}] {failure.Url}");
                        if (!string.IsNullOrWhiteSpace(failure.ErrorMessage))
                        {
                            WriteOutput($"      Error: {failure.ErrorMessage}");
                        }

                        // Display full log context leading up to the failure
                        if (failure.LogContext != null && failure.LogContext.Count > 0)
                        {
                            WriteOutput("");
                            WriteOutput("      ═══ Log Context (leading up to failure) ═══");
                            foreach (string logLine in failure.LogContext)
                            {
                                WriteOutput($"      {logLine}");
                            }
                            WriteOutput("      ════════════════════════════════════════════");
                            WriteOutput("");
                        }
                    }
                }

                if (failuresFileOnly.Count > 0)
                {
                    WriteOutput("");
                    WriteOutput($"  Missing Files (No URL) ({failuresFileOnly.Count}):");

                    foreach (DownloadCacheService.DownloadFailureInfo failure in failuresFileOnly)
                    {
                        WriteOutput($"    [{failure.ComponentName}] {failure.ExpectedFileName}");
                        if (!string.IsNullOrWhiteSpace(failure.ErrorMessage))
                        {
                            WriteOutput($"      Error: {failure.ErrorMessage}");
                        }

                        // Display full log context leading up to the failure
                        if (failure.LogContext != null && failure.LogContext.Count > 0)
                        {
                            WriteOutput("");
                            WriteOutput("      ═══ Log Context (leading up to failure) ═══");
                            foreach (string logLine in failure.LogContext)
                            {
                                WriteOutput($"      {logLine}");
                            }
                            WriteOutput("      ════════════════════════════════════════════");
                            WriteOutput("");
                        }
                    }
                }
            }

            WriteOutput("");
            WriteOutput(new string('=', 80));
            int totalErrors = (s_errorCollector?.GetErrorCount() ?? 0) + (downloadCache?.GetFailures()?.Count ?? 0);
            WriteOutput($"TOTAL ERRORS/FAILURES: {totalErrors}");
            WriteOutput(new string('=', 80));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task<DownloadCacheService> DownloadAllModFilesAsync(List<ModComponent> components, string destinationDirectory, bool verbose, bool sequential = true, CancellationToken cancellationToken = default)
        {
            int componentCount = components.Count(c => c.ResourceRegistry != null && c.ResourceRegistry.Count > 0);
            if (componentCount == 0)
            {
                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog("No components with URLs found to download");
                }
                else
                {
                    await Logger.LogVerboseAsync("No components with URLs found to download").ConfigureAwait(false);
                }

                return null;
            }

            string message = $"Processing {componentCount} component(s) for download...";
            if (s_progressDisplay != null)
            {
                s_progressDisplay.WriteScrollingLog(message);
            }
            else
            {
                await Logger.LogAsync(message).ConfigureAwait(false);
            }

            var downloadCache = new DownloadCacheService();
            s_globalDownloadCache = downloadCache;

            DownloadManager downloadManager = Services.Download.DownloadHandlerFactory.CreateDownloadManager(
                nexusModsApiKey: s_config.nexusModsApiKey);

            downloadCache.SetDownloadManager(downloadManager);

            var lastLoggedProgress = new Dictionary<string, double>(StringComparer.Ordinal);
            object progressLock = new object();

            var progressReporter = new Progress<DownloadProgress>(progress =>
            {
                string fileName = Path.GetFileName(progress.FilePath ?? progress.Url);
                string progressKey = $"{progress.ModName}:{fileName}";

                if (progress.Status == DownloadStatus.InProgress)
                {
                    if (s_progressDisplay != null)
                    {
                        string displayText = $"{progress.ModName}: {fileName}";
                        s_progressDisplay.UpdateProgress(progressKey, displayText, progress.ProgressPercentage, "downloading");
                    }
                    else if (verbose)
                    {
                        bool shouldLog = false;
                        lock (progressLock)
                        {
                            if (!lastLoggedProgress.TryGetValue(progressKey, out double lastProgress))
                            {
                                shouldLog = true;
                            }
                            else if (progress.ProgressPercentage - lastProgress >= 10.0)
                            {
                                shouldLog = true;
                            }

                            if (shouldLog)
                            {
                                lastLoggedProgress[progressKey] = progress.ProgressPercentage;
                            }
                        }

                        if (shouldLog)
                        {
                            Logger.LogVerbose($"[Download] {progress.ModName}: {progress.ProgressPercentage:F1}% - {fileName}");
                        }
                    }
                }
                else if (progress.Status == DownloadStatus.Completed)
                {
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.RemoveProgress(progressKey);
                        s_progressDisplay.WriteScrollingLog($"✓ Downloaded: {fileName}");
                    }
                    else
                    {
                        Logger.Log($"[Download] Completed: {fileName}");
                    }
                    lock (progressLock)
                    {
                        lastLoggedProgress.Remove(progressKey);
                    }
                }
                else if (progress.Status == DownloadStatus.Failed)
                {
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.RemoveProgress(progressKey);
                        s_progressDisplay.AddFailedItem(progress.Url, progress.ErrorMessage);
                        s_progressDisplay.WriteScrollingLog($"✗ Failed: {fileName}");
                    }
                    else
                    {
                        Logger.LogError($"[Download] Failed: {fileName} - {progress.ErrorMessage}");
                    }
                    lock (progressLock)
                    {
                        lastLoggedProgress.Remove(progressKey);
                    }
                }
                else if (progress.Status == DownloadStatus.Skipped)
                {
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog($"⊙ Skipped (exists): {fileName}");
                    }
                    else
                    {
                        Logger.Log($"[Download] Skipped (already exists): {fileName}");
                    }
                }
            });

            try
            {
                var componentsToProcess = components.Where(c => c.ResourceRegistry != null && c.ResourceRegistry.Count > 0).ToList();


                await Logger.LogVerboseAsync($"[Download] Processing {componentsToProcess.Count} components with concurrency limit of 10").ConfigureAwait(false);

                using (var semaphore = new SemaphoreSlim(10))
                {
                    var downloadTasks = componentsToProcess.Select(async component =>


                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await Logger.LogVerboseAsync($"[Download] Processing component: {component.Name} ({component.ResourceRegistry.Count} URL(s))").ConfigureAwait(false);

                        try
                        {
                            IReadOnlyList<DownloadCacheService.DownloadCacheEntry> readOnlyResults = await downloadCache.ResolveOrDownloadAsync(
                                component,
                                destinationDirectory,
                                progressReporter,
                                sequential: sequential,
                                cancellationToken).ConfigureAwait(false);
                            var results = readOnlyResults.ToList();

                            int successCount = results.Count(entry =>
                            {
                                string filePath = MainConfig.SourcePath != null
                                    ? Path.Combine(MainConfig.SourcePath.FullName, entry.FileName)
                                    : Path.Combine(destinationDirectory, entry.FileName);
                                return File.Exists(filePath);
                            });

                            return (component, results, successCount, error: (string)null);
                        }
                        catch (Exception ex)
                        {
                            string errorMsg = $"Error processing component {component.Name}: {ex.Message}";
                            if (s_progressDisplay != null)
                            {
                                s_progressDisplay.WriteScrollingLog($"✗ {errorMsg}");
                            }
                            else
                            {
                                await Logger.LogErrorAsync(errorMsg).ConfigureAwait(false);
                            }

                            if (verbose)
                            {
                                await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                            }

                            s_errorCollector?.RecordError(
                                ErrorCollector.ErrorCategory.Download,
                                component.Name,
                                "Failed to process component for download",
                                errorMsg,
                                ex);

                            return (component, results: new List<DownloadCacheService.DownloadCacheEntry>(), successCount: 0, error: errorMsg);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                    (ModComponent component, List<DownloadCacheService.DownloadCacheEntry> results, int successCount, string error)[] downloadResults = await Task.WhenAll(downloadTasks).ConfigureAwait(false);

                    int totalSuccessCount = downloadResults.Sum(r => r.successCount);
                    int totalFailCount = downloadResults.Count(r => r.error != null);

                    string summaryMsg = $"Download results: {totalSuccessCount} files available, {totalFailCount} failed";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(summaryMsg);
                    }
                    else
                    {
                        await Logger.LogAsync(summaryMsg).ConfigureAwait(false);
                    }

                    if (totalFailCount > 0)
                    {
                        string warningMsg = "Some downloads failed. Check logs for details.";
                        if (s_progressDisplay != null)
                        {
                            s_progressDisplay.WriteScrollingLog($"⚠ {warningMsg}");
                        }
                        else
                        {
                            await Logger.LogWarningAsync(warningMsg).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error during download: {ex.Message}";
                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog($"✗ {errorMsg}");
                }
                else
                {
                    await Logger.LogErrorAsync(errorMsg).ConfigureAwait(false);
                }

                if (verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }

                s_errorCollector?.RecordError(
                    ErrorCollector.ErrorCategory.Download,
componentName: null,
                    "Critical error during download process",
                    errorMsg,
                    ex);

                throw;
            }

            return downloadCache;
        }

        private static void ApplySelectionFilters(
            List<ModComponent> components,
            IEnumerable<string> selections)
        {
            if (components is null)
            {
                return;
            }

            if (selections is null || !selections.Any())
            {
                foreach (ModComponent component in components)
                {
                    component.IsSelected = true;
                }
                return;
            }

            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedTiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string selection in selections)
            {
                if (string.IsNullOrWhiteSpace(selection))
                {
                    continue;
                }

                string[] parts = selection.Split(new[] { ':' }, 2);
                if (parts.Length != 2)
                {
                    Logger.LogWarning($"Invalid selection format: '{selection}'. Expected format: 'category:Name' or 'tier:Name'");
                    continue;
                }

                string type = parts[0].Trim().ToLowerInvariant();
                string value = parts[1].Trim();

                if (string.Equals(type, "category", StringComparison.Ordinal))
                {
                    selectedCategories.Add(value);
                    Logger.LogVerbose($"Added category filter: {value}");
                }
                else if (string.Equals(type, "tier", StringComparison.Ordinal))
                {
                    selectedTiers.Add(value);
                    Logger.LogVerbose($"Added tier filter: {value}");
                }
                else
                {
                    Logger.LogWarning($"Unknown selection type: '{type}'. Use 'category' or 'tier'");
                }
            }

            var tierPriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Essential", 1 },
                { "Recommended", 2 },
                { "Suggested", 3 },
                { "Optional", 4 },
            };

            int selectedCount = 0;

            foreach (ModComponent component in components)
            {
                bool includeByCategory = false;
                bool includeByTier = false;

                if (selectedCategories.Count > 0)
                {
                    if (component.Category != null && component.Category.Count > 0)
                    {
                        includeByCategory = component.Category.Any(cat => selectedCategories.Contains(cat));
                    }
                }
                else
                {
                    includeByCategory = true;
                }

                if (selectedTiers.Count > 0)
                {
                    if (!string.IsNullOrEmpty(component.Tier))
                    {
                        foreach (string selectedTier in selectedTiers)
                        {
                            if (tierPriorities.TryGetValue(selectedTier, out int selectedPriority) &&
                                tierPriorities.TryGetValue(component.Tier, out int componentPriority))
                            {
                                if (componentPriority <= selectedPriority)
                                {
                                    includeByTier = true;
                                    break;
                                }
                            }
                            else if (component.Tier.Equals(selectedTier, StringComparison.OrdinalIgnoreCase))
                            {
                                includeByTier = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    includeByTier = true;
                }

                if (includeByCategory && includeByTier)
                {
                    component.IsSelected = true;
                    selectedCount++;
                }
                else
                {
                    component.IsSelected = false;
                }
            }

            Logger.LogVerbose($"Selection filters applied: {selectedCount}/{components.Count} components selected");
            if (selectedCategories.Count > 0)
            {
                Logger.LogVerbose($"Categories: {string.Join(", ", selectedCategories)}");
            }
            if (selectedTiers.Count > 0)
            {
                Logger.LogVerbose($"Tiers: {string.Join(", ", selectedTiers)}");
            }
        }

        /// <summary>
        /// Parses a spoiler-free markdown file and applies spoiler-free content to matching components.
        /// The markdown file should contain ### Component Name headers with **Field:** content below.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task ApplySpoilerFreeContentAsync(
            List<ModComponent> components,
            string spoilerFreePath)
        {
            if (string.IsNullOrEmpty(spoilerFreePath) || !File.Exists(spoilerFreePath))
            {
                return;
            }

            try
            {
                string content = await Task.Run(() => File.ReadAllText(spoilerFreePath)).ConfigureAwait(false);
                Dictionary<string, Dictionary<string, string>> componentSpoilerFreeData = ParseSpoilerFreeMarkdown(content);

                if (componentSpoilerFreeData.Count == 0)
                {
                    await Logger.LogWarningAsync("No spoiler-free content found in markdown file").ConfigureAwait(false);
                    return;
                }

                int appliedCount = 0;
                foreach (ModComponent component in components)
                {
                    // Try to find a matching entry using component name
                    string matchKey = componentSpoilerFreeData.Keys.FirstOrDefault(
                        k => string.Equals(k, component.Name, StringComparison.OrdinalIgnoreCase));

                    if (matchKey != null && componentSpoilerFreeData.TryGetValue(matchKey, out Dictionary<string, string> spoilerFreeFields))
                    {
                        bool componentUpdated = false;

                        if (spoilerFreeFields.TryGetValue("description", out string descriptionSpoilerFree) && !string.IsNullOrWhiteSpace(descriptionSpoilerFree))
                        {
                            component.DescriptionSpoilerFree = descriptionSpoilerFree;
                            componentUpdated = true;
                        }

                        if (spoilerFreeFields.TryGetValue("directions", out string directionsSpoilerFree) && !string.IsNullOrWhiteSpace(directionsSpoilerFree))
                        {
                            component.DirectionsSpoilerFree = directionsSpoilerFree;
                            componentUpdated = true;
                        }

                        if (spoilerFreeFields.TryGetValue("downloadinstructions", out string downloadInstructionsSpoilerFree) && !string.IsNullOrWhiteSpace(downloadInstructionsSpoilerFree))
                        {
                            component.DownloadInstructionsSpoilerFree = downloadInstructionsSpoilerFree;
                            componentUpdated = true;
                        }

                        if (spoilerFreeFields.TryGetValue("usagewarning", out string usageWarningSpoilerFree) && !string.IsNullOrWhiteSpace(usageWarningSpoilerFree))
                        {
                            component.UsageWarningSpoilerFree = usageWarningSpoilerFree;
                            componentUpdated = true;
                        }

                        if (spoilerFreeFields.TryGetValue("screenshots", out string screenshotsSpoilerFree) && !string.IsNullOrWhiteSpace(screenshotsSpoilerFree))
                        {
                            component.ScreenshotsSpoilerFree = screenshotsSpoilerFree;
                            componentUpdated = true;
                        }

                        if (componentUpdated)
                        {
                            appliedCount++;
                            await Logger.LogVerboseAsync($"Applied spoiler-free content to component: {component.Name}").ConfigureAwait(false);
                        }
                    }
                }

                if (appliedCount > 0)
                {
                    await Logger.LogAsync($"Applied spoiler-free content to {appliedCount} component(s)").ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogWarningAsync("No matching components found for spoiler-free content").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                s_errorCollector?.RecordError(
                    ErrorCollector.ErrorCategory.FileOperation,
componentName: null,
                    "Failed to parse spoiler-free markdown file",
                    $"File: {spoilerFreePath}",
                    ex);
                await Logger.LogWarningAsync($"Error loading spoiler-free content: {ex.Message}").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Parses a spoiler-free markdown file and extracts component-specific content.
        /// Returns a dictionary mapping component names to their spoiler-free fields.
        ///
        /// Expected markdown format:
        ///
        /// ## Mod List
        ///
        /// ### Component Name
        /// **Name:** [Component Name](url)
        /// **Author:** Author Name
        /// **Description:** Spoiler-free description content
        /// **Directions:** Spoiler-free directions content
        /// **DownloadInstructions:** Spoiler-free download instructions
        /// **UsageWarning:** Spoiler-free usage warning
        /// **Screenshots:** Spoiler-free screenshots description
        ///
        /// :::note
        /// Installation Instructions
        /// :   Additional installation notes
        /// :::
        ///
        /// ___
        ///
        /// ### Another Component
        /// ...
        ///
        /// Field values can span multiple lines. Field names are case-insensitive.
        /// ":::note" blocks with "Installation Instructions" are mapped to "Directions".
        /// Only fields with non-empty values will be applied to components.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static Dictionary<string, Dictionary<string, string>> ParseSpoilerFreeMarkdown(string content)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string currentComponentName = null;
            Dictionary<string, string> currentFields = null;
            string currentFieldName = null;
            StringBuilder currentFieldValue = null;
            bool inModList = false;
            bool inNoteBlock = false;
            bool inInstallationInstructions = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmedLine = line.TrimStart();

                // Check for "## Mod List" to start processing components
                if (!inModList && trimmedLine.Equals("## Mod List", StringComparison.Ordinal))
                {
                    inModList = true;
                    continue;
                }

                // Skip everything before "## Mod List"
                if (!inModList)
                {
                    continue;
                }

                // Component header: ### Component Name
                if (trimmedLine.StartsWith("### ", StringComparison.Ordinal))
                {
                    // Save previous field before starting new component
                    if (currentFieldValue != null && currentFields != null)
                    {
                        currentFields[currentFieldName] = currentFieldValue.ToString().Trim();
                    }

                    // Start new component
                    currentComponentName = trimmedLine.Substring(4).Trim();
                    currentFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result[currentComponentName] = currentFields;
                    currentFieldName = null;
                    currentFieldValue = null;
                    inNoteBlock = false;
                    inInstallationInstructions = false;
                }
                // Detect start of note block: :::note
                else if (trimmedLine.Equals(":::note", StringComparison.Ordinal) || trimmedLine.Equals(":::warning", StringComparison.Ordinal) || trimmedLine.Equals(":::tip", StringComparison.Ordinal))
                {
                    inNoteBlock = true;
                    inInstallationInstructions = false;
                    // Save previous field value before starting note block
                    if (currentFieldName != null && currentFieldValue != null && currentFields != null)
                    {
                        currentFields[currentFieldName] = currentFieldValue.ToString().Trim();
                        currentFieldName = null;
                        currentFieldValue = null;
                    }
                }
                // Detect end of note block: :::
                else if (inNoteBlock && trimmedLine.Equals(":::", StringComparison.Ordinal))
                {
                    // Save Installation Instructions as Directions field
                    if (inInstallationInstructions && currentFieldValue != null && currentFields != null && !string.IsNullOrWhiteSpace(currentFieldValue.ToString()))
                    {
                        currentFields["directions"] = currentFieldValue.ToString().Trim();
                        currentFieldValue = null;
                    }
                    inNoteBlock = false;
                    inInstallationInstructions = false;
                }
                // Detect "Installation Instructions" line within note block
                else if (inNoteBlock && trimmedLine.Equals("Installation Instructions", StringComparison.Ordinal))
                {
                    inInstallationInstructions = true;
                    currentFieldValue = new StringBuilder();
                    currentFieldName = null; // Don't set a field name, we'll map to "directions" when closing the note block
                }
                // Lines starting with ":" after "Installation Instructions" are the content
                else if (trimmedLine.StartsWith(":", StringComparison.Ordinal) && currentFieldValue != null)
                {
                    string noteContent = trimmedLine.Substring(1).Trim();
                    if (currentFieldValue.Length > 0)
                    {
                        currentFieldValue.AppendLine();
                    }
                    currentFieldValue.Append(noteContent);
                }
                // Field marker: **Description:** or similar
                else if (trimmedLine.StartsWith("**", StringComparison.Ordinal) && trimmedLine.IndexOf(":**", StringComparison.Ordinal) >= 0 && currentComponentName != null)
                {
                    // Save previous field value
                    if (currentFieldValue != null && currentFields != null)
                    {
                        currentFields[currentFieldName] = currentFieldValue.ToString().Trim();
                    }

                    // Extract field name and value
                    int colonIndex = trimmedLine.IndexOf(":**", StringComparison.Ordinal);
                    if (colonIndex > 2)
                    {
                        currentFieldName = trimmedLine.Substring(2, colonIndex - 2).Trim().ToLowerInvariant();
                        string afterColon = trimmedLine.Substring(colonIndex + 3).Trim();
                        currentFieldValue = new StringBuilder(afterColon);
                    }
                }
                // Continuation of field value (but not in note blocks)
                else if (!inNoteBlock && currentFieldValue != null && !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("###", StringComparison.Ordinal) && !trimmedLine.StartsWith("**", StringComparison.Ordinal) && !trimmedLine.StartsWith("___", StringComparison.Ordinal))
                {
                    currentFieldValue.AppendLine();
                    currentFieldValue.Append(line.TrimStart());
                }
            }

            // Save last field and component
            if (currentFieldName != null)
            {
                currentFields[currentFieldName] = currentFieldValue.ToString().Trim();
            }

            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task<int> RunConvertAsync(ConvertOptions opts)
        {
            SetVerboseMode(opts.Verbose);

            s_progressDisplay = new ConsoleProgressDisplay(usePlainText: opts.PlainText);
            s_errorCollector = new ErrorCollector();

            DownloadCacheService downloadCache = null;

            ConsoleCancelEventHandler cancelHandler = (sender, e) =>
            {
                e.Cancel = true;

                try
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("====================================================================");
                    Console.Error.WriteLine("CTRL+C DETECTED - Cancellation in progress...");
                    Console.Error.WriteLine("====================================================================");
                    Console.Error.Flush();

                    try
                    {
                        s_progressDisplay?.Dispose();
                        s_progressDisplay = null;
                    }
                    catch (Exception disposeEx)
                    {
                        Console.Error.WriteLine($"Warning: Error disposing progress display: {disposeEx.Message}");
                    }

                    if (s_globalDownloadCache != null)
                    {
                        try
                        {
                            Console.Error.WriteLine();
                            Console.Error.WriteLine("Logging all errors and failures...");
                            Console.Error.Flush();

                            LogAllErrors(s_globalDownloadCache, forceConsoleOutput: true);

                            Console.Error.WriteLine();
                            Console.Error.WriteLine("Error logging complete.");
                            Console.Error.Flush();
                        }
                        catch (Exception logEx)
                        {
                            Console.Error.WriteLine($"Error logging failures: {logEx.Message}");
                            Console.Error.Flush();
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("No download cache to log (no downloads were performed).");
                        Console.Error.Flush();
                    }

                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Exiting...");
                    Console.Error.Flush();

                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Critical error in CTRL+C handler: {ex.Message}");
                    Console.Error.Flush();
                    Thread.Sleep(100);
                }
                finally
                {
                    Environment.Exit(1);
                }
            };

            Console.CancelKeyPress += cancelHandler;

            try
            {
                EnsureConfigInitialized();

                if (!string.IsNullOrWhiteSpace(opts.NexusModsApiKey))
                {
                    s_config.nexusModsApiKey = opts.NexusModsApiKey;


                    await Logger.LogVerboseAsync("Using Nexus Mods API key from command line argument").ConfigureAwait(false);
                }

                // Backward compatibility: redirect to RunMergeAsync if using --merge flag
                if (opts.Merge)
                {
                    s_progressDisplay?.Dispose();
                    s_progressDisplay = null;

                    var mergeOpts = new MergeOptions
                    {
                        ExistingPath = opts.ExistingPath,
                        IncomingPath = opts.IncomingPath,
                        OutputPath = opts.OutputPath,
                        Format = opts.Format,
                        Download = opts.Download,
                        Select = opts.Select,
                        SourcePath = opts.SourcePath,
                        NexusModsApiKey = opts.NexusModsApiKey,
                        ExcludeExistingOnly = opts.ExcludeExistingOnly,
                        ExcludeIncomingOnly = opts.ExcludeIncomingOnly,
                        UseExistingOrder = opts.UseExistingOrder,
                        PreferExistingFields = opts.PreferExistingFields,
                        PreferIncomingFields = opts.PreferIncomingFields,
                        PreferExistingName = opts.PreferExistingName,
                        PreferExistingAuthor = opts.PreferExistingAuthor,
                        PreferExistingDescription = opts.PreferExistingDescription,
                        PreferExistingDirections = opts.PreferExistingDirections,
                        PreferExistingCategory = opts.PreferExistingCategory,
                        PreferExistingTier = opts.PreferExistingTier,
                        PreferExistingInstallationMethod = opts.PreferExistingInstallationMethod,
                        PreferExistingInstructions = opts.PreferExistingInstructions,
                        PreferExistingOptions = opts.PreferExistingOptions,
                        PreferExistingModLinks = opts.PreferExistingModLinks,
                        Verbose = opts.Verbose,
                        PlainText = opts.PlainText,
                    };



                    return await RunMergeAsync(mergeOpts).ConfigureAwait(false);
                }

                // Convert mode validation
                if (string.IsNullOrEmpty(opts.InputPath))
                {


                    await Logger.LogErrorAsync("--input is required for convert mode").ConfigureAwait(false);
                    await Logger.LogAsync("Usage: convert --input <file> [options]").ConfigureAwait(false);
                    await Logger.LogAsync("   OR: Use the 'merge' command to merge two instruction sets").ConfigureAwait(false);
                    await Logger.LogAsync("       merge --existing <file> --incoming <file> [options]").ConfigureAwait(false);
                    await Logger.LogAsync("   OR: convert --merge --existing <file> --incoming <file> [options] (backward compatible)").ConfigureAwait(false);
                    return 1;
                }

                if (!File.Exists(opts.InputPath))
                {
                    await Logger.LogErrorAsync($"Input file not found: {opts.InputPath}").ConfigureAwait(false);
                    return 1;
                }

                await Logger.LogVerboseAsync($"Convert mode: {opts.InputPath}").ConfigureAwait(false);
                await Logger.LogVerboseAsync($"Output format: {opts.Format}").ConfigureAwait(false);

                if (opts.Download && string.IsNullOrEmpty(opts.SourcePath))
                {
                    await Logger.LogErrorAsync("--download requires --source-path to be specified").ConfigureAwait(false);
                    return 1;
                }

                if (!string.IsNullOrEmpty(opts.SourcePath))
                {
                    await Logger.LogVerboseAsync($"Source path provided: {opts.SourcePath}").ConfigureAwait(false);
                    if (!Directory.Exists(opts.SourcePath))
                    {
                        await Logger.LogVerboseAsync($"Source path does not exist, creating: {opts.SourcePath}").ConfigureAwait(false);
                        Directory.CreateDirectory(opts.SourcePath);
                    }

                    s_config.sourcePath = new DirectoryInfo(opts.SourcePath);
                    s_config.debugLogging = opts.Verbose;
                    await Logger.LogVerboseAsync($"Source path set to: {opts.SourcePath}").ConfigureAwait(false);
                }

                // Convert mode only (merge is now handled in RunMergeAsync)
                string msg = "Loading components from input file...";
                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog(msg);
                }
                else
                {
                    await Logger.LogVerboseAsync(msg).ConfigureAwait(false);
                }

                List<ModComponent> components;
                try
                {
                    components = await FileLoadingService.LoadFromFileAsync(opts.InputPath).ConfigureAwait(false);

                    // Handle dependency resolution
                    components = (List<ModComponent>)HandleDependencyResolutionErrors(components, opts.IgnoreErrors, "Convert");

                    msg = $"Loaded {components.Count} components";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(msg);
                    }
                    else
                    {
                        await Logger.LogVerboseAsync(msg).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    s_errorCollector?.RecordError(
                        ErrorCollector.ErrorCategory.FileOperation,
componentName: null,
                        "Failed to load components from file",
                        $"Input file: {opts.InputPath}",
                        ex);
                    throw;
                }

                if (opts.Download)
                {
                    msg = "Starting download of mod files...";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(msg);
                    }
                    else
                    {
                        await Logger.LogAsync(msg).ConfigureAwait(false);
                    }

                    using (var downloadCts = new CancellationTokenSource(TimeSpan.FromHours(2)))


                    {
                        downloadCache = await DownloadAllModFilesAsync(components, opts.SourcePath, opts.Verbose, sequential: !opts.Concurrent, downloadCts.Token).ConfigureAwait(false);
                    }

                    msg = "Download complete";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(msg);
                    }
                    else
                    {
                        await Logger.LogAsync(msg).ConfigureAwait(false);
                    }
                }

                if (opts.AutoGenerate)
                {
                    string message = "Auto-generating instructions from URLs...";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(message);
                    }
                    else
                    {
                        await Logger.LogVerboseAsync(message).ConfigureAwait(false);
                    }

                    if (downloadCache is null)
                    {
                        downloadCache = new DownloadCacheService();
                        downloadCache.SetDownloadManager();
                        s_globalDownloadCache = downloadCache;
                    }

                    int totalComponents = components.Count(c => c.ResourceRegistry != null && c.ResourceRegistry.Count > 0);
                    message = $"Processing {totalComponents} components sequentially...";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(message);
                    }
                    else
                    {
                        await Logger.LogVerboseAsync(message).ConfigureAwait(false);
                    }

                    int successCount = 0;
                    int currentIndex = 0;
                    foreach (ModComponent component in components.Where(c => c.ResourceRegistry != null && c.ResourceRegistry.Count > 0))
                    {
                        component.IsSelected = true;
                        currentIndex++;

                        string progressKey = $"autogen:{component.Name}";
                        double progressPercent = (double)currentIndex / totalComponents * 100.0;

                        if (s_progressDisplay != null)
                        {
                            s_progressDisplay.UpdateProgress(progressKey, component.Name, progressPercent, "processing");
                            s_progressDisplay.WriteScrollingLog($"[{currentIndex}/{totalComponents}] Processing: {component.Name}");
                        }
                        else
                        {
                            await Logger.LogAsync($"[Auto-Generate] Processing component: {component.Name}").ConfigureAwait(false);
                        }

                        bool success = false;
                        try
                        {
                            success = await AutoInstructionGenerator.GenerateInstructionsFromUrlsAsync(
                                component, downloadCache).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            s_errorCollector?.RecordError(
                                ErrorCollector.ErrorCategory.General,
                                component.Name,
                                "Auto-instruction generation failed",
                                $"Failed to generate instructions from URLs",
                                ex);
                            success = false;
                        }

                        if (s_progressDisplay != null)
                        {
                            s_progressDisplay.RemoveProgress(progressKey);
                        }

                        if (success)
                        {
                            if (s_progressDisplay != null)
                            {
                                s_progressDisplay.WriteScrollingLog($"✓ {component.Name}");
                            }
                            else
                            {
                                await Logger.LogVerboseAsync($"Auto-generation successful for component: {component.Name}").ConfigureAwait(false);
                            }

                            successCount++;
                        }
                        else
                        {
                            if (s_progressDisplay != null)
                            {
                                s_progressDisplay.WriteScrollingLog($"✗ Failed: {component.Name}");
                            }

                            // Record as error if not already recorded
                            if (!success)
                            {
                                s_errorCollector?.RecordError(
                                    ErrorCollector.ErrorCategory.General,
                                    component.Name,
                                    "Auto-instruction generation returned false",
                                    "Failed to generate instructions from URLs");
                            }
                        }
                    }

                    message = $"Auto-generation complete: {successCount}/{totalComponents} components processed successfully";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(message);
                    }
                    else
                    {
                        await Logger.LogVerboseAsync(message).ConfigureAwait(false);
                    }
                }

                ApplySelectionFilters(components, opts.Select);

                // Apply spoiler-free content if provided
                if (!string.IsNullOrEmpty(opts.SpoilerFreePath))
                {
                    await ApplySpoilerFreeContentAsync(components, opts.SpoilerFreePath).ConfigureAwait(false);
                }

                // ResourceRegistry is already populated during PreResolveUrlsAsync
                // No additional population needed before serialization

                // Create validation context to track issues for serialization
                var validationContext = new ComponentValidationContext();

                // Collect download failures from cache
                if (downloadCache != null)
                {
                    IReadOnlyList<DownloadCacheService.DownloadFailureInfo> failures = downloadCache.GetFailures();
                    foreach (DownloadCacheService.DownloadFailureInfo failure in failures)
                    {
                        validationContext.AddUrlFailure(failure.Url, failure.ErrorMessage);
                    }
                }

                // Collect validation issues from error collector
                if (s_errorCollector != null)
                {
                    foreach (ErrorCollector.ErrorInfo error in s_errorCollector.GetErrors())
                    {
                        // Try to find the component by name
                        ModComponent component = components.Find(c => string.Equals(c.Name, error.ComponentName, StringComparison.Ordinal));
                        if (component != null)
                        {
                            validationContext.AddModComponentIssue(component.Guid, error.Message);
                        }
                    }
                }

                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog("Serializing to output format...");
                }
                else
                {
                    await Logger.LogVerboseAsync("Serializing to output format...").ConfigureAwait(false);
                }

                string output = ModComponentSerializationService.SerializeModComponentAsString(components, opts.Format, validationContext);

                if (!string.IsNullOrEmpty(opts.OutputPath))
                {
                    string outputDir = Path.GetDirectoryName(opts.OutputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                        if (s_progressDisplay != null)
                        {
                            s_progressDisplay.WriteScrollingLog($"Created output directory: {outputDir}");
                        }
                        else
                        {
                            await Logger.LogVerboseAsync($"Created output directory: {outputDir}").ConfigureAwait(false);
                        }
                    }

                    await NetFrameworkCompatibility.WriteAllTextAsync(opts.OutputPath, output).ConfigureAwait(false);

                    string successMsg = $"✓ Conversion completed successfully, saved to: {opts.OutputPath}";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(successMsg);
                    }
                    else
                    {
                        await Logger.LogVerboseAsync($"Conversion completed successfully, saved to: {opts.OutputPath}").ConfigureAwait(false);
                    }
                }
                else
                {
                    s_progressDisplay?.Dispose();
                    s_progressDisplay = null;

                    await Logger.LogAsync(output).ConfigureAwait(false);
                    await Logger.LogVerboseAsync("Conversion completed successfully (output to stdout)").ConfigureAwait(false);
                }

                if (downloadCache != null || s_errorCollector != null)
                {
                    LogAllErrors(downloadCache);
                }

                return 0;
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error during conversion: {ex.Message}";
                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog($"✗ {errorMsg}");
                }
                else
                {
                    await Logger.LogErrorAsync(errorMsg).ConfigureAwait(false);
                }

                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }

                if (downloadCache != null || s_errorCollector != null)
                {
                    LogAllErrors(downloadCache);
                }

                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;

                s_progressDisplay?.Dispose();
                s_progressDisplay = null;

                s_globalDownloadCache = null;
                s_errorCollector = null;
            }
        }

        private static async Task<int> RunMergeAsync(MergeOptions opts)
        {
            SetVerboseMode(opts.Verbose);

            s_progressDisplay = new ConsoleProgressDisplay(usePlainText: opts.PlainText);
            s_errorCollector = new ErrorCollector();

            DownloadCacheService downloadCache = null;

            ConsoleCancelEventHandler cancelHandler = (sender, e) =>
            {
                e.Cancel = true;

                try
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("====================================================================");
                    Console.Error.WriteLine("CTRL+C DETECTED - Cancellation in progress...");
                    Console.Error.WriteLine("====================================================================");
                    Console.Error.Flush();

                    try
                    {
                        s_progressDisplay?.Dispose();
                        s_progressDisplay = null;
                    }
                    catch (Exception disposeEx)
                    {
                        Console.Error.WriteLine($"Warning: Error disposing progress display: {disposeEx.Message}");
                    }

                    if (s_globalDownloadCache != null)
                    {
                        try
                        {
                            Console.Error.WriteLine();
                            Console.Error.WriteLine("Logging all errors and failures...");
                            Console.Error.Flush();

                            LogAllErrors(s_globalDownloadCache, forceConsoleOutput: true);

                            Console.Error.WriteLine();
                            Console.Error.WriteLine("Error logging complete.");
                            Console.Error.Flush();
                        }
                        catch (Exception logEx)
                        {
                            Console.Error.WriteLine($"Error logging failures: {logEx.Message}");
                            Console.Error.Flush();
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("No download cache to log (no downloads were performed).");
                        Console.Error.Flush();
                    }

                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Exiting...");
                    Console.Error.Flush();

                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Critical error in CTRL+C handler: {ex.Message}");
                    Console.Error.Flush();
                    Thread.Sleep(100);
                }
                finally
                {
                    Environment.Exit(1);
                }
            };

            Console.CancelKeyPress += cancelHandler;

            try
            {
                EnsureConfigInitialized();

                if (!string.IsNullOrWhiteSpace(opts.NexusModsApiKey))
                {
                    s_config.nexusModsApiKey = opts.NexusModsApiKey;
                    await Logger.LogVerboseAsync("Using Nexus Mods API key from command line argument").ConfigureAwait(false);
                }

                // Validate inputs
                if (!File.Exists(opts.ExistingPath))
                {
                    await Logger.LogErrorAsync($"Existing file not found: {opts.ExistingPath}").ConfigureAwait(false);
                    return 1;
                }

                if (!File.Exists(opts.IncomingPath))
                {
                    await Logger.LogErrorAsync($"Incoming file not found: {opts.IncomingPath}").ConfigureAwait(false);
                    return 1;
                }

                await Logger.LogVerboseAsync($"Merge mode: {opts.ExistingPath} + {opts.IncomingPath}").ConfigureAwait(false);
                await Logger.LogVerboseAsync($"Output format: {opts.Format}").ConfigureAwait(false);

                if (opts.Download && string.IsNullOrEmpty(opts.SourcePath))
                {
                    await Logger.LogErrorAsync("--download requires --source-path to be specified").ConfigureAwait(false);
                    return 1;
                }

                if (!string.IsNullOrEmpty(opts.SourcePath))
                {
                    await Logger.LogVerboseAsync($"Source path provided: {opts.SourcePath}").ConfigureAwait(false);
                    if (!Directory.Exists(opts.SourcePath))
                    {
                        await Logger.LogVerboseAsync($"Source path does not exist, creating: {opts.SourcePath}").ConfigureAwait(false);
                        Directory.CreateDirectory(opts.SourcePath);
                    }

                    s_config.sourcePath = new DirectoryInfo(opts.SourcePath);
                    s_config.debugLogging = opts.Verbose;
                    await Logger.LogVerboseAsync($"Source path set to: {opts.SourcePath}").ConfigureAwait(false);
                }

                // Merge instruction sets
                string msg = "Merging instruction sets...";
                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog(msg);
                }
                else
                {
                    await Logger.LogVerboseAsync(msg).ConfigureAwait(false);
                }

                List<ModComponent> components;
                try
                {
                    // Initialize download cache if we need to download or validate URLs
                    if (opts.Download && downloadCache is null)
                    {
                        downloadCache = new DownloadCacheService();
                        downloadCache.SetDownloadManager();
                        s_globalDownloadCache = downloadCache;
                    }

                    var mergeOptions = new Services.MergeOptions
                    {
                        ExcludeExistingOnly = opts.ExcludeExistingOnly,
                        ExcludeIncomingOnly = opts.ExcludeIncomingOnly,
                        UseExistingOrder = opts.UseExistingOrder,
                        HeuristicsOptions = MergeHeuristicsOptions.CreateDefault(),
                    };

                    // Apply field-level preferences
                    if (opts.PreferExistingFields)
                    {
                        mergeOptions.PreferAllExistingFields = true;
                    }
                    else if (opts.PreferIncomingFields)
                    {
                        mergeOptions.PreferAllIncomingFields = true;
                    }

                    // Individual field preferences override global settings
                    if (opts.PreferExistingName)
                    {
                        mergeOptions.PreferExistingName = true;
                    }

                    if (opts.PreferExistingAuthor)
                    {
                        mergeOptions.PreferExistingAuthor = true;
                    }

                    if (opts.PreferExistingDescription)
                    {
                        mergeOptions.PreferExistingDescription = true;
                    }

                    if (opts.PreferExistingDirections)
                    {
                        mergeOptions.PreferExistingDirections = true;
                    }

                    if (opts.PreferExistingCategory)
                    {
                        mergeOptions.PreferExistingCategory = true;
                    }

                    if (opts.PreferExistingTier)
                    {
                        mergeOptions.PreferExistingTier = true;
                    }

                    if (opts.PreferExistingInstallationMethod)
                    {
                        mergeOptions.PreferExistingInstallationMethod = true;
                    }

                    if (opts.PreferExistingInstructions)
                    {
                        mergeOptions.PreferExistingInstructions = true;
                    }

                    if (opts.PreferExistingOptions)
                    {
                        mergeOptions.PreferExistingOptions = true;
                    }

                    if (opts.PreferExistingModLinks)
                    {
                        mergeOptions.PreferExistingResourceRegistry = true;
                    }

                    // Use async merge to support URL validation with sequential flag
                    using (var cts = new CancellationTokenSource(TimeSpan.FromHours(2)))
                    {
                        components = await ComponentMergeService.MergeInstructionSetsAsync(
                            opts.ExistingPath,
                            opts.IncomingPath,
                            mergeOptions,
                            downloadCache,
                            sequential: !opts.Concurrent,
                            cancellationToken: cts.Token).ConfigureAwait(false);

                        msg = $"Merged result contains {components.Count} unique components";
                        if (s_progressDisplay != null)
                        {
                            s_progressDisplay.WriteScrollingLog(msg);
                        }
                        else
                        {
                            await Logger.LogVerboseAsync(msg).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    s_errorCollector?.RecordError(
                        ErrorCollector.ErrorCategory.General,
componentName: null,
                        "Failed to merge instruction sets",
                        $"Existing: {opts.ExistingPath}, Incoming: {opts.IncomingPath}",
                        ex);
                    throw;
                }

                if (opts.Download)
                {
                    foreach (ModComponent component in components)
                    {
                        component.IsSelected = true;
                    }

                    msg = "Downloading files for merged components...";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(msg);
                    }
                    else
                    {
                        await Logger.LogAsync(msg).ConfigureAwait(false);
                    }

                    using (var downloadCts = new CancellationTokenSource(TimeSpan.FromHours(2)))
                    {
                        downloadCache = await DownloadAllModFilesAsync(components, opts.SourcePath, opts.Verbose, sequential: !opts.Concurrent, downloadCts.Token).ConfigureAwait(false);

                        msg = "Download complete for all components";
                        if (s_progressDisplay != null)
                        {
                            s_progressDisplay.WriteScrollingLog(msg);
                        }
                        else
                        {
                            await Logger.LogAsync(msg).ConfigureAwait(false);
                        }
                    }
                }

                ApplySelectionFilters(components, opts.Select);

                // Apply spoiler-free content if provided
                if (!string.IsNullOrEmpty(opts.SpoilerFreePath))
                {
                    await ApplySpoilerFreeContentAsync(components, opts.SpoilerFreePath).ConfigureAwait(false);
                }

                // ResourceRegistry is already populated during PreResolveUrlsAsync
                // No additional population needed before serialization

                // Create validation context to track issues for serialization
                var validationContext = new ComponentValidationContext();

                // Collect download failures from cache
                if (downloadCache != null)
                {
                    IReadOnlyList<DownloadCacheService.DownloadFailureInfo> failures = downloadCache.GetFailures();
                    foreach (DownloadCacheService.DownloadFailureInfo failure in failures)
                    {
                        validationContext.AddUrlFailure(failure.Url, failure.ErrorMessage);
                    }
                }

                // Collect validation issues from error collector
                if (s_errorCollector != null)
                {
                    foreach (ErrorCollector.ErrorInfo error in s_errorCollector.GetErrors())
                    {
                        // Try to find the component by name
                        ModComponent component = components.Find(c => string.Equals(c.Name, error.ComponentName, StringComparison.Ordinal));
                        if (component != null)
                        {
                            validationContext.AddModComponentIssue(component.Guid, error.Message);
                        }
                    }
                }

                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog("Serializing to output format...");
                }
                else
                {
                    await Logger.LogVerboseAsync("Serializing to output format...").ConfigureAwait(false);
                }

                string output = ModComponentSerializationService.SerializeModComponentAsString(components, opts.Format, validationContext);

                if (!string.IsNullOrEmpty(opts.OutputPath))
                {
                    string outputDir = Path.GetDirectoryName(opts.OutputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                        if (s_progressDisplay != null)
                        {
                            s_progressDisplay.WriteScrollingLog($"Created output directory: {outputDir}");
                        }
                        else
                        {
                            await Logger.LogVerboseAsync($"Created output directory: {outputDir}").ConfigureAwait(false);
                        }
                    }

                    await NetFrameworkCompatibility.WriteAllTextAsync(opts.OutputPath, output).ConfigureAwait(false);

                    string successMsg = $"✓ Merge completed successfully, saved to: {opts.OutputPath}";
                    if (s_progressDisplay != null)
                    {
                        s_progressDisplay.WriteScrollingLog(successMsg);
                    }
                    else
                    {
                        await Logger.LogVerboseAsync($"Merge completed successfully, saved to: {opts.OutputPath}").ConfigureAwait(false);
                    }
                }
                else
                {
                    s_progressDisplay?.Dispose();
                    s_progressDisplay = null;

                    await Logger.LogAsync(output).ConfigureAwait(false);
                    await Logger.LogVerboseAsync("Merge completed successfully (output to stdout)").ConfigureAwait(false);
                }

                if (downloadCache != null || s_errorCollector != null)
                {
                    LogAllErrors(downloadCache);
                }

                return 0;
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error during merge: {ex.Message}";
                if (s_progressDisplay != null)
                {
                    s_progressDisplay.WriteScrollingLog($"✗ {errorMsg}");
                }
                else
                {
                    await Logger.LogErrorAsync(errorMsg).ConfigureAwait(false);
                }

                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }

                if (downloadCache != null || s_errorCollector != null)
                {
                    LogAllErrors(downloadCache);
                }

                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;

                s_progressDisplay?.Dispose();
                s_progressDisplay = null;

                s_globalDownloadCache = null;
                s_errorCollector = null;
            }
        }

        private static async Task<int> RunValidateAsync(ValidateOptions opts)
        {
            SetVerboseMode(opts.Verbose);
            s_errorCollector = new ErrorCollector();

            try
            {
                if (!File.Exists(opts.InputPath))
                {
                    await Logger.LogErrorAsync($"Error: Input file not found: {opts.InputPath}").ConfigureAwait(false);
                    return 1;
                }

                if (opts.FullValidation)
                {
                    if (string.IsNullOrEmpty(opts.GameDirectory) || string.IsNullOrEmpty(opts.SourceDirectory))
                    {
                        await Logger.LogErrorAsync("Error: Full validation requires both --game-dir and --source-dir").ConfigureAwait(false);
                        return 1;
                    }

                    if (!Directory.Exists(opts.GameDirectory))
                    {
                        await Logger.LogErrorAsync($"Error: Game directory not found: {opts.GameDirectory}").ConfigureAwait(false);
                        return 1;
                    }

                    if (!Directory.Exists(opts.SourceDirectory))
                    {
                        await Logger.LogErrorAsync($"Error: Source directory not found: {opts.SourceDirectory}").ConfigureAwait(false);
                        return 1;
                    }
                }

                await Logger.LogAsync($"Loading instruction file: {opts.InputPath}").ConfigureAwait(false);

                List<ModComponent> components;
                try
                {
                    components = await FileLoadingService.LoadFromFileAsync(opts.InputPath).ConfigureAwait(false);

                    // Handle dependency resolution
                    components = (List<ModComponent>)HandleDependencyResolutionErrors(components, opts.IgnoreErrors, "Validate");
                }
                catch (Exception ex)
                {
                    await Logger.LogErrorAsync($"Error loading instruction file: {ex.Message}").ConfigureAwait(false);
                    if (opts.Verbose)
                    {
                        await Logger.LogErrorAsync("Stack trace:").ConfigureAwait(false);
                        await Logger.LogErrorAsync(ex.StackTrace).ConfigureAwait(false);
                    }

                    s_errorCollector?.RecordError(
                        ErrorCollector.ErrorCategory.FileOperation,
componentName: null,
                        "Failed to load instruction file",
                        $"File: {opts.InputPath}",
                        ex);

                    return 1;
                }

                if (components is null || components.Count == 0)
                {
                    await Logger.LogErrorAsync("Error: No components loaded from instruction file.").ConfigureAwait(false);
                    return 1;
                }

                await Logger.LogAsync($"Loaded {components.Count} component(s) from instruction file.").ConfigureAwait(false);
                await Logger.LogAsync().ConfigureAwait(false);

                if (opts.FullValidation)
                {
                    EnsureConfigInitialized();
                    s_config.sourcePath = new DirectoryInfo(opts.SourceDirectory);
                    s_config.destinationPath = new DirectoryInfo(opts.GameDirectory);
                    s_config.allComponents = components;
                }

                List<ModComponent> componentsToValidate = components;
                if (opts.Select != null && opts.Select.Any())
                {
                    if (!opts.ErrorsOnly)
                    {
                        await Logger.LogAsync("Applying selection filters...").ConfigureAwait(false);
                    }

                    componentsToValidate = new List<ModComponent>(components);
                    ApplySelectionFilters(componentsToValidate, opts.Select);
                    componentsToValidate = componentsToValidate.Where(c => c.IsSelected).ToList();

                    if (componentsToValidate.Count == 0)
                    {
                        await Logger.LogErrorAsync("Error: No components match the selection criteria.").ConfigureAwait(false);
                        return 1;
                    }

                    if (!opts.ErrorsOnly)
                    {
                        await Logger.LogAsync($"{componentsToValidate.Count} component(s) selected for validation.").ConfigureAwait(false);
                    }
                }

                if (opts.FullValidation)
                {
                    if (!opts.ErrorsOnly)
                    {
                        await Logger.LogAsync("Performing full environment validation...").ConfigureAwait(false);
                        await Logger.LogAsync(new string('-', 50)).ConfigureAwait(false);
                    }

                    (bool success, string message) = await InstallationService.ValidateInstallationEnvironmentAsync(s_config).ConfigureAwait(false);

                    if (!success)
                    {
                        await Logger.LogErrorAsync("Environment validation failed:").ConfigureAwait(false);
                        await Logger.LogErrorAsync(message).ConfigureAwait(false);
                        if (!opts.ErrorsOnly)
                        {
                            await Logger.LogAsync(new string('-', 50)).ConfigureAwait(false);
                        }

                        return 1;
                    }

                    if (!opts.ErrorsOnly)
                    {
                        await Logger.LogAsync("✓ Environment validation passed").ConfigureAwait(false);
                        await Logger.LogAsync(new string('-', 50)).ConfigureAwait(false);
                        await Logger.LogAsync().ConfigureAwait(false);
                    }
                }

                if (!opts.ErrorsOnly)
                {
                    await Logger.LogAsync("Validating components...").ConfigureAwait(false);
                    await Logger.LogAsync(new string('=', 50)).ConfigureAwait(false);
                }

                int totalComponents = componentsToValidate.Count;
                int validComponents = 0;
                int componentsWithErrors = 0;
                int componentsWithWarnings = 0;
                var allErrors = new List<(ModComponent component, List<string> errors)>();
                var allWarnings = new List<(ModComponent component, List<string> warnings)>();

                foreach (ModComponent component in componentsToValidate)
                {
                    var validator = new ComponentValidation(component, components);
                    bool isValid = validator.Run();

                    List<string> errors = validator.GetErrors();
                    List<string> warnings = validator.GetWarnings();

                    if (errors.Count > 0)
                    {
                        componentsWithErrors++;
                        allErrors.Add((component, errors));

                        // Record validation errors in error collector
                        foreach (string error in errors)
                        {
                            s_errorCollector?.RecordError(
                                ErrorCollector.ErrorCategory.Validation,
                                component.Name,
                                error,
details: null,
exception: null);
                        }
                    }
                    else if (warnings.Count > 0)
                    {
                        componentsWithWarnings++;
                        allWarnings.Add((component, warnings));
                    }
                    else
                    {
                        validComponents++;
                    }

                    if (!opts.ErrorsOnly || errors.Count > 0)
                    {
                        if (isValid && errors.Count == 0 && warnings.Count == 0)
                        {
                            if (!opts.ErrorsOnly)
                            {
                                await Logger.LogAsync($"✓ {component.Name}").ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            if (errors.Count > 0)
                            {
                                await Logger.LogAsync($"✗ {component.Name}").ConfigureAwait(false);
                                foreach (string error in errors)
                                {
                                    await Logger.LogAsync($"    ERROR: {error}").ConfigureAwait(false);
                                }
                            }
                            else if (warnings.Count > 0 && !opts.ErrorsOnly)
                            {
                                await Logger.LogAsync($"⚠ {component.Name}").ConfigureAwait(false);
                                foreach (string warning in warnings)
                                {
                                    await Logger.LogAsync($"    WARNING: {warning}").ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }

                if (!opts.ErrorsOnly)
                {
                    await Logger.LogAsync(new string('=', 50)).ConfigureAwait(false);
                    await Logger.LogAsync().ConfigureAwait(false);
                    await Logger.LogAsync("Validation Summary:").ConfigureAwait(false);
                    await Logger.LogAsync($"  Total components validated: {totalComponents}").ConfigureAwait(false);
                    await Logger.LogAsync($"  ✓ Valid: {validComponents}").ConfigureAwait(false);
                    if (componentsWithWarnings > 0)
                    {
                        await Logger.LogAsync($"  ⚠ With warnings: {componentsWithWarnings}").ConfigureAwait(false);
                    }

                    if (componentsWithErrors > 0)
                    {
                        await Logger.LogAsync($"  ✗ With errors: {componentsWithErrors}").ConfigureAwait(false);
                    }

                    await Logger.LogAsync().ConfigureAwait(false);
                }

                if (componentsWithErrors > 0)
                {
                    if (opts.ErrorsOnly)
                    {
                        await Logger.LogAsync($"{componentsWithErrors} component(s) with errors").ConfigureAwait(false);
                    }
                    else
                    {
                        await Logger.LogAsync("❌ Validation failed - errors found").ConfigureAwait(false);
                    }
                    return 1;
                }

                if (componentsWithWarnings > 0)
                {
                    if (!opts.ErrorsOnly)
                    {
                        await Logger.LogAsync("⚠️ Validation passed with warnings").ConfigureAwait(false);
                    }

                    return 0;
                }

                if (!opts.ErrorsOnly)
                {
                    await Logger.LogAsync("✅ All validations passed!").ConfigureAwait(false);
                }

                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error during validation: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogErrorAsync("Stack trace:").ConfigureAwait(false);
                    await Logger.LogErrorAsync(ex.StackTrace).ConfigureAwait(false);
                }
                return 1;
            }
        }

        private static async Task<int> RunInstallAsync(InstallOptions opts)
        {
            SetVerboseMode(opts.Verbose);
            s_errorCollector = new ErrorCollector();

            try
            {
                if (!File.Exists(opts.InputPath))
                {
                    await Logger.LogErrorAsync($"Error: Input file not found: {opts.InputPath}").ConfigureAwait(false);
                    return 1;
                }

                if (!Directory.Exists(opts.GameDirectory))
                {
                    await Logger.LogErrorAsync($"Error: Game directory not found: {opts.GameDirectory}").ConfigureAwait(false);
                    return 1;
                }

                string sourceDir = opts.SourceDirectory;
                if (string.IsNullOrEmpty(sourceDir))
                {
                    sourceDir = Path.GetDirectoryName(Path.GetFullPath(opts.InputPath));
                    await Logger.LogAsync($"Using source directory: {sourceDir}").ConfigureAwait(false);
                }

                if (!Directory.Exists(sourceDir))
                {
                    await Logger.LogErrorAsync($"Error: Source directory not found: {sourceDir}").ConfigureAwait(false);
                    return 1;
                }

                EnsureConfigInitialized();
                s_config.sourcePath = new DirectoryInfo(sourceDir);
                s_config.destinationPath = new DirectoryInfo(opts.GameDirectory);

                await Logger.LogAsync($"Loading instruction file: {opts.InputPath}").ConfigureAwait(false);

                List<ModComponent> components = await FileLoadingService.LoadFromFileAsync(opts.InputPath).ConfigureAwait(false);

                // Handle dependency resolution
                components = (List<ModComponent>)HandleDependencyResolutionErrors(components, opts.IgnoreErrors, "Install");

                if (components is null || components.Count == 0)
                {
                    await Logger.LogErrorAsync("Error: No components loaded from instruction file.").ConfigureAwait(false);
                    return 1;
                }

                s_config.allComponents = components;
                await Logger.LogAsync($"Loaded {components.Count} component(s) from instruction file.").ConfigureAwait(false);

                if (opts.Select != null && opts.Select.Any())
                {
                    await Logger.LogAsync("Applying selection filters...").ConfigureAwait(false);
                    ApplySelectionFilters(components, opts.Select);
                }

                int selectedCount = components.Count(c => c.IsSelected);
                if (selectedCount == 0)
                {
                    await Logger.LogErrorAsync("Error: No components selected for installation.").ConfigureAwait(false);
                    await Logger.LogErrorAsync("Use --select to specify components, or ensure components are marked as selected in the instruction file.").ConfigureAwait(false);
                    return 1;
                }

                await Logger.LogAsync($"{selectedCount} component(s) selected for installation.").ConfigureAwait(false);
                await Logger.LogAsync().ConfigureAwait(false);

                await Logger.LogAsync("Components to install:").ConfigureAwait(false);
                int index = 1;
                foreach (ModComponent component in components.Where(c => c.IsSelected))
                {
                    await Logger.LogAsync($"  {index}. {component.Name}").ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(component.Description))
                    {
                        string desc = component.Description.Length > 80
                            ? component.Description.Substring(0, 77) + "..."
                            : component.Description;
                        await Logger.LogAsync($"     {desc}").ConfigureAwait(false);
                    }
                    index++;
                }
                await Logger.LogAsync().ConfigureAwait(false);

                if (!opts.AutoConfirm)
                {
                    await Console.Out.WriteLineAsync("Proceed with installation? [y/N]: ").ConfigureAwait(false);
                    string response = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (!string.Equals(response, "y", StringComparison.Ordinal) && !string.Equals(response, "yes", StringComparison.Ordinal))
                    {
                        await Logger.LogAsync("Installation cancelled by user.").ConfigureAwait(false);
                        return 0;
                    }
                }

                if (!opts.SkipValidation)
                {
                    await Logger.LogAsync("Validating installation environment...").ConfigureAwait(false);
                    (bool success, string message) = await InstallationService.ValidateInstallationEnvironmentAsync(
                        s_config,
                        (confirmMessage) =>
                        {
                            if (opts.AutoConfirm)
                            {
                                return Task.FromResult<bool?>(true);
                            }

                            Console.Write($"{confirmMessage} [y/N]: ");
                            string response = Console.ReadLine()?.Trim().ToLowerInvariant();
                            bool? result = string.Equals(response, "y", StringComparison.Ordinal) || string.Equals(response, "yes", StringComparison.Ordinal);
                            return Task.FromResult(result);
                        }
                    ).ConfigureAwait(false);

                    if (!success)
                    {
                        await Logger.LogErrorAsync("Validation failed:").ConfigureAwait(false);
                        await Logger.LogErrorAsync(message).ConfigureAwait(false);
                        return 1;
                    }
                    await Logger.LogAsync("Validation passed.").ConfigureAwait(false);
                    await Logger.LogAsync().ConfigureAwait(false);
                }

                await Logger.LogAsync("Starting installation...").ConfigureAwait(false);
                await Logger.LogAsync(new string('=', 50)).ConfigureAwait(false);

                ModComponent.InstallExitCode exitCode = await InstallationService.InstallAllSelectedComponentsAsync(
                    components,
                    async (currentIndex, total, componentName) =>
                    {
                        await Logger.LogAsync($"[{currentIndex + 1}/{total}] Installing: {componentName}").ConfigureAwait(false);
                    }
                ).ConfigureAwait(false);

                await Logger.LogAsync(new string('=', 50)).ConfigureAwait(false);

                if (exitCode == ModComponent.InstallExitCode.Success)
                {
                    await Logger.LogAsync("Installation completed successfully!").ConfigureAwait(false);
                    return 0;
                }

                await Logger.LogErrorAsync($"Installation failed with exit code: {exitCode}").ConfigureAwait(false);
                await Logger.LogErrorAsync("Check the logs above for more details.").ConfigureAwait(false);
                return 1;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error during installation: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogErrorAsync("Stack trace:").ConfigureAwait(false);
                    await Logger.LogErrorAsync(ex.StackTrace).ConfigureAwait(false);
                }

                s_errorCollector?.RecordError(
                    ErrorCollector.ErrorCategory.Installation,
componentName: null,
                    "Installation failed with exception",
                    $"Input: {opts.InputPath}, Game Dir: {opts.GameDirectory}",
                    ex);

                LogAllErrors(downloadCache: null, forceConsoleOutput: true);

                return 1;
            }
        }

        private static async Task<int> RunSetNexusApiKeyAsync(SetNexusApiKeyOptions opts)
        {
            SetVerboseMode(opts.Verbose);

            try
            {
                EnsureConfigInitialized();

                await Logger.LogAsync("Setting Nexus Mods API key...").ConfigureAwait(false);
                await Logger.LogAsync($"API Key: {opts.ApiKey.Substring(0, Math.Min(10, opts.ApiKey.Length))}...").ConfigureAwait(false);

                if (!opts.SkipValidation)
                {
                    await Logger.LogAsync("\nValidating API key with Nexus Mods...").ConfigureAwait(false);
                    (bool isValid, string message) = await NexusModsDownloadHandler.ValidateApiKeyAsync(opts.ApiKey).ConfigureAwait(false);

                    if (!isValid)
                    {
                        await Logger.LogErrorAsync($"API key validation failed: {message}").ConfigureAwait(false);
                        await Logger.LogAsync($"\n❌ Validation failed: {message}").ConfigureAwait(false);
                        return 1;
                    }

                    await Logger.LogAsync($"\n✓ {message}").ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogWarningAsync("Skipping API key validation").ConfigureAwait(false);
                    await Logger.LogAsync("Skipping validation (--skip-validation specified)").ConfigureAwait(false);
                }

                s_config.nexusModsApiKey = opts.ApiKey;
                await Logger.LogAsync("API key stored in MainConfig").ConfigureAwait(false);

                SaveSettings();

                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KOTORModSync",
                    "settings.json"
                );

                await Logger.LogAsync($"API key saved to: {settingsPath}").ConfigureAwait(false);

                await Logger.LogAsync($"\n✓ Nexus Mods API key set successfully!").ConfigureAwait(false);
                await Logger.LogAsync($"Settings file: {settingsPath}").ConfigureAwait(false);
                await Logger.LogAsync("\nYou can now use the download command to automatically download mods from Nexus Mods.").ConfigureAwait(false);
                await Logger.LogAsync("This setting is shared with the KOTORModSync GUI application.").ConfigureAwait(false);

                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error setting Nexus Mods API key: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                await Logger.LogAsync($"\n❌ Error: {ex.Message}").ConfigureAwait(false);
                return 1;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task<int> RunInstallPythonDepsAsync(InstallPythonDepsOptions opts)
        {
            SetVerboseMode(opts.Verbose);

            try
            {
                // Disable keyring to prevent pip from hanging
                Environment.SetEnvironmentVariable("PYTHON_KEYRING_BACKEND", "keyring.backends.null.Keyring");
                Environment.SetEnvironmentVariable("PIP_NO_INPUT", "1");
                Environment.SetEnvironmentVariable("PIP_DISABLE_PIP_VERSION_CHECK", "1");

                await Logger.LogAsync("Installing Python dependencies for HoloPatcher...").ConfigureAwait(false);
                await Logger.LogAsync("This may take several minutes on first run...").ConfigureAwait(false);

                var sw = new Stopwatch();
                sw.Start();
                // Setup Python environment
                await Logger.LogAsync("Setting up Python environment...").ConfigureAwait(false);
                await Python.Included.Installer.SetupPython().ConfigureAwait(false);

                await Logger.LogAsync("Initializing Python engine...").ConfigureAwait(false);
                await Logger.LogAsync("[DEBUG] About to call PythonEngine.Initialize()").ConfigureAwait(false);
                Python.Runtime.PythonEngine.Initialize();
                await Logger.LogAsync("[DEBUG] PythonEngine.Initialize() completed").ConfigureAwait(false);

                // Check if dependencies are already installed
                bool dependenciesInstalled = false;
                if (!opts.Force)
                {
                    try
                    {
                        await Logger.LogAsync("Checking if dependencies are already installed...").ConfigureAwait(false);
                        await Logger.LogAsync("[DEBUG] Acquiring GIL...").ConfigureAwait(false);
                        using (Python.Runtime.Py.GIL())
                        {
                            await Logger.LogAsync("[DEBUG] GIL acquired, importing loggerplus...").ConfigureAwait(false);
                            Python.Runtime.Py.Import("loggerplus");
                            await Logger.LogAsync("[DEBUG] loggerplus imported, importing ply...").ConfigureAwait(false);
                            Python.Runtime.Py.Import("ply");
                            await Logger.LogAsync("[DEBUG] ply imported").ConfigureAwait(false);
                            dependenciesInstalled = true;
                            await Logger.LogAsync("✓ Python dependencies already installed.").ConfigureAwait(false);
                        }
                        await Logger.LogAsync("[DEBUG] GIL released").ConfigureAwait(false);
                    }
                    catch (Python.Runtime.PythonException ex)
                    {
                        await Logger.LogAsync($"[DEBUG] Import failed: {ex.Message}").ConfigureAwait(false);
                        await Logger.LogAsync("Dependencies not found, will install...").ConfigureAwait(false);
                        dependenciesInstalled = false;
                    }
                }

                if (!dependenciesInstalled)
                {
                    await Logger.LogAsync("[DEBUG] ===== STARTING DEPENDENCY INSTALLATION =====").ConfigureAwait(false);
                    await Logger.LogAsync("Installing dependencies using Python.NET (bypassing Python.Included's buggy RunCommand)...").ConfigureAwait(false);

                    await Logger.LogAsync("[DEBUG] About to acquire GIL for installation").ConfigureAwait(false);
                    using (Python.Runtime.Py.GIL())
                    {
                        await Logger.LogAsync("[DEBUG] GIL acquired for installation").ConfigureAwait(false);

                        // First ensure pip is available
                        try
                        {
                            await Logger.LogAsync("[DEBUG] About to import ensurepip...").ConfigureAwait(false);
                            dynamic ensurepip = Python.Runtime.Py.Import("ensurepip");
                            await Logger.LogAsync("[DEBUG] ensurepip imported, calling _bootstrap()...").ConfigureAwait(false);
                            ensurepip._bootstrap(upgrade: true);
                            await Logger.LogAsync("✓ Pip bootstrapped").ConfigureAwait(false);
                        }
                        catch (Python.Runtime.PythonException ex)
                        {
                            await Logger.LogAsync($"[DEBUG] ensurepip exception: {ex.Message}").ConfigureAwait(false);
                            await Logger.LogAsync($"ensurepip note: {ex.Message} (pip may already exist)").ConfigureAwait(false);
                        }

                        // Install loggerplus using pip's internal API
                        await Logger.LogAsync("[DEBUG] ===== INSTALLING LOGGERPLUS =====").ConfigureAwait(false);
                        await Logger.LogAsync("Installing loggerplus using pip._internal.main()...").ConfigureAwait(false);
                        try
                        {
                            await Logger.LogAsync("[DEBUG] Importing pip._internal...").ConfigureAwait(false);
                            dynamic pip_internal = Python.Runtime.Py.Import("pip._internal");
                            await Logger.LogAsync("[DEBUG] pip._internal imported, getting main function...").ConfigureAwait(false);
                            dynamic pipMain = pip_internal.main;
                            await Logger.LogAsync("[DEBUG] Got pipMain, creating args list...").ConfigureAwait(false);

                            using (dynamic args = new Python.Runtime.PyList())
                            {
                                await Logger.LogAsync("[DEBUG] Created PyList, appending 'install'...").ConfigureAwait(false);
                                args.Append(new Python.Runtime.PyString("install"));
                                await Logger.LogAsync("[DEBUG] Appending 'loggerplus'...").ConfigureAwait(false);
                                args.Append(new Python.Runtime.PyString("loggerplus"));

                                await Logger.LogAsync("[DEBUG] About to call pipMain(args)...").ConfigureAwait(false);
                                await Logger.LogAsync("[DEBUG] THIS IS WHERE IT MIGHT HANG...").ConfigureAwait(false);
                                pipMain(args);
                                await Logger.LogAsync("[DEBUG] pipMain() returned!").ConfigureAwait(false);
                            }
                            await Logger.LogAsync("✓ loggerplus installed").ConfigureAwait(false);
                        }
                        catch (Python.Runtime.PythonException ex)
                        {
                            await Logger.LogAsync($"[DEBUG] PythonException caught: {ex.Message}").ConfigureAwait(false);
                            // pip._internal.main() raises SystemExit on success
                            if (ex.Message.Contains("SystemExit") && ex.Message.Contains("0"))
                            {
                                await Logger.LogAsync("✓ loggerplus installed (exit 0)").ConfigureAwait(false);
                            }
                            else
                            {
                                await Logger.LogErrorAsync($"Failed to install loggerplus: {ex.Message}").ConfigureAwait(false);
                                return 1;
                            }
                        }

                        // Install ply
                        await Logger.LogAsync("[DEBUG] ===== INSTALLING PLY =====").ConfigureAwait(false);
                        await Logger.LogAsync("Installing ply using pip._internal.main()...").ConfigureAwait(false);
                        try
                        {
                            await Logger.LogAsync("[DEBUG] Importing pip._internal for ply...").ConfigureAwait(false);
                            dynamic pip_internal = Python.Runtime.Py.Import("pip._internal");
                            dynamic pipMain = pip_internal.main;
                            await Logger.LogAsync("[DEBUG] Creating args for ply...").ConfigureAwait(false);

                            using (dynamic args = new Python.Runtime.PyList())
                            {
                                args.Append(new Python.Runtime.PyString("install"));
                                args.Append(new Python.Runtime.PyString("ply"));

                                await Logger.LogAsync("[DEBUG] Calling pipMain for ply...").ConfigureAwait(false);
                                pipMain(args);
                                await Logger.LogAsync("[DEBUG] pipMain for ply returned!").ConfigureAwait(false);
                            }
                            await Logger.LogAsync("✓ ply installed").ConfigureAwait(false);
                        }
                        catch (Python.Runtime.PythonException ex)
                        {
                            await Logger.LogAsync($"[DEBUG] ply PythonException: {ex.Message}").ConfigureAwait(false);
                            if (ex.Message.Contains("SystemExit") && ex.Message.Contains("0"))
                            {
                                await Logger.LogAsync("✓ ply installed (exit 0)").ConfigureAwait(false);
                            }
                            else
                            {
                                await Logger.LogErrorAsync($"Failed to install ply: {ex.Message}").ConfigureAwait(false);
                                return 1;
                            }
                        }
                    }
                    await Logger.LogAsync("[DEBUG] GIL released after installation").ConfigureAwait(false);
                    await Logger.LogAsync("[DEBUG] ===== INSTALLATION COMPLETE =====").ConfigureAwait(false);

                    await Logger.LogAsync("Python dependencies installation completed.").ConfigureAwait(false);
                    await Logger.LogAsync("Skipping verification to avoid potential GIL issues.").ConfigureAwait(false);
                }

                sw.Stop();
                TimeSpan elapsed = sw.Elapsed;
                await Logger.LogAsync($"Python dependencies setup completed in {elapsed.TotalSeconds:F1} seconds.").ConfigureAwait(false);
                await Logger.LogAsync("[DEBUG] ===== SHUTTING DOWN PYTHON ENGINE =====").ConfigureAwait(false);

                // Shutdown Python engine to prevent hanging
                try
                {
                    if (Python.Runtime.PythonEngine.IsInitialized)
                    {
                        await Logger.LogAsync("[DEBUG] Calling PythonEngine.Shutdown()...").ConfigureAwait(false);
                        Python.Runtime.PythonEngine.Shutdown();
                        await Logger.LogAsync("[DEBUG] PythonEngine.Shutdown() completed").ConfigureAwait(false);
                    }
                }
                catch (Exception shutdownEx)
                {
                    await Logger.LogAsync($"[DEBUG] Shutdown warning: {shutdownEx.Message}").ConfigureAwait(false);
                }

                await Logger.LogAsync("[DEBUG] ===== EXITING SUCCESSFULLY =====").ConfigureAwait(false);

                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error installing Python dependencies: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                return 1;
            }
        }

        private static async Task<int> RunHolopatcherAsync(HolopatcherOptions opts)
        {
            SetVerboseMode(opts.Verbose);

            try
            {
                await Logger.LogAsync("Launching HoloPatcher...").ConfigureAwait(false);
                await Logger.LogAsync($"Arguments: {(string.IsNullOrEmpty(opts.Arguments) ? "(none)" : opts.Arguments)}").ConfigureAwait(false);

                string baseDir = Core.Utility.UtilityHelper.GetBaseDirectory();
                string resourcesDir = Core.Utility.UtilityHelper.GetResourcesDirectory(baseDir);

                await Logger.LogAsync($"[DEBUG] Base directory: {baseDir}").ConfigureAwait(false);
                await Logger.LogAsync($"[DEBUG] Resources directory: {resourcesDir}").ConfigureAwait(false);

                // Find holopatcher
                (string holopatcherPath, bool usePythonVersion, bool found) = await InstallationService.FindHolopatcherAsync(resourcesDir, baseDir).ConfigureAwait(false);

                if (!found)
                {
                    await Logger.LogErrorAsync("HoloPatcher not found in Resources directory.").ConfigureAwait(false);
                    await Logger.LogAsync("Please ensure PyKotor/HoloPatcher is installed correctly.").ConfigureAwait(false);
                    return 1;
                }

                await Logger.LogAsync($"Found HoloPatcher at: {holopatcherPath}").ConfigureAwait(false);
                await Logger.LogAsync($"Using Python version: {usePythonVersion}").ConfigureAwait(false);

                // Run holopatcher
                int exitCode;
                string stdout;
                string stderr;

                if (usePythonVersion)
                {
                    await Logger.LogAsync("Running HoloPatcher via Python.NET...").ConfigureAwait(false);
                    (exitCode, stdout, stderr) = await InstallationService.RunHolopatcherPyAsync(holopatcherPath, opts.Arguments ?? "").ConfigureAwait(false);
                }
                else
                {
                    await Logger.LogAsync("Running HoloPatcher executable...").ConfigureAwait(false);
                    (exitCode, stdout, stderr) = await Utility.PlatformAgnosticMethods.ExecuteProcessAsync(holopatcherPath, opts.Arguments ?? "").ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(stdout))
                {
                    await Logger.LogAsync("=== STDOUT ===").ConfigureAwait(false);
                    await Logger.LogAsync(stdout).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(stderr))
                {
                    await Logger.LogErrorAsync("=== STDERR ===").ConfigureAwait(false);
                    await Logger.LogErrorAsync(stderr).ConfigureAwait(false);
                }

                if (exitCode == 0)
                {
                    await Logger.LogAsync("✓ HoloPatcher completed successfully").ConfigureAwait(false);
                    return 0;
                }

                await Logger.LogErrorAsync($"HoloPatcher exited with code {exitCode}").ConfigureAwait(false);
                return exitCode;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error running HoloPatcher: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                return 1;
            }
        }

        #region Cache Management Commands

        private static async Task<int> RunCacheStatsAsync(CacheStatsOptions opts)
        {
            try
            {
                SetVerboseMode(opts.Verbose);

                // Load the resource index to get statistics
                await DownloadCacheService.LoadResourceIndexAsync().ConfigureAwait(false);

                var stats = new
                {
                    TotalResources = DownloadCacheService.GetResourceCount(),
                    TotalSize = DownloadCacheService.GetTotalCacheSize(),
                    Providers = DownloadCacheService.GetProviderStats(),
                    BlockedContentIds = DownloadCacheService.GetBlockedContentIdCount(),
                    LastUpdated = DownloadCacheService.GetLastIndexUpdate(),
                };

                if (opts.JsonOutput)
                {
                    await Console.Out.WriteLineAsync(JsonConvert.SerializeObject(stats, Formatting.Indented)).ConfigureAwait(false);
                }
                else
                {
                    await Console.Out.WriteLineAsync("=== Distributed Cache Statistics ===").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Total Resources: {stats.TotalResources}").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Total Cache Size: {FormatBytes(stats.TotalSize)}").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Blocked ContentIds: {stats.BlockedContentIds}").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Last Updated: {stats.LastUpdated:yyyy-MM-dd HH:mm:ss} UTC").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync().ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("Provider Breakdown:").ConfigureAwait(false);
                    foreach (KeyValuePair<string, int> provider in stats.Providers)
                    {
                        await Console.Out.WriteLineAsync($"  {provider.Key}: {provider.Value} resources").ConfigureAwait(false);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error getting cache statistics: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                return 1;
            }
        }

        private static async Task<int> RunCacheClearAsync(CacheClearOptions opts)
        {
            try
            {
                SetVerboseMode(opts.Verbose);

                if (!opts.Force)
                {
                    await Console.Out.WriteAsync("Are you sure you want to clear the distributed cache? (y/N): ").ConfigureAwait(false);
                    string response = Console.ReadLine();
                    if (!string.Equals(response?.ToLowerInvariant(), "y", StringComparison.Ordinal) && !string.Equals(response?.ToLowerInvariant(), "yes", StringComparison.Ordinal))
                    {
                        await Console.Out.WriteLineAsync("Cache clear cancelled.").ConfigureAwait(false);
                        return 0;
                    }
                }

                await DownloadCacheService.ClearCacheAsync(opts.Provider).ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"✓ Cache cleared{(string.IsNullOrEmpty(opts.Provider) ? "" : $" for provider: {opts.Provider}")}").ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error clearing cache: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                return 1;
            }
        }

        private static async Task<int> RunCacheBlockAsync(CacheBlockOptions opts)
        {
            try
            {
                SetVerboseMode(opts.Verbose);

                // Validate ContentId format (should be 40 hex characters)
                if (string.IsNullOrEmpty(opts.ContentId) || opts.ContentId.Length != 40 || !opts.ContentId.All(c => "0123456789abcdef".Contains(c)))
                {
                    await Console.Out.WriteLineAsync("Error: ContentId must be exactly 40 hexadecimal characters").ConfigureAwait(false);
                    return 1;
                }

                DownloadCacheOptimizer.BlockContentId(opts.ContentId, opts.Reason ?? "Manual block via CLI");
                await Console.Out.WriteLineAsync($"✓ ContentId {opts.ContentId} has been blocked from distributed cache").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(opts.Reason))
                {
                    await Console.Out.WriteLineAsync($"  Reason: {opts.Reason}").ConfigureAwait(false);
                }

                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error blocking ContentId: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                return 1;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:F2} {suffixes[suffixIndex]}";
        }

        private static async Task<int> RunCacheTestAsync(CacheTestOptions opts)
        {
            try
            {
                SetVerboseMode(opts.Verbose);

                await Console.Out.WriteLineAsync("=== KOTORModSync Distributed Cache Tests ===").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Category: {opts.Category}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Include Docker Tests: {opts.IncludeDocker}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Timeout: {opts.TimeoutSeconds}s").ConfigureAwait(false);
                await Console.Out.WriteLineAsync().ConfigureAwait(false);

                // Note: Actual test execution would require xUnit test runner integration
                // TODO - STUB: For now, this provides a CLI entry point for test execution

                await Console.Out.WriteLineAsync("To run tests, use:").ConfigureAwait(false);
                await Console.Out.WriteLineAsync("  dotnet test KOTORModSync.Tests --filter Category=DistributedCache").ConfigureAwait(false);
                await Console.Out.WriteLineAsync().ConfigureAwait(false);

                if (opts.IncludeDocker)
                {
                    await Console.Out.WriteLineAsync("Docker tests can be run with:").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("  dotnet test KOTORModSync.Tests --filter \"Category=DistributedCache&RequiresDocker=true\"").ConfigureAwait(false);
                }

                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error running cache tests: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                return 1;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static async Task<int> RunCacheSeedAsync(CacheSeedOptions opts)
        {
            try
            {
                SetVerboseMode(opts.Verbose);

                await Console.Out.WriteLineAsync("=== KOTORModSync Distributed Cache Seeding ===").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"TOML: {opts.TomlPath}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Duration: {opts.DurationSeconds}s ({opts.DurationSeconds / 3600.0:F1} hours)").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Source: {opts.SourcePath}").ConfigureAwait(false);
                await Console.Out.WriteLineAsync().ConfigureAwait(false);

                if (!File.Exists(opts.TomlPath))
                {
                    await Console.Error.WriteLineAsync($"Error: TOML file not found: {opts.TomlPath}").ConfigureAwait(false);
                    return 1;
                }

                if (!Directory.Exists(opts.SourcePath))
                {
                    await Console.Error.WriteLineAsync($"Error: Source directory not found: {opts.SourcePath}").ConfigureAwait(false);
                    return 1;
                }

                EnsureConfigInitialized();
                s_config.sourcePath = new DirectoryInfo(opts.SourcePath);

                // Load components
                await Console.Out.WriteLineAsync("Loading components...").ConfigureAwait(false);
                List<ModComponent> components = await FileLoadingService.LoadFromFileAsync(opts.TomlPath).ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Loaded {components.Count} components").ConfigureAwait(false);

                // Initialize distributed cache engine
                await Console.Out.WriteLineAsync("Initializing distributed cache engine...").ConfigureAwait(false);
                await DownloadCacheOptimizer.EnsureInitializedAsync().ConfigureAwait(false);

                (int activeShares, long totalUploadBytes, int connectedSources) stats = DownloadCacheOptimizer.GetNetworkCacheStats();
                await Console.Out.WriteLineAsync($"Cache engine initialized (Port in use, {stats.activeShares} active shares)").ConfigureAwait(false);
                await Console.Out.WriteLineAsync().ConfigureAwait(false);

                // Start seeding files
                await Console.Out.WriteLineAsync("Starting seeding operation...").ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"This will run for {opts.DurationSeconds}s. Press Ctrl+C to stop early.").ConfigureAwait(false);
                await Console.Out.WriteLineAsync().ConfigureAwait(false);

                DateTime startTime = DateTime.UtcNow;
                DateTime deadline = startTime.AddSeconds(opts.DurationSeconds);
                int filesSeeded = 0;

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(opts.DurationSeconds + 60)))
                {
                    // Find all files in source directory
                    var sourceFiles = Directory.GetFiles(opts.SourcePath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => !f.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    await Console.Out.WriteLineAsync($"Found {sourceFiles.Count} files to seed").ConfigureAwait(false);

                    // For each file, start background sharing
                    foreach (string filePath in sourceFiles.Take(opts.ConcurrentLimit))
                    {
                        try
                        {
                            await DownloadCacheOptimizer.StartBackgroundSharingAsync(
                                filePath,
                                filePath,
                                contentKeyOrHash: null).ConfigureAwait(false);
                            filesSeeded++;
                            await Console.Out.WriteLineAsync($"[{filesSeeded}/{sourceFiles.Count}] Seeding: {Path.GetFileName(filePath)}").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            await Console.Out.WriteLineAsync($"Failed to seed {Path.GetFileName(filePath)}: {ex.Message}").ConfigureAwait(false);
                        }
                    }

                    await Console.Out.WriteLineAsync().ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Seeding {filesSeeded} files for {opts.DurationSeconds}s...").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync().ConfigureAwait(false);

                    // Periodic stats reporting
                    int reportIntervalSeconds = Math.Min(300, opts.DurationSeconds / 10); // Report every 5 min or 10% of duration
                    DateTime nextReport = DateTime.UtcNow.AddSeconds(reportIntervalSeconds);

                    while (DateTime.UtcNow < deadline && !cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(10000, cts.Token).ConfigureAwait(false); // Check every 10 seconds

                        if (DateTime.UtcNow >= nextReport)
                        {
                            stats = DownloadCacheOptimizer.GetNetworkCacheStats();
                            TimeSpan elapsed = DateTime.UtcNow - startTime;
                            TimeSpan remaining = deadline - DateTime.UtcNow;

                            await Console.Out.WriteLineAsync($"[{elapsed.TotalMinutes:F1}m elapsed, {remaining.TotalMinutes:F1}m remaining] " +
                                $"Active: {stats.activeShares}, Uploaded: {stats.totalUploadBytes / 1024 / 1024:F2} MB, " +
                                $"Sources: {stats.connectedSources}").ConfigureAwait(false);

                            nextReport = DateTime.UtcNow.AddSeconds(reportIntervalSeconds);
                        }
                    }

                    await Console.Out.WriteLineAsync().ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("Seeding period complete. Shutting down...").ConfigureAwait(false);

                    // Final stats
                    stats = DownloadCacheOptimizer.GetNetworkCacheStats();
                    TimeSpan totalElapsed = DateTime.UtcNow - startTime;

                    await Console.Out.WriteLineAsync().ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("=== Final Statistics ===").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Duration: {totalElapsed.TotalMinutes:F1} minutes").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Files seeded: {filesSeeded}").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Active shares: {stats.activeShares}").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Total uploaded: {stats.totalUploadBytes / 1024.0 / 1024.0:F2} MB").ConfigureAwait(false);
                    await Console.Out.WriteLineAsync($"Connected sources: {stats.connectedSources}").ConfigureAwait(false);

                    // Graceful shutdown
                    await DownloadCacheOptimizer.GracefulShutdownAsync().ConfigureAwait(false);
                    await Console.Out.WriteLineAsync().ConfigureAwait(false);
                    await Console.Out.WriteLineAsync("✓ Seeding operation completed successfully").ConfigureAwait(false);
                }

                return 0;
            }
            catch (Exception ex)
            {
                await Logger.LogErrorAsync($"Error during seeding operation: {ex.Message}").ConfigureAwait(false);
                if (opts.Verbose)
                {
                    await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                }
                return 1;
            }
        }

        #endregion

        // DEPRECATED: ModLinkFilenames population is now handled via ResourceRegistry during PreResolveUrlsAsync
    }
}
