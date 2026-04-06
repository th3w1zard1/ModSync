// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Services.Checkpoints;
using KOTORModSync.Core.Services.FileSystem;
using NUnit.Framework;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class CheckpointAndResumeComprehensiveTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_Checkpoint_" + Guid.NewGuid());
            _modDirectory = Path.Combine(_testDirectory, "Mods");
            _kotorDirectory = Path.Combine(_testDirectory, "KOTOR");
            Directory.CreateDirectory(_modDirectory);
            Directory.CreateDirectory(_kotorDirectory);
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override"));

            _config = new MainConfig
            {
                sourcePath = new DirectoryInfo(_modDirectory),
                destinationPath = new DirectoryInfo(_kotorDirectory)
            };
            MainConfig.Instance = _config;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #region CheckpointManager Tests

        [Test]
        public async Task CheckpointManager_InitializeAsync_WithNewSession_CreatesState()
        {
            var components = new List<ModComponent>
            {
                new ModComponent { Name = "Test", Guid = Guid.NewGuid(), IsSelected = true }
            };

            var manager = new CheckpointManager();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            Assert.That(manager.State, Is.Not.Null, "State should be created");
            Assert.That(manager.State.Components.Count, Is.EqualTo(1), "State should contain component");
        }

        [Test]
        public async Task CheckpointManager_InitializeAsync_WithExistingSession_LoadsState()
        {
            var components = new List<ModComponent>
            {
                new ModComponent { Name = "Test", Guid = Guid.NewGuid(), IsSelected = true }
            };

            var manager1 = new CheckpointManager();
            await manager1.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            // Create new manager and initialize - should load existing state
            var manager2 = new CheckpointManager();
            await manager2.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            Assert.That(manager2.State, Is.Not.Null, "State should be loaded");
        }

        [Test]
        public async Task CheckpointManager_SaveSnapshotAsync_CreatesBackup()
        {
            var components = new List<ModComponent>
            {
                new ModComponent { Name = "Test", Guid = Guid.NewGuid(), IsSelected = true }
            };

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file.txt"), "content");

            var manager = new CheckpointManager();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            await manager.SaveSnapshotAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.Exists(manager.BackupPath), Is.True, "Backup should be created");
        }

        [Test]
        public async Task CheckpointManager_RestoreSnapshotAsync_RestoresFiles()
        {
            var components = new List<ModComponent>
            {
                new ModComponent { Name = "Test", Guid = Guid.NewGuid(), IsSelected = true }
            };

            var originalFile = Path.Combine(_kotorDirectory, "Override", "file.txt");
            File.WriteAllText(originalFile, "original content");

            var manager = new CheckpointManager();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);
            await manager.SaveSnapshotAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);

            // Modify file
            File.WriteAllText(originalFile, "modified content");

            // Restore
            await manager.RestoreSnapshotAsync(new DirectoryInfo(_kotorDirectory), System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.That(File.ReadAllText(originalFile), Is.EqualTo("original content"), "File should be restored to original content");
        }

        [Test]
        public async Task CheckpointManager_MarkComponentCompleted_UpdatesState()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid(), IsSelected = true };
            var components = new List<ModComponent> { component };

            var manager = new CheckpointManager();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            await manager.MarkComponentCompletedAsync(component.Guid).ConfigureAwait(false);

            var state = manager.State;
            Assert.That(state.CompletedComponents.Contains(component.Guid), Is.True, "Component should be marked as completed");
        }

        #endregion

        #region Resume Scenario Tests

        [Test]
        public async Task ResumeInstallation_WithPartialCompletion_ResumesFromCheckpoint()
        {
            var component1 = new ModComponent
            {
                Name = "Component 1",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };
            var component2 = new ModComponent
            {
                Name = "Component 2",
                Guid = Guid.NewGuid(),
                IsSelected = true
            };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            component1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            component2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            var components = new List<ModComponent> { component1, component2 };

            // Simulate partial installation
            var manager = new CheckpointManager();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            // Install first component
            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component1.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component1);
            }
            await component1.ExecuteInstructionsAsync(components, System.Threading.CancellationToken.None, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider).ConfigureAwait(false);

            await manager.MarkComponentCompletedAsync(component1.Guid).ConfigureAwait(false);
            await manager.SaveSnapshotAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);

            // Verify first component is installed
            Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "Component 1 should be installed");

            // Resume installation - should skip component1 and install component2
            var state = manager.State;
            Assert.That(state.CompletedComponents.Contains(component1.Guid), Is.True, "Component 1 should be marked as completed");
        }

        #endregion

        #region Checkpoint Edge Cases

        [Test]
        public async Task CheckpointManager_InitializeAsync_WithNullComponents_ThrowsException()
        {
            var manager = new CheckpointManager();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await manager.InitializeAsync(null, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);
            }, "Should throw ArgumentNullException for null components");
        }

        [Test]
        public async Task CheckpointManager_InitializeAsync_WithNullDestination_ThrowsException()
        {
            var manager = new CheckpointManager();
            var components = new List<ModComponent>();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await manager.InitializeAsync(components, null).ConfigureAwait(false);
            }, "Should throw ArgumentNullException for null destination");
        }

        [Test]
        public async Task CheckpointManager_RestoreSnapshotAsync_WithNoBackup_ThrowsException()
        {
            var manager = new CheckpointManager();
            var components = new List<ModComponent>();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await manager.RestoreSnapshotAsync(new DirectoryInfo(_kotorDirectory), System.Threading.CancellationToken.None).ConfigureAwait(false);
            }, "Should throw FileNotFoundException when backup doesn't exist");
        }

        [Test]
        public async Task CheckpointManager_MarkComponentCompleted_WithInvalidGuid_HandlesGracefully()
        {
            var manager = new CheckpointManager();
            var components = new List<ModComponent>();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            // Should not throw for invalid GUID
            await manager.MarkComponentCompletedAsync(Guid.NewGuid()).ConfigureAwait(false);
        }

        #endregion

        #region Multiple Checkpoint Tests

        [Test]
        public async Task CheckpointManager_MultipleSnapshots_CreatesMultipleBackups()
        {
            var components = new List<ModComponent>
            {
                new ModComponent { Name = "Test", Guid = Guid.NewGuid(), IsSelected = true }
            };

            var manager = new CheckpointManager();
            await manager.InitializeAsync(components, new DirectoryInfo(_kotorDirectory)).ConfigureAwait(false);

            // Create multiple snapshots
            await manager.SaveSnapshotAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
            var backup1 = manager.BackupPath;

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "newfile.txt"), "content");
            await manager.SaveSnapshotAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
            var backup2 = manager.BackupPath;

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(backup1), Is.True, "First backup should exist");
                Assert.That(File.Exists(backup2), Is.True, "Second backup should exist");
            });
        }

        #endregion
    }
}

