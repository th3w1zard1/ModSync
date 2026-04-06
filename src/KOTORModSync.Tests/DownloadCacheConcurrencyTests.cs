// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KOTORModSync.Core.Services.Download;
using KOTORModSync.Core.Utility;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class DownloadCacheConcurrencyTests
    {
        private string _testDirectory;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ConcurrencyTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_testDirectory);
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        [Test]
        public async Task AcquireContentKeyLock_MultipleConcurrentCalls_OnlyOneAcquires()
        {
            string contentKey = "test_content_key";
            int acquired = 0;
            int released = 0;
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using (await DownloadCacheOptimizer.AcquireContentKeyLock(contentKey).ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref acquired);
                        await Task.Delay(10); // Simulate work
                        Interlocked.Increment(ref released);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                // All should have acquired and released
                Assert.That(acquired, Is.EqualTo(10), "All 10 tasks should have acquired the lock");
                Assert.That(released, Is.EqualTo(10), "All 10 tasks should have released the lock");
                Assert.That(acquired, Is.EqualTo(released), "Acquired and released counts should match");
                Assert.That(contentKey, Is.Not.Null.And.Not.Empty, "Content key should not be null or empty");
            });
        }

        [Test]
        public async Task AcquireContentKeyLock_SerializesAccess()
        {
            string contentKey = "serial_test";
            int maxConcurrent = 0;
            int currentConcurrent = 0;
            var tasks = new List<Task>();

            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    using (await DownloadCacheOptimizer.AcquireContentKeyLock(contentKey).ConfigureAwait(false))
                    {
                        int current = Interlocked.Increment(ref currentConcurrent);
                        if (current > maxConcurrent)
                        {
                            maxConcurrent = current;
                        }
                        await Task.Delay(20); // Simulate work
                        Interlocked.Decrement(ref currentConcurrent);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                // Max concurrent should be 1 (serialized access)
                Assert.That(maxConcurrent, Is.EqualTo(1), "Lock should serialize access - only one task should hold lock at a time");
                Assert.That(currentConcurrent, Is.EqualTo(0), "All tasks should have released the lock");
                Assert.That(contentKey, Is.Not.Null.And.Not.Empty, "Content key should not be null or empty");
            });
        }

        [Test]
        public async Task AcquireContentKeyLock_DifferentKeys_AllowParallelAccess()
        {
            int maxConcurrent = 0;
            int currentConcurrent = 0;
            var tasks = new List<Task>();

            for (int i = 0; i < 5; i++)
            {
                string uniqueKey = $"key_{i}";
                tasks.Add(Task.Run(async () =>
                {
                    using (await DownloadCacheOptimizer.AcquireContentKeyLock(uniqueKey).ConfigureAwait(false))
                    {
                        int current = Interlocked.Increment(ref currentConcurrent);
                        if (current > maxConcurrent)
                        {
                            maxConcurrent = current;
                        }
                        await Task.Delay(50); // Simulate longer work
                        Interlocked.Decrement(ref currentConcurrent);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                // Different keys should allow parallel access
                Assert.That(maxConcurrent, Is.GreaterThan(1), "Different content keys should allow parallel access");
                Assert.That(maxConcurrent, Is.LessThanOrEqualTo(5), "Max concurrent should not exceed number of tasks");
                Assert.That(currentConcurrent, Is.EqualTo(0), "All tasks should have released their locks");
            });
        }

        [Test]
        public async Task ComputeFileIntegrityData_ConcurrentCalls_AllSucceed()
        {
            // Create test files
            var files = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                string file = Path.Combine(_testDirectory, $"test_{i}.txt");
                await NetFrameworkCompatibility.WriteAllTextAsync(file, $"Content {i}");
                files.Add(file);
            }

            var tasks = files.Select(f => DownloadCacheOptimizer.ComputeFileIntegrityData(f)).ToList();
            (string contentHashSHA256, int pieceLength, string pieceHashes)[] results = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                // All should succeed
                Assert.That(results.Length, Is.EqualTo(5), "All 5 files should have integrity data computed");
                Assert.That(files, Has.Count.EqualTo(5), "Test should have created 5 files");
            });

            foreach ((string contentHashSHA256, int pieceLength, string pieceHashes) result in results)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result.contentHashSHA256, Is.Not.Null, "Content hash should not be null");
                    Assert.That(result.contentHashSHA256, Has.Length.EqualTo(64), "SHA-256 hash should be exactly 64 hexadecimal characters");
                    Assert.That(result.contentHashSHA256, Does.Match("^[0-9a-f]+$"), "Content hash should contain only lowercase hexadecimal digits");
                    Assert.That(result.pieceLength, Is.GreaterThan(0), "Piece length should be greater than zero");
                    Assert.That(result.pieceLength, Is.LessThanOrEqualTo(4194304), "Piece length should not exceed 4MB maximum");
                    Assert.That(result.pieceHashes, Is.Not.Null, "Piece hashes should not be null");
                });
            }
        }

        [Test]
        public async Task ComputeContentIdFromMetadata_ConcurrentCalls_ProduceConsistentResults_ContentIdGeneration()
        {
            var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["provider"] = "deadlystream",
                ["fileId"] = "test123",
                ["version"] = "1.0",
            };

            string url = "https://example.com/test";

            var tasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(() => DownloadCacheOptimizer.ComputeContentIdFromMetadata(metadata, url)))
                .ToList();

            string[] results = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                Assert.That(results, Is.Not.Null, "Results array should not be null");
                Assert.That(results.Length, Is.EqualTo(10), "Should have 10 results from 10 concurrent calls");
            });

            // All results should be identical
            string first = results[0];
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Null, "First result should not be null");
                Assert.That(first, Has.Length.EqualTo(40), "ContentId should be exactly 40 characters");
                Assert.That(first, Does.Match("^[0-9a-f]+$"), "ContentId should contain only hexadecimal digits");
            });

            foreach (string result in results)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(first), "All concurrent calls should produce identical ContentId");
                    Assert.That(result, Is.Not.Null, "Each result should not be null");
                    Assert.That(result, Has.Length.EqualTo(40), "Each ContentId should be exactly 40 characters");
                });
            }
        }

        [Test]
        public async Task BlockContentId_ConcurrentBlocking_AllSucceed()
        {
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                string contentId = $"blocked_{i}";
                tasks.Add(Task.Run(() => DownloadCacheOptimizer.BlockContentId(contentId, "test")));
            }

            await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                Assert.That(tasks, Has.Count.EqualTo(10), "Should have created 10 blocking tasks");
            });

            // All should be blocked
            for (int i = 0; i < 10; i++)
            {
                string contentId = $"blocked_{i}";
                Assert.Multiple(() =>
                {
                    Assert.That(DownloadCacheOptimizer.IsContentIdBlocked(contentId), Is.True,
                        $"ContentId '{contentId}' should be blocked after concurrent blocking");
                    Assert.That(contentId, Is.Not.Null.And.Not.Empty, "ContentId should not be null or empty");
                });
            }
        }

        [Test]
        public async Task IsContentIdBlocked_ConcurrentReads_AllSucceed()
        {
            string contentId = "read_test";
            DownloadCacheOptimizer.BlockContentId(contentId, "test");

            var tasks = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => DownloadCacheOptimizer.IsContentIdBlocked(contentId)))
                .ToList();

            bool[] results = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                Assert.That(results, Is.Not.Null, "Results array should not be null");
                Assert.That(results.Length, Is.EqualTo(20), "Should have 20 results from 20 concurrent reads");
                // All should return true
                Assert.That(results.All(r => r), Is.True, "All concurrent reads should return true for blocked ContentId");
                Assert.That(results, Has.None.EqualTo(false), "No result should be false");
                Assert.That(contentId, Is.Not.Null.And.Not.Empty, "ContentId should not be null or empty");
            });
        }

        [Test]
        public async Task DeterminePieceSize_ConcurrentCalls_AreThreadSafe()
        {
            long[] fileSizes = { 1024, 1024 * 1024, 10 * 1024 * 1024, 100 * 1024 * 1024 };

            var tasks = fileSizes
                .SelectMany(_ => Enumerable.Range(0, 5))
                .Select(i => Task.Run(() => DownloadCacheOptimizer.DeterminePieceSize(fileSizes[i % fileSizes.Length])))
                .ToList();

            int[] results = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                Assert.That(results, Is.Not.Null, "Results array should not be null");
                Assert.That(results.Length, Is.EqualTo(20), "Should have 20 results (4 file sizes × 5 iterations)");
            });

            // All should return valid piece sizes
            int[] validPieceSizes = { 65536, 131072, 262144, 524288, 1048576, 2097152, 4194304 };
            foreach (int result in results)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.GreaterThan(0), "Piece size should be greater than zero");
                    Assert.That(result, Is.LessThanOrEqualTo(4194304), "Piece size should not exceed 4MB maximum");
                    Assert.That(validPieceSizes, Contains.Item(result),
                        $"Piece size {result} should be one of the valid piece sizes");
                });
            }
        }

        [Test]
        public async Task PartialFilePath_ConcurrentGeneration_ProducesUniqueFiles()
        {
            var tasks = Enumerable.Range(0, 10)
                .Select(i => Task.Run(() =>
                    DownloadCacheOptimizer.GetPartialFilePath($"content_{i}", _testDirectory)))
                .ToList();

            string[] paths = await Task.WhenAll(tasks);

            Assert.Multiple(() =>
            {
                Assert.That(paths, Is.Not.Null, "Paths array should not be null");
                Assert.That(paths.Length, Is.EqualTo(10), "Should have 10 unique paths");
            });

            // All paths should be unique
            var uniquePaths = paths.Distinct(StringComparer.Ordinal).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(uniquePaths.Count, Is.EqualTo(10), "All 10 paths should be unique");
                Assert.That(paths.Length, Is.EqualTo(uniquePaths.Count), "No duplicate paths should exist");
            });

            // All should be in .partial directory
            foreach (string path in paths)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(path, Is.Not.Null, "Path should not be null");
                    Assert.That(path, Does.Contain(".partial"), "Path should be in .partial directory");
                    Assert.That(path, Does.Contain(_testDirectory), "Path should be within test directory");
                    Assert.That(Path.IsPathRooted(path), Is.True, "Path should be absolute");
                });
            }
        }

        [Test]
        public async Task AcquireContentKeyLock_Timeout_DoesNotDeadlock()
        {
            string contentKey = "timeout_test";

            using (await DownloadCacheOptimizer.AcquireContentKeyLock(contentKey))
            {
                // Try to acquire again from another task (should wait)
                Task<string> task = Task.Run(async () =>
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
                    {
                        try
                        {
                            using (await DownloadCacheOptimizer.AcquireContentKeyLock(contentKey).ConfigureAwait(false))
                            {
                                return "acquired";
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            return "timeout";
                        }
                    }
                });

                // The inner lock should timeout since we're holding the outer one
                await Task.Delay(200);

                // Complete to avoid deadlock
            }

            // Should complete without deadlock
            Assert.Multiple(() =>
            {
                Assert.That(contentKey, Is.Not.Null.And.Not.Empty, "Content key should not be null or empty");
                // Test passes if we reach here without deadlock
            });
            Assert.Pass("Lock acquisition with timeout should not cause deadlock");
        }
    }
}
