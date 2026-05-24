// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

using KOTORModSync.Core.Utility;

using Newtonsoft.Json;

using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;
namespace KOTORModSync.Core
{
    public class ModComponent : INotifyPropertyChanged
    {
        public enum InstallExitCode
        {
            [Description("Completed Successfully")]
            Success,
            [Description("A dependency or restriction violation between components has occurred.")]
            DependencyViolation,
            [Description("User cancelled the installation.")]
            UserCancelledInstall,
            [Description("An invalid operation was attempted.")]
            InvalidOperation,
            [Description("Required mod archive or file was not found in the mod workspace (download or copy files first).")]
            MissingSourceFiles,
            [Description("One or more mods failed but the batch continued (--continue-on-mod-failure).")]
            CompletedWithFailures,
            UnknownError,
        }
        public enum ComponentInstallState
        {
            Pending,
            Running,
            Completed,
            Failed,
            Blocked,
            Skipped,
        }
        public enum InstructionInstallState
        {
            Pending,
            Completed,
            Failed,
        }

        [NotNull] private string _author = string.Empty;
        [NotNull] private List<string> _category = new List<string>();
        [NotNull] private List<Guid> _dependencies = new List<Guid>();
        [NotNull] private List<string> _dependencyNames = new List<string>();
        [NotNull] private string _description = string.Empty;
        [NotNull] internal string _descriptionSpoilerFree = string.Empty;
        [NotNull] private string _directions = string.Empty;
        [NotNull] internal string _directionsSpoilerFree = string.Empty;
        [NotNull] private string _downloadInstructions = string.Empty;
        [NotNull] internal string _downloadInstructionsSpoilerFree = string.Empty;
        [NotNull] private string _usageWarning = string.Empty;
        [NotNull] internal string _usageWarningSpoilerFree = string.Empty;
        [NotNull] private string _screenshots = string.Empty;
        [NotNull] internal string _screenshotsSpoilerFree = string.Empty;
        private Guid _guid;
        [NotNull] private List<Guid> _installAfter = new List<Guid>();
        [NotNull] private string _installationMethod = string.Empty;
        [NotNull] private List<Guid> _installBefore = new List<Guid>();
        [NotNull]
        [ItemNotNull]
        private ObservableCollection<Instruction> _instructions = new ObservableCollection<Instruction>();
        private bool _isSelected;
        private ComponentInstallState _installState = ComponentInstallState.Pending;
        private DateTimeOffset? _lastStartedUtc;
        private DateTimeOffset? _lastCompletedUtc;


        public static string CheckpointFolderName => Services.Checkpoints.CheckpointPaths.CheckpointFolderName;
        [NotNull][ItemNotNull] private List<string> _language = new List<string>();
        [NotNull] private Dictionary<string, ResourceMetadata> _resourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal);
        [NotNull] private List<string> _excludedDownloads = new List<string>();
        [NotNull] private string _name = string.Empty;
        [NotNull] private string _nameSpoilerFree = string.Empty;
        [NotNull] private string _nameFieldContent = string.Empty;
        [NotNull] private string _heading = string.Empty;
        [NotNull] private ObservableCollection<Option> _options = new ObservableCollection<Option>();
        [NotNull] private List<Guid> _restrictions = new List<Guid>();
        [NotNull] private string _tier = string.Empty;
        private bool _isDownloaded;
        private bool _isValidating;
        private bool _widescreenOnly;
        public Guid Guid
        {
            get => _guid;
            set
            {
                if (_guid == value)
                {
                    return;
                }

                _guid = value;
                OnPropertyChanged();
            }
        }

        [NotNull]
        public string Name
        {
            get => _name;
            set
            {
                if (string.Equals(_name, value, StringComparison.Ordinal))
                {
                    return;
                }

                _name = value;
                OnPropertyChanged();
            }
        }

        [NotNull]
        public string NameSpoilerFree
        {
            get => _nameSpoilerFree;
            set
            {
                if (string.Equals(_nameSpoilerFree, value, StringComparison.Ordinal))
                {
                    return;
                }

                _nameSpoilerFree = value;
                OnPropertyChanged();
            }
        }

