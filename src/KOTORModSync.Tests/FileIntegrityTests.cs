// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using KOTORModSync.Core;
using KOTORModSync.Core.Services.Download;
using KOTORModSync.Core.Utility;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class FileIntegrityTests
    {
        private string _testDirectory;

        [SetUp]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_IntegrityTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_testDirectory);
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }

        [Test]
        public async Task ComputeFileIntegrityData_WithSmallFile_ProducesCorrectHashes()
        {
            string testFile = Path.Combine(_testDirectory, "test.txt");
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Hello, World!");

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            Assert.Multiple(() =>
            {
                // SHA-256 should be 64 hex characters
                Assert.That(sha256, Is.Not.Null, "SHA-256 hash should not be null");
                Assert.That(sha256, Has.Length.EqualTo(64), "SHA-256 hash should be exactly 64 hexadecimal characters");
                Assert.That(sha256, Does.Match("^[0-9a-f]+$"), "SHA-256 hash should contain only lowercase hexadecimal digits");
                Assert.That(sha256, Is.Not.Empty, "SHA-256 hash should not be empty");

                // Piece length should be reasonable for small file
                Assert.That(pieceLength, Is.GreaterThan(0), "Piece length should be greater than zero");
                Assert.That(pieceLength, Is.LessThanOrEqualTo(4194304), "Piece length should not exceed 4MB maximum");
                Assert.That(pieceLength, Is.GreaterThanOrEqualTo(65536), "Piece length should be at least 64KB for small files");

                // Piece hashes (40 hex chars per piece)
                Assert.That(pieceHashes, Is.Not.Null, "Piece hashes should not be null");
                Assert.That(pieceHashes.Length % 40, Is.EqualTo(0), "Piece hashes length should be multiple of 40 (20 bytes per piece hash)");
                Assert.That(pieceHashes, Does.Match("^[0-9a-f]*$"), "Piece hashes should contain only hexadecimal digits");

                // File should exist
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
            });
        }

        [Test]
        public async Task ComputeFileIntegrityData_WithLargeFile_UsesLargerPieceSize()
        {
            string testFile = Path.Combine(_testDirectory, "large.bin");
            byte[] largeData = new byte[10 * 1024 * 1024]; // 10 MB
            await NetFrameworkCompatibility.WriteAllBytesAsync(testFile, largeData);

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            Assert.Multiple(() =>
            {
                Assert.That(sha256, Is.Not.Null, "SHA-256 hash should not be null");
                Assert.That(sha256, Has.Length.EqualTo(64), "SHA-256 hash should be exactly 64 hexadecimal characters");
                Assert.That(sha256, Does.Match("^[0-9a-f]+$"), "SHA-256 hash should contain only hexadecimal digits");

                // Piece length should be larger for large files
                Assert.That(pieceLength, Is.GreaterThanOrEqualTo(65536), "Piece length should be at least 64KB for large files");
                Assert.That(pieceLength, Is.LessThanOrEqualTo(4194304), "Piece length should not exceed 4MB maximum");

                // Should have multiple pieces
                Assert.That(pieceHashes, Is.Not.Null, "Piece hashes should not be null");
                Assert.That(pieceHashes.Length % 40, Is.EqualTo(0), "Piece hashes length should be multiple of 40");
                int pieceCount = pieceHashes.Length / 40;
                Assert.That(pieceCount, Is.GreaterThan(1), "Large file should have multiple pieces");
                Assert.That(pieceCount, Is.LessThanOrEqualTo(1048576), "Piece count should not exceed 2^20 maximum");

                // File should exist and have correct size
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
                Assert.That(new FileInfo(testFile).Length, Is.EqualTo(10L * 1024 * 1024), "File should be 10MB");
            });
        }

        [Test]
        public async Task ComputeFileIntegrityData_SameFile_ProducesSameHashes()
        {
            string testFile = Path.Combine(_testDirectory, "deterministic.txt");
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Deterministic content");

            (string contentHashSHA256, int pieceLength, string pieceHashes) result1 = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);
            (string contentHashSHA256, int pieceLength, string pieceHashes) result2 = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            Assert.Multiple(() =>
            {
                Assert.That(result1.contentHashSHA256, Is.Not.Null, "First SHA-256 hash should not be null");
                Assert.That(result2.contentHashSHA256, Is.Not.Null, "Second SHA-256 hash should not be null");
                Assert.That(result1.contentHashSHA256, Is.EqualTo(result2.contentHashSHA256),
                    "Same file should produce identical SHA-256 hashes on multiple computations");
                Assert.That(result1.contentHashSHA256, Has.Length.EqualTo(64), "SHA-256 hash should be 64 characters");

                Assert.That(result1.pieceLength, Is.EqualTo(result2.pieceLength),
                    "Same file should produce identical piece length on multiple computations");
                Assert.That(result1.pieceLength, Is.GreaterThan(0), "Piece length should be greater than zero");

                Assert.That(result1.pieceHashes, Is.Not.Null, "First piece hashes should not be null");
                Assert.That(result2.pieceHashes, Is.Not.Null, "Second piece hashes should not be null");
                Assert.That(result1.pieceHashes, Is.EqualTo(result2.pieceHashes),
                    "Same file should produce identical piece hashes on multiple computations");
                Assert.That(result1.pieceHashes.Length % 40, Is.EqualTo(0), "Piece hashes length should be multiple of 40");

                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
            });
        }

        [Test]
        public async Task VerifyContentIntegrity_WithValidFile_ReturnsTrue()
        {
            string testFile = Path.Combine(_testDirectory, "valid.txt");
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Valid content for verification");

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            var metadata = new ResourceMetadata
            {
                ContentHashSHA256 = sha256,
                PieceLength = pieceLength,
                PieceHashes = pieceHashes,
                FileSize = new FileInfo(testFile).Length,
            };

            bool isValid = await DownloadCacheOptimizer.VerifyContentIntegrity(testFile, metadata);

            Assert.Multiple(() =>
            {
                Assert.That(isValid, Is.True, "Valid file with correct metadata should pass integrity verification");
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
                Assert.That(metadata, Is.Not.Null, "Metadata should not be null");
                Assert.That(metadata.ContentHashSHA256, Is.Not.Null.And.Not.Empty, "Content hash should not be null or empty");
                Assert.That(metadata.PieceLength, Is.GreaterThan(0), "Piece length should be greater than zero");
                Assert.That(metadata.PieceHashes, Is.Not.Null.And.Not.Empty, "Piece hashes should not be null or empty");
            });
        }

        [Test]
        public async Task VerifyContentIntegrity_WithModifiedFile_ReturnsFalse()
        {
            string testFile = Path.Combine(_testDirectory, "modified.txt");
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Original content");

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            var metadata = new ResourceMetadata
            {
                ContentHashSHA256 = sha256,
                PieceLength = pieceLength,
                PieceHashes = pieceHashes,
                FileSize = new FileInfo(testFile).Length,
            };

            // Modify the file
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Modified content");

            bool isValid = await DownloadCacheOptimizer.VerifyContentIntegrity(testFile, metadata);

            Assert.Multiple(() =>
            {
                Assert.That(isValid, Is.False, "Modified file should fail integrity verification");
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
                Assert.That(metadata, Is.Not.Null, "Metadata should not be null");
                // Original metadata should still be valid
                Assert.That(metadata.ContentHashSHA256, Is.Not.Null.And.Not.Empty, "Original content hash should not be null or empty");
            });
        }

        [Test]
        public async Task VerifyContentIntegrity_WithWrongSize_ReturnsFalse()
        {
            string testFile = Path.Combine(_testDirectory, "size_test.txt");
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Size test");

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            var metadata = new ResourceMetadata
            {
                ContentHashSHA256 = sha256,
                PieceLength = pieceLength,
                PieceHashes = pieceHashes,
                FileSize = 99999, // Wrong size
            };

            bool isValid = await DownloadCacheOptimizer.VerifyContentIntegrity(testFile, metadata);

            Assert.Multiple(() =>
            {
                Assert.That(isValid, Is.False, "File with wrong size should fail integrity verification");
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
                Assert.That(metadata, Is.Not.Null, "Metadata should not be null");
                Assert.That(metadata.FileSize, Is.EqualTo(99999), "Metadata should have wrong file size");
                Assert.That(new FileInfo(testFile).Length, Is.Not.EqualTo(metadata.FileSize),
                    "Actual file size should differ from metadata file size");
            });
        }

        [Test]
        public void DeterminePieceSize_WithSmallFile_Returns64KB()
        {
            long fileSize = 1024 * 1024; // 1 MB
            int pieceSize = DownloadCacheOptimizer.DeterminePieceSize(fileSize);

            Assert.Multiple(() =>
            {
                Assert.That(pieceSize, Is.EqualTo(65536), "1MB file should use 64KB piece size");
                Assert.That(pieceSize, Is.GreaterThan(0), "Piece size should be greater than zero");
                Assert.That(pieceSize, Is.LessThanOrEqualTo(4194304), "Piece size should not exceed 4MB maximum");
            });
        }

        [Test]
        public void DeterminePieceSize_WithLargeFile_ReturnsLargerPiece()
        {
            long fileSize = 100L * 1024 * 1024 * 1024; // 100 GB
            int pieceSize = DownloadCacheOptimizer.DeterminePieceSize(fileSize);

            Assert.Multiple(() =>
            {
                // Should use larger piece size for huge files
                Assert.That(pieceSize, Is.GreaterThanOrEqualTo(131072), "100GB file should use at least 128KB piece size");
                Assert.That(pieceSize, Is.GreaterThan(0), "Piece size should be greater than zero");
                Assert.That(pieceSize, Is.LessThanOrEqualTo(4194304), "Piece size should not exceed 4MB maximum");

                // Verify piece count constraint
                long pieceCount = (fileSize + pieceSize - 1) / pieceSize;
                Assert.That(pieceCount, Is.LessThanOrEqualTo(1048576), "Piece count should not exceed 2^20 maximum");
            });
        }

        [Test]
        public void DeterminePieceSize_EnsuresMaxPieceCount()
        {
            long fileSize = 10L * 1024 * 1024 * 1024; // 10 GB
            int pieceSize = DownloadCacheOptimizer.DeterminePieceSize(fileSize);

            long pieceCount = (fileSize + pieceSize - 1) / pieceSize;

            Assert.Multiple(() =>
            {
                // Must not exceed 2^20 pieces (1,048,576)
                Assert.That(pieceCount, Is.LessThanOrEqualTo(1048576), "Piece count should not exceed 2^20 (1,048,576) maximum");
                Assert.That(pieceCount, Is.GreaterThan(0), "Piece count should be greater than zero");
                Assert.That(pieceSize, Is.GreaterThan(0), "Piece size should be greater than zero");
                Assert.That(pieceSize, Is.LessThanOrEqualTo(4194304), "Piece size should not exceed 4MB maximum");
            });
        }

        [Test]
        public async Task VerifyPieceHashesFromStored_WithValidPieces_ReturnsTrue()
        {
            string testFile = Path.Combine(_testDirectory, "piece_test.txt");
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Content for piece verification");

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            bool isValid = await DownloadCacheOptimizer.VerifyPieceHashesFromStored(testFile, pieceLength, pieceHashes);

            Assert.Multiple(() =>
            {
                Assert.That(isValid, Is.True, "Valid file with correct piece hashes should pass verification");
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
                Assert.That(pieceLength, Is.GreaterThan(0), "Piece length should be greater than zero");
                Assert.That(pieceHashes, Is.Not.Null.And.Not.Empty, "Piece hashes should not be null or empty");
                Assert.That(pieceHashes.Length % 40, Is.EqualTo(0), "Piece hashes length should be multiple of 40");
            });
        }

        [Test]
        public async Task VerifyPieceHashesFromStored_WithCorruptedPiece_ReturnsFalse()
        {
            string testFile = Path.Combine(_testDirectory, "corrupt_piece.txt");
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Original piece data");

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            // Corrupt the file
            await NetFrameworkCompatibility.WriteAllTextAsync(testFile, "Corrupted piece data");

            bool isValid = await DownloadCacheOptimizer.VerifyPieceHashesFromStored(testFile, pieceLength, pieceHashes);

            Assert.Multiple(() =>
            {
                Assert.That(isValid, Is.False, "Corrupted file should fail piece hash verification");
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
                Assert.That(pieceLength, Is.GreaterThan(0), "Piece length should be greater than zero");
                Assert.That(pieceHashes, Is.Not.Null.And.Not.Empty, "Original piece hashes should not be null or empty");
            });
        }

        [Test]
        public async Task ComputeFileIntegrityData_WithEmptyFile_ProducesValidHashes()
        {
            string testFile = Path.Combine(_testDirectory, "empty.txt");
            // Use WriteAllBytesAsync with empty array to ensure truly empty file (no BOM/encoding)
            await NetFrameworkCompatibility.WriteAllBytesAsync(testFile, Array.Empty<byte>());

            (string sha256, int pieceLength, string pieceHashes) = await DownloadCacheOptimizer.ComputeFileIntegrityData(testFile);

            Assert.Multiple(() =>
            {
                Assert.That(sha256, Is.Not.Null, "SHA-256 hash for empty file should not be null");
                Assert.That(sha256, Has.Length.EqualTo(64), "SHA-256 hash should be exactly 64 characters");
                Assert.That(sha256, Does.Match("^[0-9a-f]+$"), "SHA-256 hash should contain only hexadecimal digits");
                Assert.That(pieceLength, Is.GreaterThan(0), "Piece length should be greater than zero");
                Assert.That(pieceHashes, Is.Not.Null, "Piece hashes should not be null");
                Assert.That(File.Exists(testFile), Is.True, "Test file should exist");
                Assert.That(new FileInfo(testFile).Length, Is.EqualTo(0), "File should be empty");
            });
        }

        [Test]
        public async Task ComputeFileIntegrityData_WithNonexistentFile_ThrowsFileNotFoundException()
        {
            string nonexistentFile = Path.Combine(_testDirectory, "nonexistent.txt");

            Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await DownloadCacheOptimizer.ComputeFileIntegrityData(nonexistentFile);
            }, "Nonexistent file should throw FileNotFoundException");
        }
    }
}
