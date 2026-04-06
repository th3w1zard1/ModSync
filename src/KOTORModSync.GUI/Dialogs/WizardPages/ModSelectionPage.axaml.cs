// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using JetBrains.Annotations;
using KOTORModSync;
using KOTORModSync.Core;

namespace KOTORModSync.Dialogs.WizardPages
{
    public partial class ModSelectionPage : WizardPageBase
    {
        private readonly List<ModComponent> _allComponents;
        private readonly List<CheckBox> _modCheckBoxes = new List<CheckBox>();
        private readonly Dictionary<string, Expander> _categoryExpanders = new Dictionary<string, Expander>(StringComparer.Ordinal);
        private TextBlock _selectionCountText;
        private TextBlock _selectionDetailsText;
        private TextBlock _filterSummaryText;
        private Button _selectAllButton;
        private Button _deselectAllButton;
        private Button _selectByTierButton;
        private Button _selectByCategoryButton;
        private Button _expandCollapseAllButton;
        private Button _clearFiltersButton;
        private TextBox _searchTextBox;
        private ComboBox _categoryFilterComboBox;
        private ComboBox _tierFilterComboBox;
        private ToggleSwitch _spoilerFreeToggle;
        private StackPanel _modListPanel;
        private TextBlock _selectedCountBadge;
        private TextBlock _requiredCountBadge;
        private TextBlock _optionalCountBadge;

        private string _currentSearchText = string.Empty;
        private string _currentCategoryFilter = null;
        private string _currentTierFilter = null;
        private bool _spoilerFreeMode;
        private bool _allExpanded = false;
        private MainWindow _parentWindow;

        public ModSelectionPage()
            : this(new List<ModComponent>(), parentWindow: null)
        {
        }

        public ModSelectionPage([NotNull][ItemNotNull] List<ModComponent> allComponents, [CanBeNull] MainWindow parentWindow = null)
        {
            _allComponents = allComponents ?? throw new ArgumentNullException(nameof(allComponents));
            _parentWindow = parentWindow;

            InitializeComponent();
            InitializeControls();

            // Sync spoiler-free mode with parent window if available
            if (_parentWindow != null)
            {
                _spoilerFreeMode = _parentWindow.SpoilerFreeMode;
                if (_spoilerFreeToggle != null)
                {
                    _spoilerFreeToggle.IsChecked = _spoilerFreeMode;
                }

                // Subscribe to changes in parent window's spoiler-free mode
                _parentWindow.GetObservable(MainWindow.SpoilerFreeModeProperty).Subscribe(value =>
                {
                    _spoilerFreeMode = value;
                    if (_spoilerFreeToggle != null)
                    {
                        _spoilerFreeToggle.IsChecked = _spoilerFreeMode;
                    }
                    RebuildModList();
                });
            }

            PopulateFilters();
            BuildModList();
            UpdateSelectionCount();

            // Update expand/collapse button text
            if (_expandCollapseAllButton != null)
            {
                _expandCollapseAllButton.Content = _allExpanded ? "▲ Collapse All" : "▼ Expand All";
            }
        }

        public override string Title => "Mod Selection";

        public override string Subtitle => "Choose the mods you want to install";

        public override Task OnNavigatedToAsync(CancellationToken cancellationToken)
        {
            UpdateSelectionCount();
            UpdateFilterSummary();
            return Task.CompletedTask;
        }

        public override Task<(bool isValid, string errorMessage)> ValidateAsync(CancellationToken cancellationToken)
        {
            int selectedCount = _allComponents.Count(c => c.IsSelected && !c.WidescreenOnly);
            if (selectedCount == 0)
            {
                return Task.FromResult((false, "Please select at least one mod to install."));
            }

            return Task.FromResult((true, (string)null));
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _selectionCountText = this.FindControl<TextBlock>("SelectionCountText");
            _selectionDetailsText = this.FindControl<TextBlock>("SelectionDetailsText");
            _filterSummaryText = this.FindControl<TextBlock>("FilterSummaryText");
            _selectAllButton = this.FindControl<Button>("SelectAllButton");
            _deselectAllButton = this.FindControl<Button>("DeselectAllButton");
            _selectByTierButton = this.FindControl<Button>("SelectByTierButton");
            _selectByCategoryButton = this.FindControl<Button>("SelectByCategoryButton");
            _expandCollapseAllButton = this.FindControl<Button>("ExpandCollapseAllButton");
            _clearFiltersButton = this.FindControl<Button>("ClearFiltersButton");
            _searchTextBox = this.FindControl<TextBox>("SearchTextBox");
            _categoryFilterComboBox = this.FindControl<ComboBox>("CategoryFilterComboBox");
            _tierFilterComboBox = this.FindControl<ComboBox>("TierFilterComboBox");
            _spoilerFreeToggle = this.FindControl<ToggleSwitch>("SpoilerFreeToggle");
            _modListPanel = this.FindControl<StackPanel>("ModListPanel");
            _selectedCountBadge = this.FindControl<TextBlock>("SelectedCountBadge");
            _requiredCountBadge = this.FindControl<TextBlock>("RequiredCountBadge");
            _optionalCountBadge = this.FindControl<TextBlock>("OptionalCountBadge");
        }

