// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using ItemNotNullAttribute = JetBrains.Annotations.ItemNotNullAttribute;
using NotNullAttribute = JetBrains.Annotations.NotNullAttribute;
namespace KOTORModSync.Core
{
    [SuppressMessage(
        category: "Performance",
        checkId: "CA1822:Mark members as static",
        Justification = "unique naming scheme used for class"
    )]
    [SuppressMessage(
        category: "CodeQuality",
        checkId: "IDE0079:Remove unnecessary suppression",
        Justification = "<Pending>"
    )]
    [SuppressMessage(category: "ReSharper", checkId: "MemberCanBeMadeStatic.Global")]
    [SuppressMessage(category: "ReSharper", checkId: "InconsistentNaming")]
    [SuppressMessage(category: "ReSharper", checkId: "MemberCanBePrivate.Global")]
    [SuppressMessage(category: "ReSharper", checkId: "UnusedMember.Global")]
    /// <summary>
    /// Central configuration container for both global (static) settings and instance-based access.
    /// <para>
    /// Every mutable static property has a matching lowercase instance property. The static property exposes
    /// a <c>private set</c>, which forces callers to mutate configuration via an instance:
    /// <code>
    /// var cfg = new MainConfig();
    /// cfg.debugLogging = true; // ✅ valid
    /// </code>
    /// <code>
    /// MainConfig.DebugLogging = true; // ❌ invalid - setter is private
    /// </code>
    /// This pattern ensures all writes flow through a single code-path and makes it obvious how to follow the
    /// existing design.
    /// </para>
    /// </summary>
    public sealed class MainConfig : INotifyPropertyChanged
    {
        [JetBrains.Annotations.NotNull]
        public static MainConfig Instance { get; set; } = new MainConfig();

        public MainConfig()
        {
            debugLogging = true; // Default to true for debugging persistence issues
            attemptFixes = true;
            noAdmin = false;
            caseInsensitivePathing = true;
            validateAndReplaceInvalidArchives = true;
            filterDownloadsByResolution = false;
        }

        [JetBrains.Annotations.NotNull]
        public static string CurrentVersion => "2.0.0a1";

        public static class ValidTargetGames
        {
            public const string K1 = "K1";
            public const string TSL = "TSL";
            public const string KOTOR1 = "KOTOR1";
            public const string KOTOR2 = "KOTOR2";
        }

        /// <summary>Indicates whether elevation checks should be skipped. Mutate via <see cref="noAdmin"/>.</summary>
        public static bool NoAdmin { get; private set; }
        /// <summary>Instance accessor for <see cref="NoAdmin"/>.</summary>
        public bool noAdmin
        {
            get => NoAdmin;
            set => NoAdmin = value;
        }

        /// <summary>Forces move actions to use copy + delete semantics. Mutate via <see cref="useCopyForMoveActions"/>.</summary>
        public static bool UseCopyForMoveActions { get; private set; }
        /// <summary>Instance accessor for <see cref="UseCopyForMoveActions"/>.</summary>
        public bool useCopyForMoveActions
        {
            get => UseCopyForMoveActions;
            set => UseCopyForMoveActions = value;
        }

        /// <summary>Enables multi-threaded file I/O. Mutate via <see cref="useMultiThreadedIO"/>.</summary>
        public static bool UseMultiThreadedIO { get; private set; }
        /// <summary>Instance accessor for <see cref="UseMultiThreadedIO"/>.</summary>
        public bool useMultiThreadedIO { get => UseMultiThreadedIO; set => UseMultiThreadedIO = value; }


        /// <summary>Controls case-insensitive path comparisons (forced <c>false</c> on Windows). Mutate via <see cref="caseInsensitivePathing"/>.</summary>
        public static bool CaseInsensitivePathing { get; private set; }
        /// <summary>Instance accessor for <see cref="CaseInsensitivePathing"/>.</summary>
        public bool caseInsensitivePathing
        {
            get => CaseInsensitivePathing;
            set => CaseInsensitivePathing = Utility.UtilityHelper.GetOperatingSystem() != OSPlatform.Windows && value;
        }

