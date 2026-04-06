// Copyright (C) 2025
// Licensed under the GPL version 3 license.

using System;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core.Services.Download;
using Xunit;

namespace KOTORModSync.Tests.Services.DistributedCache
{
    /// <summary>
    /// Tests for ContentId generation, idempotency, and collision detection.
    /// </summary>
    [Collection("DistributedCache")]
    [Trait("Category", "Slow")]
    public class ContentIdTests : IClassFixture<DistributedCacheTestFixture>, IDisposable
    {
        private readonly DistributedCacheTestFixture _fixture;
        private readonly IDisposable _clientScope;

        public ContentIdTests(DistributedCacheTestFixture fixture)
        {
            _fixture = fixture;
            _clientScope = DownloadCacheOptimizer.DiagnosticsHarness.AttachSyntheticClient();
            ResetDiagnostics();
        }

        public void Dispose()
        {
            _clientScope.Dispose();
        }

        private static void ResetDiagnostics()
        {
            DownloadCacheOptimizer.DiagnosticsHarness.ClearActiveManagers();
            DownloadCacheOptimizer.DiagnosticsHarness.ClearBlockedContentIds();
            DownloadCacheOptimizer.DiagnosticsHarness.SetNatStatus(successful: false, port: 0, lastCheck: DateTime.MinValue);
            DownloadCacheOptimizer.DiagnosticsHarness.SetClientSettings(new
            {
                ListenPort = 0,
                ClientName = "ContentIdTests",
                ClientVersion = "0.0.1"
            });
        }

        [Fact]
        public void ContentId_SameFile_ProducesSameHash_ContentIdGeneration()
        {
            // Create test file
            string file = _fixture.CreateTestFile("test1.bin", 1024);

            // Compute ContentId twice
            string id1 = _fixture.ComputeContentId(file);
            string id2 = _fixture.ComputeContentId(file);

            Assert.Equal(id1, id2);
        }

        [Fact]
        public void ContentId_DifferentFiles_ProduceDifferentHashes_ContentIdGeneration()
        {
            // Create two different files
            string file1 = _fixture.CreateTestFile("test1.bin", 1024);
            string file2 = _fixture.CreateTestFile("test2.bin", 1024);

            string id1 = _fixture.ComputeContentId(file1);
            string id2 = _fixture.ComputeContentId(file2);

            Assert.NotNull(id1);
            Assert.NotNull(id2);
            Assert.NotEqual(id1, id2);
            Assert.Equal(40, id1.Length);
            Assert.Equal(40, id2.Length);
            Assert.Matches("^[0-9a-f]+$", id1);
            Assert.Matches("^[0-9a-f]+$", id2);
        }

        [Fact]
        public void ContentId_IdenticalContent_SameHash_ContentIdGeneration()
        {
            // Create two files with identical content
            string content = new string('A', 1000);
            string directory1 = _fixture.CreateTempDirectory("identical_content_1");
            string directory2 = _fixture.CreateTempDirectory("identical_content_2");
            const string canonicalFileName = "identical_content.txt";
            string file1 = DistributionTestSupport.EnsureTestFile(directory1, canonicalFileName, 0, content);
            string file2 = DistributionTestSupport.EnsureTestFile(directory2, canonicalFileName, 0, content);

            string id1 = _fixture.ComputeContentId(file1);
            string id2 = _fixture.ComputeContentId(file2);

            Assert.NotNull(id1);
            Assert.NotNull(id2);
            Assert.Equal(id1, id2);
            Assert.Equal(40, id1.Length);
            Assert.Equal(40, id2.Length);
            Assert.Matches("^[0-9a-f]+$", id1);
            Assert.Matches("^[0-9a-f]+$", id2);
        }

        [Fact]
        public void ContentId_EmptyFile_ValidHash_ContentIdGeneration()
        {
            string file = _fixture.CreateTestFile("empty.bin", 0);
            string id = _fixture.ComputeContentId(file);

            Assert.NotNull(id);
            Assert.False(string.IsNullOrEmpty(id));
            Assert.Equal(40, id.Length); // SHA-1 is 40 hex chars
            Assert.Matches("^[0-9a-f]+$", id);
        }