        private void InitializeControls()
        {
            if (_selectAllButton != null)
            {
                _selectAllButton.Click += (_, __) => SelectAllVisibleMods();
            }

            if (_deselectAllButton != null)
            {
                _deselectAllButton.Click += (_, __) => DeselectAllMods();
            }

            if (_clearFiltersButton != null)
            {
                _clearFiltersButton.Click += (_, __) => ClearAllFilters();
            }

            if (_searchTextBox != null)
            {
                _searchTextBox.TextChanged += (_, __) =>
                {
                    _currentSearchText = _searchTextBox.Text ?? string.Empty;
                    RebuildModList();
                };
            }

            if (_categoryFilterComboBox != null)
            {
                _categoryFilterComboBox.SelectionChanged += (_, __) =>
                {
                    _currentCategoryFilter = _categoryFilterComboBox.SelectedItem as string;
                    RebuildModList();
                };
            }

            if (_tierFilterComboBox != null)
            {
                _tierFilterComboBox.SelectionChanged += (_, __) =>
                {
                    _currentTierFilter = _tierFilterComboBox.SelectedItem as string;
                    RebuildModList();
                };
            }

            if (_spoilerFreeToggle != null)
            {
                _spoilerFreeToggle.IsCheckedChanged += (_, __) =>
                {
                    bool newValue = _spoilerFreeToggle.IsChecked == true;
                    if (_spoilerFreeMode != newValue)
                    {
                        _spoilerFreeMode = newValue;

                        // Update parent window's spoiler-free mode if available
                        if (_parentWindow != null)
                        {
                            _parentWindow.SpoilerFreeMode = _spoilerFreeMode;
                        }

                        RebuildModList();
                    }
                };
            }

            if (_selectByTierButton != null)
            {
                _selectByTierButton.Click += async (_, __) => await ShowSelectByTierDialog();
            }

            if (_selectByCategoryButton != null)
            {
                _selectByCategoryButton.Click += async (_, __) => await ShowSelectByCategoryDialog();
            }

            if (_expandCollapseAllButton != null)
            {
                _expandCollapseAllButton.Click += (_, __) => ToggleExpandCollapseAll();
            }
        }

        private void PopulateFilters()
        {
            // Populate category filter
            if (_categoryFilterComboBox != null)
            {
                var categories = _allComponents
                    .Where(c => !c.WidescreenOnly)
                    .SelectMany(c => c.Category ?? new List<string>())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(cat => cat, StringComparer.Ordinal)
                    .ToList();

                _categoryFilterComboBox.Items.Clear();
                _categoryFilterComboBox.Items.Add("All Categories");
                foreach (string category in categories)
                {
                    _categoryFilterComboBox.Items.Add(category);
                }
                _categoryFilterComboBox.SelectedIndex = 0;
            }

            // Populate tier filter
            if (_tierFilterComboBox != null)
            {
                var tiers = _allComponents
                    .Where(c => !c.WidescreenOnly && !string.IsNullOrEmpty(c.Tier))
                    .Select(c => c.Tier)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(tier => tier, StringComparer.Ordinal)
                    .ToList();

                _tierFilterComboBox.Items.Clear();
                _tierFilterComboBox.Items.Add("All Tiers");
                foreach (string tier in tiers)
                {
                    _tierFilterComboBox.Items.Add(tier);
                }
                _tierFilterComboBox.SelectedIndex = 0;
            }
        }