        /// <summary>Enables verbose diagnostic logging. Mutate via <see cref="debugLogging"/>.</summary>
        public static bool DebugLogging { get; private set; }
        /// <summary>Instance accessor for <see cref="DebugLogging"/>.</summary>
        public bool debugLogging { get => DebugLogging; set => DebugLogging = value; }


        /// <summary>Remembers the last output directory selected by the user. Mutate via <see cref="lastOutputDirectory"/>.</summary>
        public static DirectoryInfo LastOutputDirectory { get; private set; }
        /// <summary>Instance accessor for <see cref="LastOutputDirectory"/>.</summary>
        [CanBeNull]
        public DirectoryInfo lastOutputDirectory
        {
            get => LastOutputDirectory;
            set => LastOutputDirectory = value;
        }

        /// <summary>Controls automated fixups (e.g. path corrections). Mutate via <see cref="attemptFixes"/>.</summary>
        public static bool AttemptFixes { get; private set; }
        /// <summary>Instance accessor for <see cref="AttemptFixes"/>.</summary>
        public bool attemptFixes { get => AttemptFixes; set => AttemptFixes = value; }


        /// <summary>Enables deeper archive validation routines. Mutate via <see cref="archiveDeepCheck"/>.</summary>
        public static bool ArchiveDeepCheck { get; private set; }
        /// <summary>Instance accessor for <see cref="ArchiveDeepCheck"/>.</summary>
        public bool archiveDeepCheck { get => ArchiveDeepCheck; set => ArchiveDeepCheck = value; }


        /// <summary>Validates and replaces invalid archives automatically. Mutate via <see cref="validateAndReplaceInvalidArchives"/>.</summary>
        public static bool ValidateAndReplaceInvalidArchives { get; private set; }
        /// <summary>Instance accessor for <see cref="ValidateAndReplaceInvalidArchives"/>.</summary>
        public bool validateAndReplaceInvalidArchives { get => ValidateAndReplaceInvalidArchives; set => ValidateAndReplaceInvalidArchives = value; }


        /// <summary>Filters downloads using the configured resolution preference. Mutate via <see cref="filterDownloadsByResolution"/>.</summary>
        public static bool FilterDownloadsByResolution { get; private set; }
        /// <summary>Instance accessor for <see cref="FilterDownloadsByResolution"/>.</summary>
        public bool filterDownloadsByResolution { get => FilterDownloadsByResolution; set => FilterDownloadsByResolution = value; }


        /// <summary>Stores the Nexus Mods API key. Mutate via <see cref="nexusModsApiKey"/>.</summary>
        public static string NexusModsApiKey { get; private set; }
        /// <summary>Instance accessor for <see cref="NexusModsApiKey"/>.</summary>
        public string nexusModsApiKey { get => NexusModsApiKey; set => NexusModsApiKey = value; }


        /// <summary>Active file encoding for configuration serialization. Mutate via <see cref="fileEncoding"/>.</summary>
        public static string FileEncoding { get; private set; } = "utf-8";
        /// <summary>Instance accessor for <see cref="FileEncoding"/>.</summary>
        public string fileEncoding { get => FileEncoding; set => FileEncoding = value ?? "utf-8"; }


        /// <summary>Selected Holopatcher version. Mutate via <see cref="selectedHolopatcherVersion"/>.</summary>
        public static string SelectedHolopatcherVersion { get; private set; }
        /// <summary>Instance accessor for <see cref="SelectedHolopatcherVersion"/>.</summary>
        public string selectedHolopatcherVersion { get => SelectedHolopatcherVersion; set => SelectedHolopatcherVersion = value; }

        /// <summary>
        /// Which backend runs TSLPatcher-style installs (<c>--install --game-dir --tslpatchdata</c>).
        /// Use <see cref="PatcherEngines.Holopatcher"/> (Resources holopatcher / PyKotor) or <see cref="PatcherEngines.KPatcher"/> (external KPatcher CLI).
        /// </summary>
        public static string PatcherEngine { get; private set; } = PatcherEngines.Holopatcher;
        /// <summary>Instance accessor for <see cref="PatcherEngine"/>.</summary>
        public string patcherEngine
        {
            get => PatcherEngine;
            set => PatcherEngine = string.IsNullOrWhiteSpace(value) ? PatcherEngines.Holopatcher : value;
        }

