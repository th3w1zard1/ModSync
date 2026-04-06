// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using KOTORModSync.Core;
using KOTORModSync.Core.Installation;
using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class DependencyResolutionTests
    {
        #region Basic Dependency Ordering

        [Test]
        public void GetOrderedInstallList_SimpleDependency_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid } };

            var components = new List<ModComponent> { modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid));
            Assert.That(ordered[1].Guid, Is.EqualTo(modB.Guid));
        }

        [Test]
        public void GetOrderedInstallList_ChainDependencies_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid } };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modB.Guid } };

            var components = new List<ModComponent> { modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered, Has.Count.EqualTo(3), "Ordered list should contain all three components");
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A (root dependency) should be first");
                Assert.That(ordered[1].Guid, Is.EqualTo(modB.Guid), "Mod B (middle dependency) should be second");
                Assert.That(ordered[2].Guid, Is.EqualTo(modC.Guid), "Mod C (leaf dependent) should be third");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
            });
        }

        [Test]
        public void GetOrderedInstallList_MultipleDependencies_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid() };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid, modB.Guid } };

            var components = new List<ModComponent> { modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered, Has.Count.EqualTo(3), "Ordered list should contain all three components");
                Assert.That(ordered.IndexOf(modC), Is.GreaterThan(ordered.IndexOf(modA)),
                    "Mod C (dependent) should come after Mod A (dependency)");
                Assert.That(ordered.IndexOf(modC), Is.GreaterThan(ordered.IndexOf(modB)),
                    "Mod C (dependent) should come after Mod B (dependency)");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
            });
        }

        #endregion

        #region InstallAfter/InstallBefore Ordering

        [Test]
        public void GetOrderedInstallList_InstallAfter_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), InstallAfter = new List<Guid> { modA.Guid } };

            var components = new List<ModComponent> { modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered, Has.Count.EqualTo(2), "Ordered list should contain both components");
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A (InstallAfter target) should come before Mod B");
                Assert.That(ordered[1].Guid, Is.EqualTo(modB.Guid), "Mod B (InstallAfter) should come after Mod A");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
            });
        }

        [Test]
        public void GetOrderedInstallList_InstallBefore_OrdersCorrectly()
        {
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid() };
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), InstallBefore = new List<Guid> { modB.Guid } };

            var components = new List<ModComponent> { modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered, Has.Count.EqualTo(2), "Ordered list should contain both components");
                Assert.That(ordered[0].Guid, Is.EqualTo(modA.Guid), "Mod A (InstallBefore) should come before Mod B");
                Assert.That(ordered[1].Guid, Is.EqualTo(modB.Guid), "Mod B (InstallBefore target) should come after Mod A");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
            });
        }

        [Test]
        public void GetOrderedInstallList_ComplexOrdering_OrdersCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid } };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), InstallAfter = new List<Guid> { modB.Guid } };
            var modD = new ModComponent { Name = "Mod D", Guid = Guid.NewGuid(), InstallBefore = new List<Guid> { modC.Guid } };

            var components = new List<ModComponent> { modD, modC, modB, modA };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered, Has.Count.EqualTo(4), "Ordered list should contain all four components");
                Assert.That(ordered.IndexOf(modA), Is.LessThan(ordered.IndexOf(modB)),
                    "Mod A should come before Mod B (dependency relationship)");
                Assert.That(ordered.IndexOf(modB), Is.LessThan(ordered.IndexOf(modC)),
                    "Mod B should come before Mod C (InstallAfter relationship)");
                Assert.That(ordered.IndexOf(modD), Is.LessThan(ordered.IndexOf(modC)),
                    "Mod D should come before Mod C (InstallBefore relationship)");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
            });
        }

        #endregion

        #region Restrictions Handling

        [Test]
        public void Component_WithRestriction_BlocksWhenRestrictedSelected()
        {
            var restrictedMod = new ModComponent { Name = "Restricted Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var mod = new ModComponent
            {
                Name = "Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            Assert.Multiple(() =>
            {
                Assert.That(mod, Is.Not.Null, "Mod component should not be null");
                Assert.That(restrictedMod, Is.Not.Null, "Restricted mod component should not be null");
                Assert.That(mod.IsSelected, Is.True, "Mod should be selected");
                Assert.That(restrictedMod.IsSelected, Is.True, "Restricted mod should be selected");
                Assert.That(mod.Restrictions, Is.Not.Null, "Restrictions list should not be null");
                Assert.That(mod.Restrictions, Has.Count.EqualTo(1), "Mod should have one restriction");
                Assert.That(mod.Restrictions, Contains.Item(restrictedMod.Guid), "Mod should restrict against restricted mod");
            });
        }

        [Test]
        public void Component_WithRestriction_AllowsWhenRestrictedNotSelected()
        {
            var restrictedMod = new ModComponent { Name = "Restricted Mod", Guid = Guid.NewGuid(), IsSelected = false };
            var mod = new ModComponent
            {
                Name = "Mod",
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Restrictions = new List<Guid> { restrictedMod.Guid }
            };

            Assert.Multiple(() =>
            {
                Assert.That(mod, Is.Not.Null, "Mod component should not be null");
                Assert.That(restrictedMod, Is.Not.Null, "Restricted mod component should not be null");
                Assert.That(mod.IsSelected, Is.True, "Mod should be selected when restriction is not selected");
                Assert.That(restrictedMod.IsSelected, Is.False, "Restricted mod should not be selected");
                Assert.That(mod.Restrictions, Is.Not.Null, "Restrictions list should not be null");
                Assert.That(mod.Restrictions, Has.Count.EqualTo(1), "Mod should have one restriction");
                Assert.That(mod.Restrictions, Contains.Item(restrictedMod.Guid), "Mod should restrict against restricted mod");
            });
        }

        #endregion

        #region Circular Dependency Handling

        [Test]
        public void GetOrderedInstallList_CircularDependency_StillReturnsAllComponents()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { Guid.NewGuid() } };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid } };
            modA.Dependencies[0] = modB.Guid;

            var components = new List<ModComponent> { modA, modB };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered.Count, Is.EqualTo(2), "Ordered list should contain both components even with circular dependency");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
                Assert.That(modA.Dependencies, Is.Not.Null, "Mod A dependencies should not be null");
                Assert.That(modB.Dependencies, Is.Not.Null, "Mod B dependencies should not be null");
            });
        }

        #endregion

        #region Missing Dependency Handling

        [Test]
        public void GetOrderedInstallList_MissingDependency_IgnoresMissingDependency()
        {
            var missingGuid = Guid.NewGuid();
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { missingGuid } };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid() };

            var components = new List<ModComponent> { modA, modB };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered.Count, Is.EqualTo(2), "Ordered list should contain both components even with missing dependency");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
                Assert.That(modA.Dependencies, Is.Not.Null, "Mod A dependencies should not be null");
                Assert.That(modA.Dependencies, Contains.Item(missingGuid), "Mod A should reference missing dependency");
            });
        }

        #endregion

        #region Blocked Descendants

        [Test]
        public void MarkBlockedDescendants_SingleDependent_BlocksDependent()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid } };

            var components = new List<ModComponent> { modA, modB };
            modA.InstallState = ModComponent.ComponentInstallState.Failed;

            InstallCoordinator.MarkBlockedDescendants(components, modA.Guid);

            Assert.Multiple(() =>
            {
                Assert.That(modA, Is.Not.Null, "Mod A should not be null");
                Assert.That(modB, Is.Not.Null, "Mod B should not be null");
                Assert.That(modA.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Failed),
                    "Mod A should be in Failed state");
                Assert.That(modB.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Blocked),
                    "Mod B should be blocked when its dependency (Mod A) fails");
                Assert.That(modB.Dependencies, Is.Not.Null, "Mod B dependencies should not be null");
                Assert.That(modB.Dependencies, Contains.Item(modA.Guid), "Mod B should depend on Mod A");
            });
        }

        [Test]
        public void MarkBlockedDescendants_ChainDependents_BlocksAllDependents()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid } };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modB.Guid } };

            var components = new List<ModComponent> { modA, modB, modC };
            modA.InstallState = ModComponent.ComponentInstallState.Failed;

            InstallCoordinator.MarkBlockedDescendants(components, modA.Guid);

            Assert.Multiple(() =>
            {
                Assert.That(modA, Is.Not.Null, "Mod A should not be null");
                Assert.That(modB, Is.Not.Null, "Mod B should not be null");
                Assert.That(modC, Is.Not.Null, "Mod C should not be null");
                Assert.That(modA.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Failed),
                    "Mod A should be in Failed state");
                Assert.That(modB.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Blocked),
                    "Mod B should be blocked when its dependency (Mod A) fails");
                Assert.That(modC.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Blocked),
                    "Mod C should be blocked when its dependency chain (Mod A -> Mod B) fails");
                Assert.That(modB.Dependencies, Is.Not.Null, "Mod B dependencies should not be null");
                Assert.That(modC.Dependencies, Is.Not.Null, "Mod C dependencies should not be null");
                Assert.That(modB.Dependencies, Contains.Item(modA.Guid), "Mod B should depend on Mod A");
                Assert.That(modC.Dependencies, Contains.Item(modB.Guid), "Mod C should depend on Mod B");
            });
        }

        [Test]
        public void MarkBlockedDescendants_AlreadyCompleted_DoesNotBlock()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid } };

            var components = new List<ModComponent> { modA, modB };
            modA.InstallState = ModComponent.ComponentInstallState.Failed;
            modB.InstallState = ModComponent.ComponentInstallState.Completed;

            InstallCoordinator.MarkBlockedDescendants(components, modA.Guid);

            Assert.Multiple(() =>
            {
                Assert.That(modA, Is.Not.Null, "Mod A should not be null");
                Assert.That(modB, Is.Not.Null, "Mod B should not be null");
                Assert.That(modA.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Failed),
                    "Mod A should be in Failed state");
                Assert.That(modB.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed),
                    "Mod B should remain Completed even when dependency fails (already completed)");
                Assert.That(modB.Dependencies, Is.Not.Null, "Mod B dependencies should not be null");
                Assert.That(modB.Dependencies, Contains.Item(modA.Guid), "Mod B should depend on Mod A");
            });
        }

        #endregion

        #region Option Dependencies

        [Test]
        public void Option_WithDependency_RespectsDependency()
        {
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { option1.Guid }, IsSelected = true };

            component.Options.Add(option1);
            component.Options.Add(option2);

            Assert.Multiple(() =>
            {
                Assert.That(component, Is.Not.Null, "Component should not be null");
                Assert.That(option1, Is.Not.Null, "Option 1 should not be null");
                Assert.That(option2, Is.Not.Null, "Option 2 should not be null");
                Assert.That(option1.IsSelected, Is.True, "Option 1 should be selected");
                Assert.That(option2.IsSelected, Is.True, "Option 2 should be selected when its dependency (Option 1) is selected");
                Assert.That(option2.Dependencies, Is.Not.Null, "Option 2 dependencies should not be null");
                Assert.That(option2.Dependencies, Contains.Item(option1.Guid), "Option 2 should depend on Option 1");
                Assert.That(component.Options, Has.Count.EqualTo(2), "Component should have 2 options");
            });
        }

        [Test]
        public void Option_WithRestriction_BlocksWhenRestrictedSelected()
        {
            var component = new ModComponent { Name = "Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Option 2", Guid = Guid.NewGuid(), Restrictions = new List<Guid> { option1.Guid }, IsSelected = true };

            component.Options.Add(option1);
            component.Options.Add(option2);

            Assert.Multiple(() =>
            {
                Assert.That(component, Is.Not.Null, "Component should not be null");
                Assert.That(option1, Is.Not.Null, "Option 1 should not be null");
                Assert.That(option2, Is.Not.Null, "Option 2 should not be null");
                Assert.That(option1.IsSelected, Is.True, "Option 1 should be selected");
                Assert.That(option2.IsSelected, Is.True, "Option 2 should be selected");
                Assert.That(option2.Restrictions, Is.Not.Null, "Option 2 restrictions should not be null");
                Assert.That(option2.Restrictions, Contains.Item(option1.Guid), "Option 2 should restrict against Option 1");
                Assert.That(component.Options, Has.Count.EqualTo(2), "Component should have 2 options");
            });
        }

        #endregion

        #region Edge Cases

        [Test]
        public void GetOrderedInstallList_EmptyList_ReturnsEmpty()
        {
            var components = new List<ModComponent>();
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered, Is.Empty, "Empty component list should return empty ordered list");
                Assert.That(components, Is.Empty, "Input components list should be empty");
            });
        }

        [Test]
        public void GetOrderedInstallList_SingleComponent_ReturnsSingle()
        {
            var mod = new ModComponent { Name = "Mod", Guid = Guid.NewGuid() };
            var components = new List<ModComponent> { mod };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered.Count, Is.EqualTo(1), "Single component list should return single-item ordered list");
                Assert.That(ordered[0].Guid, Is.EqualTo(mod.Guid), "Ordered list should contain the single component");
                Assert.That(ordered[0], Is.EqualTo(mod), "Ordered list should contain the same component instance");
                Assert.That(components, Has.Count.EqualTo(1), "Input components list should have one component");
            });
        }

        [Test]
        public void GetOrderedInstallList_NoDependencies_PreservesOrder()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid() };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid() };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid() };

            var components = new List<ModComponent> { modA, modB, modC };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Is.Not.Null, "Ordered list should not be null");
                Assert.That(ordered.Count, Is.EqualTo(3), "Ordered list should contain all three components");
                Assert.That(ordered.Select(c => c.Guid).ToHashSet(), Is.EquivalentTo(components.Select(c => c.Guid).ToHashSet()),
                    "Ordered list should contain all components");
                Assert.That(modA.Dependencies, Is.Null.Or.Empty, "Mod A should have no dependencies");
                Assert.That(modB.Dependencies, Is.Null.Or.Empty, "Mod B should have no dependencies");
                Assert.That(modC.Dependencies, Is.Null.Or.Empty, "Mod C should have no dependencies");
            });
        }

        #endregion
    }
}

