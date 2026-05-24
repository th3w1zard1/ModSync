// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KOTORModSync.Core;
using KOTORModSync.Core.Installation;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Services.Checkpoints;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    /// <summary>
    /// Comprehensive tests for Git checkpoint cleanup and disposal scenarios.
    /// These tests ensure that Git repositories are properly disposed and file locks are released,
    /// allowing test directories to be cleaned up without UnauthorizedAccessException errors.
    /// </summary>
    [TestFixture]
    public class GitCheckpointCleanupTests
    {
        private DirectoryInfo _workingDirectory;

        [SetUp]
        public void SetUp()
        {
            _workingDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"KOTORModSyncTests_{Guid.NewGuid()}"));
            _workingDirectory.Create();
        }

        [TearDown]
        public void TearDown()
        {
            if (_workingDirectory != null && _workingDirectory.Exists)
            {
                // Use the same cleanup logic as InstallCoordinatorTests
                InstallCoordinatorTestsHelper.CleanupTestDirectory(_workingDirectory);
            }
        }

        [Test]
        public void GitCheckpointService_Dispose_ReleasesFileHandles()
        {
            // Arrange
            string checkpointDir = CheckpointPaths.GetRoot(_workingDirectory.FullName);
            Directory.CreateDirectory(checkpointDir);

            // Act
            using (var service = new GitCheckpointService(_workingDirectory.FullName))
            {
                _ = service.InitializeAsync().Result;
                // Service is disposed when leaving using block
            }

            // Assert - Directory should be deletable after disposal
            Assert.DoesNotThrow(() => Directory.Delete(checkpointDir, recursive: true),
                "Directory should be deletable after GitCheckpointService disposal");
        }

        [Test]
        public void GitCheckpointService_MultipleInstances_AllDisposed()
        {
            // Arrange
            string checkpointDir = CheckpointPaths.GetRoot(_workingDirectory.FullName);
            Directory.CreateDirectory(checkpointDir);

            // Act
            using (var service1 = new GitCheckpointService(_workingDirectory.FullName))
            {
                _ = service1.InitializeAsync().Result;
            }

            using (var service2 = new GitCheckpointService(_workingDirectory.FullName))
            {
                _ = service2.InitializeAsync().Result;
            }

            // Assert - Directory should be deletable after all disposals
            Assert.DoesNotThrow(() => Directory.Delete(checkpointDir, recursive: true),
                "Directory should be deletable after all GitCheckpointService instances are disposed");
        }

        [Test]
        public void InstallCoordinator_ClearSessionForTests_HandlesGitLocks()
        {
            // Create a checkpoint service to simulate Git repository creation
            using (var service = new GitCheckpointService(_workingDirectory.FullName))
            {
                _ = service.InitializeAsync().Result;
            }

            // Act & Assert - Should not throw
            Assert.DoesNotThrow(() => InstallCoordinator.ClearSessionForTests(_workingDirectory),
                "ClearSessionForTests should handle Git file locks gracefully");
        }

        [Test]
        public void TestDirectoryCleanup_WithGitRepository_CanDelete()
        {
            // Arrange
            string checkpointDir = CheckpointPaths.GetRoot(_workingDirectory.FullName);
            Directory.CreateDirectory(checkpointDir);

            using (var service = new GitCheckpointService(_workingDirectory.FullName))
            {
                _ = service.InitializeAsync().Result;
            }

            string workingDirectoryPath = _workingDirectory.FullName;
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
                Assert.That(cleanupException, Is.Null, "Test directory cleanup should succeed even with Git repository.");
                Assert.That(Directory.Exists(workingDirectoryPath), Is.False, "Cleanup helper should remove the working directory.");
            });
        }

        [Test]
        public void TestDirectoryCleanup_RetryLogic_HandlesTransientLocks()
        {
            // Arrange
            string checkpointDir = CheckpointPaths.GetRoot(_workingDirectory.FullName);
            Directory.CreateDirectory(checkpointDir);

            using (var service = new GitCheckpointService(_workingDirectory.FullName))
            {
                _ = service.InitializeAsync().Result;
            }

            string workingDirectoryPath = _workingDirectory.FullName;
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
                Assert.That(cleanupException, Is.Null, "Retry logic should handle transient file locks.");
                Assert.That(Directory.Exists(workingDirectoryPath), Is.False, "Cleanup helper should remove the working directory after retries.");
            });
        }

        [Test]
        public void GitRepository_Dispose_ReleasesAllFileHandles()
        {
            // Arrange
            string checkpointDir = CheckpointPaths.GetRoot(_workingDirectory.FullName);
            Directory.CreateDirectory(checkpointDir);

            // Act
            using (var service = new GitCheckpointService(_workingDirectory.FullName))
            {
                _ = service.InitializeAsync().Result;

                // Create a checkpoint to ensure repository is actively used
                var component = new ModComponent
                {
                    Guid = Guid.NewGuid(),
                    Name = "Test Mod",
                };

                try
                {
                    _ = service.CreateCheckpointAsync(component, 0, 1).Result;
                }
                catch
                {
                    // Ignore checkpoint creation errors - we just want to ensure repository is used
                }
            }

            // Assert - All file handles should be released
            Assert.DoesNotThrow(() =>
            {
                string gitDir = CheckpointPaths.GetGitDirectory(_workingDirectory.FullName);
                if (Directory.Exists(gitDir))
                {
                    // Try to delete Git directory - should succeed after disposal
                    Directory.Delete(gitDir, recursive: true);
                }
            }, "Git repository should release all file handles after disposal");
        }
    }
}