        private void BuildModList()
        {
            if (_modListPanel is null)
            {
                return;
            }

            _modListPanel.Children.Clear();
            _modCheckBoxes.Clear();
            var previousExpanderStates = new Dictionary<string, bool>(StringComparer.Ordinal);

            // Preserve expander states
            foreach (var kvp in _categoryExpanders)
            {
                previousExpanderStates[kvp.Key] = kvp.Value.IsExpanded;
            }
            _categoryExpanders.Clear();

            var filteredMods = GetFilteredMods();
            IOrderedEnumerable<IGrouping<string, ModComponent>> categorizedMods = filteredMods
                .GroupBy(c => c.Category?.FirstOrDefault() ?? "Other", StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (IGrouping<string, ModComponent> categoryGroup in categorizedMods)
            {
                string categoryName = categoryGroup.Key;
                int modCount = categoryGroup.Count();
                int selectedCount = categoryGroup.Count(c => c.IsSelected);

                // Create expandable category section
                var expander = new Expander
                {
                    Margin = new Avalonia.Thickness(0, 4, 0, 4),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    IsExpanded = previousExpanderStates.TryGetValue(categoryName, out bool wasExpanded) ? wasExpanded : _allExpanded,
                };

                // Create header with category name, count, and selection status
                var headerGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };

                var categoryNameBlock = new TextBlock
                {
                    Text = categoryName,
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                Grid.SetColumn(categoryNameBlock, 0);
                headerGrid.Children.Add(categoryNameBlock);

                var countBlock = new TextBlock
                {
                    Text = $"({modCount} mods)",
                    FontSize = 13,
                    Opacity = 0.7,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(8, 0, 0, 0),
                };
                Grid.SetColumn(countBlock, 1);
                headerGrid.Children.Add(countBlock);

                if (selectedCount > 0)
                {
                    var selectedBadge = new Border
                    {
                        Padding = new Avalonia.Thickness(6, 2),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Avalonia.Thickness(8, 0, 0, 0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = $"✓ {selectedCount} selected",
                            FontSize = 11,
                            FontWeight = FontWeight.SemiBold,
                        },
                    };
                    Grid.SetColumn(selectedBadge, 2);
                    headerGrid.Children.Add(selectedBadge);
                }

                // Add category-level select/deselect button
                var categorySelectButton = new Button
                {
                    Padding = new Avalonia.Thickness(8, 4),
                    Margin = new Avalonia.Thickness(8, 0, 0, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Content = selectedCount == modCount ? "✗ Deselect All" : "✓ Select All",
                    FontSize = 10,
                };
                ToolTip.SetTip(categorySelectButton, selectedCount == modCount ? "Deselect all mods in this category" : "Select all mods in this category");
                categorySelectButton.Click += (_, __) =>
                {
                    bool shouldSelect = selectedCount < modCount;
                    foreach (var component in categoryGroup)
                    {
                        component.IsSelected = shouldSelect;
                    }
                    RebuildModList();
                };
                Grid.SetColumn(categorySelectButton, 3);
                headerGrid.Children.Add(categorySelectButton);

                expander.Header = headerGrid;

                // Create content panel with mods
                var modsPanel = new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };
                foreach (ModComponent component in categoryGroup.OrderBy(c => c.Name, StringComparer.Ordinal))
                {
                    Border modCard = CreateModCard(component);
                    modsPanel.Children.Add(modCard);
                }
                expander.Content = modsPanel;

                _categoryExpanders[categoryName] = expander;
                _modListPanel.Children.Add(expander);
            }

            if (!filteredMods.Any())
            {
                _modListPanel.Children.Add(new Border
                {
                    Padding = new Avalonia.Thickness(24),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = "🔍", FontSize = 32, TextAlignment = TextAlignment.Center },
                            new TextBlock
                            {
                                Text = "No mods match your filters",
                                FontSize = 16,
                                TextAlignment = TextAlignment.Center,
                            },
                            new TextBlock
                            {
                                Text = "Try adjusting your search or filter criteria",
                                FontSize = 13,
                                Opacity = 0.7,
                                TextAlignment = TextAlignment.Center,
                            },
                        },
                    },
                });
            }
        }

        private Border CreateModCard(ModComponent component)
        {
            var card = new Border
            {
                Padding = new Avalonia.Thickness(14, 12),
                Margin = new Avalonia.Thickness(8, 4),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = component,
            };

            // Add hover styles
            card.PointerEntered += (sender, e) =>
            {
                if (sender is Border border)
                {
                    border.Background = ThemeResourceHelper.ModListItemHoverBackgroundBrush;
                    border.BorderBrush = ThemeResourceHelper.ModListItemHoverDefaultBrush;
                }
            };

            card.PointerExited += (sender, e) =>
            {
                if (sender is Border border)
                {
                    border.Background = ThemeResourceHelper.ModListItemDefaultBackgroundBrush;
                    border.BorderBrush = null; // Reset to default
                }
            };

            var mainGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto"),
            };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name row
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Badges row
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) }); // Spacing
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Description row

            // Checkbox
            var checkBox = new CheckBox
            {
                IsChecked = component.IsSelected,
                Tag = component,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };

            checkBox.IsCheckedChanged += (_, __) =>
            {
                if (checkBox.Tag is ModComponent comp)
                {
                    comp.IsSelected = checkBox.IsChecked == true;
                    UpdateSelectionCount();
                }
            };

            _modCheckBoxes.Add(checkBox);
            Grid.SetColumn(checkBox, 0);
            Grid.SetRow(checkBox, 0);
            Grid.SetRowSpan(checkBox, 5); // Span all rows including badges and options
            mainGrid.Children.Add(checkBox);

            // Make entire card clickable to toggle checkbox (after checkbox is created)
            card.PointerPressed += (sender, e) =>
            {
                if (e.Source is CheckBox)
                {
                    return; // Let checkbox handle its own clicks
                }

                e.Handled = true;
                if (sender is Border border && border.Tag is ModComponent comp)
                {
                    comp.IsSelected = !comp.IsSelected;
                    checkBox.IsChecked = comp.IsSelected; // Update checkbox directly
                    UpdateSelectionCount();
                }
            };

            // Mod name (with spoiler-free handling and auto-generation)
            string displayName = GetDisplayName(component, _spoilerFreeMode);

            var nameText = new TextBlock
            {
                Text = displayName,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(nameText, 2);
            Grid.SetRow(nameText, 0);
            mainGrid.Children.Add(nameText);

            // Tier badge (if exists) - moved to badges row
            // Category and Tier badges row
            var badgesGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Avalonia.Thickness(0, 4, 0, 0),
            };

            // Category badges (left side)
            if (component.Category != null && component.Category.Count > 0)
            {
                var categoryBadgesPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 4,
                };

                foreach (string category in component.Category)
                {
                    var categoryBadge = new Border
                    {
                        Padding = new Avalonia.Thickness(6, 2),
                        Margin = new Avalonia.Thickness(0, 0, 4, 0),
                        CornerRadius = new CornerRadius(6),
                    };
                    categoryBadge.Classes.Add("mod-list-item-badge");
                    categoryBadge.Classes.Add("category-badge");
                    var categoryText = new TextBlock
                    {
                        Text = category,
                        FontSize = 10,
                        FontWeight = FontWeight.Medium,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                    };
                    ToolTip.SetTip(categoryText, Core.Utility.CategoryTierDefinitions.GetCategoryDescription(category));
                    categoryBadge.Child = categoryText;
                    categoryBadgesPanel.Children.Add(categoryBadge);
                }
                Grid.SetColumn(categoryBadgesPanel, 0);
                badgesGrid.Children.Add(categoryBadgesPanel);
            }

            // Tier badge (right side)
            if (!string.IsNullOrEmpty(component.Tier))
            {
                var tierBadge = new Border
                {
                    Padding = new Avalonia.Thickness(6, 2),
                    CornerRadius = new CornerRadius(6),
                };
                tierBadge.Classes.Add("mod-list-item-badge");
                tierBadge.Classes.Add("tier-badge");
                var tierText = new TextBlock
                {
                    Text = component.Tier,
                    FontSize = 10,
                    FontWeight = FontWeight.Medium,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                };
                ToolTip.SetTip(tierText, Core.Utility.CategoryTierDefinitions.GetTierDescription(component.Tier));
                tierBadge.Child = tierText;
                Grid.SetColumn(tierBadge, 1);
                badgesGrid.Children.Add(tierBadge);
            }

            Grid.SetColumn(badgesGrid, 2);
            Grid.SetColumnSpan(badgesGrid, 2);
            Grid.SetRow(badgesGrid, 1);
            mainGrid.Children.Add(badgesGrid);

            // Description (simplified, with spoiler-free handling)
            string displayDesc = GetSimplifiedDescription(component, _spoilerFreeMode);
            if (!string.IsNullOrWhiteSpace(displayDesc))
            {
                var descText = new TextBlock
                {
                    Text = displayDesc,
                    FontSize = 13,
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(descText, 2);
                Grid.SetRow(descText, 3);
                Grid.SetColumnSpan(descText, 2);
                mainGrid.Children.Add(descText);
            }

            // Add options if they exist
            if (component.Options != null && component.Options.Count > 0)
            {
                var optionsPanel = new StackPanel
                {
                    Margin = new Avalonia.Thickness(0, 8, 0, 0),
                    Spacing = 4,
                };

                foreach (var option in component.Options)
                {
                    var optionBorder = CreateOptionCard(option);
                    optionsPanel.Children.Add(optionBorder);
                }

                Grid.SetRow(optionsPanel, 4);
                Grid.SetColumn(optionsPanel, 0);
                Grid.SetColumnSpan(optionsPanel, 4);
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                mainGrid.Children.Add(optionsPanel);
            }

            card.Child = mainGrid;

            // Set tooltip on the card itself for better reliability (after child is set)
            string tooltipText = CreateModTooltip(component, _spoilerFreeMode);
            if (!string.IsNullOrWhiteSpace(tooltipText))
            {
                ToolTip.SetTip(card, tooltipText);
            }

            return card;
        }

        private Border CreateOptionCard(Option option)
        {
            var optionBorder = new Border
            {
                Margin = new Avalonia.Thickness(0, 2),
                Padding = new Avalonia.Thickness(8, 4),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = option,
            };

            var optionGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            };

            var optionCheckBox = new CheckBox
            {
                IsChecked = option.IsSelected,
                Tag = option,
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            optionCheckBox.IsCheckedChanged += (_, __) =>
            {
                if (optionCheckBox.Tag is Option opt)
                {
                    opt.IsSelected = optionCheckBox.IsChecked == true;
                    // Update background
                    optionBorder.Background = opt.IsSelected
                        ? ThemeResourceHelper.ModListItemHoverBackgroundBrush
                        : Brushes.Transparent;
                }
            };

            Grid.SetColumn(optionCheckBox, 0);
            optionGrid.Children.Add(optionCheckBox);

            var optionContentPanel = new StackPanel
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            // Option name
            string optionDisplayName = GetDisplayName(option, _spoilerFreeMode);
            var optionNameText = new TextBlock
            {
                Text = optionDisplayName,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                TextWrapping = TextWrapping.Wrap,
            };
            optionContentPanel.Children.Add(optionNameText);

            // Option description (simplified)
            string optionDisplayDesc = GetSimplifiedDescription(option, _spoilerFreeMode);
            if (!string.IsNullOrWhiteSpace(optionDisplayDesc))
            {
                var optionDescText = new TextBlock
                {
                    Text = optionDisplayDesc,
                    FontSize = 11,
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap,
                };
                optionContentPanel.Children.Add(optionDescText);
            }

            Grid.SetColumn(optionContentPanel, 1);
            optionGrid.Children.Add(optionContentPanel);

            optionBorder.Child = optionGrid;

            // Set tooltip on the option card itself (after child is set)
            string optionTooltipText = CreateModTooltip(option, _spoilerFreeMode);
            if (!string.IsNullOrWhiteSpace(optionTooltipText))
            {
                ToolTip.SetTip(optionBorder, optionTooltipText);
            }

            // Set initial background based on selection state
            optionBorder.Background = option.IsSelected
                ? ThemeResourceHelper.ModListItemHoverBackgroundBrush
                : Brushes.Transparent;

            // Add hover styles (after checkbox is created to avoid closure issues)
            optionBorder.PointerEntered += (sender, e) =>
            {
                if (sender is Border border)
                {
                    border.Background = ThemeResourceHelper.ModListItemHoverBackgroundBrush;
                }
            };

            optionBorder.PointerExited += (sender, e) =>
            {
                if (sender is Border border && border.Tag is Option opt)
                {
                    border.Background = opt.IsSelected
                        ? ThemeResourceHelper.ModListItemHoverBackgroundBrush
                        : Brushes.Transparent;
                }
            };

            // Make entire option card clickable to toggle checkbox (after checkbox is created)
            optionBorder.PointerPressed += (sender, e) =>
            {
                if (e.Source is CheckBox)
                {
                    return; // Let checkbox handle its own clicks
                }

                e.Handled = true;
                if (sender is Border border && border.Tag is Option opt)
                {
                    opt.IsSelected = !opt.IsSelected;
                    optionCheckBox.IsChecked = opt.IsSelected; // Update checkbox directly
                    // Update background immediately
                    border.Background = opt.IsSelected
                        ? ThemeResourceHelper.ModListItemHoverBackgroundBrush
                        : Brushes.Transparent;
                }
            };

            return optionBorder;
        }

        private List<ModComponent> GetFilteredMods()
        {
            var filtered = _allComponents.Where(c => !c.WidescreenOnly);

            // Apply category filter
            if (!string.IsNullOrEmpty(_currentCategoryFilter) && !string.Equals(_currentCategoryFilter, "All Categories", StringComparison.Ordinal))
            {
                filtered = filtered.Where(c => c.Category?.Contains(_currentCategoryFilter, StringComparer.OrdinalIgnoreCase) == true);
            }

            // Apply tier filter
            if (!string.IsNullOrEmpty(_currentTierFilter) && !string.Equals(_currentTierFilter, "All Tiers", StringComparison.Ordinal))
            {
                filtered = filtered.Where(c => string.Equals(c.Tier, _currentTierFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(_currentSearchText))
            {
                filtered = filtered.Where(c =>
                {
                    string searchTarget = GetDisplayName(c, _spoilerFreeMode);
                    string descTarget = GetSimplifiedDescription(c, _spoilerFreeMode);

                    return (searchTarget?.IndexOf(_currentSearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (descTarget?.IndexOf(_currentSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
                });
            }

            return filtered.ToList();
        }

        private void RebuildModList()
        {
            BuildModList();
            UpdateSelectionCount();
            UpdateFilterSummary();
            UpdateClearFiltersButton();
        }

        private void SelectAllVisibleMods()
        {
            foreach (CheckBox checkBox in _modCheckBoxes)
            {
                checkBox.IsChecked = true;
            }
            UpdateSelectionCount();
        }

        private void DeselectAllMods()
        {
            foreach (ModComponent component in _allComponents.Where(c => !c.WidescreenOnly))
            {
                component.IsSelected = false;
            }
            BuildModList();
            UpdateSelectionCount();
        }

        private void ClearAllFilters()
        {
            _currentSearchText = string.Empty;
            _currentCategoryFilter = null;
            _currentTierFilter = null;

            if (_searchTextBox != null)
            {
                _searchTextBox.Text = string.Empty;
            }

            if (_categoryFilterComboBox != null)
            {
                _categoryFilterComboBox.SelectedIndex = 0;
            }

            if (_tierFilterComboBox != null)
            {
                _tierFilterComboBox.SelectedIndex = 0;
            }

            RebuildModList();
        }

        private void UpdateSelectionCount()
        {
            var nonWidescreenMods = _allComponents.Where(c => !c.WidescreenOnly).ToList();
            int selectedCount = nonWidescreenMods.Count(c => c.IsSelected);
            int totalCount = nonWidescreenMods.Count;
            int requiredCount = nonWidescreenMods.Count(c => c.IsSelected && c.Dependencies?.Any() == true);
            int optionalCount = selectedCount - requiredCount;

            if (_selectionCountText != null)
            {
                _selectionCountText.Text = $"{selectedCount} of {totalCount} mods selected";
            }

            if (_selectionDetailsText != null)
            {
                if (selectedCount == 0)
                {
                    _selectionDetailsText.Text = "Select the mods you want to install";
                }
                else if (selectedCount == totalCount)
                {
                    _selectionDetailsText.Text = "All mods selected for installation";
                }
                else
                {
                    _selectionDetailsText.Text = $"{totalCount - selectedCount} mods not selected";
                }
            }

            if (_selectedCountBadge != null)
            {
                _selectedCountBadge.Text = selectedCount.ToString();
            }

            if (_requiredCountBadge != null)
            {
                _requiredCountBadge.Text = requiredCount.ToString();
            }

            if (_optionalCountBadge != null)
            {
                _optionalCountBadge.Text = optionalCount.ToString();
            }
        }

        private void UpdateFilterSummary()
        {
            if (_filterSummaryText is null)
            {
                return;
            }

            var activeFilters = new List<string>();

            if (!string.IsNullOrEmpty(_currentSearchText))
            {
                activeFilters.Add($"Search: \"{_currentSearchText}\"");
            }

            if (!string.IsNullOrEmpty(_currentCategoryFilter) && !string.Equals(_currentCategoryFilter, "All Categories", StringComparison.Ordinal))
            {
                activeFilters.Add($"Category: {_currentCategoryFilter}");
            }

            if (!string.IsNullOrEmpty(_currentTierFilter) && !string.Equals(_currentTierFilter, "All Tiers", StringComparison.Ordinal))
            {
                activeFilters.Add($"Tier: {_currentTierFilter}");
            }

            if (_spoilerFreeMode)
            {
                activeFilters.Add("Spoiler-Free Mode");
            }

            var filteredMods = GetFilteredMods();
            int visibleCount = filteredMods.Count;
            int totalCount = _allComponents.Count(c => !c.WidescreenOnly);

            if (activeFilters.Any())
            {
                _filterSummaryText.Text = $"Showing {visibleCount} of {totalCount} mods • Filters: {string.Join(", ", activeFilters)}";
            }
            else
            {
                _filterSummaryText.Text = $"Showing all {totalCount} mods";
            }
        }

        private void UpdateClearFiltersButton()
        {
            if (_clearFiltersButton is null)
            {
                return;
            }

            bool hasActiveFilters = !string.IsNullOrEmpty(_currentSearchText) ||
                                   (!string.IsNullOrEmpty(_currentCategoryFilter) && !string.Equals(_currentCategoryFilter, "All Categories", StringComparison.Ordinal)) ||
                                   (!string.IsNullOrEmpty(_currentTierFilter) && !string.Equals(_currentTierFilter, "All Tiers", StringComparison.Ordinal));

            _clearFiltersButton.IsVisible = hasActiveFilters;
        }

        /// <summary>
        /// Gets the display name for a component based on spoiler-free mode.
        /// Uses the converter's auto-generation logic when spoiler-free property is empty.
        /// </summary>
        private static string GetDisplayName(ModComponent component, bool spoilerFreeMode)
        {
            if (component == null)
            {
                return "Unknown Mod";
            }

            if (spoilerFreeMode)
            {
                // If spoiler-free name is provided, use it
                if (!string.IsNullOrWhiteSpace(component.NameSpoilerFree))
                {
                    return component.NameSpoilerFree;
                }

                // Generate automatic spoiler-free name using the converter
                return Converters.SpoilerFreeContentConverter.GenerateAutoName(component);
            }

            // Return regular name
            return component.Name ?? "Unnamed Mod";
        }

        /// <summary>
        /// Gets a simplified, user-friendly description without verbose metadata.
        /// Verbose information is moved to tooltips.
        /// </summary>
        private static string GetSimplifiedDescription(ModComponent component, bool spoilerFreeMode)
        {
            if (component == null)
            {
                return string.Empty;
            }

            if (spoilerFreeMode)
            {
                // If spoiler-free description is provided, use it (but still simplify if needed)
                if (!string.IsNullOrWhiteSpace(component.DescriptionSpoilerFree))
                {
                    return component.DescriptionSpoilerFree;
                }

                // Generate simplified spoiler-free description
                return GenerateSimplifiedSpoilerFreeDescription(component);
            }

            // Return regular description (truncate if too long)
            string desc = component.Description ?? string.Empty;
            if (desc.Length > 200)
            {
                return desc.Substring(0, 197) + "...";
            }
            return desc;
        }

        /// <summary>
        /// Generates a simplified spoiler-free description without verbose metadata.
        /// </summary>
        private static string GenerateSimplifiedSpoilerFreeDescription(ModComponent component)
        {
            if (component == null)
            {
                return "Mod description available.";
            }

            // Just provide a basic, non-spoiler description
            if (component.Category != null && component.Category.Count > 0)
            {
                string categoryStr = string.Join(", ", component.Category);
                return $"This {categoryStr} modification enhances your gameplay.";
            }

            return "This modification enhances your gameplay.";
        }

        /// <summary>
        /// Creates a comprehensive tooltip with detailed mod information.
        /// </summary>
        private static string CreateModTooltip(ModComponent component, bool spoilerFreeMode)
        {
            if (component == null)
            {
                return "No information available.";
            }

            var tooltipParts = new List<string>();

            // Name
            string displayName = GetDisplayName(component, spoilerFreeMode);
            tooltipParts.Add($"📦 {displayName}");

            // Author
            if (!string.IsNullOrWhiteSpace(component.Author))
            {
                tooltipParts.Add($"👤 Author: {component.Author}");
            }

            // Categories
            if (component.Category != null && component.Category.Count > 0)
            {
                var categoryDescriptions = component.Category
                    .Select(cat => $"• {cat}: {Core.Utility.CategoryTierDefinitions.GetCategoryDescription(cat)}")
                    .ToList();
                tooltipParts.Add($"🏷️ Categories:\n{string.Join("\n", categoryDescriptions)}");
            }

            // Tier
            if (!string.IsNullOrWhiteSpace(component.Tier))
            {
                tooltipParts.Add($"⭐ Tier: {component.Tier} - {Core.Utility.CategoryTierDefinitions.GetTierDescription(component.Tier)}");
            }

            // Description - use spoiler-free handling
            string description;
            if (spoilerFreeMode)
            {
                // If spoiler-free description is provided, use it
                if (!string.IsNullOrWhiteSpace(component.DescriptionSpoilerFree))
                {
                    description = component.DescriptionSpoilerFree;
                }
                else
                {
                    // Generate simplified spoiler-free description
                    description = GenerateSimplifiedSpoilerFreeDescription(component);
                }
            }
            else
            {
                // Regular mode - use actual description
                description = component.Description;
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                tooltipParts.Add($"📝 Description: {description}");
            }

            // Installation details
            var installDetails = new List<string>();
            if (component.Instructions != null && component.Instructions.Count > 0)
            {
                installDetails.Add($"{component.Instructions.Count} installation step(s)");
            }
            if (component.Options != null && component.Options.Count > 0)
            {
                installDetails.Add($"{component.Options.Count} customization option(s)");
            }
            if (!string.IsNullOrWhiteSpace(component.InstallationMethod))
            {
                installDetails.Add($"uses {component.InstallationMethod}");
            }
            if (installDetails.Count > 0)
            {
                tooltipParts.Add($"⚙️ Installation: {string.Join(", ", installDetails)}");
            }

            // Language support
            if (component.Language != null && component.Language.Count > 0)
            {
                string languageStr = string.Join(", ", component.Language);
                tooltipParts.Add($"🌐 Language support: {languageStr}");
            }

            // Download sources
            if (component.ResourceRegistry != null && component.ResourceRegistry.Count > 0)
            {
                tooltipParts.Add($"🔗 {component.ResourceRegistry.Count} download source(s) available");
            }

            return string.Join("\n\n", tooltipParts);
        }

        private void ToggleExpandCollapseAll()
        {
            _allExpanded = !_allExpanded;

            foreach (var expander in _categoryExpanders.Values)
            {
                expander.IsExpanded = _allExpanded;
            }

            if (_expandCollapseAllButton != null)
            {
                _expandCollapseAllButton.Content = _allExpanded ? "▲ Collapse All" : "▼ Expand All";
            }
        }

        private async Task ShowSelectByTierDialog()
        {
            var tiers = _allComponents
                .Where(c => !c.WidescreenOnly && !string.IsNullOrEmpty(c.Tier))
                .Select(c => c.Tier)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(tier => tier, StringComparer.Ordinal)
                .ToList();

            if (tiers.Count == 0)
            {
                return;
            }

            var dialog = new Window
            {
                Title = "Select Mods by Tier",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var mainPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
            };

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Select a tier to select all mods in that tier and higher priority tiers:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 8),
            });

            var tierComboBox = new ComboBox
            {
                ItemsSource = tiers,
                SelectedIndex = 0,
            };
            mainPanel.Children.Add(tierComboBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
            };

            var selectButton = new Button
            {
                Content = "Select",
                Padding = new Avalonia.Thickness(12, 6),
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Avalonia.Thickness(12, 6),
            };

            selectButton.Click += (_, __) =>
            {
                if (tierComboBox.SelectedItem is string selectedTier)
                {
                    SelectByTier(selectedTier);
                    dialog.Close();
                }
            };

            cancelButton.Click += (_, __) => dialog.Close();

            buttonPanel.Children.Add(selectButton);
            buttonPanel.Children.Add(cancelButton);
            mainPanel.Children.Add(buttonPanel);

            dialog.Content = mainPanel;

            var parentWindow = this.FindAncestorOfType<Window>();
            if (parentWindow != null)
            {
                await dialog.ShowDialog(parentWindow);
            }
        }

        private void SelectByTier(string selectedTier)
        {
            // Define tier priorities (lower number = higher priority)
            var tierPriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "1", 1 },
                { "2", 2 },
                { "3", 3 },
                { "4", 4 },
                { "Optional", 5 },
            };

            if (!tierPriorities.TryGetValue(selectedTier, out int selectedPriority))
            {
                // Unknown tier, just select that tier
                foreach (var component in _allComponents.Where(c => !c.WidescreenOnly && string.Equals(c.Tier, selectedTier, StringComparison.OrdinalIgnoreCase)))
                {
                    component.IsSelected = true;
                }
            }
            else
            {
                // Select all tiers with priority <= selected priority
                foreach (var component in _allComponents.Where(c => !c.WidescreenOnly && !string.IsNullOrEmpty(c.Tier)))
                {
                    if (tierPriorities.TryGetValue(component.Tier, out int compPriority) && compPriority <= selectedPriority)
                    {
                        component.IsSelected = true;
                    }
                }
            }

            RebuildModList();
            UpdateSelectionCount();
        }

        private async Task ShowSelectByCategoryDialog()
        {
            var categories = _allComponents
                .Where(c => !c.WidescreenOnly)
                .SelectMany(c => c.Category ?? new List<string>())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(cat => cat, StringComparer.Ordinal)
                .ToList();

            if (categories.Count == 0)
            {
                return;
            }

            var dialog = new Window
            {
                Title = "Select Mods by Category",
                Width = 450,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var mainPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
            };

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Select one or more categories:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 8),
            });

            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 300,
            };
            // ScrollViewer defaults to Auto for VerticalScrollBarVisibility

            var categoryPanel = new StackPanel { Spacing = 4 };
            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string category in categories)
            {
                var checkBox = new CheckBox
                {
                    Content = category,
                    Margin = new Avalonia.Thickness(0, 2),
                };
                checkBox.IsCheckedChanged += (_, __) =>
                {
                    if (checkBox.IsChecked == true)
                    {
                        selectedCategories.Add(category);
                    }
                    else
                    {
                        selectedCategories.Remove(category);
                    }
                };
                categoryPanel.Children.Add(checkBox);
            }

            scrollViewer.Content = categoryPanel;
            mainPanel.Children.Add(scrollViewer);

            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
            };

            var selectButton = new Button
            {
                Content = "Select",
                Padding = new Avalonia.Thickness(12, 6),
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Avalonia.Thickness(12, 6),
            };

            selectButton.Click += (_, __) =>
            {
                if (selectedCategories.Count > 0)
                {
                    SelectByCategories(selectedCategories.ToList());
                    dialog.Close();
                }
            };

            cancelButton.Click += (_, __) => dialog.Close();

            buttonPanel.Children.Add(selectButton);
            buttonPanel.Children.Add(cancelButton);
            mainPanel.Children.Add(buttonPanel);

            dialog.Content = mainPanel;

            var parentWindow = this.FindAncestorOfType<Window>();
            if (parentWindow != null)
            {
                await dialog.ShowDialog(parentWindow);
            }
        }

        private void SelectByCategories(List<string> selectedCategories)
        {
            foreach (var component in _allComponents.Where(c => !c.WidescreenOnly))
            {
                if (component.Category != null && component.Category.Any(cat => selectedCategories.Contains(cat, StringComparer.OrdinalIgnoreCase)))
                {
                    component.IsSelected = true;
                }
            }

            RebuildModList();
            UpdateSelectionCount();
        }
    }
}