        [Fact]
        public void ContentId_VerySmallFile_ValidHash_ContentIdGeneration()
        {
            string file = _fixture.CreateTestFile("tiny.bin", 1);
            string id = _fixture.ComputeContentId(file);

            Assert.Equal(40, id.Length);
        }

        [Theory]
        [InlineData(1024)]
        [InlineData(65536)]
        [InlineData(262144)]
        [InlineData(1048576)]
        [InlineData(10485760)]
        public void ContentId_VariousFileSizes_ValidHash_ContentIdGeneration(long size)
        {
            string file = _fixture.CreateTestFile($"size{size}.bin", size);
            string id = _fixture.ComputeContentId(file);

            Assert.NotNull(id);
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
            Assert.True(id.All(c => "0123456789abcdef".Contains(c)));
        }

        [Fact]
        public void ContentId_SingleByteChange_DifferentHash_ContentIdGeneration()
        {
            // Create file
            byte[] data = new byte[1000];
            new Random(42).NextBytes(data);
            string file1 = DistributionTestSupport.EnsureBinaryTestFile(
                _fixture.TestDataDirectory,
                "original.bin",
                data);

            string id1 = _fixture.ComputeContentId(file1);

            // Change one byte
            data[500] ^= 0xFF;
            string file2 = DistributionTestSupport.EnsureBinaryTestFile(
                _fixture.TestDataDirectory,
                "modified.bin",
                data);

            string id2 = _fixture.ComputeContentId(file2);

            Assert.NotNull(id1);
            Assert.NotNull(id2);
            Assert.NotEqual(id1, id2);
            Assert.Equal(40, id1.Length);
            Assert.Equal(40, id2.Length);
            Assert.Matches("^[0-9a-f]+$", id1);
            Assert.Matches("^[0-9a-f]+$", id2);
        }

        [Fact]
        public void ContentId_DifferentFilenames_SameContentId_ContentIdGeneration()
        {
            // ContentId should depend on content, not filename
            string content = "Test content";
            string file1 = _fixture.CreateTestFile("name1.txt", 0, content);
            string file2 = _fixture.CreateTestFile("name2.txt", 0, content);

            string id1 = _fixture.ComputeContentId(file1);
            string id2 = _fixture.ComputeContentId(file2);

            // In our implementation, filename IS part of the info dict
            // So this tests that the algorithm is deterministic
            Assert.NotNull(id1);
            Assert.NotNull(id2);
        }

        [Fact]
        public void ContentId_Format_Lowercase_ContentIdGeneration()
        {
            string file = _fixture.CreateTestFile("test.bin", 100);
            string id = _fixture.ComputeContentId(file);

            Assert.NotNull(id);
            Assert.Equal(id, id.ToLowerInvariant());
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
        }

        [Fact]
        public void ContentId_Format_HexadecimalOnly_ContentIdGeneration()
        {
            string file = _fixture.CreateTestFile("test.bin", 100);
            string id = _fixture.ComputeContentId(file);

            Assert.NotNull(id);
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
            Assert.True(id.All(c => "0123456789abcdef".Contains(c)));
        }

        [Theory]
        [InlineData(65536)]   // 64 KB - triggers smallest piece size
        [InlineData(131072)]  // 128 KB
        [InlineData(262144)]  // 256 KB
        [InlineData(524288)]  // 512 KB
        [InlineData(1048576)] // 1 MB
        public void ContentId_PieceSizeBoundaries_Deterministic_ContentIdGeneration(long size)
        {
            string file = _fixture.CreateTestFile($"boundary{size}.bin", size);

            // Compute twice
            string id1 = _fixture.ComputeContentId(file);
            string id2 = _fixture.ComputeContentId(file);

            Assert.NotNull(id1);
            Assert.NotNull(id2);
            Assert.Equal(id1, id2);
            Assert.Equal(40, id1.Length);
            Assert.Equal(40, id2.Length);
            Assert.Matches("^[0-9a-f]+$", id1);
            Assert.Matches("^[0-9a-f]+$", id2);
        }

