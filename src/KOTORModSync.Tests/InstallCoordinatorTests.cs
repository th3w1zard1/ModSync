// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KOTORModSync.Core;
using KOTORModSync.Core.Installation;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Services.Checkpoints;

using KOTORModSync.Tests.TestHelpers;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class InstallCoordinatorTests
    {
        private DirectoryInfo _workingDirectory;
        private MainConfig _mainConfigInstance;

        [SetUp]
        public void SetUp()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "KOTORModSyncTests", Guid.NewGuid().ToString("N"));
            _workingDirectory = Directory.CreateDirectory(tempRoot);
            _ = Directory.CreateDirectory(Path.Combine(tempRoot, ModComponent.CheckpointFolderName));

            _mainConfigInstance = new MainConfig
            {
                destinationPath = _workingDirectory,
                sourcePath = _workingDirectory,
                allComponents = new List<ModComponent>(),
            };
            InstallCoordinator.ClearSessionForTests(_workingDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_workingDirectory != null && _workingDirectory.Exists)
            {
                InstallCoordinatorTestsHelper.CleanupTestDirectory(_workingDirectory);
            }
        }

        [Test]
        public async Task InstallCoordinator_CreatesCheckpointAndBackup()
        {
            ModComponent component = TestComponentFactory.CreateComponent("SingleComponent", _workingDirectory);
            _mainConfigInstance.allComponents = new List<ModComponent> { component };

            var coordinator = new InstallCoordinator();
            using (var cts = new CancellationTokenSource())
            {
                ResumeResult resume = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, cts.Token);

                Assert.That(resume.OrderedComponents, Has.Count.EqualTo(1), "Coordinator should return component order");
                string sessionPath = Path.Combine(MainConfig.DestinationPath.FullName, ModComponent.CheckpointFolderName, "install_session.json");
                Assert.That(File.Exists(sessionPath), "Checkpoint state should be written to disk");

                string backupPath = Path.Combine(MainConfig.DestinationPath.FullName, ModComponent.CheckpointFolderName, "last_good_backup.zip");
                Assert.That(File.Exists(backupPath), Is.True, "Checkpoint manager should create backup snapshot");
            }
        }

        [Test]
        public async Task CheckpointManager_Persists_ComponentState_BetweenRuns()
        {
            ModComponent component = TestComponentFactory.CreateComponent("ResumeComponent", _workingDirectory);
            _mainConfigInstance.allComponents = new List<ModComponent> { component };

            var coordinator = new InstallCoordinator();
            _ = await coordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            component.InstallState = ModComponent.ComponentInstallState.Completed;
            coordinator.CheckpointManager.UpdateComponentState(component);
            await coordinator.CheckpointManager.SaveAsync();

            var secondCoordinator = new InstallCoordinator();
            ResumeResult resume = await secondCoordinator.InitializeAsync(MainConfig.AllComponents, MainConfig.DestinationPath, CancellationToken.None);

            ModComponent rehydratedComponent = resume.OrderedComponents.Single();
            Assert.That(rehydratedComponent.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed), "ModComponent state should resume from checkpoint");
        }

        [Test]
        public async Task InstallationService_RespectsCheckpointSkippingCompleted()
        {
            ModComponent component1 = TestComponentFactory.CreateComponent("CompletedComponent", _workingDirectory);
            component1.InstallState = ModComponent.ComponentInstallState.Completed;
            ModComponent component2 = TestComponentFactory.CreateComponent("PendingComponent", _workingDirectory);
            _mainConfigInstance.allComponents = new List<ModComponent> { component1, component2 };

            ModComponent.InstallExitCode exitCode = await InstallationService.InstallAllSelectedComponentsAsync(MainConfig.AllComponents, progressCallback: null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success));
                Assert.That(component1.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed));
                Assert.That(component2.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed));
            });
        }

        [Test]
        public async Task InstallationService_InvokesSharedProgressCallbackInInstallOrder()
        {
            ModComponent firstComponent = TestComponentFactory.CreateComponent("FirstComponent", _workingDirectory);
            ModComponent secondComponent = TestComponentFactory.CreateComponent("SecondComponent", _workingDirectory);
            _mainConfigInstance.allComponents = new List<ModComponent> { firstComponent, secondComponent };

            var progressEvents = new List<(int index, int total, string name)>();

            ModComponent.InstallExitCode exitCode = await InstallationService.InstallAllSelectedComponentsAsync(
                MainConfig.AllComponents,
                (currentIndex, total, componentName) => progressEvents.Add((currentIndex, total, componentName)),
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success));
                Assert.That(progressEvents, Has.Count.EqualTo(2), "Progress callback should be invoked once per selected component");
                Assert.That(progressEvents[0], Is.EqualTo((0, 2, "FirstComponent")));
                Assert.That(progressEvents[1], Is.EqualTo((1, 2, "SecondComponent")));
                Assert.That(firstComponent.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed));
                Assert.That(secondComponent.InstallState, Is.EqualTo(ModComponent.ComponentInstallState.Completed));
            });
        }

        [Test]
        public async Task InstallationService_LeavesCheckpointSessionDeletableAfterSuccess()
        {
            ModComponent component = TestComponentFactory.CreateComponent("CleanupComponent", _workingDirectory);
            _mainConfigInstance.allComponents = new List<ModComponent> { component };

            ModComponent.InstallExitCode exitCode = await InstallationService.InstallAllSelectedComponentsAsync(
                MainConfig.AllComponents,
                progressCallback: null,
                CancellationToken.None);

            string workingDirectoryPath = _workingDirectory.FullName;
            string gitDir = CheckpointPaths.GetGitDirectory(workingDirectoryPath);
            bool gitDirectoryExistsBeforeCleanup = Directory.Exists(gitDir);

            Exception cleanupException = null;
            try
            {
                InstallCoordinatorTestsHelper.CleanupTestDirectory(_workingDirectory);
            }
            catch (Exception ex)
            {
                cleanupException = ex;
            }
            finally
            {
                _workingDirectory = null;
            }

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(ModComponent.InstallExitCode.Success));
                Assert.That(gitDirectoryExistsBeforeCleanup, Is.True, "Install path should create the checkpoint repository.");
                Assert.That(cleanupException, Is.Null, "Cleanup helper should not throw after install completion.");
                Assert.That(Directory.Exists(workingDirectoryPath), Is.False, "Cleanup helper should remove the working directory after install completion.");
            });
        }
    }
}
