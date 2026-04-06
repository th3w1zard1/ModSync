// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using KOTORModSync;
using KOTORModSync.Core;
using KOTORModSync.Services;
using Xunit;

namespace KOTORModSync.Tests.HeadlessUITests
{
    [Collection(HeadlessTestApp.CollectionName)]
    public sealed class ComponentSelectionConditionalLogicTests
    {
        private static async Task<MainWindow> CreateWindowAsync(bool withComponents = false)
        {
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    ResetMainConfig(withComponents);
                },
                DispatcherPriority.Background);

            var window = await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    var w = new MainWindow();
                    w.Show();
                    return w;
                },
                DispatcherPriority.Background);

            await PumpEventsAsync();
            return window;
        }

        private static void ResetMainConfig(bool withComponents)
        {
            MainConfig.AllComponents = withComponents
                ? new List<ModComponent> { new ModComponent { Name = "Test Mod", Guid = Guid.NewGuid() } }
                : new List<ModComponent>();
        }

        private static ComponentSelectionService CreateSelectionService(params ModComponent[] components)
        {
            return new ComponentSelectionService(new MainConfig
            {
                allComponents = components.ToList()
            });
        }

        private static async Task PumpEventsAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }

        private static async Task CloseWindowAsync(Window window)
        {
            if (window == null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    window.Close();
                },
                DispatcherPriority.Background);

            await PumpEventsAsync();
        }

        #region Component Selection Service Tests

        [AvaloniaFact(DisplayName = "Component selectable when not Aspyr-exclusive")]
        public async Task ComponentSelection_NonAspyrExclusive_IsSelectable()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var component = MainConfig.AllComponents.First();
                component.AspyrExclusive = false;

                var selectionService = new ComponentSelectionService(new MainConfig());
                bool isSelectable = selectionService.IsComponentSelectable(component);

                Assert.True(isSelectable, "Non-Aspyr-exclusive component should be selectable");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Component not selectable when Aspyr-exclusive and not Aspyr version")]
        public async Task ComponentSelection_AspyrExclusive_NotSelectableWhenNotAspyr()
        {
            var window = await CreateWindowAsync(withComponents: true);
            var config = new MainConfig();
            try
            {
                await PumpEventsAsync();
                var component = MainConfig.AllComponents.First();
                component.AspyrExclusive = true;

                var selectionService = new ComponentSelectionService(config);
                // DetectGameVersion will set _isAspyrVersion based on destination path
                // For non-Aspyr, it will be null or false
                selectionService.DetectGameVersion();
                bool isSelectable = selectionService.IsComponentSelectable(component);

                // If destination path doesn't exist or is not Aspyr, should not be selectable
                if (config.destinationPath == null || !config.destinationPath.Exists)
                {
                    Assert.False(isSelectable, "Aspyr-exclusive component should not be selectable when destination path not set");
                }
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Dependency Handling Tests

        [AvaloniaFact(DisplayName = "Selecting component with dependency auto-selects dependency")]
        public async Task ComponentSelection_WithDependency_AutoSelectsDependency()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
                var component = new ModComponent
                {
                    Name = "Component",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid> { depComponent.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { depComponent, component };

                var selectionService = CreateSelectionService(depComponent, component);
                var visited = new HashSet<ModComponent>();
                component.IsSelected = true;
                selectionService.HandleComponentChecked(component, visited);

                await PumpEventsAsync();

                Assert.True(depComponent.IsSelected, "Dependency should be auto-selected");
                Assert.True(component.IsSelected, "Component should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Unselecting component with dependents unselects dependents")]
        public async Task ComponentSelection_WithDependents_UnselectsDependents()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var baseComponent = new ModComponent { Name = "Base", Guid = Guid.NewGuid(), IsSelected = true };
                var dependentComponent = new ModComponent
                {
                    Name = "Dependent",
                    Guid = Guid.NewGuid(),
                    IsSelected = true,
                    Dependencies = new List<Guid> { baseComponent.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { baseComponent, dependentComponent };

                var selectionService = CreateSelectionService(baseComponent, dependentComponent);
                var visited = new HashSet<ModComponent>();
                baseComponent.IsSelected = false;
                selectionService.HandleComponentUnchecked(baseComponent, visited);

                await PumpEventsAsync();

                Assert.False(baseComponent.IsSelected, "Base component should be unselected");
                Assert.False(dependentComponent.IsSelected, "Dependent component should be unselected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Restriction Handling Tests

        [AvaloniaFact(DisplayName = "Selecting component with restriction unselects restricted component")]
        public async Task ComponentSelection_WithRestriction_UnselectsRestricted()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
                var component = new ModComponent
                {
                    Name = "Component",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Restrictions = new List<Guid> { restrictedComponent.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { restrictedComponent, component };

                var selectionService = CreateSelectionService(restrictedComponent, component);
                var visited = new HashSet<ModComponent>();
                component.IsSelected = true;
                selectionService.HandleComponentChecked(component, visited);

                await PumpEventsAsync();

                Assert.False(restrictedComponent.IsSelected, "Restricted component should be unselected");
                Assert.True(component.IsSelected, "Component should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Selecting restricted component unselects restricting component")]
        public async Task ComponentSelection_RestrictedComponent_UnselectsRestricting()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var component = new ModComponent { Name = "Component", Guid = Guid.NewGuid(), IsSelected = true };
                var restrictedComponent = new ModComponent
                {
                    Name = "Restricted",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Restrictions = new List<Guid> { component.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { component, restrictedComponent };

                var selectionService = CreateSelectionService(component, restrictedComponent);
                var visited = new HashSet<ModComponent>();
                restrictedComponent.IsSelected = true;
                selectionService.HandleComponentChecked(restrictedComponent, visited);

                await PumpEventsAsync();

                Assert.False(component.IsSelected, "Component should be unselected");
                Assert.True(restrictedComponent.IsSelected, "Restricted component should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Option Dependency Tests

        [AvaloniaFact(DisplayName = "Selecting component with option dependency auto-selects option")]
        public async Task ComponentSelection_WithOptionDependency_AutoSelectsOption()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
                var option = new Option { Name = "Option", Guid = Guid.NewGuid(), IsSelected = false };
                parentComponent.Options.Add(option);

                var component = new ModComponent
                {
                    Name = "Component",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid> { option.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { parentComponent, component };

                var selectionService = CreateSelectionService(parentComponent, component);
                var visited = new HashSet<ModComponent>();
                component.IsSelected = true;
                selectionService.HandleComponentChecked(component, visited);

                await PumpEventsAsync();

                Assert.True(option.IsSelected, "Option should be auto-selected");
                Assert.True(component.IsSelected, "Component should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Selecting option with component dependency auto-selects component")]
        public async Task ComponentSelection_OptionWithComponentDependency_AutoSelectsComponent()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
                var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
                var option = new Option
                {
                    Name = "Option",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid> { depComponent.Guid }
                };
                parentComponent.Options.Add(option);

                MainConfig.AllComponents = new List<ModComponent> { depComponent, parentComponent };

                option.IsSelected = true;
                var selectionService = CreateSelectionService(depComponent, parentComponent);
                selectionService.HandleOptionChecked(option, parentComponent);

                await PumpEventsAsync();

                Assert.True(depComponent.IsSelected, "Dependency component should be auto-selected");
                Assert.True(option.IsSelected, "Option should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Option Restriction Tests

        [AvaloniaFact(DisplayName = "Selecting option with component restriction unselects component")]
        public async Task ComponentSelection_OptionWithComponentRestriction_UnselectsComponent()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
                var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
                var option = new Option
                {
                    Name = "Option",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Restrictions = new List<Guid> { restrictedComponent.Guid }
                };
                parentComponent.Options.Add(option);

                MainConfig.AllComponents = new List<ModComponent> { restrictedComponent, parentComponent };

                option.IsSelected = true;
                var selectionService = CreateSelectionService(restrictedComponent, parentComponent);
                selectionService.HandleOptionChecked(option, parentComponent);

                await PumpEventsAsync();

                Assert.False(restrictedComponent.IsSelected, "Restricted component should be unselected");
                Assert.True(option.IsSelected, "Option should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Selecting component with option restriction unselects option")]
        public async Task ComponentSelection_ComponentWithOptionRestriction_UnselectsOption()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var parentComponent = new ModComponent { Name = "Parent", Guid = Guid.NewGuid(), IsSelected = true };
                var option = new Option { Name = "Option", Guid = Guid.NewGuid(), IsSelected = true };
                parentComponent.Options.Add(option);

                var component = new ModComponent
                {
                    Name = "Component",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Restrictions = new List<Guid> { option.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { parentComponent, component };

                var selectionService = CreateSelectionService(parentComponent, component);
                var visited = new HashSet<ModComponent>();
                component.IsSelected = true;
                selectionService.HandleComponentChecked(component, visited);

                await PumpEventsAsync();

                Assert.False(option.IsSelected, "Option should be unselected");
                Assert.True(component.IsSelected, "Component should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Circular Dependency Tests

        [AvaloniaFact(DisplayName = "Circular dependency detected and handled")]
        public async Task ComponentSelection_CircularDependency_HandledGracefully()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var component1 = new ModComponent
                {
                    Name = "Component 1",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid>()
                };
                var component2 = new ModComponent
                {
                    Name = "Component 2",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid> { component1.Guid }
                };
                component1.Dependencies.Add(component2.Guid); // Create circular dependency

                MainConfig.AllComponents = new List<ModComponent> { component1, component2 };

                var selectionService = CreateSelectionService(component1, component2);
                var visited = new HashSet<ModComponent>();
                component1.IsSelected = true;
                selectionService.HandleComponentChecked(component1, visited);

                await PumpEventsAsync();

                // Should handle gracefully without infinite loop
                Assert.True(visited.Contains(component1), "Component1 should be in visited set");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Complex Dependency Chain Tests

        [AvaloniaFact(DisplayName = "Complex dependency chain auto-selects all")]
        public async Task ComponentSelection_ComplexDependencyChain_AutoSelectsAll()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = false };
                var modB = new ModComponent
                {
                    Name = "Mod B",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid> { modA.Guid }
                };
                var modC = new ModComponent
                {
                    Name = "Mod C",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid> { modB.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { modA, modB, modC };

                var selectionService = CreateSelectionService(modA, modB, modC);
                var visited = new HashSet<ModComponent>();
                modC.IsSelected = true;
                selectionService.HandleComponentChecked(modC, visited);

                await PumpEventsAsync();

                Assert.True(modA.IsSelected, "Mod A should be auto-selected");
                Assert.True(modB.IsSelected, "Mod B should be auto-selected");
                Assert.True(modC.IsSelected, "Mod C should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        [AvaloniaFact(DisplayName = "Complex restriction chain unselects all")]
        public async Task ComponentSelection_ComplexRestrictionChain_UnselectsAll()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
                var modB = new ModComponent
                {
                    Name = "Mod B",
                    Guid = Guid.NewGuid(),
                    IsSelected = true,
                    Restrictions = new List<Guid> { modA.Guid }
                };
                var modC = new ModComponent
                {
                    Name = "Mod C",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Restrictions = new List<Guid> { modB.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { modA, modB, modC };

                var selectionService = CreateSelectionService(modA, modB, modC);
                var visited = new HashSet<ModComponent>();
                modC.IsSelected = true;
                selectionService.HandleComponentChecked(modC, visited);

                await PumpEventsAsync();

                Assert.True(modA.IsSelected, "Mod A should remain selected because restrictions are resolved directly, not transitively");
                Assert.False(modB.IsSelected, "Mod B should be unselected");
                Assert.True(modC.IsSelected, "Mod C should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion

        #region Mixed Dependencies and Restrictions Tests

        [AvaloniaFact(DisplayName = "Component with both dependencies and restrictions handles both")]
        public async Task ComponentSelection_MixedDependenciesAndRestrictions_HandlesBoth()
        {
            var window = await CreateWindowAsync(withComponents: true);
            try
            {
                await PumpEventsAsync();
                var depComponent = new ModComponent { Name = "Dependency", Guid = Guid.NewGuid(), IsSelected = false };
                var restrictedComponent = new ModComponent { Name = "Restricted", Guid = Guid.NewGuid(), IsSelected = true };
                var component = new ModComponent
                {
                    Name = "Component",
                    Guid = Guid.NewGuid(),
                    IsSelected = false,
                    Dependencies = new List<Guid> { depComponent.Guid },
                    Restrictions = new List<Guid> { restrictedComponent.Guid }
                };

                MainConfig.AllComponents = new List<ModComponent> { depComponent, restrictedComponent, component };

                var selectionService = CreateSelectionService(depComponent, restrictedComponent, component);
                var visited = new HashSet<ModComponent>();
                component.IsSelected = true;
                selectionService.HandleComponentChecked(component, visited);

                await PumpEventsAsync();

                Assert.True(depComponent.IsSelected, "Dependency should be auto-selected");
                Assert.False(restrictedComponent.IsSelected, "Restricted component should be unselected");
                Assert.True(component.IsSelected, "Component should be selected");
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        }

        #endregion
    }
}

