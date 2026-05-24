// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia.Media;

using JetBrains.Annotations;

using KOTORModSync.Core;

using ModComponent = KOTORModSync.Core.ModComponent;

namespace KOTORModSync.Dialogs
{

    public class FieldMergePreference : INotifyPropertyChanged
    {
        public enum FieldSource
        {
            UseExisting,
            UseIncoming,
            Merge,
        }

        private FieldSource _name = FieldSource.UseIncoming;
        private FieldSource _author = FieldSource.UseIncoming;
        private FieldSource _description = FieldSource.UseIncoming;
        private FieldSource _directions = FieldSource.UseIncoming;
        private FieldSource _category = FieldSource.UseIncoming;
        private FieldSource _tier = FieldSource.UseIncoming;
        private FieldSource _installationMethod = FieldSource.UseIncoming;
        private FieldSource _instructions = FieldSource.UseIncoming;
        private FieldSource _dependencies = FieldSource.Merge;
        private FieldSource _restrictions = FieldSource.Merge;
        private FieldSource _installAfter = FieldSource.Merge;
        private FieldSource _options = FieldSource.UseIncoming;
        private FieldSource _resourceRegistry = FieldSource.Merge;
        private FieldSource _language = FieldSource.Merge;

        public FieldSource Name { get => _name; set { if (_name == value) { return; } _name = value; OnPropertyChanged(); } }
        public FieldSource Author { get => _author; set { if (_author == value) { return; } _author = value; OnPropertyChanged(); } }
        public FieldSource Description { get => _description; set { if (_description == value) { return; } _description = value; OnPropertyChanged(); } }
        public FieldSource Directions { get => _directions; set { if (_directions == value) { return; } _directions = value; OnPropertyChanged(); } }
        public FieldSource Category { get => _category; set { if (_category == value) { return; } _category = value; OnPropertyChanged(); } }
        public FieldSource Tier { get => _tier; set { if (_tier == value) { return; } _tier = value; OnPropertyChanged(); } }
        public FieldSource InstallationMethod { get => _installationMethod; set { if (_installationMethod == value) { return; } _installationMethod = value; OnPropertyChanged(); } }
        public FieldSource Instructions { get => _instructions; set { if (_instructions == value) { return; } _instructions = value; OnPropertyChanged(); } }
        public FieldSource Dependencies { get => _dependencies; set { if (_dependencies == value) { return; } _dependencies = value; OnPropertyChanged(); } }
        public FieldSource Restrictions { get => _restrictions; set { if (_restrictions == value) { return; } _restrictions = value; OnPropertyChanged(); } }
        public FieldSource InstallAfter { get => _installAfter; set { if (_installAfter == value) { return; } _installAfter = value; OnPropertyChanged(); } }
        public FieldSource Options { get => _options; set { if (_options == value) { return; } _options = value; OnPropertyChanged(); } }
        public FieldSource ResourceRegistry { get => _resourceRegistry; set { if (_resourceRegistry == value) { return; } _resourceRegistry = value; OnPropertyChanged(); } }
        public FieldSource Language { get => _language; set { if (_language == value) { return; } _language = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ComponentMergeConflictViewModel : INotifyPropertyChanged
    {
        private bool _useIncomingOrder = true;
        private bool _selectAllExisting;
        private bool _selectAllIncoming = true;
        private bool _skipDuplicates = true;
        private ComponentConflictItem _selectedExistingItem;
        private ComponentConflictItem _selectedIncomingItem;
        private TomlDiffResult _selectedMergedTomlLine;
        private string _searchText = string.Empty;

        private readonly
            Dictionary<Tuple<ComponentConflictItem, ComponentConflictItem>, GuidConflictResolver.GuidResolution>
            _guidResolutions =
                new Dictionary<Tuple<ComponentConflictItem, ComponentConflictItem>,
                    GuidConflictResolver.GuidResolution>();

        private readonly Dictionary<Tuple<ComponentConflictItem, ComponentConflictItem>, FieldMergePreference>
            _fieldPreferences = new Dictionary<Tuple<ComponentConflictItem, ComponentConflictItem>, FieldMergePreference>();

        public ComponentMergeConflictViewModel(
            [NotNull] List<ModComponent> existingComponents,
            [NotNull] List<ModComponent> incomingComponents,
            [NotNull] string existingSource,
            [NotNull] string incomingSource,
            [NotNull] Func<ModComponent, ModComponent, bool> matchFunc)
        {
            ExistingComponents = new ObservableCollection<ComponentConflictItem>();
            IncomingComponents = new ObservableCollection<ComponentConflictItem>();
            PreviewComponents = new ObservableCollection<PreviewItem>();
            FilteredExistingComponents = new ObservableCollection<ComponentConflictItem>();
            FilteredIncomingComponents = new ObservableCollection<ComponentConflictItem>();
            RealtimeMergedComponents = new ObservableCollection<ModComponent>();
            CurrentTomlDiff = new ObservableCollection<TomlDiffResult>();
            ExistingComponentsToml = new ObservableCollection<TomlDiffResult>();
            IncomingComponentsToml = new ObservableCollection<TomlDiffResult>();
            MergedComponentsToml = new ObservableCollection<TomlDiffResult>();

            ExistingSourceInfo = existingSource;
            IncomingSourceInfo = incomingSource;

            SelectAllIncomingMatchesCommand = new RelayCommand(_ => SelectAllIncomingMatches());
            SelectAllExistingMatchesCommand = new RelayCommand(_ => SelectAllExistingMatches());
            KeepAllNewCommand = new RelayCommand(_ => KeepAllNew());
            KeepAllExistingUnmatchedCommand = new RelayCommand(_ => KeepAllExistingUnmatched());
            LinkSelectedCommand = new RelayCommand(_ => LinkSelectedItems(), _ => CanLinkSelected());
            UnlinkSelectedCommand = new RelayCommand(_ => UnlinkSelectedItems(), _ => CanUnlinkSelected());
            UseAllIncomingFieldsCommand = new RelayCommand(_ => UseAllIncomingFields());
            UseAllExistingFieldsCommand = new RelayCommand(_ => UseAllExistingFields());
            JumpToRawViewCommand = new RelayCommand(param => JumpToRawView(param as ComponentConflictItem), param => param is ComponentConflictItem);

            BuildConflictItems(existingComponents, incomingComponents, matchFunc);

            PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(UseIncomingOrder):
                    case nameof(SelectAllExisting):
                    case nameof(SelectAllIncoming):
                    case nameof(SkipDuplicates):
                        UpdatePreview();
                        break;
                    case nameof(SearchText):
                        ApplySearchFilter();
                        break;
                }
            };

            UpdatePreview();
            ApplySearchFilter();
        }

        public RelayCommand SelectAllIncomingMatchesCommand { get; }
        public RelayCommand SelectAllExistingMatchesCommand { get; }
        public RelayCommand KeepAllNewCommand { get; }
        public RelayCommand KeepAllExistingUnmatchedCommand { get; }
        public RelayCommand LinkSelectedCommand { get; }
        public RelayCommand UnlinkSelectedCommand { get; }
        public RelayCommand UseAllIncomingFieldsCommand { get; }
        public RelayCommand UseAllExistingFieldsCommand { get; }

        private readonly List<(ComponentConflictItem Existing, ComponentConflictItem Incoming)> _matchedPairs =
            new List<(ComponentConflictItem, ComponentConflictItem)>();

        private readonly List<ComponentConflictItem> _existingOnly = new List<ComponentConflictItem>();
        private readonly List<ComponentConflictItem> _incomingOnly = new List<ComponentConflictItem>();

        public ObservableCollection<ComponentConflictItem> ExistingComponents { get; }
        public ObservableCollection<ComponentConflictItem> IncomingComponents { get; }
        public ObservableCollection<PreviewItem> PreviewComponents { get; }
        public ObservableCollection<ComponentConflictItem> FilteredExistingComponents { get; }
        public ObservableCollection<ComponentConflictItem> FilteredIncomingComponents { get; }
        public ObservableCollection<ModComponent> RealtimeMergedComponents { get; }
        public ObservableCollection<TomlDiffResult> CurrentTomlDiff { get; }
        public ObservableCollection<TomlDiffResult> ExistingComponentsToml { get; set; }
        public ObservableCollection<TomlDiffResult> IncomingComponentsToml { get; set; }
        public ObservableCollection<TomlDiffResult> MergedComponentsToml { get; }

        public RelayCommand JumpToRawViewCommand { get; }

        public string ExistingSourceInfo { get; }
        public string IncomingSourceInfo { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (string.Equals(_searchText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _searchText = value;
                OnPropertyChanged();
            }
        }

        public string ConflictDescription =>
            $"Found {_matchedPairs.Count} matching component(s), " +
            $"{_incomingOnly.Count} new component(s) in incoming list, " +
            $"and {_existingOnly.Count} component(s) only in existing list.";

        public string ConflictSummary
        {
            get
            {
                int existingSelected = ExistingComponents.Count(c => c.IsSelected);
                int incomingSelected = IncomingComponents.Count(c => c.IsSelected);
                return
                    $"Selected: {existingSelected} from existing, {incomingSelected} from incoming → {PreviewComponents.Count} total components";
            }
        }

        public int NewComponentsCount => _incomingOnly.Count(i => i.IsSelected);
        public int UpdatedComponentsCount => _matchedPairs.Count(p => p.Incoming.IsSelected && !p.Existing.IsSelected);

        public int KeptComponentsCount => _existingOnly.Count(e => e.IsSelected) +
                                          _matchedPairs.Count(p => p.Existing.IsSelected && !p.Incoming.IsSelected);

        public int RemovedComponentsCount => _existingOnly.Count(e => !e.IsSelected) +
                                             _matchedPairs.Count(p =>
                                                 p.Existing.IsSelected && !p.Incoming.IsSelected &&
                                                 !p.Incoming.IsSelected);

        public int TotalChanges => NewComponentsCount + UpdatedComponentsCount + RemovedComponentsCount;

        public string MergeImpactSummary =>
            $"📊 Merge Impact: {NewComponentsCount} new, {UpdatedComponentsCount} updated, {KeptComponentsCount} kept, {RemovedComponentsCount} removed";

        public bool HasMatchedPairSelected
        {
            get
            {
                if (_selectedExistingItem is null && _selectedIncomingItem is null)
                {
                    return false;
                }

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair;

                if (_selectedExistingItem != null)
                {
                    matchedPair = _matchedPairs.FirstOrDefault(p => p.Existing == _selectedExistingItem);
                }
                else
                {
                    matchedPair = _matchedPairs.FirstOrDefault(p => p.Incoming == _selectedIncomingItem);
                }

                return matchedPair.Existing != null && matchedPair.Incoming != null;
            }
        }

        public FieldMergePreference CurrentFieldPreferences
        {
            get
            {

                if (_selectedExistingItem is null && _selectedIncomingItem is null)
                {
                    return new FieldMergePreference();
                }

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair;

                if (_selectedExistingItem != null)
                {
                    matchedPair = _matchedPairs.FirstOrDefault(p => p.Existing == _selectedExistingItem);
                }
                else
                {
                    matchedPair = _matchedPairs.FirstOrDefault(p => p.Incoming == _selectedIncomingItem);
                }

                if (matchedPair.Existing is null || matchedPair.Incoming is null)
                {
                    return new FieldMergePreference();
                }

                var key = Tuple.Create(matchedPair.Existing, matchedPair.Incoming);

                if (_fieldPreferences.TryGetValue(key, out FieldMergePreference prefs))
                {
                    return prefs;
                }

                prefs = CreateAndSubscribeFieldPreferences(matchedPair.Existing.ModComponent, matchedPair.Incoming.ModComponent);
                _fieldPreferences[key] = prefs;

                return prefs;
            }
        }

        private FieldMergePreference CreateAndSubscribeFieldPreferences(ModComponent existing, ModComponent incoming)
        {
            FieldMergePreference prefs = ComponentMergeConflictViewModel.CreateSmartFieldPreferences(existing, incoming);

            prefs.PropertyChanged += (_, __) =>
            {
                UpdatePreview();
                OnPropertyChanged(nameof(PreviewName));
                OnPropertyChanged(nameof(PreviewAuthor));
                OnPropertyChanged(nameof(PreviewInstructionsCount));
            };

            return prefs;
        }

        private static FieldMergePreference CreateSmartFieldPreferences(ModComponent existing, ModComponent incoming)
        {
            var prefs = new FieldMergePreference();


            bool existingHasName = !string.IsNullOrWhiteSpace(existing.Name);
            bool incomingHasName = !string.IsNullOrWhiteSpace(incoming.Name);
            if (!existingHasName && incomingHasName)
            {
                prefs.Name = FieldMergePreference.FieldSource.UseIncoming;
            }
            else if (existingHasName && !incomingHasName)
            {
                prefs.Name = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.Name = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasAuthor = !string.IsNullOrWhiteSpace(existing.Author);
            bool incomingHasAuthor = !string.IsNullOrWhiteSpace(incoming.Author);
            if (!existingHasAuthor && incomingHasAuthor)
            {
                prefs.Author = FieldMergePreference.FieldSource.UseIncoming;
            }
            else if (existingHasAuthor && !incomingHasAuthor)
            {
                prefs.Author = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.Author = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasInstructions = existing.Instructions.Count > 0;
            bool incomingHasInstructions = incoming.Instructions.Count > 0;
            if (!existingHasInstructions && incomingHasInstructions)
            {
                prefs.Instructions = FieldMergePreference.FieldSource.UseIncoming;
            }
            else if (existingHasInstructions && !incomingHasInstructions)
            {
                prefs.Instructions = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.Instructions = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasOptions = existing.Options.Count > 0;
            bool incomingHasOptions = incoming.Options.Count > 0;
            if (!existingHasOptions && incomingHasOptions)
            {
                prefs.Options = FieldMergePreference.FieldSource.UseIncoming;
            }
            else if (existingHasOptions && !incomingHasOptions)
            {
                prefs.Options = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.Options = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasDeps = existing.Dependencies.Count > 0;
            bool incomingHasDeps = incoming.Dependencies.Count > 0;
            if (existingHasDeps && incomingHasDeps)
            {
                prefs.Dependencies = FieldMergePreference.FieldSource.Merge;
            }
            else if (existingHasDeps)
            {
                prefs.Dependencies = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.Dependencies = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasRestrictions = existing.Restrictions.Count > 0;
            bool incomingHasRestrictions = incoming.Restrictions.Count > 0;
            if (existingHasRestrictions && incomingHasRestrictions)
            {
                prefs.Restrictions = FieldMergePreference.FieldSource.Merge;
            }
            else if (existingHasRestrictions)
            {
                prefs.Restrictions = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.Restrictions = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasInstallAfter = existing.InstallAfter.Count > 0;
            bool incomingHasInstallAfter = incoming.InstallAfter.Count > 0;
            if (existingHasInstallAfter && incomingHasInstallAfter)
            {
                prefs.InstallAfter = FieldMergePreference.FieldSource.Merge;
            }
            else if (existingHasInstallAfter)
            {
                prefs.InstallAfter = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.InstallAfter = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasModLink = existing.ResourceRegistry.Count > 0;
            bool incomingHasModLink = incoming.ResourceRegistry.Count > 0;
            if (existingHasModLink && incomingHasModLink)
            {
                prefs.ResourceRegistry = FieldMergePreference.FieldSource.Merge;
            }
            else if (existingHasModLink)
            {
                prefs.ResourceRegistry = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.ResourceRegistry = FieldMergePreference.FieldSource.UseIncoming;
            }

            bool existingHasLanguage = existing.Language.Count > 0;
            bool incomingHasLanguage = incoming.Language.Count > 0;
            if (existingHasLanguage && incomingHasLanguage)
            {
                prefs.Language = FieldMergePreference.FieldSource.Merge;
            }
            else if (existingHasLanguage)
            {
                prefs.Language = FieldMergePreference.FieldSource.UseExisting;
            }
            else
            {
                prefs.Language = FieldMergePreference.FieldSource.UseIncoming;
            }

            prefs.Description = FieldMergePreference.FieldSource.UseIncoming;
            prefs.Directions = FieldMergePreference.FieldSource.UseIncoming;
            prefs.Category = FieldMergePreference.FieldSource.UseIncoming;
            prefs.Tier = FieldMergePreference.FieldSource.UseIncoming;
            prefs.InstallationMethod = FieldMergePreference.FieldSource.UseIncoming;

            return prefs;
        }

        public string PreviewName
        {
            get
            {
                if (!HasMatchedPairSelected)
                {
                    return string.Empty;
                }

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair = _matchedPairs.FirstOrDefault(p =>
                p.Existing == _selectedExistingItem || p.Incoming == _selectedIncomingItem);

                return matchedPair.Existing is null || matchedPair.Incoming is null
                ? string.Empty
                : CurrentFieldPreferences.Name == FieldMergePreference.FieldSource.UseExisting
                ? matchedPair.Existing.ModComponent.Name
                : matchedPair.Incoming.ModComponent.Name;
            }
        }

        public string PreviewAuthor
        {
            get
            {
                if (!HasMatchedPairSelected)
                {
                    return string.Empty;
                }

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair = _matchedPairs.FirstOrDefault(p =>
                    p.Existing == _selectedExistingItem || p.Incoming == _selectedIncomingItem);

                return matchedPair.Existing is null || matchedPair.Incoming is null
                    ? string.Empty
                    : CurrentFieldPreferences.Author == FieldMergePreference.FieldSource.UseExisting
                    ? matchedPair.Existing.ModComponent.Author
                    : matchedPair.Incoming.ModComponent.Author;
            }
        }

        public string PreviewInstructionsCount
        {
            get
            {
                if (!HasMatchedPairSelected)
                {
                    return "0 instructions";
                }

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair = _matchedPairs.FirstOrDefault(p =>
                    p.Existing == _selectedExistingItem || p.Incoming == _selectedIncomingItem);

                if (matchedPair.Existing is null || matchedPair.Incoming is null)
                {
                    return "0 instructions";
                }

                int count = CurrentFieldPreferences.Instructions == FieldMergePreference.FieldSource.UseExisting
                    ? matchedPair.Existing.ModComponent.Instructions.Count
                    : matchedPair.Incoming.ModComponent.Instructions.Count;

                return $"{count} instruction{(count != 1 ? "s" : "")}";
            }
        }

        public ComponentConflictItem SelectedExistingItem
        {
            get => _selectedExistingItem;
            set
            {
                if (_selectedExistingItem == value)
                {
                    return;
                }

                if (_selectedExistingItem != null)
                {
                    _selectedExistingItem.IsVisuallySelected = false;
                }

                _selectedExistingItem = value;

                if (_selectedExistingItem != null)
                {
                    _selectedExistingItem.IsVisuallySelected = true;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ComparisonVisible));
                OnPropertyChanged(nameof(ComparisonText));
                OnPropertyChanged(nameof(CanLinkItems));
                OnPropertyChanged(nameof(ExistingComponentsToml));
                OnPropertyChanged(nameof(HasMatchedPairSelected));
                OnPropertyChanged(nameof(CurrentFieldPreferences));
                OnPropertyChanged(nameof(PreviewName));
                OnPropertyChanged(nameof(PreviewAuthor));
                OnPropertyChanged(nameof(PreviewInstructionsCount));

                if (value is null)
                {
                    return;
                }

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                    _matchedPairs.FirstOrDefault(p => p.Existing == value);

                if (matchedPair.Incoming is null)
                {
                    if (_selectedIncomingItem != null)
                    {
                        _selectedIncomingItem.IsVisuallySelected = false;
                        _selectedIncomingItem = null;
                        OnPropertyChanged(nameof(SelectedIncomingItem));
                    }
                    return;
                }

                foreach (ComponentConflictItem item in IncomingComponents)
                {
                    if (item != matchedPair.Incoming)
                    {
                        item.IsVisuallySelected = false;
                    }
                }

                _selectedIncomingItem = matchedPair.Incoming;
                matchedPair.Incoming.IsVisuallySelected = true;
                OnPropertyChanged(nameof(SelectedIncomingItem));
                OnPropertyChanged(nameof(IncomingComponentsToml));
                OnPropertyChanged(nameof(CurrentFieldPreferences));
                OnPropertyChanged(nameof(PreviewName));
                OnPropertyChanged(nameof(PreviewAuthor));
                OnPropertyChanged(nameof(PreviewInstructionsCount));

                SyncSelectionRequested?.Invoke(this, new SyncSelectionEventArgs { SelectedItem = value, MatchedItem = matchedPair.Incoming });
            }
        }

        public ComponentConflictItem SelectedIncomingItem
        {
            get => _selectedIncomingItem;
            set
            {
                if (_selectedIncomingItem == value)
                {
                    return;
                }

                if (_selectedIncomingItem != null)
                {
                    _selectedIncomingItem.IsVisuallySelected = false;
                }

                _selectedIncomingItem = value;

                if (_selectedIncomingItem != null)
                {
                    _selectedIncomingItem.IsVisuallySelected = true;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ComparisonVisible));
                OnPropertyChanged(nameof(ComparisonText));
                OnPropertyChanged(nameof(CanLinkItems));
                OnPropertyChanged(nameof(IncomingComponentsToml));
                OnPropertyChanged(nameof(HasMatchedPairSelected));
                OnPropertyChanged(nameof(CurrentFieldPreferences));
                OnPropertyChanged(nameof(PreviewName));
                OnPropertyChanged(nameof(PreviewAuthor));
                OnPropertyChanged(nameof(PreviewInstructionsCount));

                if (value is null)
                {
                    return;
                }

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                    _matchedPairs.FirstOrDefault(p => p.Incoming == value);

                if (matchedPair.Existing is null)
                {
                    if (_selectedExistingItem != null)
                    {
                        _selectedExistingItem.IsVisuallySelected = false;
                        _selectedExistingItem = null;
                        OnPropertyChanged(nameof(SelectedExistingItem));
                    }
                    return;
                }

                foreach (ComponentConflictItem item in ExistingComponents)
                {
                    if (item != matchedPair.Existing)
                    {
                        item.IsVisuallySelected = false;
                    }
                }

                _selectedExistingItem = matchedPair.Existing;
                matchedPair.Existing.IsVisuallySelected = true;
                OnPropertyChanged(nameof(SelectedExistingItem));
                OnPropertyChanged(nameof(ExistingComponentsToml));
                OnPropertyChanged(nameof(CurrentFieldPreferences));
                OnPropertyChanged(nameof(PreviewName));
                OnPropertyChanged(nameof(PreviewAuthor));
                OnPropertyChanged(nameof(PreviewInstructionsCount));

                SyncSelectionRequested?.Invoke(this, new SyncSelectionEventArgs { SelectedItem = value, MatchedItem = matchedPair.Existing });
            }
        }

        public TomlDiffResult SelectedMergedTomlLine
        {
            get => _selectedMergedTomlLine;
            set
            {
                if (_selectedMergedTomlLine == value)
                {
                    return;
                }

                _selectedMergedTomlLine = value;
                OnPropertyChanged();

            }
        }

        public bool ComparisonVisible => _selectedExistingItem != null || _selectedIncomingItem != null;

        public bool CanLinkItems => _selectedExistingItem != null && _selectedIncomingItem != null &&
                                    !_matchedPairs.Any(p =>
                                        p.Existing == _selectedExistingItem || p.Incoming == _selectedIncomingItem);

        public string LinkButtonText
        {
            get
            {
                if (_selectedExistingItem is null || _selectedIncomingItem is null)
                {
                    return "Select one from each list to link";
                }

                if (CanLinkItems)
                {
                    return $"🔗 Link \"{_selectedExistingItem.Name}\" ↔ \"{_selectedIncomingItem.Name}\"";
                }

                return "Already linked or part of another link";

            }
        }

        public string ComparisonText
        {
            get
            {
                if (_selectedExistingItem is null && _selectedIncomingItem is null)
                {
                    return "Select a component to see details";
                }

                ComponentConflictItem item = _selectedExistingItem ?? _selectedIncomingItem;
                ModComponent component = item.ModComponent;

                var sb = new System.Text.StringBuilder();
                _ = sb.Append("ModComponent: ").Append(component.Name).AppendLine();
                _ = sb.Append("Author: ").Append(component.Author).AppendLine();
                string categoryStr = component.Category != null && component.Category.Count > 0
                    ? string.Join(", ", component.Category)
                    : "No category";
                _ = sb.Append("Category: ").Append(categoryStr).Append(" / ").Append(component.Tier).AppendLine();
                _ = sb.AppendLine($"Instructions: {component.Instructions.Count}");
                _ = sb.AppendLine($"Options: {component.Options.Count}");
                _ = sb.AppendLine($"Dependencies: {component.Dependencies.Count}");
                _ = sb.AppendLine($"Links: {component.ResourceRegistry.Count}");

                if (_selectedExistingItem != null)
                {
                    (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                        _matchedPairs.FirstOrDefault(p => p.Existing == _selectedExistingItem);
                    if (matchedPair.Incoming is null)
                    {
                        return sb.ToString();
                    }

                    _ = sb.AppendLine("\n🔄 DIFFERENCES FROM INCOMING:");
                    CompareComponents(component, matchedPair.Incoming.ModComponent, sb);
                }
                else
                {
                    (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                        _matchedPairs.FirstOrDefault(p => p.Incoming == _selectedIncomingItem);
                    if (matchedPair.Existing != null)
                    {
                        _ = sb.AppendLine("\n🔄 DIFFERENCES FROM EXISTING:");
                        CompareComponents(component, matchedPair.Existing.ModComponent, sb);
                    }
                }

                return sb.ToString();
            }
        }

        private static void CompareComponents(ModComponent a, ModComponent b, System.Text.StringBuilder sb)
        {
            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal))
            {
                _ = sb.AppendLine($"  Name: '{a.Name}' vs '{b.Name}'");
            }

            if (!string.Equals(a.Author, b.Author, StringComparison.Ordinal))
            {
                _ = sb.AppendLine($"  Author: '{a.Author}' vs '{b.Author}'");
            }

            if (a.Category != b.Category)
            {
                _ = sb.AppendLine($"  Category: '{a.Category}' vs '{b.Category}'");
            }

            if (!string.Equals(a.Tier, b.Tier, StringComparison.Ordinal))
            {
                _ = sb.AppendLine($"  Tier: '{a.Tier}' vs '{b.Tier}'");
            }

            if (a.Instructions.Count != b.Instructions.Count)
            {
                _ = sb.AppendLine($"  Instructions: {a.Instructions.Count} vs {b.Instructions.Count}");
            }

            if (a.Options.Count != b.Options.Count)
            {
                _ = sb.AppendLine($"  Options: {a.Options.Count} vs {b.Options.Count}");
            }

            if (a.ResourceRegistry.Count != b.ResourceRegistry.Count)
            {
                _ = sb.AppendLine($"  Links: {a.ResourceRegistry.Count} vs {b.ResourceRegistry.Count}");
            }
        }

        public bool UseIncomingOrder
        {
            get => _useIncomingOrder;
            set
            {
                if (_useIncomingOrder == value)
                {
                    return;
                }

                _useIncomingOrder = value;
                OnPropertyChanged();
            }
        }

        public bool SelectAllExisting
        {
            get => _selectAllExisting;
            set
            {
                if (_selectAllExisting == value)
                {
                    return;
                }

                _selectAllExisting = value;

                foreach (ComponentConflictItem item in ExistingComponents)
                {
                    item.PropertyChanged -= OnItemSelectionChanged;
                    item.IsSelected = value;
                    item.PropertyChanged += OnItemSelectionChanged;
                }

                OnPropertyChanged();
                UpdatePreview();
            }
        }

        public bool SelectAllIncoming
        {
            get => _selectAllIncoming;
            set
            {
                if (_selectAllIncoming == value)
                {
                    return;
                }

                _selectAllIncoming = value;

                foreach (ComponentConflictItem item in IncomingComponents)
                {
                    item.PropertyChanged -= OnItemSelectionChanged;
                    item.IsSelected = value;
                    item.PropertyChanged += OnItemSelectionChanged;
                }

                OnPropertyChanged();
                UpdatePreview();
            }
        }

        public bool SkipDuplicates
        {
            get => _skipDuplicates;
            set
            {
                if (_skipDuplicates == value)
                {
                    return;
                }

                _skipDuplicates = value;
                OnPropertyChanged();
            }
        }

        private void BuildConflictItems(
            List<ModComponent> existingComponents,
            List<ModComponent> incomingComponents,
            Func<ModComponent, ModComponent, bool> matchFunc)
        {
            var existingSet = new HashSet<ModComponent>();
            var incomingSet = new HashSet<ModComponent>();

            var potentialMatches = (from existing in existingComponents from incoming in incomingComponents where matchFunc(existing, incoming) let score = FuzzyMatcher.GetComponentMatchScore(existing, incoming) select (existing, incoming, score)).ToList();

            potentialMatches = potentialMatches.OrderByDescending(m => m.score).Cast<(ModComponent, ModComponent, double)>().ToList();

            var existingToIncomingMatch = new Dictionary<ModComponent, ModComponent>();
            var incomingToExistingMatch = new Dictionary<ModComponent, ModComponent>();
            var existingItemLookup = new Dictionary<ModComponent, ComponentConflictItem>();
            var incomingItemLookup = new Dictionary<ModComponent, ComponentConflictItem>();

            foreach ((ModComponent existing, ModComponent incoming, double _) in potentialMatches)
            {

                if (existingSet.Contains(existing) || incomingSet.Contains(incoming))
                {
                    continue;
                }

                existingToIncomingMatch[existing] = incoming;
                incomingToExistingMatch[incoming] = existing;

                _ = existingSet.Add(existing);
                _ = incomingSet.Add(incoming);
            }

            foreach (ModComponent existing in existingComponents)
            {
                ComponentConflictItem existingItem;

                if (existingToIncomingMatch.ContainsKey(existing))
                {

                    existingItem = new ComponentConflictItem(existing, isFromExisting: true, ComponentConflictStatus.Matched);
                    existingItem.PropertyChanged += OnItemSelectionChanged;
                    existingItem.IsSelected = false;

                    existingItemLookup[existing] = existingItem;
                }
                else
                {

                    existingItem = new ComponentConflictItem(existing, isFromExisting: true, ComponentConflictStatus.ExistingOnly);
                    existingItem.PropertyChanged += OnItemSelectionChanged;
                    existingItem.IsSelected = true;
                    _existingOnly.Add(existingItem);
                }

                ExistingComponents.Add(existingItem);
            }

            foreach (ModComponent incoming in incomingComponents)
            {
                ComponentConflictItem incomingItem;

                if (incomingToExistingMatch.ContainsKey(incoming))
                {

                    incomingItem = new ComponentConflictItem(incoming, isFromExisting: false, ComponentConflictStatus.Matched);
                    incomingItem.PropertyChanged += OnItemSelectionChanged;
                    incomingItem.IsSelected = true;

                    incomingItemLookup[incoming] = incomingItem;
                }
                else
                {

                    incomingItem = new ComponentConflictItem(incoming, isFromExisting: false, ComponentConflictStatus.New);
                    incomingItem.PropertyChanged += OnItemSelectionChanged;
                    incomingItem.IsSelected = true;
                    _incomingOnly.Add(incomingItem);
                }

                IncomingComponents.Add(incomingItem);
            }

            foreach (KeyValuePair<ModComponent, ModComponent> kvp in existingToIncomingMatch)
            {
                ModComponent existing = kvp.Key;
                ModComponent incoming = kvp.Value;

                ComponentConflictItem existingItem = existingItemLookup[existing];
                ComponentConflictItem incomingItem = incomingItemLookup[incoming];

                var pair = Tuple.Create(existingItem, incomingItem);
                _matchedPairs.Add((existingItem, incomingItem));

                GuidConflictResolver.GuidResolution guidResolution =
                    GuidConflictResolver.ResolveGuidConflict(existing, incoming);
                if (guidResolution != null)
                {
                    _guidResolutions[pair] = guidResolution;

                    if (guidResolution.RequiresManualResolution)
                    {
                        existingItem.HasGuidConflict = true;
                        incomingItem.HasGuidConflict = true;
                        existingItem.GuidConflictTooltip = guidResolution.ConflictReason;
                        incomingItem.GuidConflictTooltip = guidResolution.ConflictReason;
                    }
                }
            }
        }

        public void SelectAllIncomingMatches()
        {
            foreach ((ComponentConflictItem Existing, ComponentConflictItem Incoming) pair in _matchedPairs)
            {
                pair.Existing.IsSelected = false;
                pair.Incoming.IsSelected = true;
            }

        }

        public void SelectAllExistingMatches()
        {
            foreach ((ComponentConflictItem Existing, ComponentConflictItem Incoming) pair in _matchedPairs)
            {
                pair.Existing.IsSelected = true;
                pair.Incoming.IsSelected = false;
            }

        }

        public void KeepAllNew()
        {

            bool anyUnselected = _incomingOnly.Any(item => !item.IsSelected);
            foreach (ComponentConflictItem item in _incomingOnly)
            {
                item.IsSelected = anyUnselected;
            }
        }

        public void KeepAllExistingUnmatched()
        {

            bool anyUnselected = _existingOnly.Any(item => !item.IsSelected);
            foreach (ComponentConflictItem item in _existingOnly)
            {
                item.IsSelected = anyUnselected;
            }
        }


        public void UseAllIncomingFields()
        {
            foreach ((ComponentConflictItem Existing, ComponentConflictItem Incoming) pair in _matchedPairs)
            {
                var key = Tuple.Create(pair.Existing, pair.Incoming);

                if (!_fieldPreferences.ContainsKey(key))
                {
                    _ = CurrentFieldPreferences;
                }

                if (_fieldPreferences.TryGetValue(key, out FieldMergePreference fieldPrefs))
                {
                    ModComponent existing = pair.Existing.ModComponent;
                    ModComponent incoming = pair.Incoming.ModComponent;

                    if (!string.IsNullOrWhiteSpace(incoming.Name))
                    {
                        fieldPrefs.Name = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.Name))
                    {
                        fieldPrefs.Name = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (!string.IsNullOrWhiteSpace(incoming.Author))
                    {
                        fieldPrefs.Author = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.Author))
                    {
                        fieldPrefs.Author = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (incoming.Instructions != null && incoming.Instructions.Count > 0)
                    {
                        fieldPrefs.Instructions = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existing.Instructions != null && existing.Instructions.Count > 0)
                    {
                        fieldPrefs.Instructions = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (incoming.Options != null && incoming.Options.Count > 0)
                    {
                        fieldPrefs.Options = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existing.Options != null && existing.Options.Count > 0)
                    {
                        fieldPrefs.Options = FieldMergePreference.FieldSource.UseExisting;
                    }

                    bool existingHasDeps = existing.Dependencies != null && existing.Dependencies.Count > 0;
                    bool incomingHasDeps = incoming.Dependencies != null && incoming.Dependencies.Count > 0;
                    if (existingHasDeps && incomingHasDeps)
                    {
                        fieldPrefs.Dependencies = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (incomingHasDeps)
                    {
                        fieldPrefs.Dependencies = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existingHasDeps)
                    {
                        fieldPrefs.Dependencies = FieldMergePreference.FieldSource.UseExisting;
                    }

                    bool existingHasRestrictions = existing.Restrictions != null && existing.Restrictions.Count > 0;
                    bool incomingHasRestrictions = incoming.Restrictions != null && incoming.Restrictions.Count > 0;
                    if (existingHasRestrictions && incomingHasRestrictions)
                    {
                        fieldPrefs.Restrictions = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (incomingHasRestrictions)
                    {
                        fieldPrefs.Restrictions = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existingHasRestrictions)
                    {
                        fieldPrefs.Restrictions = FieldMergePreference.FieldSource.UseExisting;
                    }

                    bool existingHasInstallAfter = existing.InstallAfter != null && existing.InstallAfter.Count > 0;
                    bool incomingHasInstallAfter = incoming.InstallAfter != null && incoming.InstallAfter.Count > 0;
                    if (existingHasInstallAfter && incomingHasInstallAfter)
                    {
                        fieldPrefs.InstallAfter = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (incomingHasInstallAfter)
                    {
                        fieldPrefs.InstallAfter = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existingHasInstallAfter)
                    {
                        fieldPrefs.InstallAfter = FieldMergePreference.FieldSource.UseExisting;
                    }

                    bool existingHasResourceRegistry = existing.ResourceRegistry != null && existing.ResourceRegistry.Count > 0;
                    bool incomingHasResourceRegistry = incoming.ResourceRegistry != null && incoming.ResourceRegistry.Count > 0;
                    if (existingHasResourceRegistry && incomingHasResourceRegistry)
                    {
                        fieldPrefs.ResourceRegistry = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (incomingHasResourceRegistry)
                    {
                        fieldPrefs.ResourceRegistry = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existingHasResourceRegistry)
                    {
                        fieldPrefs.ResourceRegistry = FieldMergePreference.FieldSource.UseExisting;
                    }

                    bool existingHasLanguage = existing.Language != null && existing.Language.Count > 0;
                    bool incomingHasLanguage = incoming.Language != null && incoming.Language.Count > 0;
                    if (existingHasLanguage && incomingHasLanguage)
                    {
                        fieldPrefs.Language = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (incomingHasLanguage)
                    {
                        fieldPrefs.Language = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existingHasLanguage)
                    {
                        fieldPrefs.Language = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (!string.IsNullOrWhiteSpace(incoming.Description))
                    {
                        fieldPrefs.Description = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.Description))
                    {
                        fieldPrefs.Description = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (!string.IsNullOrWhiteSpace(incoming.Directions))
                    {
                        fieldPrefs.Directions = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.Directions))
                    {
                        fieldPrefs.Directions = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (incoming.Category != null && incoming.Category.Count > 0)
                    {
                        fieldPrefs.Category = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (existing.Category != null && existing.Category.Count > 0)
                    {
                        fieldPrefs.Category = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (!string.IsNullOrWhiteSpace(incoming.Tier))
                    {
                        fieldPrefs.Tier = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.Tier))
                    {
                        fieldPrefs.Tier = FieldMergePreference.FieldSource.UseExisting;
                    }

                    if (!string.IsNullOrWhiteSpace(incoming.InstallationMethod))
                    {
                        fieldPrefs.InstallationMethod = FieldMergePreference.FieldSource.UseIncoming;
                    }
                    else if (!string.IsNullOrWhiteSpace(existing.InstallationMethod))
                    {
                        fieldPrefs.InstallationMethod = FieldMergePreference.FieldSource.UseExisting;
                    }
                }
            }

            OnPropertyChanged(nameof(CurrentFieldPreferences));
            OnPropertyChanged(nameof(PreviewName));
            OnPropertyChanged(nameof(PreviewAuthor));
            OnPropertyChanged(nameof(PreviewInstructionsCount));
            UpdatePreview();
        }


        public void UseAllExistingFields()
        {
            foreach ((ComponentConflictItem Existing, ComponentConflictItem Incoming) pair in _matchedPairs)
            {
                var key = Tuple.Create(pair.Existing, pair.Incoming);

                if (!_fieldPreferences.ContainsKey(key))
                {
                    _ = CurrentFieldPreferences;
                }

                if (_fieldPreferences.TryGetValue(key, out FieldMergePreference fieldPrefs))
                {
                    ModComponent existing = pair.Existing.ModComponent;
                    ModComponent incoming = pair.Incoming.ModComponent;

                    if (!string.IsNullOrWhiteSpace(existing.Name))
                    {
                        fieldPrefs.Name = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (!string.IsNullOrWhiteSpace(incoming.Name))
                    {
                        fieldPrefs.Name = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (!string.IsNullOrWhiteSpace(existing.Author))
                    {
                        fieldPrefs.Author = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (!string.IsNullOrWhiteSpace(incoming.Author))
                    {
                        fieldPrefs.Author = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (existing.Instructions != null && existing.Instructions.Count > 0)
                    {
                        fieldPrefs.Instructions = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incoming.Instructions != null && incoming.Instructions.Count > 0)
                    {
                        fieldPrefs.Instructions = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (existing.Options != null && existing.Options.Count > 0)
                    {
                        fieldPrefs.Options = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incoming.Options != null && incoming.Options.Count > 0)
                    {
                        fieldPrefs.Options = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    bool existingHasDeps = existing.Dependencies != null && existing.Dependencies.Count > 0;
                    bool incomingHasDeps = incoming.Dependencies != null && incoming.Dependencies.Count > 0;
                    if (existingHasDeps && incomingHasDeps)
                    {
                        fieldPrefs.Dependencies = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (existingHasDeps)
                    {
                        fieldPrefs.Dependencies = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incomingHasDeps)
                    {
                        fieldPrefs.Dependencies = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    bool existingHasRestrictions = existing.Restrictions != null && existing.Restrictions.Count > 0;
                    bool incomingHasRestrictions = incoming.Restrictions != null && incoming.Restrictions.Count > 0;
                    if (existingHasRestrictions && incomingHasRestrictions)
                    {
                        fieldPrefs.Restrictions = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (existingHasRestrictions)
                    {
                        fieldPrefs.Restrictions = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incomingHasRestrictions)
                    {
                        fieldPrefs.Restrictions = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    bool existingHasInstallAfter = existing.InstallAfter != null && existing.InstallAfter.Count > 0;
                    bool incomingHasInstallAfter = incoming.InstallAfter != null && incoming.InstallAfter.Count > 0;
                    if (existingHasInstallAfter && incomingHasInstallAfter)
                    {
                        fieldPrefs.InstallAfter = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (existingHasInstallAfter)
                    {
                        fieldPrefs.InstallAfter = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incomingHasInstallAfter)
                    {
                        fieldPrefs.InstallAfter = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    bool existingHasResourceRegistry = existing.ResourceRegistry != null && existing.ResourceRegistry.Count > 0;
                    bool incomingHasResourceRegistry = incoming.ResourceRegistry != null && incoming.ResourceRegistry.Count > 0;
                    if (existingHasResourceRegistry && incomingHasResourceRegistry)
                    {
                        fieldPrefs.ResourceRegistry = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (existingHasResourceRegistry)
                    {
                        fieldPrefs.ResourceRegistry = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incomingHasResourceRegistry)
                    {
                        fieldPrefs.ResourceRegistry = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    bool existingHasLanguage = existing.Language != null && existing.Language.Count > 0;
                    bool incomingHasLanguage = incoming.Language != null && incoming.Language.Count > 0;
                    if (existingHasLanguage && incomingHasLanguage)
                    {
                        fieldPrefs.Language = FieldMergePreference.FieldSource.Merge;
                    }
                    else if (existingHasLanguage)
                    {
                        fieldPrefs.Language = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incomingHasLanguage)
                    {
                        fieldPrefs.Language = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (!string.IsNullOrWhiteSpace(existing.Description))
                    {
                        fieldPrefs.Description = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (!string.IsNullOrWhiteSpace(incoming.Description))
                    {
                        fieldPrefs.Description = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (!string.IsNullOrWhiteSpace(existing.Directions))
                    {
                        fieldPrefs.Directions = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (!string.IsNullOrWhiteSpace(incoming.Directions))
                    {
                        fieldPrefs.Directions = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (existing.Category != null && existing.Category.Count > 0)
                    {
                        fieldPrefs.Category = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (incoming.Category != null && incoming.Category.Count > 0)
                    {
                        fieldPrefs.Category = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (!string.IsNullOrWhiteSpace(existing.Tier))
                    {
                        fieldPrefs.Tier = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (!string.IsNullOrWhiteSpace(incoming.Tier))
                    {
                        fieldPrefs.Tier = FieldMergePreference.FieldSource.UseIncoming;
                    }

                    if (!string.IsNullOrWhiteSpace(existing.InstallationMethod))
                    {
                        fieldPrefs.InstallationMethod = FieldMergePreference.FieldSource.UseExisting;
                    }
                    else if (!string.IsNullOrWhiteSpace(incoming.InstallationMethod))
                    {
                        fieldPrefs.InstallationMethod = FieldMergePreference.FieldSource.UseIncoming;
                    }
                }
            }

            OnPropertyChanged(nameof(CurrentFieldPreferences));
            OnPropertyChanged(nameof(PreviewName));
            OnPropertyChanged(nameof(PreviewAuthor));
            OnPropertyChanged(nameof(PreviewInstructionsCount));
            UpdatePreview();
        }

        private void ApplySearchFilter()
        {
            FilteredExistingComponents.Clear();
            FilteredIncomingComponents.Clear();

            string searchLower = (SearchText ?? string.Empty).ToLowerInvariant().Trim();
            bool hasSearch = !string.IsNullOrEmpty(searchLower);

            foreach (ComponentConflictItem item in ExistingComponents)
            {
                if (!hasSearch ||
                     item.Name.IndexOf(searchLower, StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                     item.Author.IndexOf(searchLower, StringComparison.InvariantCultureIgnoreCase) >= 0)
                {
                    FilteredExistingComponents.Add(item);
                }
            }

            foreach (ComponentConflictItem item in IncomingComponents)
            {
                if (!hasSearch ||
                     item.Name.IndexOf(searchLower, StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                     item.Author.IndexOf(searchLower, StringComparison.InvariantCultureIgnoreCase) >= 0)
                {
                    FilteredIncomingComponents.Add(item);
                }
            }
        }

        private bool CanLinkSelected() => _selectedExistingItem != null && _selectedIncomingItem != null &&
                                          !_matchedPairs.Any(p =>
                                              p.Existing == _selectedExistingItem ||
                                              p.Incoming == _selectedIncomingItem);

        public void ChooseGuidForItem(ComponentConflictItem item)
        {

            (ComponentConflictItem existing, ComponentConflictItem incoming) =
                _matchedPairs.FirstOrDefault(p => p.Existing == item || p.Incoming == item);

            if (existing is null || incoming is null)
            {
                return;
            }

            var pairKey = Tuple.Create(existing, incoming);

            if (!_guidResolutions.TryGetValue(pairKey, out GuidConflictResolver.GuidResolution resolution))
            {
                return;
            }

            if (item == existing)
            {
                resolution.ChosenGuid = existing.ModComponent.Guid;
                resolution.RejectedGuid = incoming.ModComponent.Guid;
            }
            else
            {
                resolution.ChosenGuid = incoming.ModComponent.Guid;
                resolution.RejectedGuid = existing.ModComponent.Guid;
            }

            resolution.RequiresManualResolution = false;

            existing.HasGuidConflict = false;
            incoming.HasGuidConflict = false;

            UpdatePreview();
        }

        private bool CanUnlinkSelected()
        {
            if (_selectedExistingItem != null)
            {
                return _matchedPairs.Any(p => p.Existing == _selectedExistingItem);
            }

            if (_selectedIncomingItem != null)
            {
                return _matchedPairs.Any(p => p.Incoming == _selectedIncomingItem);
            }

            return false;
        }

        private void LinkSelectedItems()
        {
            if (!CanLinkSelected())
            {
                return;
            }

            _ = _existingOnly.Remove(_selectedExistingItem);
            _ = _incomingOnly.Remove(_selectedIncomingItem);

            _selectedExistingItem.UpdateStatus(ComponentConflictStatus.Matched);
            _selectedIncomingItem.UpdateStatus(ComponentConflictStatus.Matched);

            _matchedPairs.Add((_selectedExistingItem, _selectedIncomingItem));

            _selectedExistingItem.IsSelected = false;
            _selectedIncomingItem.IsSelected = true;

            UpdatePreview();
            OnPropertyChanged(nameof(ConflictDescription));
            OnPropertyChanged(nameof(MergeImpactSummary));
            OnPropertyChanged(nameof(LinkButtonText));
            OnPropertyChanged(nameof(HasMatchedPairSelected));
            OnPropertyChanged(nameof(CurrentFieldPreferences));
            LinkSelectedCommand.RaiseCanExecuteChanged();
            UnlinkSelectedCommand.RaiseCanExecuteChanged();
        }

        private void UnlinkSelectedItems()
        {

            (ComponentConflictItem Existing, ComponentConflictItem Incoming) pairToRemove = default;

            if (_selectedExistingItem != null)
            {
                pairToRemove = _matchedPairs.FirstOrDefault(p => p.Existing == _selectedExistingItem);
            }
            else if (_selectedIncomingItem != null)
            {
                pairToRemove = _matchedPairs.FirstOrDefault(p => p.Incoming == _selectedIncomingItem);
            }

            if (pairToRemove.Existing is null)
            {
                return;
            }

            ComponentConflictItem existingToUnlink = pairToRemove.Existing;
            ComponentConflictItem incomingToUnlink = pairToRemove.Incoming;

            _ = _matchedPairs.Remove(pairToRemove);

            existingToUnlink.UpdateStatus(ComponentConflictStatus.ExistingOnly);
            incomingToUnlink.UpdateStatus(ComponentConflictStatus.New);

            if (!_existingOnly.Contains(existingToUnlink))
            {
                _existingOnly.Add(existingToUnlink);
            }

            if (!_incomingOnly.Contains(incomingToUnlink))
            {
                _incomingOnly.Add(incomingToUnlink);
            }

            existingToUnlink.IsSelected = true;
            incomingToUnlink.IsSelected = true;

            UpdatePreview();
            OnPropertyChanged(nameof(ConflictDescription));
            OnPropertyChanged(nameof(MergeImpactSummary));
            OnPropertyChanged(nameof(LinkButtonText));
            LinkSelectedCommand.RaiseCanExecuteChanged();
            UnlinkSelectedCommand.RaiseCanExecuteChanged();
        }

        private void OnItemSelectionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(ComponentConflictItem.IsSelected), StringComparison.Ordinal))
            {
                return;
            }

            UpdatePreview();
            OnPropertyChanged(nameof(ConflictSummary));
            OnPropertyChanged(nameof(NewComponentsCount));
            OnPropertyChanged(nameof(UpdatedComponentsCount));
            OnPropertyChanged(nameof(KeptComponentsCount));
            OnPropertyChanged(nameof(RemovedComponentsCount));
            OnPropertyChanged(nameof(TotalChanges));
            OnPropertyChanged(nameof(MergeImpactSummary));
        }

        private void UpdatePreview()
        {
            PreviewComponents.Clear();

            var result = new List<PreviewItem>();

            if (UseIncomingOrder)
            {

                int order = 1;

                foreach (ComponentConflictItem incomingItem in IncomingComponents)
                {
                    if (!incomingItem.IsSelected)
                    {
                        continue;
                    }

                    (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                        _matchedPairs.FirstOrDefault(p => p.Incoming == incomingItem);
                    if (matchedPair.Existing != null && matchedPair.Existing.IsSelected)
                    {

                        if (!SkipDuplicates)
                        {
                            result.Add(new PreviewItem
                            {
                                OrderNumber = $"{order++}.",
                                Name = incomingItem.Name,
                                Source = "From: Incoming (Updated)",
                                SourceColor = ThemeResourceHelper.MergeSourceIncomingBrush,
                                ModComponent = incomingItem.ModComponent,
                                StatusIcon = "⬆️",
                                PositionChange = "UPDATED",
                                PositionChangeColor = ThemeResourceHelper.MergePositionChangedBrush,
                            });
                        }
                        else
                        {
                            result.Add(new PreviewItem
                            {
                                OrderNumber = $"{order++}.",
                                Name = incomingItem.Name,
                                Source = "From: Incoming",
                                SourceColor = ThemeResourceHelper.MergeSourceIncomingBrush,
                                ModComponent = incomingItem.ModComponent,
                                StatusIcon = "🔄",
                                PositionChange = "MATCH",
                                PositionChangeColor = ThemeResourceHelper.MergePositionNewBrush,
                            });
                        }
                    }
                    else
                    {
                        result.Add(new PreviewItem
                        {
                            OrderNumber = $"{order++}.",
                            Name = incomingItem.Name,
                            Source = "From: Incoming",
                            SourceColor = ThemeResourceHelper.MergeSourceIncomingBrush,
                            ModComponent = incomingItem.ModComponent,
                            StatusIcon = incomingItem.Status == ComponentConflictStatus.New ? "✨" : "🔄",
                            PositionChange = incomingItem.Status == ComponentConflictStatus.New ? "NEW" : "MATCH",
                            PositionChangeColor = incomingItem.Status == ComponentConflictStatus.New
                                ? ThemeResourceHelper.MergeStatusNewBrush
                                : ThemeResourceHelper.MergePositionNewBrush,
                        });
                    }
                }

                foreach (ComponentConflictItem existingItem in _existingOnly.Where(e => e.IsSelected))
                {

                    int insertAt = FindInsertionPoint(result, existingItem);
                    result.Insert(insertAt,
                        new PreviewItem
                        {
                            OrderNumber = $"{insertAt + 1}.",
                            Name = existingItem.Name,
                            Source = "From: Existing (Kept)",
                            SourceColor = ThemeResourceHelper.MergeSourceExistingBrush,
                            ModComponent = existingItem.ModComponent,
                            StatusIcon = "📦",
                            PositionChange = "KEPT",
                            PositionChangeColor = ThemeResourceHelper.MergeStatusExistingOnlyBrush,
                        });
                }

                for (int i = 0; i < result.Count; i++)
                {
                    result[i].OrderNumber = $"{i + 1}.";
                }
            }
            else
            {

                int order = 1;

                foreach (ComponentConflictItem existingItem in ExistingComponents)
                {
                    if (!existingItem.IsSelected)
                    {
                        continue;
                    }

                    (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                        _matchedPairs.FirstOrDefault(p => p.Existing == existingItem);
                    if (matchedPair.Incoming != null && matchedPair.Incoming.IsSelected)
                    {

                        if (!SkipDuplicates)
                        {
                            result.Add(new PreviewItem
                            {
                                OrderNumber = $"{order++}.",
                                Name = existingItem.Name,
                                Source = "From: Existing (Kept)",
                                SourceColor = ThemeResourceHelper.MergeSourceExistingBrush,
                                ModComponent = existingItem.ModComponent,
                                StatusIcon = "🔄",
                                PositionChange = "MATCH",
                                PositionChangeColor = ThemeResourceHelper.MergePositionNewBrush,
                            });
                        }
                        else
                        {
                            result.Add(new PreviewItem
                            {
                                OrderNumber = $"{order++}.",
                                Name = existingItem.Name,
                                Source = "From: Existing",
                                SourceColor = ThemeResourceHelper.MergeSourceExistingBrush,
                                ModComponent = existingItem.ModComponent,
                                StatusIcon = "📦",
                                PositionChange = "KEPT",
                                PositionChangeColor = ThemeResourceHelper.MergeStatusExistingOnlyBrush,
                            });
                        }
                    }
                    else
                    {
                        result.Add(new PreviewItem
                        {
                            OrderNumber = $"{order++}.",
                            Name = existingItem.Name,
                            Source = "From: Existing",
                            SourceColor = ThemeResourceHelper.MergeSourceExistingBrush,
                            ModComponent = existingItem.ModComponent,
                            StatusIcon = "📦",
                            PositionChange = "KEPT",
                            PositionChangeColor = ThemeResourceHelper.MergeStatusExistingOnlyBrush,
                        });
                    }
                }

                result.AddRange(_incomingOnly.Where(i => i.IsSelected)
                .Select(incomingItem => new PreviewItem
                {
                    OrderNumber = $"{order++}.",
                    Name = incomingItem.Name,
                    Source = "From: Incoming (New)",
                    SourceColor = ThemeResourceHelper.MergeSourceIncomingBrush,
                    ModComponent = incomingItem.ModComponent,
                    StatusIcon = "✨",
                    PositionChange = "NEW",
                    PositionChangeColor = ThemeResourceHelper.MergeStatusNewBrush,
                }));
            }

            foreach (PreviewItem item in result)
            {
                PreviewComponents.Add(item);
            }

            UpdateRealtimeMergedComponents();

            OnPropertyChanged(nameof(ConflictSummary));
        }

        private int FindInsertionPoint(List<PreviewItem> result, ComponentConflictItem itemToInsert)
        {

            int originalIndex = ExistingComponents.ToList().FindIndex(c => c == itemToInsert);
            if (originalIndex < 0)
            {
                return result.Count;
            }

            for (int i = originalIndex + 1; i < ExistingComponents.Count; i++)
            {
                ComponentConflictItem afterComponent = ExistingComponents[i];
                int afterIndexInResult = result.FindIndex(p => p.ModComponent == afterComponent.ModComponent);
                if (afterIndexInResult >= 0)
                {
                    return afterIndexInResult;
                }
            }

            return result.Count;
        }

        private void UpdateRealtimeMergedComponents()
        {
            try
            {
                RealtimeMergedComponents.Clear();

                var mergedComponents = new List<ModComponent>();
                var guidMap = new Dictionary<Guid, Guid>();

                foreach (PreviewItem previewItem in PreviewComponents)
                {
                    ModComponent component = previewItem.ModComponent;

                    (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                        _matchedPairs.FirstOrDefault(p =>
                            p.Existing.ModComponent == component || p.Incoming.ModComponent == component);

                    if (matchedPair.Existing != null && matchedPair.Incoming != null)
                    {

                        var pair = Tuple.Create(matchedPair.Existing, matchedPair.Incoming);

                        if (!_fieldPreferences.TryGetValue(pair, out FieldMergePreference fieldPrefs))
                        {
                            fieldPrefs = CreateAndSubscribeFieldPreferences(
                                matchedPair.Existing.ModComponent,
                                matchedPair.Incoming.ModComponent
                            );
                            _fieldPreferences[pair] = fieldPrefs;
                        }

                        ModComponent mergedComponent = ComponentMergeConflictViewModel.MergeComponentData(
                            matchedPair.Existing.ModComponent,
                            matchedPair.Incoming.ModComponent,
                            fieldPrefs
                        );

                        if (_guidResolutions.TryGetValue(pair, out GuidConflictResolver.GuidResolution resolution))
                        {
                            Guid chosenGuid = resolution.ChosenGuid;
                            Guid rejectedGuid = resolution.RejectedGuid;

                            mergedComponent.Guid = chosenGuid;

                            if (chosenGuid != rejectedGuid)
                            {
                                guidMap[rejectedGuid] = chosenGuid;
                            }
                        }

                        mergedComponents.Add(mergedComponent);
                    }
                    else
                    {

                        mergedComponents.Add(component);
                    }
                }

                foreach (ModComponent component in mergedComponents)
                {

                    for (int i = 0; i < component.Dependencies.Count; i++)
                    {
                        if (guidMap.TryGetValue(component.Dependencies[i], out Guid newGuid))
                        {
                            component.Dependencies[i] = newGuid;
                        }
                    }

                    for (int i = 0; i < component.Restrictions.Count; i++)
                    {
                        if (guidMap.TryGetValue(component.Restrictions[i], out Guid newGuid))
                        {
                            component.Restrictions[i] = newGuid;
                        }
                    }

                    for (int i = 0; i < component.InstallAfter.Count; i++)
                    {
                        if (guidMap.TryGetValue(component.InstallAfter[i], out Guid newGuid))
                        {
                            component.InstallAfter[i] = newGuid;
                        }
                    }
                }

                foreach (ModComponent component in mergedComponents)
                {
                    RealtimeMergedComponents.Add(component);
                }

                UpdateCurrentTomlDiff();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error updating real-time merged components");
            }
        }

        public List<ModComponent> GetMergedComponents()
        {
            var mergedComponents = new List<ModComponent>();
            var guidMap = new Dictionary<Guid, Guid>();

            foreach (PreviewItem previewItem in PreviewComponents)
            {
                ModComponent component = previewItem.ModComponent;

                (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair =
                    _matchedPairs.FirstOrDefault(p =>
                        p.Existing.ModComponent == component || p.Incoming.ModComponent == component);

                if (matchedPair.Existing != null && matchedPair.Incoming != null)
                {

                    var pair = Tuple.Create(matchedPair.Existing, matchedPair.Incoming);

                    if (!_fieldPreferences.TryGetValue(pair, out FieldMergePreference fieldPrefs))
                    {
                        fieldPrefs = CreateAndSubscribeFieldPreferences(
                            matchedPair.Existing.ModComponent,
                            matchedPair.Incoming.ModComponent
                        );
                        _fieldPreferences[pair] = fieldPrefs;
                    }

                    ModComponent mergedComponent = ComponentMergeConflictViewModel.MergeComponentData(
                        matchedPair.Existing.ModComponent,
                        matchedPair.Incoming.ModComponent,
                        fieldPrefs
                    );

                    if (_guidResolutions.TryGetValue(pair, out GuidConflictResolver.GuidResolution resolution))
                    {
                        Guid chosenGuid = resolution.ChosenGuid;
                        Guid rejectedGuid = resolution.RejectedGuid;

                        mergedComponent.Guid = chosenGuid;

                        if (chosenGuid != rejectedGuid)
                        {
                            guidMap[rejectedGuid] = chosenGuid;
                        }
                    }

                    mergedComponents.Add(mergedComponent);
                }
                else
                {

                    mergedComponents.Add(component);
                }
            }

            foreach (ModComponent component in mergedComponents)
            {

                for (int i = 0; i < component.Dependencies.Count; i++)
                {
                    if (guidMap.TryGetValue(component.Dependencies[i], out Guid newGuid))
                    {
                        component.Dependencies[i] = newGuid;
                    }
                }

                for (int i = 0; i < component.Restrictions.Count; i++)
                {
                    if (guidMap.TryGetValue(component.Restrictions[i], out Guid newGuid))
                    {
                        component.Restrictions[i] = newGuid;
                    }
                }

                for (int i = 0; i < component.InstallAfter.Count; i++)
                {
                    if (guidMap.TryGetValue(component.InstallAfter[i], out Guid newGuid))
                    {
                        component.InstallAfter[i] = newGuid;
                    }
                }
            }

            return mergedComponents;
        }



        private static ModComponent MergeComponentData(ModComponent existing, ModComponent incoming, FieldMergePreference fieldPrefs)
        {

            if (fieldPrefs is null)
            {
                fieldPrefs = new FieldMergePreference();
            }

            var merged = new ModComponent
            {

                Guid = existing.Guid,

                Name = MergeStringField(existing.Name, incoming.Name, fieldPrefs.Name),
                Author = MergeStringField(existing.Author, incoming.Author, fieldPrefs.Author),
                Description = MergeStringField(existing.Description, incoming.Description, fieldPrefs.Description),
                Directions = MergeStringField(existing.Directions, incoming.Directions, fieldPrefs.Directions),
                Category = MergeListField(existing.Category?.ToList()
                           ?? new List<string>(), incoming.Category?.ToList()
                           ?? new List<string>(), fieldPrefs.Category),
                Tier = MergeStringField(existing.Tier, incoming.Tier, fieldPrefs.Tier),
                InstallationMethod = MergeStringField(existing.InstallationMethod, incoming.InstallationMethod, fieldPrefs.InstallationMethod),

                Instructions = MergeListField(existing.Instructions, incoming.Instructions, fieldPrefs.Instructions),
                Options = MergeListField(existing.Options, incoming.Options, fieldPrefs.Options),

                Dependencies = fieldPrefs.Dependencies == FieldMergePreference.FieldSource.Merge
                        ? MergeLists(existing.Dependencies, incoming.Dependencies, deduplicate: true)
                        : MergeListField(existing.Dependencies, incoming.Dependencies, fieldPrefs.Dependencies),

                Restrictions = fieldPrefs.Restrictions == FieldMergePreference.FieldSource.Merge
                        ? MergeLists(existing.Restrictions, incoming.Restrictions, deduplicate: true)
                        : MergeListField(existing.Restrictions, incoming.Restrictions, fieldPrefs.Restrictions),

                InstallAfter = fieldPrefs.InstallAfter == FieldMergePreference.FieldSource.Merge
                        ? MergeLists(existing.InstallAfter, incoming.InstallAfter, deduplicate: true)
                        : MergeListField(existing.InstallAfter, incoming.InstallAfter, fieldPrefs.InstallAfter),

                ResourceRegistry = fieldPrefs.ResourceRegistry == FieldMergePreference.FieldSource.Merge
                    ? MergeResourceRegistries(existing.ResourceRegistry, incoming.ResourceRegistry)
                    : MergeResourceRegistryField(existing.ResourceRegistry, incoming.ResourceRegistry, fieldPrefs.ResourceRegistry),

                Language = fieldPrefs.Language == FieldMergePreference.FieldSource.Merge
                        ? MergeLists(existing.Language?.ToList()
                            ?? new List<string>(), incoming.Language?.ToList()
                            ?? new List<string>(), deduplicate: true)
                        : MergeListField(existing.Language?.ToList()
                            ?? new List<string>(), incoming.Language?.ToList()
                            ?? new List<string>(), fieldPrefs.Language),

                IsSelected = existing.IsSelected,
                InstallState = existing.InstallState,
                IsDownloaded = existing.IsDownloaded,
            };

            return merged;

            T MergeListField<T>(T existingList, T incomingList, FieldMergePreference.FieldSource preference) where T : System.Collections.ICollection, new()
            {
                bool existingHasValues = !object.Equals(existingList, default(T)) && existingList.Count > 0;
                bool incomingHasValues = !object.Equals(incomingList, default(T)) && incomingList.Count > 0;

                switch (existingHasValues)
                {

                    case false when !incomingHasValues:
                        return new T();
                    case false:
                        return incomingList;
                }

                if (!incomingHasValues)
                {
                    return existingList;
                }

                if (preference == FieldMergePreference.FieldSource.UseExisting)
                {
                    return existingList;
                }

                if (preference == FieldMergePreference.FieldSource.UseIncoming)
                {
                    return incomingList;
                }

                return existingList;
            }

            string MergeStringField(string existingVal, string incomingVal, FieldMergePreference.FieldSource preference)
            {
                bool existingHasValue = !string.IsNullOrWhiteSpace(existingVal);
                bool incomingHasValue = !string.IsNullOrWhiteSpace(incomingVal);

                switch (existingHasValue)
                {

                    case false when !incomingHasValue:
                        return null;
                    case false:
                        return incomingVal;
                }

                if (!incomingHasValue)
                {
                    return existingVal;
                }

                return preference == FieldMergePreference.FieldSource.UseExisting ? existingVal : incomingVal;
            }
        }

        private static List<T> MergeLists<T>(List<T> existingList, List<T> incomingList, bool deduplicate = false)
        {
            if (existingList is null && incomingList is null)
            {
                return new List<T>();
            }

            if (existingList is null || existingList.Count == 0)
            {
                return incomingList != null ? new List<T>(incomingList) : new List<T>();
            }

            if (incomingList is null || incomingList.Count == 0)
            {
                return new List<T>(existingList);
            }

            var merged = new List<T>(existingList);

            if (deduplicate)
            {

                foreach (T item in incomingList)
                {
                    if (!merged.Contains(item))
                    {
                        merged.Add(item);
                    }
                }
            }
            else
            {

                merged.AddRange(incomingList);
            }

            return merged;
        }

        private static Dictionary<string, ResourceMetadata> MergeResourceRegistries(
            Dictionary<string, ResourceMetadata> existingDict,
            Dictionary<string, ResourceMetadata> incomingDict)
        {
            var result = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);

            if (existingDict != null)
            {
                foreach (KeyValuePair<string, ResourceMetadata> kvp in existingDict)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            if (incomingDict != null)
            {
                foreach (KeyValuePair<string, ResourceMetadata> kvp in incomingDict)
                {
                    if (!result.ContainsKey(kvp.Key))
                    {
                        // Clone the ResourceMetadata
                        result[kvp.Key] = CloneResourceMetadata(kvp.Value);
                    }
                    else
                    {
                        // Merge filename dictionaries - prefer incoming by default when both have explicit values
                        ResourceMetadata existingMetadata = result[kvp.Key];
                        ResourceMetadata incomingMetadata = kvp.Value;

                        foreach (KeyValuePair<string, bool?> fileKvp in incomingMetadata.Files)
                        {
                            if (!existingMetadata.Files.TryGetValue(fileKvp.Key, out bool? value))
                            {
                                // File doesn't exist in result, add incoming
                                existingMetadata.Files[fileKvp.Key] = fileKvp.Value;
                            }
                            else if (fileKvp.Value.HasValue && !value.HasValue)
                            {
                                // Incoming has explicit value, existing doesn't - use incoming
                                existingMetadata.Files[fileKvp.Key] = fileKvp.Value;
                            }
                            else if (fileKvp.Value.HasValue && value.HasValue)
                            {
                                // Both have explicit values - use incoming by default (new/updated data)
                                existingMetadata.Files[fileKvp.Key] = fileKvp.Value;
                            }
                            else
                            {
                                // Existing has value (or both null) - keep existing
                                existingMetadata.Files[fileKvp.Key] = value;
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static ResourceMetadata CloneResourceMetadata(ResourceMetadata source)
        {
            if (source == null)
            {
                return null;
            }

            return new ResourceMetadata
            {
                ContentKey = source.ContentKey,
                MetadataHash = source.MetadataHash,
                HandlerMetadata = source.HandlerMetadata != null
                    ? new Dictionary<string, object>(source.HandlerMetadata, StringComparer.Ordinal)
                    : new Dictionary<string, object>(StringComparer.Ordinal),
                Files = source.Files != null
                    ? new Dictionary<string, bool?>(source.Files, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase),
                FileSize = source.FileSize,
                FirstSeen = source.FirstSeen,
                LastVerified = source.LastVerified,
            };
        }

        private static Dictionary<string, ResourceMetadata> MergeResourceRegistryField(
            Dictionary<string, ResourceMetadata> existingDict,
            Dictionary<string, ResourceMetadata> incomingDict,
            FieldMergePreference.FieldSource source)
        {
            switch (source)
            {
                case FieldMergePreference.FieldSource.UseExisting:
                    return existingDict != null
                        ? existingDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal)
                        : new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);

                case FieldMergePreference.FieldSource.UseIncoming:
                    return incomingDict != null
                        ? incomingDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal)
                        : new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);

                case FieldMergePreference.FieldSource.Merge:
                default:
                    return MergeResourceRegistries(existingDict, incomingDict);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public event EventHandler<JumpToRawViewEventArgs> JumpToRawViewRequested;
        public event EventHandler<SyncSelectionEventArgs> SyncSelectionRequested;

        private void JumpToRawView(ComponentConflictItem item)
        {
            if (item is null)
            {
                return;
            }

            JumpToRawViewRequested?.Invoke(this, new JumpToRawViewEventArgs { Item = item });
        }

        public void UpdateExistingTomlView()
        {
            try
            {
                var selectedComponents = ExistingComponents.Where(c => c.IsSelected).Select(c => c.ModComponent).ToList();

                _existingComponentLineNumbers.Clear();
                int currentLine = 1;

                var newCollection = new ObservableCollection<TomlDiffResult>
                {

                    new TomlDiffResult
                    {
                        DiffType = DiffType.Unchanged, Text = "# ModComponent List", LineNumber = currentLine++,
                    },
                    new TomlDiffResult { DiffType = DiffType.Unchanged, Text = "", LineNumber = currentLine++ },
                };

                for (int i = 0; i < selectedComponents.Count; i++)
                {
                    ModComponent component = selectedComponents[i];
                    string componentGuid = component.Guid.ToString();

                    if (i > 0)
                    {
                        newCollection.Add(new TomlDiffResult { DiffType = DiffType.Unchanged, Text = "", LineNumber = currentLine++, ComponentGuid = componentGuid });
                    }

                    _existingComponentLineNumbers[component] = currentLine;

                    ComponentConflictItem conflictItem = ExistingComponents.FirstOrDefault(ci => ci.ModComponent == component);
                    DiffType componentDiffType = DiffType.Removed;

                    if (conflictItem != null && conflictItem.IsSelected)
                    {
                        (ComponentConflictItem Existing, ComponentConflictItem Incoming) matchedPair = _matchedPairs.FirstOrDefault(p => p.Existing == conflictItem);

                        componentDiffType = (matchedPair.Incoming != null && matchedPair.Incoming.IsSelected)
                            ? DiffType.Modified
                            : DiffType.Added;
                    }

                    newCollection.Add(new TomlDiffResult
                    {
                        DiffType = componentDiffType,
                        Text = $"# ModComponent {i + 1}: {component.Name}",
                        LineNumber = currentLine++,
                        ComponentGuid = componentGuid,
                    });

                    string componentToml = component.SerializeComponent();
                    string[] lines = componentToml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    foreach (string line in lines)
                    {
                        newCollection.Add(new TomlDiffResult { DiffType = componentDiffType, Text = line, LineNumber = currentLine++, ComponentGuid = componentGuid });
                    }
                }

                ExistingComponentsToml = newCollection;
                OnPropertyChanged(nameof(ExistingComponentsToml));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error updating existing TOML view");
            }
        }

        public void UpdateIncomingTomlView()
        {
            try
            {
                var selectedComponents = IncomingComponents.Where(c => c.IsSelected).Select(c => c.ModComponent).ToList();

                _incomingComponentLineNumbers.Clear();
                int currentLine = 1;

                var newCollection = new ObservableCollection<TomlDiffResult>
                {

                    new TomlDiffResult { DiffType = DiffType.Unchanged, Text = "# ModComponent List", LineNumber = currentLine++ },
                    new TomlDiffResult { DiffType = DiffType.Unchanged, Text = "", LineNumber = currentLine++ },
                };

                for (int i = 0; i < selectedComponents.Count; i++)
                {
                    ModComponent component = selectedComponents[i];
                    string componentGuid = component.Guid.ToString();

                    if (i > 0)
                    {
                        newCollection.Add(new TomlDiffResult { DiffType = DiffType.Unchanged, Text = "", LineNumber = currentLine++, ComponentGuid = componentGuid });
                    }

                    _incomingComponentLineNumbers[component] = currentLine;

                    ComponentConflictItem conflictItem = IncomingComponents.FirstOrDefault(ci => ci.ModComponent == component);
                    DiffType componentDiffType = DiffType.Added;

                    if (conflictItem != null)
                    {

                        ComponentConflictItem existing = _matchedPairs.FirstOrDefault(p => p.Incoming == conflictItem).Existing;

                        if (existing != null)
                        {

                            if (existing.IsSelected && conflictItem.IsSelected)
                            {

                                componentDiffType = DiffType.Modified;
                            }
                            else if (conflictItem.IsSelected)
                            {

                                componentDiffType = DiffType.Added;
                            }
                            else
                            {

                                componentDiffType = DiffType.Unchanged;
                            }
                        }
                        else
                        {

                            componentDiffType = DiffType.Added;
                        }
                    }

                    newCollection.Add(new TomlDiffResult
                    {
                        DiffType = componentDiffType,
                        Text = $"# ModComponent {i + 1}: {component.Name}",
                        LineNumber = currentLine++,
                        ComponentGuid = componentGuid,
                    });

                    string componentToml = component.SerializeComponent();
                    string[] lines = componentToml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    foreach (string line in lines)
                    {
                        newCollection.Add(new TomlDiffResult
                        {
                            DiffType = componentDiffType,
                            Text = line,
                            LineNumber = currentLine++,
                            ComponentGuid = componentGuid,
                        });
                    }
                }

                IncomingComponentsToml = newCollection;
                OnPropertyChanged(nameof(IncomingComponentsToml));
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error updating incoming TOML view");
            }
        }

        public void UpdateMergedTomlView()
        {
            try
            {
                MergedComponentsToml.Clear();

                string existingToml = GenerateFullToml(ExistingComponents.Where(c => c.IsSelected).Select(c => c.ModComponent).ToList());
                string mergedToml = GenerateFullToml(RealtimeMergedComponents.ToList());

                List<TomlDiffResult> diffResults = GenerateTomlDiff(existingToml, mergedToml);
                foreach (TomlDiffResult line in diffResults)
                {
                    MergedComponentsToml.Add(line);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error updating merged TOML view");
            }
        }

        public int GetComponentLineNumber(ComponentConflictItem item)
        {
            if (item is null)
            {
                return 0;
            }

            Dictionary<ModComponent, int> map = item.IsFromExisting ? _existingComponentLineNumbers : _incomingComponentLineNumbers;
            return map.TryGetValue(item.ModComponent, out int value) ? value : 0;
        }

        private readonly Dictionary<ModComponent, int> _existingComponentLineNumbers = new Dictionary<ModComponent, int>();
        private readonly Dictionary<ModComponent, int> _incomingComponentLineNumbers = new Dictionary<ModComponent, int>();

        private static string GenerateFullToml(List<ModComponent> components)
        {
            if (components is null || components.Count == 0)
            {
                return "# No components selected";
            }

            var sb = new System.Text.StringBuilder();
            _ = sb.AppendLine("# ModComponent List");
            _ = sb.AppendLine();

            for (int i = 0; i < components.Count; i++)
            {
                if (i > 0)
                {
                    _ = sb.AppendLine();
                }

                _ = sb.Append("# ModComponent ").Append(i + 1).Append(": ").Append(components[i].Name).AppendLine();
                _ = sb.Append(components[i].SerializeComponent());
            }

            return sb.ToString();
        }

        private void UpdateCurrentTomlDiff()
        {
            try
            {
                CurrentTomlDiff.Clear();

                if (_selectedExistingItem is null && _selectedIncomingItem is null)
                {
                    return;
                }

                ComponentConflictItem selectedItem = _selectedExistingItem ?? _selectedIncomingItem;
                ModComponent component = selectedItem.ModComponent;

                ModComponent mergedComponent = RealtimeMergedComponents.FirstOrDefault(c => string.Equals(c.Name, component.Name, StringComparison.Ordinal));
                if (mergedComponent is null)
                {
                    return;
                }

                string originalToml = component.SerializeComponent();
                string mergedToml = mergedComponent.SerializeComponent();

                List<TomlDiffResult> diffResults = GenerateTomlDiff(originalToml, mergedToml);

                foreach (TomlDiffResult result in diffResults)
                {
                    CurrentTomlDiff.Add(result);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error updating TOML diff");
            }
        }

        private static List<TomlDiffResult> GenerateTomlDiff(string original, string merged)
        {
            var results = new List<TomlDiffResult>();

            if (string.IsNullOrEmpty(original) && string.IsNullOrEmpty(merged))
            {
                return results;
            }

            if (string.IsNullOrEmpty(original))
            {

                string[] lines = merged.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                results.AddRange(lines.Select((t, i) => new TomlDiffResult { DiffType = DiffType.Added, Text = t, LineNumber = i + 1 }));

                return results;
            }

            if (string.IsNullOrEmpty(merged))
            {

                string[] lines = original.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                results.AddRange(lines.Select((t, i) => new TomlDiffResult { DiffType = DiffType.Removed, Text = t, LineNumber = i + 1 }));

                return results;
            }

            string[] originalLines = original.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string[] mergedLines = merged.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int originalIndex = 0;
            int mergedIndex = 0;

            while (originalIndex < originalLines.Length && mergedIndex < mergedLines.Length)
            {
                string originalLine = originalLines[originalIndex];
                string mergedLine = mergedLines[mergedIndex];

                if (string.Equals(originalLine, mergedLine, StringComparison.Ordinal))
                {

                    results.Add(new TomlDiffResult
                    {
                        DiffType = DiffType.Unchanged,
                        Text = originalLine,
                        LineNumber = mergedIndex + 1,
                    });
                    originalIndex++;
                    mergedIndex++;
                }
                else if (mergedIndex + 1 < mergedLines.Length && string.Equals(originalLine, mergedLines[mergedIndex + 1], StringComparison.Ordinal))
                {

                    results.Add(new TomlDiffResult
                    {
                        DiffType = DiffType.Added,
                        Text = mergedLine,
                        LineNumber = mergedIndex + 1,
                    });
                    mergedIndex++;
                }
                else if (originalIndex + 1 < originalLines.Length && string.Equals(mergedLine, originalLines[originalIndex + 1], StringComparison.Ordinal))
                {

                    results.Add(new TomlDiffResult
                    {
                        DiffType = DiffType.Removed,
                        Text = originalLine,
                        LineNumber = mergedIndex + 1,
                    });
                    originalIndex++;
                }
                else
                {

                    results.Add(new TomlDiffResult
                    {
                        DiffType = DiffType.Modified,
                        Text = mergedLine,
                        LineNumber = mergedIndex + 1,
                    });
                    originalIndex++;
                    mergedIndex++;
                }
            }

            while (originalIndex < originalLines.Length)
            {
                results.Add(new TomlDiffResult
                {
                    DiffType = DiffType.Removed,
                    Text = originalLines[originalIndex],
                    LineNumber = mergedIndex + 1,
                });
                originalIndex++;
            }

            while (mergedIndex < mergedLines.Length)
            {
                results.Add(new TomlDiffResult
                {
                    DiffType = DiffType.Added,
                    Text = mergedLines[mergedIndex],
                    LineNumber = mergedIndex + 1,
                });
                mergedIndex++;
            }

            return results;
        }

        public enum DiffType
        {
            Unchanged,
            Added,
            Removed,
            Modified,
        }

        public class TomlDiffResult : INotifyPropertyChanged
        {
            private DiffType _diffType;
            private string _text;
            private int _lineNumber;
            private string _componentGuid;

            public DiffType DiffType
            {
                get => _diffType;
                set
                {
                    if (_diffType == value)
                    {
                        return;
                    }

                    _diffType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DiffColor));
                }
            }

            public string Text
            {
                get => _text;
                set
                {
                    if (string.Equals(_text, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _text = value;
                    OnPropertyChanged();
                }
            }

            public int LineNumber
            {
                get => _lineNumber;
                set
                {
                    if (_lineNumber == value)
                    {
                        return;
                    }

                    _lineNumber = value;
                    OnPropertyChanged();
                }
            }

            public string ComponentGuid
            {
                get => _componentGuid;
                set
                {
                    if (string.Equals(_componentGuid, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _componentGuid = value;
                    OnPropertyChanged();
                }
            }

            public IBrush DiffColor
            {
                get
                {
                    switch (DiffType)
                    {
                        case DiffType.Added:
                            return ThemeResourceHelper.MergeDiffAddedBrush;
                        case DiffType.Removed:
                            return ThemeResourceHelper.MergeDiffRemovedBrush;
                        case DiffType.Modified:
                            return ThemeResourceHelper.MergeDiffModifiedBrush;
                        case DiffType.Unchanged:
                        default:
                            return ThemeResourceHelper.MergeDiffUnchangedBrush;
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public enum ComponentConflictStatus
        {
            New,
            ExistingOnly,
            Matched,
            Updated,
        }

        public class ComponentConflictItem : INotifyPropertyChanged
        {
            private bool _isSelected;
            private bool _isVisuallySelected;
            private ComponentConflictStatus _status;
            private string _statusIcon;
            private IBrush _statusColor;
            private bool _hasGuidConflict;
            private string _guidConflictTooltip;

            public ComponentConflictItem([NotNull] ModComponent component, bool isFromExisting,
                ComponentConflictStatus status)
            {
                ModComponent = component;
                Name = component.Name;
                Author = string.IsNullOrWhiteSpace(component.Author) ? "Unknown Author" : component.Author;
                DateInfo = "Modified: N/A";
                SizeInfo = $"{component.Instructions.Count} instruction(s)";
                IsFromExisting = isFromExisting;
                _status = status;
                _statusIcon = GetStatusIcon(status);
                _statusColor = GetStatusColor(status);
            }

            public ModComponent ModComponent { get; }
            public string Name { get; }
            public string Author { get; }
            public string DateInfo { get; }
            public string SizeInfo { get; }
            public bool IsFromExisting { get; }

            public ComponentConflictStatus Status
            {
                get => _status;
                private set
                {
                    if (_status == value)
                    {
                        return;
                    }

                    _status = value;
                    OnPropertyChanged();
                }
            }

            public string StatusIcon
            {
                get => _statusIcon;
                private set
                {
                    if (string.Equals(_statusIcon, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _statusIcon = value;
                    OnPropertyChanged();
                }
            }

            public IBrush StatusColor
            {
                get => _statusColor;
                private set
                {
                    if (Equals(_statusColor, value))
                    {
                        return;
                    }

                    _statusColor = value;
                    OnPropertyChanged();
                }
            }

            public bool IsVisuallySelected
            {
                get => _isVisuallySelected;
                set
                {
                    if (_isVisuallySelected == value)
                    {
                        return;
                    }

                    _isVisuallySelected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectionBorderBrush));
                    OnPropertyChanged(nameof(SelectionBackground));
                }
            }

            public IBrush SelectionBorderBrush => _isVisuallySelected
                ? ThemeResourceHelper.MergeSelectionBorderBrush
                : Brushes.Transparent;

            public IBrush SelectionBackground => _isVisuallySelected
                ? ThemeResourceHelper.MergeSelectionBackgroundBrush
                : Brushes.Transparent;

            public bool HasGuidConflict
            {
                get => _hasGuidConflict;
                set
                {
                    if (_hasGuidConflict == value)
                    {
                        return;
                    }

                    _hasGuidConflict = value;
                    OnPropertyChanged();
                }
            }

            public string GuidConflictTooltip
            {
                get => _guidConflictTooltip;
                set
                {
                    if (string.Equals(_guidConflictTooltip, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _guidConflictTooltip = value;
                    OnPropertyChanged();
                }
            }

            public string RichTooltip
            {
                get
                {
                    var sb = new System.Text.StringBuilder();
                    ModComponent component = ModComponent;

                    _ = sb.Append("📦 ").Append(component.Name).AppendLine();
                    _ = sb.AppendLine();

                    if (!string.IsNullOrEmpty(component.Author))
                    {
                        _ = sb.Append("👤 Author: ").Append(component.Author).AppendLine();
                    }

                    if (component.Category.Count > 0)
                    {
                        _ = sb.Append("📁 Category: ").Append(string.Join(", ", component.Category)).AppendLine();
                    }

                    if (!string.IsNullOrEmpty(component.Tier))
                    {
                        _ = sb.Append("⭐ Tier: ").Append(component.Tier).AppendLine();
                    }

                    if (!string.IsNullOrEmpty(component.Description))
                    {
                        _ = sb.AppendLine();
                        _ = sb.AppendLine("📝 Description:");
                        string desc = component.Description.Length > 200
                            ? component.Description.Substring(0, 200) + "..."
                            : component.Description;
                        _ = sb.AppendLine(desc);
                    }

                    _ = sb.AppendLine();
                    _ = sb.Append("🔧 Instructions: ").Append(component.Instructions.Count).AppendLine();

                    if (!string.IsNullOrEmpty(component.InstallationMethod))
                    {
                        _ = sb.Append("⚙️ Method: ").Append(component.InstallationMethod).AppendLine();
                    }

                    if (component.Dependencies.Count > 0)
                    {
                        _ = sb.AppendLine();
                        _ = sb.Append("✓ Requires: ").Append(component.Dependencies.Count).AppendLine(" mod(s)");
                    }

                    if (component.Restrictions.Count > 0)
                    {
                        _ = sb.Append("✗ Conflicts with: ").Append(component.Restrictions.Count).AppendLine(" mod(s)");
                    }

                    if (component.Options.Count > 0)
                    {
                        _ = sb.Append("⚙️ Has ").Append(component.Options.Count).AppendLine(" optional component(s)");
                    }

                    _ = sb.AppendLine();
                    _ = sb.Append("📊 Status: ").Append(Status).AppendLine();

                    if (!component.IsDownloaded)
                    {
                        _ = sb.AppendLine();
                        _ = sb.AppendLine("⚠️ Mod archive not downloaded");
                        if (component.ResourceRegistry.Count > 0)
                        {
                            _ = sb.Append("🔗 Download: ").Append(component.ResourceRegistry.Keys.FirstOrDefault()).AppendLine();
                        }
                    }

                    if (HasGuidConflict && !string.IsNullOrEmpty(GuidConflictTooltip))
                    {
                        _ = sb.AppendLine();
                        _ = sb.AppendLine("⚠️ GUID CONFLICT ⚠️");
                        _ = sb.AppendLine(GuidConflictTooltip);
                    }

                    return sb.ToString();
                }
            }

            public void UpdateStatus(ComponentConflictStatus newStatus)
            {
                if (Status == newStatus)
                {
                    return;
                }

                Status = newStatus;
                StatusIcon = GetStatusIcon(newStatus);
                StatusColor = GetStatusColor(newStatus);
            }

            private static string GetStatusIcon(ComponentConflictStatus status)
            {
                if (status == ComponentConflictStatus.New)
                {
                    return "✨";
                }

                if (status == ComponentConflictStatus.ExistingOnly)
                {
                    return "📦";
                }

                if (status == ComponentConflictStatus.Matched)
                {
                    return "🔄";
                }

                if (status == ComponentConflictStatus.Updated)
                {
                    return "⬆️";
                }

                return "";
            }

            private static IBrush GetStatusColor(ComponentConflictStatus status)
            {
                if (status == ComponentConflictStatus.New)
                {
                    return ThemeResourceHelper.MergeStatusNewBrush;
                }

                if (status == ComponentConflictStatus.ExistingOnly)
                {
                    return ThemeResourceHelper.MergeStatusExistingOnlyBrush;
                }

                if (status == ComponentConflictStatus.Matched)
                {
                    return ThemeResourceHelper.MergeStatusMatchedBrush;
                }

                if (status == ComponentConflictStatus.Updated)
                {
                    return ThemeResourceHelper.MergeStatusUpdatedBrush;
                }

                return ThemeResourceHelper.MergeStatusDefaultBrush;
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

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public class PreviewItem
        {
            public string OrderNumber { get; set; }
            public string Name { get; set; }
            public string Source { get; set; }
            public IBrush SourceColor { get; set; }
            public ModComponent ModComponent { get; set; }
            public string StatusIcon { get; set; }
            public string PositionChange { get; set; }
            public IBrush PositionChangeColor { get; set; }

            public string RichTooltip
            {
                get
                {
                    if (ModComponent is null)
                    {
                        return Name ?? "Unknown Component";
                    }

                    var sb = new System.Text.StringBuilder();
                    _ = sb.Append("📦 ").Append(ModComponent.Name).AppendLine();
                    _ = sb.AppendLine();

                    if (!string.IsNullOrEmpty(ModComponent.Author))
                    {
                        _ = sb.Append("👤 Author: ").Append(ModComponent.Author).AppendLine();
                    }

                    if (ModComponent.Category.Count > 0)
                    {
                        _ = sb.Append("📁 Category: ").Append(string.Join(", ", ModComponent.Category)).AppendLine();
                    }

                    if (!string.IsNullOrEmpty(ModComponent.Tier))
                    {
                        _ = sb.Append("⭐ Tier: ").Append(ModComponent.Tier).AppendLine();
                    }

                    _ = sb.Append("📋 Source: ").Append(Source).AppendLine();

                    if (!string.IsNullOrEmpty(PositionChange))
                    {
                        _ = sb.Append("🔄 Position: ").Append(PositionChange).AppendLine();
                    }

                    return sb.ToString();
                }
            }
        }
    }

    public class JumpToRawViewEventArgs : EventArgs
    {
        public ComponentMergeConflictViewModel.ComponentConflictItem Item { get; set; }
    }

    public class SyncSelectionEventArgs : EventArgs
    {
        public ComponentMergeConflictViewModel.ComponentConflictItem SelectedItem { get; set; }
        public ComponentMergeConflictViewModel.ComponentConflictItem MatchedItem { get; set; }
    }
}