        [NotNull]
        [JsonIgnore]
        public string NameFieldContent
        {
            get => _nameFieldContent;


            set
            {
                if (string.Equals(_nameFieldContent, value, StringComparison.Ordinal))
                {
                    return;
                }

                _nameFieldContent = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string Heading
        {
            get => _heading;


            set
            {
                if (string.Equals(_heading, value, StringComparison.Ordinal))
                {
                    return;
                }

                _heading = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string Author
        {
            get => _author;


            set
            {
                if (string.Equals(_author, value, StringComparison.Ordinal))
                {
                    return;
                }

                _author = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public IReadOnlyList<string> Category
        {
            get => new List<string>(_category);
            set
            {
                if (_category == value)
                {
                    return;
                }

                _category = new List<string>(value);
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string Tier
        {
            get => _tier;
            set
            {
                string normalizedValue = CategoryTierDefinitions.NormalizeTier(value);


                if (string.Equals(_tier, normalizedValue, StringComparison.Ordinal))
                {
                    return;
                }

                _tier = normalizedValue;
                OnPropertyChanged();
            }
        }
        [NotNull]
        [ItemNotNull]
        public IReadOnlyList<string> Language
        {
            get => new List<string>(_language);
            set
            {
                if (_language == value)
                {
                    return;
                }

                _language = new List<string>(value);
                OnPropertyChanged();
            }
        }

        [NotNull]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0016:Prefer using collection abstraction instead of implementation", Justification = "<Pending>")]
        public Dictionary<string, ResourceMetadata> ResourceRegistry
        {
            get => new Dictionary<string, ResourceMetadata>(_resourceRegistry, StringComparer.OrdinalIgnoreCase);
            set
            {
                if (_resourceRegistry == value)
                {
                    return;
                }

                _resourceRegistry = new Dictionary<string, ResourceMetadata>(value, StringComparer.OrdinalIgnoreCase);
                OnPropertyChanged();
            }
        }

        [NotNull]
        public IReadOnlyList<string> ExcludedDownloads
        {
            get => new List<string>(_excludedDownloads);
            set
            {
                if (_excludedDownloads == value)
                {
                    return;
                }

                _excludedDownloads = new List<string>(value);
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string Description
        {
            get => _description;


            set
            {
                if (string.Equals(_description, value, StringComparison.Ordinal))
                {
                    return;
                }

                _description = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string DescriptionSpoilerFree
        {
            get => _descriptionSpoilerFree;


            set
            {
                if (string.Equals(_descriptionSpoilerFree, value, StringComparison.Ordinal))
                {
                    return;
                }

                _descriptionSpoilerFree = value;
                OnPropertyChanged();
            }
        }

        [NotNull]
        public string InstallationMethod
        {
            get => _installationMethod;


            set
            {
                if (string.Equals(_installationMethod, value, StringComparison.Ordinal))
                {
                    return;
                }

                _installationMethod = value;
                OnPropertyChanged();
            }
        }

        [NotNull]
        public string Directions
        {
            get => _directions;


            set
            {
                if (string.Equals(_directions, value, StringComparison.Ordinal))
                {
                    return;
                }

                _directions = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string DirectionsSpoilerFree
        {
            get => _directionsSpoilerFree;


            set
            {
                if (string.Equals(_directionsSpoilerFree, value, StringComparison.Ordinal))
                {
                    return;
                }

                _directionsSpoilerFree = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string DownloadInstructions
        {
            get => _downloadInstructions;


            set
            {
                if (string.Equals(_downloadInstructions, value, StringComparison.Ordinal))
                {
                    return;
                }

                _downloadInstructions = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string DownloadInstructionsSpoilerFree
        {
            get => _downloadInstructionsSpoilerFree;


            set
            {
                if (string.Equals(_downloadInstructionsSpoilerFree, value, StringComparison.Ordinal))
                {
                    return;
                }

                _downloadInstructionsSpoilerFree = value;
                OnPropertyChanged();
            }
        }

        [NotNull]
        public string UsageWarning
        {
            get => _usageWarning;


            set
            {
                if (string.Equals(_usageWarning, value, StringComparison.Ordinal))
                {
                    return;
                }

                _usageWarning = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string UsageWarningSpoilerFree
        {
            get => _usageWarningSpoilerFree;


            set
            {
                if (string.Equals(_usageWarningSpoilerFree, value, StringComparison.Ordinal))
                {
                    return;
                }

                _usageWarningSpoilerFree = value;
                OnPropertyChanged();
            }
        }

        [NotNull]
        public string Screenshots
        {
            get => _screenshots;


            set
            {
                if (string.Equals(_screenshots, value, StringComparison.Ordinal))
                {
                    return;
                }

                _screenshots = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string ScreenshotsSpoilerFree
        {
            get => _screenshotsSpoilerFree;


            set
            {
                if (string.Equals(_screenshotsSpoilerFree, value, StringComparison.Ordinal))
                {
                    return;
                }

                _screenshotsSpoilerFree = value;
                OnPropertyChanged();
            }
        }

        [NotNull] private string _knownBugs = string.Empty;
        [NotNull]
        public string KnownBugs
        {
            get => _knownBugs;


            set
            {
                if (string.Equals(_knownBugs, value, StringComparison.Ordinal))
                {
                    return;
                }

                _knownBugs = value;
                OnPropertyChanged();
            }
        }
        [NotNull] private string _installationWarning = string.Empty;
        [NotNull] private string _compatibilityWarning = string.Empty;
        [NotNull] private string _steamNotes = string.Empty;
        [NotNull]
        public string InstallationWarning
        {
            get => _installationWarning;


            set
            {
                if (string.Equals(_installationWarning, value, StringComparison.Ordinal))
                {
                    return;
                }

                _installationWarning = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string CompatibilityWarning
        {
            get => _compatibilityWarning;


            set
            {
                if (string.Equals(_compatibilityWarning, value, StringComparison.Ordinal))
                {
                    return;
                }

                _compatibilityWarning = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public string SteamNotes
        {
            get => _steamNotes;


            set
            {
                if (string.Equals(_steamNotes, value, StringComparison.Ordinal))
                {
                    return;
                }

                _steamNotes = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public List<Guid> Dependencies
        {
            get => _dependencies;
            set
            {
                if (_dependencies == value)
                {
                    return;
                }

                _dependencies = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public List<string> DependencyNames
        {
            get => _dependencyNames;
            set
            {
                if (_dependencyNames == value)
                {
                    return;
                }

                _dependencyNames = value;
                OnPropertyChanged();
            }
        }
        [NotNull] private Dictionary<Guid, string> _dependencyGuidToOriginalName = new Dictionary<Guid, string>();
        [NotNull]
        [JsonIgnore]
        public Dictionary<Guid, string> DependencyGuidToOriginalName
        {
            get => _dependencyGuidToOriginalName;
            set
            {
                _dependencyGuidToOriginalName = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public List<Guid> Restrictions
        {
            get => _restrictions;
            set
            {
                _restrictions = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public List<Guid> InstallBefore
        {
            get => _installBefore;
            set
            {
                _installBefore = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public List<Guid> InstallAfter
        {
            get => _installAfter;
            set
            {
                _installAfter = value;
                OnPropertyChanged();
            }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        [ItemNotNull]
        public ObservableCollection<Instruction> Instructions
        {
            get => _instructions;
            set
            {
                if (_instructions != value)
                {
                    _instructions = value;
                    OnPropertyChanged();
                }
            }
        }
        [JsonIgnore]
        public ComponentInstallState InstallState
        {
            get => _installState;
            set
            {
                if (_installState == value)
                {
                    return;
                }

                _installState = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        [CanBeNull]
        public DateTimeOffset? LastStartedUtc
        {
            get => _lastStartedUtc;
            set
            {
                if (_lastStartedUtc == value)
                {
                    return;
                }

                _lastStartedUtc = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        [CanBeNull]
        public DateTimeOffset? LastCompletedUtc
        {
            get => _lastCompletedUtc;
            set
            {
                if (_lastCompletedUtc == value)
                {
                    return;
                }

                _lastCompletedUtc = value;
                OnPropertyChanged();
            }
        }
        [NotNull]
        public ObservableCollection<Option> Options
        {
            get => _options;
            set
            {
                if (_options == value)
                {
                    return;
                }

                _options.CollectionChanged -= OptionsCollectionChanged;
                _options = value;
                _options.CollectionChanged += OptionsCollectionChanged;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool IsDownloaded
        {
            get => _isDownloaded;
            set
            {
                if (_isDownloaded == value)
                {
                    return;
                }

                _isDownloaded = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool IsValidating
        {
            get => _isValidating;
            set
            {
                if (_isValidating == value)
                {
                    return;
                }

                _isValidating = value;
                OnPropertyChanged();
            }
        }
        public bool WidescreenOnly
        {
            get => _widescreenOnly;
            set
            {
                if (_widescreenOnly == value)
                {
                    return;
                }

                _widescreenOnly = value;
                OnPropertyChanged();
            }
        }
        private bool? _aspyrExclusive;
        [JsonIgnore]
        public bool? AspyrExclusive
        {
            get => _aspyrExclusive;
            set
            {
                if (_aspyrExclusive == value)
                {
                    return;
                }

                _aspyrExclusive = value;
                OnPropertyChanged();
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OptionsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Options));
        }
        private void OnPropertyChanged([CallerMemberName][CanBeNull] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        [NotNull]
        public string SerializeComponent()
        {
            return Services.ModComponentSerializationService.SerializeSingleComponentAsTomlString(this);
        }

        [CanBeNull]
        public static ModComponent DeserializeTomlComponent([NotNull] string tomlString)
        {
            if (tomlString is null)
            {
                throw new ArgumentNullException(nameof(tomlString));
            }

            // Use the unified deserialization service
            IReadOnlyList<ModComponent> components = Services.ModComponentSerializationService.DeserializeModComponentFromTomlString(tomlString);
            return components?.FirstOrDefault();
        }
        public async Task<InstallExitCode> InstallAsync(
            [NotNull] IReadOnlyList<ModComponent> componentsList,
            CancellationToken cancellationToken)
        {
            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            cancellationToken.ThrowIfCancellationRequested();

            InstallState = ComponentInstallState.Running;
            LastStartedUtc = DateTimeOffset.UtcNow;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var realFileSystem = new Services.FileSystem.RealFileSystemProvider();
                InstallExitCode exitCode = await ExecuteInstructionsAsync(
                    Instructions,
                    componentsList,
                    cancellationToken,
                    realFileSystem).ConfigureAwait(false);
                await Logger.LogAsync((string)UtilityHelper.GetEnumDescription(exitCode)).ConfigureAwait(false);

                sw.Stop();
                bool success = exitCode == InstallExitCode.Success;
                Services.TelemetryService.Instance.RecordModInstallation(
                    modName: Name,
                    success: success,
                    durationMs: sw.Elapsed.TotalMilliseconds,
                    errorMessage: success ? null : exitCode.ToString()
                );

                if (exitCode == InstallExitCode.Success)
                {
                    InstallState = ComponentInstallState.Completed;
                    LastCompletedUtc = DateTimeOffset.UtcNow;
                }
                else if (exitCode == InstallExitCode.DependencyViolation)
                {
                    InstallState = ComponentInstallState.Blocked;
                    LastCompletedUtc = DateTimeOffset.UtcNow;
                }
                else if (exitCode == InstallExitCode.MissingSourceFiles && MainConfig.ContinueInstallOnMissingSources)
                {
                    InstallState = ComponentInstallState.Skipped;
                    LastCompletedUtc = DateTimeOffset.UtcNow;
                }
                else
                {
                    InstallState = ComponentInstallState.Failed;
                    LastCompletedUtc = DateTimeOffset.UtcNow;
                }
                return exitCode;
            }
            catch (InvalidOperationException ex)


            {
                await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                sw.Stop();
                Services.TelemetryService.Instance.RecordModInstallation(
                    modName: Name,
                    success: false,
                    durationMs: sw.Elapsed.TotalMilliseconds,
                    errorMessage: ex.Message
                );
                InstallState = ComponentInstallState.Failed;
                LastCompletedUtc = DateTimeOffset.UtcNow;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                Services.TelemetryService.Instance.RecordModInstallation(
                    modName: Name,
                    success: false,
                    durationMs: sw.Elapsed.TotalMilliseconds,
                    errorMessage: "Cancelled"
                );
                InstallState = ComponentInstallState.Failed;
                throw;
            }
            catch (Exception ex)
            {
                await Logger.LogExceptionAsync(ex).ConfigureAwait(false);
                await Logger.LogErrorAsync(
                    "The above exception is not planned and has not been experienced."
                    + " Please report this to the developer."
                ).ConfigureAwait(false);
                sw.Stop();
                Services.TelemetryService.Instance.RecordModInstallation(
                    modName: Name,
                    success: false,
                    durationMs: sw.Elapsed.TotalMilliseconds,
                    errorMessage: ex.Message
                );
                InstallState = ComponentInstallState.Failed;
                LastCompletedUtc = DateTimeOffset.UtcNow;
            }
            return InstallExitCode.UnknownError;
        }
        /// <summary>
        /// Resolves instruction source paths on disk, optionally pulling from downloaded archives first.
        /// Returns false when sources are still missing (caller should return <see cref="Instruction.ActionExitCode.FileNotFoundPre"/>).
        /// </summary>
        private async Task<bool> TryResolveInstructionSourcesAsync(
            [NotNull] Instruction instruction,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider,
            bool skipExistenceCheck = false,
            bool sourceIsNotFilePath = false)
        {
            try
            {
                instruction.SetRealPaths(sourceIsNotFilePath, skipExistenceCheck);
                return true;
            }
            catch (FileNotFoundException)
            {
                await Logger.LogVerboseAsync("Source files not found, attempting auto-extraction...").ConfigureAwait(false);
                if (await TryAutoExtractMissingFilesAsync(instruction, fileSystemProvider).ConfigureAwait(false))
                {
                    return await TryFinalizeInstructionSourceResolutionAsync(
                        instruction,
                        fileSystemProvider,
                        skipExistenceCheck,
                        sourceIsNotFilePath).ConfigureAwait(false);
                }

                if (TryRemapMoveFirstModSubfolderToVariant(instruction, fileSystemProvider))
                {
                    instruction.SetRealPaths(sourceIsNotFilePath, skipExistenceCheck);
                    return true;
                }

                return false;
            }
            catch (Exceptions.WildcardPatternNotFoundException)
            {
                await Logger.LogVerboseAsync("Source pattern not found, attempting auto-extraction...").ConfigureAwait(false);
                if (await TryAutoExtractMissingFilesAsync(instruction, fileSystemProvider).ConfigureAwait(false))
                {
                    return await TryFinalizeInstructionSourceResolutionAsync(
                        instruction,
                        fileSystemProvider,
                        skipExistenceCheck,
                        sourceIsNotFilePath).ConfigureAwait(false);
                }

                if (TryRemapMoveFirstModSubfolderToVariant(instruction, fileSystemProvider))
                {
                    instruction.SetRealPaths(sourceIsNotFilePath, skipExistenceCheck);
                    return true;
                }

                return false;
            }
        }

        private async Task<bool> TryFinalizeInstructionSourceResolutionAsync(
            [NotNull] Instruction instruction,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider,
            bool skipExistenceCheck,
            bool sourceIsNotFilePath)
        {
            try
            {
                instruction.SetRealPaths(sourceIsNotFilePath, skipExistenceCheck);
                return true;
            }
            catch (FileNotFoundException)
            {
            }
            catch (Exceptions.WildcardPatternNotFoundException)
            {
            }

            if (!TryRemapSourcesToExtractedDescendants(instruction, fileSystemProvider))
            {
                return false;
            }

            instruction.SetRealPaths(sourceIsNotFilePath, skipExistenceCheck);
            await Logger.LogVerboseAsync("Remapped extracted archive sources to resolved descendants.").ConfigureAwait(false);
            return true;
        }

        private static bool TryRemapSourcesToExtractedDescendants(
            [NotNull] Instruction instruction,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider)
        {
            if (instruction?.Source == null || instruction.Source.Count == 0 || MainConfig.SourcePath == null)
            {
                return false;
            }

            string modRoot = MainConfig.SourcePath.FullName;
            if (!fileSystemProvider.DirectoryExists(modRoot))
            {
                return false;
            }

            var remappedSources = new List<string>();
            bool changed = false;

            foreach (string source in instruction.Source)
            {
                if (string.IsNullOrWhiteSpace(source))
                {
                    return false;
                }

                string resolved = UtilityHelper.ReplaceCustomVariables(source)
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);

                if (fileSystemProvider.FileExists(resolved))
                {
                    remappedSources.Add(source);
                    continue;
                }

                bool containsWildcards = resolved.IndexOf('*') >= 0 || resolved.IndexOf('?') >= 0;
                if (containsWildcards)
                {
                    string pattern = Path.GetFileName(resolved);
                    List<string> matches = fileSystemProvider.GetFilesInDirectory(modRoot, pattern, SearchOption.AllDirectories)
                        .Where(path => !string.Equals(Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                                      modRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                                      StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (matches.Count == 0)
                    {
                        return false;
                    }

                    remappedSources.AddRange(matches);
                    changed = true;
                    continue;
                }

                string fileName = Path.GetFileName(resolved);
                List<string> candidates = fileSystemProvider.GetFilesInDirectory(modRoot, fileName, SearchOption.AllDirectories)
                    .Where(path => !string.Equals(path, resolved, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (candidates.Count != 1)
                {
                    return false;
                }

                remappedSources.Add(candidates[0]);
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            instruction.Source = remappedSources
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return true;
        }

        /// <summary>
        /// Archives sometimes extract to a variant folder (e.g. HQSkyboxesII_K1_1k) while TOML says HQSkyboxesII_K1.
        /// Remap the first path segment under &lt;&lt;modDirectory&gt;&gt; to a matching sibling directory.
        /// </summary>
        private static bool TryRemapMoveFirstModSubfolderToVariant(
            [NotNull] Instruction instruction,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider)
        {
            if (instruction.Action != Instruction.ActionType.Move)
            {
                return false;
            }

            if (MainConfig.SourcePath is null || instruction.Source is null || instruction.Source.Count == 0)
            {
                return false;
            }

            string modRoot = MainConfig.SourcePath.FullName;
            if (string.IsNullOrEmpty(modRoot) || !fileSystemProvider.DirectoryExists(modRoot))
            {
                return false;
            }

            var newSources = new List<string>(instruction.Source.Count);
            bool anyRemap = false;

            foreach (string raw in instruction.Source)
            {
                if (string.IsNullOrWhiteSpace(raw) ||
                    raw.IndexOf("<<modDirectory>>", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    newSources.Add(raw);
                    continue;
                }

                string marker = "<<modDirectory>>";
                int m = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                int afterMarker = m + marker.Length;
                while (afterMarker < raw.Length && (raw[afterMarker] == '\\' || raw[afterMarker] == '/'))
                {
                    afterMarker++;
                }

                if (afterMarker >= raw.Length)
                {
                    newSources.Add(raw);
                    continue;
                }

                int endSeg = afterMarker;
                while (endSeg < raw.Length && raw[endSeg] != '\\' && raw[endSeg] != '/')
                {
                    endSeg++;
                }

                string expectedFolder = raw.Substring(afterMarker, endSeg - afterMarker);
                if (string.IsNullOrEmpty(expectedFolder))
                {
                    newSources.Add(raw);
                    continue;
                }

                string expectedPath = Path.Combine(modRoot, expectedFolder);
                if (fileSystemProvider.DirectoryExists(expectedPath))
                {
                    newSources.Add(raw);
                    continue;
                }

                string[] subdirs = Directory.GetDirectories(modRoot);
                string bestDir = null;
                foreach (string d in subdirs)
                {
                    string name = Path.GetFileName(d);
                    if (name.Length < expectedFolder.Length)
                    {
                        continue;
                    }

                    if (!name.StartsWith(expectedFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (name.Length > expectedFolder.Length)
                    {
                        char c = name[expectedFolder.Length];
                        if (c != '_' && c != '-' && !char.IsDigit(c))
                        {
                            continue;
                        }
                    }

                    if (endSeg < raw.Length)
                    {
                        string tail = raw.Substring(endSeg).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                        if (!string.IsNullOrEmpty(tail))
                        {
                            int sep = tail.IndexOfAny(new[] { Path.DirectorySeparatorChar, '/', '*' });
                            string nextSeg = sep >= 0 ? tail.Substring(0, sep) : tail;
                            if (!string.IsNullOrEmpty(nextSeg) && nextSeg.IndexOf('*') < 0)
                            {
                                string probe = Path.Combine(d, nextSeg);
                                if (!fileSystemProvider.DirectoryExists(probe))
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    if (bestDir is null || name.Length < Path.GetFileName(bestDir).Length)
                    {
                        bestDir = d;
                    }
                }

                if (bestDir is null)
                {
                    newSources.Add(raw);
                    continue;
                }

                string actualFolder = Path.GetFileName(bestDir);
                string rebuilt = raw.Substring(0, afterMarker) + actualFolder + raw.Substring(endSeg);
                newSources.Add(rebuilt);
                anyRemap = true;
                Logger.LogVerbose(
                    $"[TryRemapMoveFirstModSubfolderToVariant] Remapped '{expectedFolder}' -> '{actualFolder}' for Move instruction"
                );
            }

            if (!anyRemap)
            {
                return false;
            }

            instruction.Source = newSources;
            return true;
        }

        /// <summary>
        /// Some TOMLs chain Patcher (writes into game dir) with Move from a separate "patch" folder that was never
        /// extracted. If every named file already exists at the Move destination, treat the step as satisfied.
        /// </summary>
        private async Task<bool> TrySatisfyRedundantMoveToGameAsync(
            [NotNull] Instruction instruction,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider)
        {
            if (instruction.Action != Instruction.ActionType.Move)
            {
                return false;
            }

            if (MainConfig.DestinationPath is null || instruction.Source is null || instruction.Source.Count == 0)
            {
                return false;
            }

            string destRaw = instruction.Destination ?? string.Empty;
            if (!NetFrameworkCompatibility.Contains(destRaw, "<<kotorDirectory>>", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string relToGame = NetFrameworkCompatibility.Replace(
                destRaw,
                "<<kotorDirectory>>",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);
            relToGame = NetFrameworkCompatibility.Replace(
                relToGame,
                "<<KotorDirectory>>",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);
            relToGame = relToGame.Trim().Trim('\\', '/');
            string gameRoot = MainConfig.DestinationPath.FullName;
            string destDir = string.IsNullOrEmpty(relToGame)
                ? gameRoot
                : Path.Combine(gameRoot, relToGame);
            destDir = Path.GetFullPath(destDir);
            if (!fileSystemProvider.DirectoryExists(destDir))
            {
                return false;
            }

            foreach (string src in instruction.Source)
            {
                if (string.IsNullOrWhiteSpace(src))
                {
                    return false;
                }

                string resolved = UtilityHelper.ReplaceCustomVariables(src).Replace('\\', Path.DirectorySeparatorChar);
                string fileName = Path.GetFileName(resolved);
                if (string.IsNullOrEmpty(fileName) || fileName.IndexOf('*') >= 0 || fileName.IndexOf('?') >= 0)
                {
                    return false;
                }

                string destFile = Path.Combine(destDir, fileName);
                if (!fileSystemProvider.FileExists(destFile))
                {
                    return false;
                }
            }

            await Logger.LogVerboseAsync(
                $"Move skipped: all {instruction.Source.Count} file(s) already present under '{destDir}' (likely applied by a prior Patcher step)."
            ).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Attempts to find and extract files from archives if they're missing.
        /// Returns true if files were found and extracted (or don't need extraction).
        /// </summary>
        private async Task<bool> TryAutoExtractMissingFilesAsync(
            [NotNull] Instruction instruction,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider
        )
        {
            if (instruction?.Source == null || instruction.Source.Count == 0)
            {
                return true;
            }

            // Get the parent component to access ResourceRegistry
            ModComponent parentComponent = instruction.GetParentComponent();
            if (parentComponent?.ResourceRegistry == null || parentComponent.ResourceRegistry.Count == 0)
            {
                return false;
            }

            // Check which source files are missing
            var missingFiles = new List<string>();
            foreach (string sourcePath in instruction.Source)
            {
                string resolvedPath = UtilityHelper.ReplaceCustomVariables(sourcePath);
                if (!fileSystemProvider.FileExists(resolvedPath))
                {
                    missingFiles.Add(Path.GetFileName(resolvedPath));
                }
            }

            if (missingFiles.Count == 0)
            {
                return true; // All files exist
            }

            // Search for missing files in ResourceRegistry archives
            var archiveMatches = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string missingFile in missingFiles)
            {
                foreach (var resource in parentComponent.ResourceRegistry)
                {
                    if (resource.Value?.Files == null)
                    {
                        continue;
                    }

                    // Check if this resource contains the missing file
                    foreach (var file in resource.Value.Files)
                    {
                        if (file.Value == true && ResourceRegistryEntryMatchesMissingSource(file.Key, missingFile))
                        {
                            if (!archiveMatches.ContainsKey(missingFile))
                            {
                                archiveMatches[missingFile] = new List<string>();
                            }
                            if (!archiveMatches[missingFile].Any(existing => string.Equals(existing, resource.Key, StringComparison.OrdinalIgnoreCase)))
                            {
                                archiveMatches[missingFile].Add(resource.Key);
                            }
                        }
                    }
                }
            }

            // Check for ambiguous matches
            bool hasWarnings = false;
            foreach (var match in archiveMatches)
            {
                if (match.Value.Count > 1)
                {
                    await Logger.LogWarningAsync(
                        $"File '{match.Key}' found in multiple archives: {string.Join(", ", match.Value)}. " +
                        "Please add an explicit Extract instruction to specify which archive to use."
                    ).ConfigureAwait(false);
                    hasWarnings = true;
                }
                else if (match.Value.Count == 1)
                {
                    // Found exactly one archive containing this file - extract it
                    string archiveName = match.Value[0];
                    string archivePath = Path.Combine(MainConfig.SourcePath.FullName, archiveName);

                    if (fileSystemProvider.FileExists(archivePath))
                    {
                        await Logger.LogAsync(
                            $"Auto-extracting '{archiveName}' to provide missing file '{match.Key}'"
                        ).ConfigureAwait(false);

                        var extractInstruction = new Instruction
                        {
                            Action = Instruction.ActionType.Extract,
                            Source = new List<string> { archivePath },
                        };
                        extractInstruction.SetFileSystemProvider(fileSystemProvider);
                        extractInstruction.SetParentComponent(parentComponent);

                        try
                        {
                            extractInstruction.SetRealPaths();
                            var extractResult = await extractInstruction.ExtractFileAsync().ConfigureAwait(false);

                            if (extractResult != Instruction.ActionExitCode.Success)
                            {
                                await Logger.LogWarningAsync(
                                    $"Failed to auto-extract '{archiveName}' (exit code: {extractResult})"
                                ).ConfigureAwait(false);
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            await Logger.LogWarningAsync(
                                $"Failed to auto-extract '{archiveName}': {ex.Message}"
                            ).ConfigureAwait(false);
                            return false;
                        }
                    }
                }
            }

            // Check if we found all missing files
            foreach (string missingFile in missingFiles)
            {
                if (!archiveMatches.ContainsKey(missingFile) || archiveMatches[missingFile].Count == 0)
                {
                    await Logger.LogWarningAsync(
                        $"File '{missingFile}' not found in any ResourceRegistry archives"
                    ).ConfigureAwait(false);
                    return false;
                }
            }

            return !hasWarnings;
        }

        private static bool ResourceRegistryEntryMatchesMissingSource([CanBeNull] string resourceEntryPath, [CanBeNull] string missingSource)
        {
            if (string.IsNullOrWhiteSpace(resourceEntryPath) || string.IsNullOrWhiteSpace(missingSource))
            {
                return false;
            }

            string normalizedEntry = resourceEntryPath.Replace('\\', '/');
            string normalizedMissing = missingSource.Replace('\\', '/');
            bool containsWildcards = normalizedMissing.IndexOf('*') >= 0 || normalizedMissing.IndexOf('?') >= 0;

            if (!containsWildcards)
            {
                return string.Equals(normalizedEntry, normalizedMissing, StringComparison.OrdinalIgnoreCase)
                    || normalizedEntry.EndsWith($"/{normalizedMissing}", StringComparison.OrdinalIgnoreCase);
            }

            return WildcardMatches(normalizedEntry, normalizedMissing)
                || WildcardMatches(Path.GetFileName(normalizedEntry), normalizedMissing);
        }

        private static bool WildcardMatches([CanBeNull] string candidate, [CanBeNull] string pattern)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(candidate, regexPattern, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Executes a single instruction using the unified instruction execution pipeline.
        /// This method is used by both real installations and dry-run validation.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public async Task<Instruction.ActionExitCode> ExecuteSingleInstructionAsync(
            [NotNull] Instruction instruction,
            int instructionIndex,
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider,
            bool skipDependencyCheck = false,
            CancellationToken cancellationToken = default
        )
        {
            if (instruction is null)
            {
                throw new ArgumentNullException(nameof(instruction));
            }

            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            if (fileSystemProvider is null)
            {
                throw new ArgumentNullException(nameof(fileSystemProvider));
            }

            // Check if instruction should run based on dependencies and restrictions
            if (!skipDependencyCheck && !ShouldRunInstruction(instruction, componentsList))
            {
                await Logger.LogVerboseAsync($"Skipping instruction due to unmet dependencies or restrictions").ConfigureAwait(false);
                return Instruction.ActionExitCode.Success; // Return success when skipped
            }

            Instruction.ActionExitCode exitCode = Instruction.ActionExitCode.Success;
            switch (instruction.Action)
            {
                case Instruction.ActionType.Extract:
                    if (!await TryResolveInstructionSourcesAsync(instruction, fileSystemProvider).ConfigureAwait(false))
                    {
                        await Logger.LogErrorAsync(
                            $"Missing mod archive(s) for '{Name}'; run Fetch Downloads or place files in the mod workspace."
                        ).ConfigureAwait(false);
                        return Instruction.ActionExitCode.FileNotFoundPre;
                    }

                    exitCode = await instruction.ExtractFileAsync().ConfigureAwait(false);
                    break;
                case Instruction.ActionType.Delete:
                    instruction.SetRealPaths(skipExistenceCheck: true);
                    exitCode = instruction.DeleteFile();
                    break;
                case Instruction.ActionType.DelDuplicate:
                    instruction.SetRealPaths(sourceIsNotFilePath: true);
                    instruction.DeleteDuplicateFile(caseInsensitive: true);
                    exitCode = Instruction.ActionExitCode.Success;
                    break;
                case Instruction.ActionType.Copy:
                    if (!await TryResolveInstructionSourcesAsync(instruction, fileSystemProvider).ConfigureAwait(false))
                    {
                        await Logger.LogErrorAsync(
                            $"Missing mod file(s) for '{Name}'; run Fetch Downloads or place archives in the mod workspace."
                        ).ConfigureAwait(false);
                        return Instruction.ActionExitCode.FileNotFoundPre;
                    }

                    exitCode = await instruction.CopyFileAsync().ConfigureAwait(false);
                    break;
                case Instruction.ActionType.Move:
                    if (!await TryResolveInstructionSourcesAsync(instruction, fileSystemProvider).ConfigureAwait(false))
                    {
                        if (await TrySatisfyRedundantMoveToGameAsync(instruction, fileSystemProvider).ConfigureAwait(false))
                        {
                            exitCode = Instruction.ActionExitCode.Success;
                            break;
                        }

                        await Logger.LogErrorAsync(
                            $"Missing mod file(s) for '{Name}'; run Fetch Downloads or place archives in the mod workspace."
                        ).ConfigureAwait(false);
                        return Instruction.ActionExitCode.FileNotFoundPre;
                    }

                    exitCode = await instruction.MoveFileAsync().ConfigureAwait(false);
                    break;
                case Instruction.ActionType.Rename:
                    instruction.SetRealPaths(skipExistenceCheck: true);
                    exitCode = instruction.RenameFile();
                    break;
                case Instruction.ActionType.Patcher:
                    if (!await TryResolveInstructionSourcesAsync(instruction, fileSystemProvider).ConfigureAwait(false))
                    {
                        await Logger.LogErrorAsync(
                            $"Missing patcher folder/archive for '{Name}'; run Fetch Downloads or extract mod files to the workspace."
                        ).ConfigureAwait(false);
                        return Instruction.ActionExitCode.FileNotFoundPre;
                    }

                    exitCode = await instruction.ExecuteTSLPatcherAsync().ConfigureAwait(false);
                    break;
                case Instruction.ActionType.Execute:
                case Instruction.ActionType.Run:
                    instruction.SetRealPaths(skipExistenceCheck: true);
                    exitCode = await instruction.ExecuteProgramAsync().ConfigureAwait(false);
                    break;
                case Instruction.ActionType.Choose:
                    instruction.SetRealPaths(sourceIsNotFilePath: true);
                    IReadOnlyList<Option> list = instruction.GetChosenOptions();
                    for (int i = 0; i < list.Count; i++)
                    {
                        Option thisOption = list[i];
                        InstallExitCode optionExitCode = await ExecuteInstructionsAsync(
                            thisOption.Instructions,
                            componentsList,
                            cancellationToken,
                            fileSystemProvider,
                            skipDependencyCheck
                        ).ConfigureAwait(false);
                        if (optionExitCode != InstallExitCode.Success)
                        {
                            await Logger.LogErrorAsync($"Failed to install chosen option {i + 1} in main instruction index {instructionIndex}").ConfigureAwait(false);
                            exitCode = Instruction.ActionExitCode.OptionalInstallFailed;
                            break;
                        }
                    }
                    break;
                case Instruction.ActionType.CleanList:
                    instruction.SetRealPaths(skipExistenceCheck: true);
                    // Robust auto-selection: token-based matching and special-cases
                    bool IsModSelected(string modName)
                    {
                        if (string.IsNullOrWhiteSpace(modName))
                        {
                            return false;
                        }
                        // Always apply mandatory deletions
                        if (modName.StartsWith("Mandatory Deletions", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        string[] stopWords = new[] { "by", "for", "the", "and", "of", "mod", "pack", "k1", "k2", "hd", "kotor", "kotorn", "version", "recolored", "reskin", "retexture", "fix", "fixes" };
                        HashSet<string> Tokenize(string name, string[] stop)
                        {
                            string lower = name.ToLowerInvariant();
                            char[] buf = lower.ToCharArray();
                            for (int i = 0; i < buf.Length; i++)
                            {
                                char c = buf[i];
                                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != ' ')
                                {
                                    buf[i] = ' ';
                                }
                            }
                            string cleaned = new string(buf);
                            string[] raw = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (string r in raw)
                            {
                                string t = r.Trim();
                                if (t.Length == 0)
                                {
                                    continue;
                                }

                                if (System.Array.IndexOf(stop, t) >= 0)
                                {
                                    continue;
                                }
                                // crude singularize
                                if (t.EndsWith("s", StringComparison.Ordinal) && t.Length > 3)
                                {
                                    t = t.Substring(0, t.Length - 1);
                                }

                                set.Add(t);
                            }
                            return set;
                        }
                        HashSet<string> target = Tokenize(modName, stopWords);
                        if (target.Count == 0)
                        {
                            return false;
                        }

                        // Exact or substring quick checks first
                        foreach (ModComponent component in componentsList)
                        {
                            if (component is null || !component.IsSelected)
                            {
                                continue;
                            }

                            string compName = component.Name ?? string.Empty;
                            if (string.Equals(compName, modName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }

                            if (compName.IndexOf(modName, StringComparison.OrdinalIgnoreCase) >= 0 || modName.IndexOf(compName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return true;
                            }
                        }
                        // Token overlap heuristic
                        foreach (ModComponent component in componentsList)
                        {
                            if (component is null || !component.IsSelected)
                            {
                                continue;
                            }

                            HashSet<string> comp = Tokenize(component.Name ?? string.Empty, stopWords);
                            if (comp.Count == 0)
                            {
                                continue;
                            }

                            int intersect = 0;
                            foreach (string t in target)
                            {
                                if (comp.Contains(t))
                                {
                                    intersect++;
                                }
                            }

                            int minSize = Math.Min(target.Count, comp.Count);
                            // require at least 2 shared tokens and 60% of the smaller token set
                            if (intersect >= 2 && intersect >= (int)Math.Ceiling(minSize * 0.6))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                    exitCode = await instruction.ExecuteCleanListAsync(
                        cleanlistPath: null,
                        targetDirectory: null,
                        isModSelectedFunc: IsModSelected
                    ).ConfigureAwait(false);
                    break;
                default:
                    await Logger.LogWarningAsync($"Unknown instruction '{instruction.ActionString}'").ConfigureAwait(false);
                    exitCode = Instruction.ActionExitCode.UnknownInstruction;
                    break;
            }
            return exitCode;
        }

        public async Task<InstallExitCode> ExecuteInstructionsAsync(
            [NotNull][ItemNotNull] ObservableCollection<Instruction> theseInstructions,
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            CancellationToken cancellationToken,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider,
            bool skipDependencyCheck = false
        )
        {
            if (theseInstructions is null)
            {
                throw new ArgumentNullException(nameof(theseInstructions));
            }

            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            if (fileSystemProvider is null)
            {
                throw new ArgumentNullException(nameof(fileSystemProvider));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!skipDependencyCheck)
                {
                    bool shouldInstall = ShouldInstallComponent(componentsList);
                    if (!shouldInstall)
                    {
                        return InstallExitCode.DependencyViolation;
                    }
                }

                InstallExitCode installExitCode = InstallExitCode.Success;
                for (int instructionIndex = 1; instructionIndex <= theseInstructions.Count; instructionIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Instruction instruction = theseInstructions[instructionIndex - 1];
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    if (!ShouldRunInstruction(instruction, componentsList))
                    {
                        continue;
                    }

                    Instruction.ActionExitCode exitCode = await ExecuteSingleInstructionAsync(
                        instruction,
                        instructionIndex,
                        componentsList,
                        fileSystemProvider,
                        skipDependencyCheck,
                        cancellationToken
                    ).ConfigureAwait(false);

                    _ = Logger.LogVerboseAsync(
                        $"Instruction #{instructionIndex} '{instruction.ActionString}' exited with code {exitCode}"
                    ).ConfigureAwait(false);
                    if (exitCode != Instruction.ActionExitCode.Success)
                    {
                        await Logger.LogErrorAsync(
                            $"FAILED Instruction #{instructionIndex} Action '{instruction.ActionString}'"
                        ).ConfigureAwait(false);
                        if (exitCode == Instruction.ActionExitCode.OptionalInstallFailed)
                        {
                            return InstallExitCode.UserCancelledInstall;
                        }

                        if (exitCode == Instruction.ActionExitCode.FileNotFoundPre ||
                            exitCode == Instruction.ActionExitCode.FileNotFoundPost)
                        {
                            return InstallExitCode.MissingSourceFiles;
                        }

                        return InstallExitCode.UnknownError;
                    }
                    _ = Logger.LogVerboseAsync($"Successfully completed instruction #{instructionIndex} '{instruction.Action}'");
                }

                sw.Stop();
                Services.TelemetryService.Instance.RecordComponentExecution(
                    componentName: Name,
                    success: true,
                    instructionCount: theseInstructions.Count,
                    durationMs: sw.Elapsed.TotalMilliseconds
                );

                return installExitCode;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Services.TelemetryService.Instance.RecordComponentExecution(
                    componentName: Name,
                    success: false,
                    instructionCount: theseInstructions.Count,
                    durationMs: sw.Elapsed.TotalMilliseconds,
                    errorMessage: ex.Message
                );
                throw;
            }
        }

        public Task<InstallExitCode> ExecuteInstructionsAsync(
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider)
        {
            return ExecuteInstructionsAsync(Instructions, componentsList, CancellationToken.None, fileSystemProvider);
        }

        public Task<InstallExitCode> ExecuteInstructionsAsync(
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            [NotNull] Services.FileSystem.IFileSystemProvider _,
            CancellationToken cancellationToken,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider)
        {
            return ExecuteInstructionsAsync(Instructions, componentsList, cancellationToken, fileSystemProvider);
        }

        public Task<InstallExitCode> ExecuteInstructionsAsync(
            [NotNull][ItemNotNull] ObservableCollection<Instruction> theseInstructions,
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            CancellationToken cancellationToken,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider,
            CancellationToken _,
            [NotNull] Services.FileSystem.IFileSystemProvider __)
        {
            return ExecuteInstructionsAsync(theseInstructions, componentsList, cancellationToken, fileSystemProvider);
        }

        public Task<InstallExitCode> ExecuteInstructionsAsync(
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            CancellationToken cancellationToken,
            [NotNull] Services.FileSystem.IFileSystemProvider _,
            CancellationToken __,
            [NotNull] Services.FileSystem.IFileSystemProvider fileSystemProvider)
        {
            return ExecuteInstructionsAsync(Instructions, componentsList, cancellationToken, fileSystemProvider);
        }
        [NotNull]
        public static Dictionary<string, List<ModComponent>> GetConflictingComponents(
            [NotNull] List<Guid> dependencyGuids,
            [NotNull] List<Guid> restrictionGuids,
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList
        )
        {
            if (dependencyGuids is null)
            {
                throw new ArgumentNullException(nameof(dependencyGuids));
            }

            if (restrictionGuids is null)
            {
                throw new ArgumentNullException(nameof(restrictionGuids));
            }

            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            var conflicts = new Dictionary<string, List<ModComponent>>(StringComparer.Ordinal);
            if (dependencyGuids.Count > 0)
            {
                var dependencyConflicts = new List<ModComponent>();
                foreach (Guid requiredGuid in dependencyGuids)
                {
                    ModComponent checkComponent = FindComponentFromGuid(requiredGuid, componentsList);
                    if (checkComponent is null)
                    {
                        var componentGuidNotFound = new ModComponent
                        {
                            Name = "ModComponent Undefined with GUID.",
                            Guid = requiredGuid,
                        };
                        dependencyConflicts.Add(componentGuidNotFound);
                    }
                    else if (!checkComponent.IsSelected)
                    {
                        dependencyConflicts.Add(checkComponent);
                    }
                }
                if (dependencyConflicts.Count > 0)
                {
                    conflicts["Dependency"] = dependencyConflicts;
                }
            }
            if (restrictionGuids.Count > 0)
            {
                var restrictionConflicts = restrictionGuids
                    .Select(requiredGuid => FindComponentFromGuid(requiredGuid, componentsList)).Where(
                        checkComponent => checkComponent?.IsSelected ?? false
                    ).ToList();
                if (restrictionConflicts.Count > 0)
                {
                    conflicts["Restriction"] = restrictionConflicts;
                }
            }
            return conflicts;
        }
        public bool ShouldInstallComponent([NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList)
        {
            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            Dictionary<string, List<ModComponent>> conflicts = GetConflictingComponents(
                Dependencies,
                Restrictions,
                componentsList
            );
            return conflicts.Count == 0;
        }
        public static bool ShouldRunInstruction(
            [NotNull] Instruction instruction,
            [NotNull] IReadOnlyList<ModComponent> componentsList
        )
        {
            if (instruction is null)
            {
                throw new ArgumentNullException(nameof(instruction));
            }

            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            Dictionary<string, List<ModComponent>> conflicts = GetConflictingComponents(
                instruction.Dependencies,
                instruction.Restrictions,
                componentsList
            );
            return conflicts.Count == 0;
        }
        [CanBeNull]
        public static ModComponent FindComponentFromGuid(
            Guid guidToFind,
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList
        )
        {
            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            ModComponent foundComponent = null;
            foreach (ModComponent component in componentsList)
            {
                if (component.Guid == guidToFind)
                {
                    foundComponent = component;
                    break;
                }
                foreach (Option thisOption in component.Options)
                {
                    if (thisOption.Guid == guidToFind)
                    {
                        foundComponent = thisOption;
                        break;
                    }
                }
                if (foundComponent != null)
                {
                    break;
                }
            }
            return foundComponent;
        }
        [NotNull]
        public static List<ModComponent> FindComponentsFromGuidList(
            [NotNull] List<Guid> guidsToFind,
            [NotNull] IReadOnlyList<ModComponent> componentsList
        )
        {
            if (guidsToFind is null)
            {
                throw new ArgumentNullException(nameof(guidsToFind));
            }

            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            var foundComponents = new List<ModComponent>();
            foreach (Guid guidToFind in guidsToFind)
            {
                ModComponent foundComponent = FindComponentFromGuid(guidToFind, componentsList);
                if (foundComponent is null)
                {
                    continue;
                }

                foundComponents.Add(foundComponent);
            }
            return foundComponents;
        }
        public static (bool isCorrectOrder, List<ModComponent> reorderedComponents) ConfirmComponentsInstallOrder(
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> components
        )
        {
            if (components is null)
            {
                throw new ArgumentNullException(nameof(components));
            }

            Dictionary<Guid, GraphNode> nodeMap = CreateDependencyGraph(components);
            var permanentMark = new HashSet<GraphNode>();
            var temporaryMark = new HashSet<GraphNode>();
            if (nodeMap.Values.Where(node => !permanentMark.Contains(node)).Any(node => HasCycle(node, permanentMark, temporaryMark)))
            {
                throw new KeyNotFoundException("Circular dependency detected in component ordering");
            }

            var visitedNodes = new HashSet<GraphNode>();
            var orderedComponents = new List<ModComponent>();
            foreach (GraphNode node in nodeMap.Values.Where(node => !visitedNodes.Contains(node)))
            {
                DepthFirstSearch(node, visitedNodes, orderedComponents);
            }
            bool isCorrectOrder = orderedComponents.SequenceEqual(components);
            return (isCorrectOrder, orderedComponents);
        }
        private static bool HasCycle(
            [NotNull] GraphNode node,
            [NotNull] ISet<GraphNode> permanentMark,
            [NotNull] ISet<GraphNode> temporaryMark
        )
        {
            if (permanentMark.Contains(node))
            {
                return false;
            }

            if (temporaryMark.Contains(node))
            {
                return true;
            }

            _ = temporaryMark.Add(node);
            foreach (GraphNode dependency in node.Dependencies)
            {
                if (HasCycle(dependency, permanentMark, temporaryMark))
                {
                    return true;
                }
            }
            _ = temporaryMark.Remove(node);
            _ = permanentMark.Add(node);
            return false;
        }
        private static void DepthFirstSearch(
            [NotNull] GraphNode node,
            [NotNull] ISet<GraphNode> visitedNodes,
            [NotNull] ICollection<ModComponent> orderedComponents
        )
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (visitedNodes is null)
            {
                throw new ArgumentNullException(nameof(visitedNodes));
            }

            if (orderedComponents is null)
            {
                throw new ArgumentNullException(nameof(orderedComponents));
            }

            _ = visitedNodes.Add(node);
            foreach (GraphNode dependency in node.Dependencies.Where(dependency => !visitedNodes.Contains(dependency)))
            {
                DepthFirstSearch(dependency, visitedNodes, orderedComponents);
            }
            orderedComponents.Add(node.ModComponent);
        }
        private static Dictionary<Guid, GraphNode> CreateDependencyGraph(
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> components
        )
        {
            if (components is null)
            {
                throw new ArgumentNullException(nameof(components));
            }

            var nodeMap = new Dictionary<Guid, GraphNode>();
            foreach (ModComponent component in components)
            {
                var node = new GraphNode(component);
                nodeMap[component.Guid] = node;
            }
            foreach (ModComponent component in components)
            {
                GraphNode node = nodeMap[component.Guid];
                foreach (Guid dependencyGuid in component.InstallAfter)
                {
                    if (!nodeMap.TryGetValue(dependencyGuid, out GraphNode dependencyNode))
                    {
                        Logger.LogWarning($"ModComponent '{component.Name}' references InstallAfter GUID {dependencyGuid} which is not in the current component list");
                        continue;
                    }
                    _ = node?.Dependencies?.Add(dependencyNode);
                }
                foreach (Guid dependentGuid in component.InstallBefore)
                {
                    if (!nodeMap.TryGetValue(dependentGuid, out GraphNode dependentNode))
                    {
                        Logger.LogWarning($"ModComponent '{component.Name}' references InstallBefore GUID {dependentGuid} which is not in the current component list");
                        continue;
                    }
                    _ = dependentNode?.Dependencies?.Add(node);
                }
            }
            return nodeMap;
        }
        public void CreateInstruction(int index = 0)
        {
            var instruction = new Instruction();
            if (Instructions.IsNullOrEmptyOrAllNull())
            {
                if (index != 0)
                {
                    Logger.LogError("Cannot create instruction at index when list is empty.");
                    return;
                }
                Instructions.Add(instruction);
            }
            else
            {
                Instructions.Insert(index, instruction);
            }
            instruction.SetParentComponent(this);
        }
        public void DeleteInstruction(int index) => Instructions.RemoveAt(index);
        public void DeleteOption(int index) => Options.RemoveAt(index);
        public void MoveInstructionToIndex([NotNull] Instruction thisInstruction, int index)
        {
            if (thisInstruction is null)
            {
                throw new ArgumentException("Instruction cannot be null.", nameof(thisInstruction));
            }
            if (index < 0 || index >= Instructions.Count)
            {
                throw new ArgumentException("Index is out of range.", nameof(index));
            }

            int currentIndex = Instructions.IndexOf(thisInstruction);
            if (currentIndex < 0)
            {
                throw new ArgumentException("Instruction does not exist in the list.", nameof(thisInstruction));
            }

            if (index == currentIndex)
            {
                _ = Logger.LogAsync(
                    $"Cannot move Instruction '{thisInstruction.Action}' from {currentIndex} to {index}. Reason: Indices are the same."
                );
                return;
            }
            Instructions.RemoveAt(currentIndex);
            Instructions.Insert(index, thisInstruction);
            _ = Logger.LogVerboseAsync($"Instruction '{thisInstruction.Action}' moved from {currentIndex} to {index}");
        }
        public void CreateOption(int index = 0)
        {
            var option = new Option
            {
                Name = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()),
                Guid = Guid.NewGuid(),
            };
            if (Instructions.IsNullOrEmptyOrAllNull())
            {
                if (index != 0)
                {
                    Logger.LogError("Cannot create option at index when list is empty.");
                    return;
                }
                Options.Add(option);
            }
            else
            {
                Options.Insert(index, option);
            }
        }
        public void MoveOptionToIndex([NotNull] Option thisOption, int index)
        {
            if (thisOption is null)
            {
                throw new ArgumentException("Option cannot be null.", nameof(thisOption));
            }
            if (index < 0 || index >= Options.Count)
            {
                throw new ArgumentException("Index is out of range.", nameof(index));
            }

            int currentIndex = Options.IndexOf(thisOption);
            if (currentIndex < 0)
            {
                throw new ArgumentException("Option does not exist in the list.", nameof(thisOption));
            }

            if (index == currentIndex)
            {
                Logger.LogError(
                    $"Cannot move Option '{thisOption.Name}' from {currentIndex} to {index}. Reason: Indices are the same."
                );
                return;
            }
            Options.RemoveAt(currentIndex);
            Options.Insert(index, thisOption);
            Logger.LogVerbose($"Option '{thisOption.Name}' moved from {currentIndex} to {index}");
        }
        public class GraphNode
        {
            public GraphNode([NotNull] ModComponent component)
            {
                if (component is null)
                {
                    throw new ArgumentNullException(nameof(component));
                }
                ModComponent = component;
                Dependencies = new HashSet<GraphNode>();
            }
            public ModComponent ModComponent { get; }
            public HashSet<GraphNode> Dependencies { get; }
        }
    }

    /// <summary>
    /// Metadata for content-addressable resource tracking.
    /// </summary>
    public class ResourceMetadata
    {
        /// <summary>Current lookup key (MetadataHash)</summary>
        public string ContentKey { get; set; }

        /// <summary>SHA-256 of canonical provider metadata</summary>
        public string MetadataHash { get; set; }

        /// <summary>Provider-specific metadata (normalized)</summary>
        [NotNull]
        public Dictionary<string, object> HandlerMetadata { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Files contained in this resource (filename -> exists in archive)</summary>
        [NotNull]
        public Dictionary<string, bool?> Files { get; set; } = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>File size in bytes</summary>
        public long FileSize { get; set; }

        /// <summary>First time this resource was observed</summary>
        public DateTime? FirstSeen { get; set; }

        /// <summary>Last time integrity was verified</summary>
        public DateTime? LastVerified { get; set; }
    }


    public sealed class ResourceFileInfo
    {
        public long Size { get; set; }
    }

    public sealed class ResourceRegistryEntry : ResourceMetadata
    {
        private Dictionary<string, ResourceFileInfo> _compatibilityFiles = new Dictionary<string, ResourceFileInfo>(StringComparer.OrdinalIgnoreCase);

        public new Dictionary<string, ResourceFileInfo> Files
        {
            get => _compatibilityFiles;
            set
            {
                _compatibilityFiles = value ?? new Dictionary<string, ResourceFileInfo>(StringComparer.OrdinalIgnoreCase);
                base.Files = _compatibilityFiles.ToDictionary(kvp => kvp.Key, kvp => (bool?)true, StringComparer.OrdinalIgnoreCase);
                FileSize = _compatibilityFiles.Values.Sum(file => file?.Size ?? 0L);
            }
        }
    }

}