        /// <summary>Optional full path to KPatcher executable when <see cref="PatcherEngine"/> is <see cref="PatcherEngines.KPatcher"/>.</summary>
        public static string KPatcherExecutablePath { get; private set; }
        /// <summary>Instance accessor for <see cref="KPatcherExecutablePath"/>.</summary>
        public string kpatcherExecutablePath
        {
            get => KPatcherExecutablePath;
            set => KPatcherExecutablePath = value;
        }

        /// <summary>Determines whether file-system watchers are enabled. Mutate via <see cref="enableFileWatcher"/>.</summary>
        public static bool EnableFileWatcher { get; private set; }
        /// <summary>Instance accessor for <see cref="EnableFileWatcher"/>.</summary>
        public bool enableFileWatcher { get => EnableFileWatcher; set => EnableFileWatcher = value; }
        /// <summary>Collection of all components currently loaded into the application. Mutate via <see cref="allComponents"/>.</summary>
        [NotNull][ItemNotNull] public static List<ModComponent> AllComponents { get; set; } = new List<ModComponent>();
        /// <summary>Instance accessor for <see cref="AllComponents"/>.</summary>
        [NotNull]
        [ItemNotNull]
        public List<ModComponent> allComponents
        {
            get => AllComponents;
            set => AllComponents = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Editor mode enables advanced mod authoring features.</summary>
        public static bool EditorMode { get; private set; }
        /// <summary>Instance accessor for <see cref="EditorMode"/>.</summary>
        public bool editorMode
        {
            get => EditorMode;
            set
            {
                if (EditorMode != value)
                {
                    EditorMode = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Spoiler-free mode hides mod names and descriptions to prevent story spoilers.</summary>
        public static bool SpoilerFreeMode { get; private set; }
        /// <summary>Instance accessor for <see cref="SpoilerFreeMode"/>.</summary>
        public bool spoilerFreeMode
        {
            get => SpoilerFreeMode;
            set
            {
                if (SpoilerFreeMode != value)
                {
                    SpoilerFreeMode = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Singleton VirtualFileSystemProvider for all dry-run validation and file state simulation.
        /// Initialized once and reused throughout the application lifecycle.
        /// </summary>
        [NotNull]
        public static Services.FileSystem.VirtualFileSystemProvider VirtualFileSystemProvider { get; private set; } = new Services.FileSystem.VirtualFileSystemProvider();

        /// <summary>Currently selected mod component. Mutate via <see cref="currentComponent"/>.</summary>
        [CanBeNull] public static ModComponent CurrentComponent { get; set; }
        [CanBeNull]
        /// <summary>Instance accessor for <see cref="CurrentComponent"/>.</summary>
        public ModComponent currentComponent
        {
            get => CurrentComponent;
            set
            {
                if (CurrentComponent == value)
                {
                    return;
                }

                CurrentComponent = value;
                OnPropertyChanged(nameof(currentComponent));
            }
        }
        /// <summary>Markdown content displayed at the beginning of the installation guide. Mutate via <see cref="preambleContent"/>.</summary>
        public static string PreambleContent { get; set; } = "# Installation Guide\n\nWelcome to the KOTOR Mod Installation Guide. This guide will help you install this mod build for Knights of the Old Republic.\n\n:::warning\nImportant\n:   Please read through these instructions carefully before beginning the installation process.\n:::\n\n### Prerequisites\n\n- A fresh installation of Knights of the Old Republic\n- KOTORModSync - an automated installer that handles TSLPatcher and HoloPatcher installations\n- Approximately 7GB free disk space for mod archives (before extraction)\n\n### Installation Process\n\nKOTORModSync will automatically handle extraction and installation of mods. You just need to:\n\n1. Ensure your game directory is not read-only\n2. Configure your source (mod archives) and destination (game directory) paths\n3. Select the mods you want to install\n4. Let the installer handle the rest\n\n:::warning\nZeroing Step\n:   If you have previously installed mods, it's recommended to perform a fresh install. Uninstall the game, delete all remaining files in the game directory, and reinstall before proceeding.\n:::\n\n### Known Issues\n\n- Some users may experience rare crashes when entering new areas. If this occurs, temporarily disable 'Frame Buffer Effects' and 'Soft Shadows' in Advanced Graphics Options.";
        /// <summary>Instance accessor for <see cref="PreambleContent"/>.</summary>
        public string preambleContent
        {
            get => PreambleContent;
            set => PreambleContent = value ?? string.Empty;
        }
        /// <summary>Markdown content displayed at the end of the installation guide. Mutate via <see cref="epilogueContent"/>.</summary>
        public static string EpilogueContent { get; set; } = "## Post-Installation Notes\n\n### Launch Options\n\nAfter installation, launch the game directly from the executable, not through the Steam interface (if using widescreen support).\n\n### Troubleshooting\n\nIf you encounter issues:\n\n- **Crash on load**: Try disabling 'Frame Buffer Effects' in Advanced Graphics Options\n- **Character stuck after combat**: Enable v-sync or set your monitor to 60hz\n- **Rare crashes**: Update your graphics drivers\n\nFor additional support, please consult the troubleshooting section in the main documentation.";
        /// <summary>Instance accessor for <see cref="EpilogueContent"/>.</summary>
        public string epilogueContent
        {
            get => EpilogueContent;
            set => EpilogueContent = value ?? string.Empty;
        }
        /// <summary>Markdown block warning about widescreen instructions. Mutate via <see cref="widescreenWarningContent"/>.</summary>
        public static string WidescreenWarningContent { get; set; } = ":::note\nWidescreen Support\n:   This build includes optional widescreen support. Widescreen mods must be installed before applying the 4GB patcher. Please see the widescreen section below for details.\n:::";
        /// <summary>Instance accessor for <see cref="WidescreenWarningContent"/>.</summary>
        public string widescreenWarningContent
        {
            get => WidescreenWarningContent;
            set => WidescreenWarningContent = value ?? string.Empty;
        }
        /// <summary>Markdown block highlighting Aspyr-exclusive content. Mutate via <see cref="aspyrExclusiveWarningContent"/>.</summary>
        public static string AspyrExclusiveWarningContent { get; set; } = ":::warning\nAspyr Version Required\n:   The following mods require the Aspyr patch version of KOTOR 2. If you are using the legacy version, these mods should be skipped.\n:::";
        /// <summary>Instance accessor for <see cref="AspyrExclusiveWarningContent"/>.</summary>
        public string aspyrExclusiveWarningContent
        {
            get => AspyrExclusiveWarningContent;
            set => AspyrExclusiveWarningContent = value ?? string.Empty;
        }
        /// <summary>Modal warning presented before installation begins. Mutate via <see cref="installationWarningContent"/>.</summary>
        public static string InstallationWarningContent { get; set; } = ":::warning\nInstallation Warning\n:   This mod build is intended for use with the Aspyr patch version of KOTOR 2. If you are using the legacy version, some mods may not work correctly.\n:::";
        /// <summary>Instance accessor for <see cref="InstallationWarningContent"/>.</summary>
        public string installationWarningContent
        {
            get => InstallationWarningContent;
            set => InstallationWarningContent = value ?? string.Empty;
        }
        /// <summary>Target game identifier for the current mod build. Mutate via <see cref="targetGame"/>.</summary>
        public static string TargetGame { get; set; } = string.Empty;
        /// <summary>Instance accessor for <see cref="TargetGame"/>.</summary>
        public string targetGame
        {
            get => TargetGame;
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && !IsValidTargetGame(value))
                {
                    Logger.LogWarning($"Invalid target game '{value}'. Valid values are 'K1' or 'TSL'. Value will be stored as-is but may cause issues.");
                }
                TargetGame = value ?? string.Empty;
            }
        }
        public static bool IsValidTargetGame(string game)
        {
            if (string.IsNullOrWhiteSpace(game))
            {
                return false;
            }

            return game.Equals(ValidTargetGames.K1, StringComparison.OrdinalIgnoreCase)
                || game.Equals(ValidTargetGames.TSL, StringComparison.OrdinalIgnoreCase)
                || game.Equals(ValidTargetGames.KOTOR1, StringComparison.OrdinalIgnoreCase)
                || game.Equals(ValidTargetGames.KOTOR2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Semantic version of the serialized configuration format. Mutate via <see cref="fileFormatVersion"/>.</summary>
        public static string FileFormatVersion { get; private set; } = "2.0";
        /// <summary>Instance accessor for <see cref="FileFormatVersion"/>.</summary>
        public string fileFormatVersion
        {
            get => FileFormatVersion;
            set => FileFormatVersion = value ?? "2.0";
        }

        /// <summary>Name of the current mod build. Mutate via <see cref="buildName"/>.</summary>
        public static string BuildName { get; private set; } = string.Empty;
        /// <summary>Instance accessor for <see cref="BuildName"/>.</summary>
        public string buildName
        {
            get => BuildName;
            set => BuildName = value ?? string.Empty;
        }

        /// <summary>Author attribution for the current mod build. Mutate via <see cref="buildAuthor"/>.</summary>
        public static string BuildAuthor { get; private set; } = string.Empty;
        /// <summary>Instance accessor for <see cref="BuildAuthor"/>.</summary>
        public string buildAuthor
        {
            get => BuildAuthor;
            set => BuildAuthor = value ?? string.Empty;
        }

        /// <summary>Description for the current mod build. Mutate via <see cref="buildDescription"/>.</summary>
        public static string BuildDescription { get; private set; } = string.Empty;
        /// <summary>Instance accessor for <see cref="BuildDescription"/>.</summary>
        public string buildDescription
        {
            get => BuildDescription;
            set => BuildDescription = value ?? string.Empty;
        }

        /// <summary>Timestamp indicating when the configuration was last modified. Mutate via <see cref="lastModified"/>.</summary>
        public static DateTime? LastModified { get; private set; }
        [CanBeNull]
        /// <summary>Instance accessor for <see cref="LastModified"/>.</summary>
        public DateTime? lastModified
        {
            get => LastModified;
            set => LastModified = value;
        }

        /// <summary>Absolute path to the source directory (mod archives). Mutate via <see cref="sourcePath"/>.</summary>
        [CanBeNull] public static DirectoryInfo SourcePath { get; private set; }
        [CanBeNull]
        /// <summary>Instance accessor for <see cref="SourcePath"/>.</summary>
        public DirectoryInfo sourcePath
        {
            get => SourcePath;
            set
            {
                if (SourcePath == value)
                {
                    return;
                }

                SourcePath = value;
                OnPropertyChanged(nameof(sourcePathFullName));
            }
        }
        [CanBeNull] public string sourcePathFullName => SourcePath?.FullName;
        /// <summary>Absolute path to the destination directory (game folder). Mutate via <see cref="destinationPath"/>.</summary>

        [CanBeNull] public static DirectoryInfo DestinationPath { get; private set; }
        [CanBeNull]
        /// <summary>Instance accessor for <see cref="DestinationPath"/>.</summary>
        public DirectoryInfo destinationPath
        {
            get => DestinationPath;
            set
            {
                if (DestinationPath == value)
                {
                    return;
                }

                DestinationPath = value;
                OnPropertyChanged(nameof(destinationPathFullName));
            }
        }
        [CanBeNull] public string destinationPathFullName => DestinationPath?.FullName;

        /// <summary>Maximum cache size in megabytes for distributed cache storage (default 10GB). Mutate via <see cref="maxCacheSizeMB"/>.</summary>
        public static long MaxCacheSizeMB { get; set; } = 10240;
        /// <summary>Instance accessor for <see cref="MaxCacheSizeMB"/>.</summary>
        public long maxCacheSizeMB
        {
            get => MaxCacheSizeMB;
            set => MaxCacheSizeMB = value;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName][CanBeNull] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
