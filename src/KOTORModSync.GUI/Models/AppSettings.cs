// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using JetBrains.Annotations;

using KOTORModSync.Core;

using PatcherEngines = KOTORModSync.Core.PatcherEngines;

namespace KOTORModSync.Models
{

    public sealed class AppSettings
    {

        [JsonPropertyName("theme")]
        [CanBeNull]
        public string Theme { get; set; } = "/Styles/LightStyle.axaml";

        [JsonPropertyName("sourcePath")]
        [CanBeNull]
        public string SourcePath { get; set; }

        [JsonPropertyName("destinationPath")]
        [CanBeNull]
        public string DestinationPath { get; set; }

        [JsonPropertyName("debugLogging")]
        public bool DebugLogging { get; set; } = true;

        [JsonPropertyName("attemptFixes")]
        public bool AttemptFixes { get; set; } = true;

        [JsonPropertyName("noAdmin")]
        public bool NoAdmin { get; set; }

        [JsonPropertyName("caseInsensitivePathing")]
        public bool CaseInsensitivePathing { get; set; } = true;

        [JsonPropertyName("archiveDeepCheck")]
        public bool ArchiveDeepCheck { get; set; }

        [JsonPropertyName("useMultiThreadedIO")]
        public bool UseMultiThreadedIO { get; set; }

        [JsonPropertyName("useCopyForMoveActions")]
        public bool UseCopyForMoveActions { get; set; }

        [JsonPropertyName("lastOutputDirectory")]
        [CanBeNull]
        public string LastOutputDirectory { get; set; }

        [JsonPropertyName("validateAndReplaceInvalidArchives")]
        public bool ValidateAndReplaceInvalidArchives { get; set; } = true;

        [JsonPropertyName("filterDownloadsByResolution")]
        public bool FilterDownloadsByResolution { get; set; } = false;

        [JsonPropertyName("nexusModsApiKey")]
        [CanBeNull]
        public string NexusModsApiKey { get; set; }

        [JsonPropertyName("fileEncoding")]
        [CanBeNull]
        public string FileEncoding { get; set; } = "utf-8";

        [JsonPropertyName("selectedHolopatcherVersion")]
        [CanBeNull]
        public string SelectedHolopatcherVersion { get; set; }

        [JsonPropertyName("patcherEngine")]
        [CanBeNull]
        public string PatcherEngine { get; set; } = PatcherEngines.Holopatcher;

        [JsonPropertyName("kpatcherExecutablePath")]
        [CanBeNull]
        public string KPatcherExecutablePath { get; set; }

        [JsonPropertyName("enableFileWatcher")]
        public bool EnableFileWatcher { get; set; } = true;

        [JsonPropertyName("spoilerFreeMode")]
        public bool SpoilerFreeMode { get; set; } = false;

        public AppSettings()
        {
        }

        public static AppSettings FromCurrentState([NotNull] MainConfig mainConfig, [CanBeNull] string currentTheme, bool spoilerFreeMode = false)
        {
            if (mainConfig is null)
            {
                throw new ArgumentNullException(nameof(mainConfig));
            }

            Logger.LogVerbose($"[AppSettings.FromCurrentState] Creating settings from MainConfig:");
            Logger.LogVerbose($"[AppSettings.FromCurrentState] SourcePath: '{mainConfig.sourcePathFullName}'");
            Logger.LogVerbose($"[AppSettings.FromCurrentState] DestinationPath: '{mainConfig.destinationPathFullName}'");
            Logger.LogVerbose($"[AppSettings.FromCurrentState] Theme: '{currentTheme}'");
            Logger.LogVerbose($"[AppSettings.FromCurrentState] SpoilerFreeMode: '{spoilerFreeMode}'");

            return new AppSettings
            {
                Theme = currentTheme ?? "/Styles/LightStyle.axaml",
                SourcePath = mainConfig.sourcePathFullName,
                DestinationPath = mainConfig.destinationPathFullName,
                DebugLogging = mainConfig.debugLogging,
                AttemptFixes = mainConfig.attemptFixes,
                NoAdmin = mainConfig.noAdmin,
                CaseInsensitivePathing = mainConfig.caseInsensitivePathing,
                ArchiveDeepCheck = mainConfig.archiveDeepCheck,
                UseMultiThreadedIO = mainConfig.useMultiThreadedIO,
                UseCopyForMoveActions = mainConfig.useCopyForMoveActions,
                LastOutputDirectory = mainConfig.lastOutputDirectory?.FullName,
                ValidateAndReplaceInvalidArchives = mainConfig.validateAndReplaceInvalidArchives,
                FilterDownloadsByResolution = mainConfig.filterDownloadsByResolution,
                NexusModsApiKey = mainConfig.nexusModsApiKey,
                FileEncoding = mainConfig.fileEncoding,
                SelectedHolopatcherVersion = mainConfig.selectedHolopatcherVersion,
                PatcherEngine = mainConfig.patcherEngine,
                KPatcherExecutablePath = mainConfig.kpatcherExecutablePath,
                EnableFileWatcher = mainConfig.enableFileWatcher,
                SpoilerFreeMode = spoilerFreeMode,
            };
        }

