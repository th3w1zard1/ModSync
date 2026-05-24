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
using KOTORModSync.Core.Services.Checkpoints;

namespace KOTORModSync.Tests
{
    /// <summary>
    /// Helper class for test cleanup operations, extracted from InstallCoordinatorTests
    /// to allow reuse across multiple test classes.
    /// </summary>
    public static class InstallCoordinatorTestsHelper
    {
        /// <summary>
        /// Cleans up a test directory, handling Git file locks with retry logic.
        /// </summary>
        public static void CleanupTestDirectory(DirectoryInfo directory)
        {
            if (directory == null || !directory.Exists)
            {
                return;
            }

            // Clear persisted checkpoint/session state before filesystem cleanup.
            InstallCoordinator.ClearSessionForTests(directory);

            // Force garbage collection to help release file handles
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);

            // Retry deletion with exponential backoff to handle Git file locks
            const int maxRetries = 15;
            const int baseDelayMs = 100;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    // On retries, try to clean up Git files more aggressively
                    if (attempt > 0)
                    {
                        CleanupGitFiles(directory.FullName, attempt);

                        // Additional GC after cleanup attempts
                        if (attempt % 3 == 0)
                        {
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                            GC.WaitForPendingFinalizers();
                        }
                    }

                    Directory.Delete(directory.FullName, recursive: true);
                    return; // Success
                }
                catch (UnauthorizedAccessException) when (attempt < maxRetries - 1)
                {
                    // Exponential backoff: baseDelayMs * 2^attempt, capped at 2 seconds
                    int delayMs = Math.Min(baseDelayMs * (1 << attempt), 2000);
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    // Exponential backoff: baseDelayMs * 2^attempt, capped at 2 seconds
                    int delayMs = Math.Min(baseDelayMs * (1 << attempt), 2000);
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
        }

        /// <summary>
        /// Aggressively cleans up Git files that might be locked.
        /// </summary>
        private static void CleanupGitFiles(string directoryPath, int attempt)
        {
            try
            {
                string gitDir = CheckpointPaths.GetGitDirectory(directoryPath);
                if (!Directory.Exists(gitDir))
                {
                    return;
                }

                // Remove read-only attributes from all files
                foreach (string file in Directory.GetFiles(gitDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                        // Ignore individual file attribute errors
                    }
                }

                // On later attempts, try to delete individual files
                if (attempt >= 3)
                {
                    foreach (string file in Directory.GetFiles(gitDir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // Ignore individual file deletion errors
                        }
                    }

                    // Try to delete empty directories
                    foreach (string dir in Directory.GetDirectories(gitDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    {
                        try
                        {
                            if (Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                            {
                                Directory.Delete(dir, recursive: false);
                            }
                        }
                        catch
                        {
                            // Ignore directory deletion errors
                        }
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