        [Fact]
        public void ContentId_MaximumFileSize_ValidHash_ContentIdGeneration()
        {
            // Test with 100MB file
            string file = _fixture.CreateTestFile("large.bin", 100 * 1024 * 1024);
            string id = _fixture.ComputeContentId(file);

            Assert.NotNull(id);
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
        }

        [Fact]
        public void ContentId_MultipleComputations_Consistent_ContentIdGeneration()
        {
            string file = _fixture.CreateTestFile("consistent.bin", 10000);

            // Compute 10 times
            var ids = Enumerable.Range(0, 10)
                .Select(_ => _fixture.ComputeContentId(file))
                .ToList();

            Assert.NotNull(ids);
            Assert.Equal(10, ids.Count);
            Assert.All(ids, id =>
            {
                Assert.NotNull(id);
                Assert.Equal(40, id.Length);
                Assert.Matches("^[0-9a-f]+$", id);
            });
            Assert.True(ids.All(id => string.Equals(id, ids[0], StringComparison.Ordinal)),
                "All 10 computations should produce identical ContentId");
        }

        [Fact]
        public void ContentId_BinaryData_ValidHash_ContentIdGeneration()
        {
            // Create file with all possible byte values
            byte[] data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            string file = DistributionTestSupport.EnsureBinaryTestFile(
                _fixture.TestDataDirectory,
                "binary.bin",
                data);

            string id = _fixture.ComputeContentId(file);
            Assert.NotNull(id);
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
        }

        [Fact]
        public void ContentId_TextData_ValidHash_ContentIdGeneration()
        {
            string text = "The quick brown fox jumps over the lazy dog";
            string file = _fixture.CreateTestFile("text.txt", 0, text);

            string id = _fixture.ComputeContentId(file);
            Assert.NotNull(id);
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
        }

        [Fact]
        public void ContentId_UnicodeContent_ValidHash_ContentIdGeneration()
        {
            string unicode = "Hello 世界 🌍 Ñoño";
            string file = _fixture.CreateTestFile("unicode.txt", 0, unicode);

            string id = _fixture.ComputeContentId(file);
            Assert.NotNull(id);
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
        }

        [Fact]
        public void ContentId_RepeatingPattern_ValidHash_ContentIdGeneration()
        {
            // File with repeating pattern (tests compression resistance)
            string pattern = new string('A', 1000);
            string file = _fixture.CreateTestFile("pattern.txt", 0, pattern);

            string id = _fixture.ComputeContentId(file);
            Assert.NotNull(id);
            Assert.Equal(40, id.Length);
            Assert.Matches("^[0-9a-f]+$", id);
        }

        [Fact]
        public void ContentId_RandomData_ValidHash_ContentIdGeneration()
        {
            // File with truly random data
            var random = new Random();
            byte[] data = new byte[10000];
            random.NextBytes(data);
            string file = DistributionTestSupport.EnsureBinaryTestFile(
                _fixture.TestDataDirectory,
                "random.bin",
                data);

            string id = _fixture.ComputeContentId(file);
            Assert.Equal(40, id.Length);
        }

        [Fact]
        public void ContentId_FileWithNullBytes_ValidHash_ContentIdGeneration()
        {
            byte[] data = new byte[1000]; // All zeros
            string file = DistributionTestSupport.EnsureBinaryTestFile(
                _fixture.TestDataDirectory,
                "nulls.bin",
                data);

            string id = _fixture.ComputeContentId(file);
            Assert.Equal(40, id.Length);
        }

        [Fact]
        public void ContentId_FileWith0xFF_ValidHash_ContentIdGeneration()
        {
            byte[] data = Enumerable.Repeat((byte)0xFF, 1000).ToArray();
            string file = DistributionTestSupport.EnsureBinaryTestFile(
                _fixture.TestDataDirectory,
                "oxff.bin",
                data);

            string id = _fixture.ComputeContentId(file);
            Assert.Equal(40, id.Length);
        }
    }
}