        public void ApplyToMainConfig([NotNull] MainConfig mainConfig, [NotNull] out string theme, out bool spoilerFreeMode)
        {
            if (mainConfig is null)
            {
                throw new ArgumentNullException(nameof(mainConfig));
            }

            Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] Applying settings to MainConfig:");
            Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] SourcePath from settings: '{SourcePath}'");
            Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] DestinationPath from settings: '{DestinationPath}'");
            Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] SpoilerFreeMode from settings: '{SpoilerFreeMode}'");

            if (!string.IsNullOrEmpty(SourcePath))
            {
                mainConfig.sourcePath = new DirectoryInfo(SourcePath);
                Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] Applied SourcePath: '{mainConfig.sourcePathFullName}'");
            }
            else
            {
                Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] SourcePath is empty, not applying");
            }

            if (!string.IsNullOrEmpty(DestinationPath))
            {
                mainConfig.destinationPath = new DirectoryInfo(DestinationPath);
                Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] Applied DestinationPath: '{mainConfig.destinationPathFullName}'");
            }
            else
            {
                Logger.LogVerbose($"[AppSettings.ApplyToMainConfig] DestinationPath is empty, not applying");
            }

            mainConfig.debugLogging = DebugLogging;
            mainConfig.attemptFixes = AttemptFixes;
            mainConfig.noAdmin = NoAdmin;
            mainConfig.caseInsensitivePathing = CaseInsensitivePathing;
            mainConfig.archiveDeepCheck = ArchiveDeepCheck;
            mainConfig.useMultiThreadedIO = UseMultiThreadedIO;
            mainConfig.useCopyForMoveActions = UseCopyForMoveActions;
            mainConfig.validateAndReplaceInvalidArchives = ValidateAndReplaceInvalidArchives;
            mainConfig.filterDownloadsByResolution = FilterDownloadsByResolution;
            mainConfig.nexusModsApiKey = NexusModsApiKey;
            mainConfig.fileEncoding = FileEncoding
                                      ?? "utf-8";
            mainConfig.selectedHolopatcherVersion = SelectedHolopatcherVersion;
            mainConfig.patcherEngine = PatcherEngine;
            mainConfig.kpatcherExecutablePath = KPatcherExecutablePath;
            mainConfig.enableFileWatcher = EnableFileWatcher;

            if (!string.IsNullOrEmpty(LastOutputDirectory) && Directory.Exists(LastOutputDirectory))
            {
                mainConfig.lastOutputDirectory = new DirectoryInfo(LastOutputDirectory);
            }

            theme = Theme ?? "/Styles/LightStyle.axaml"; // Default to Light theme
            spoilerFreeMode = SpoilerFreeMode;

            // Set TargetGame from theme (they're the same thing)
            MainConfig.TargetGame = GetTargetGameFromTheme(theme);
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

            // Handle Light theme (not game-specific)
            if (themePath.IndexOf("LightStyle", StringComparison.OrdinalIgnoreCase) >= 0
                || themePath.IndexOf("FluentLightStyle", StringComparison.OrdinalIgnoreCase) >= 0)
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
    }

    public static class SettingsManager
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KOTORModSync"
        );

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        [NotNull]
        public static AppSettings LoadSettings()
        {
            try
            {
                Logger.LogVerbose($"[SettingsManager.LoadSettings] Loading settings from: '{SettingsFilePath}'");

                if (!File.Exists(SettingsFilePath))
                {
                    Logger.LogVerbose($"[SettingsManager.LoadSettings] No settings file found, using defaults");
                    return new AppSettings();
                }

                string json = File.ReadAllText(SettingsFilePath);
                Logger.LogVerbose($"[SettingsManager.LoadSettings] Read settings JSON: {json}");

                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

                if (settings is null)
                {
                    Logger.LogWarning("[SettingsManager.LoadSettings] Failed to deserialize settings, using defaults");
                    return new AppSettings();
                }

                Logger.LogVerbose($"[SettingsManager.LoadSettings] Successfully loaded settings:");
                Logger.LogVerbose($"[SettingsManager.LoadSettings] SourcePath: '{settings.SourcePath}'");
                Logger.LogVerbose($"[SettingsManager.LoadSettings] DestinationPath: '{settings.DestinationPath}'");
                Logger.LogVerbose($"[SettingsManager.LoadSettings] Theme: '{settings.Theme}'");

                return settings;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, customMessage: "Failed to load settings, using defaults");
                return new AppSettings();
            }
        }

        public static void SaveSettings([NotNull] AppSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            try
            {
                Logger.LogVerbose($"[SettingsManager.SaveSettings] Saving settings to: '{SettingsFilePath}'");
                Logger.LogVerbose($"[SettingsManager.SaveSettings] SourcePath: '{settings.SourcePath}'");
                Logger.LogVerbose($"[SettingsManager.SaveSettings] DestinationPath: '{settings.DestinationPath}'");
                Logger.LogVerbose($"[SettingsManager.SaveSettings] Theme: '{settings.Theme}'");

                if (!Directory.Exists(SettingsDirectory))
                {
                    _ = Directory.CreateDirectory(SettingsDirectory);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                Logger.LogVerbose($"[SettingsManager.SaveSettings] Serialized JSON: {json}");

                File.WriteAllText(SettingsFilePath, json);

                Logger.LogVerbose($"[SettingsManager.SaveSettings] Successfully saved settings to '{SettingsFilePath}'");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, customMessage: "Failed to save settings");
            }
        }
    }
}
