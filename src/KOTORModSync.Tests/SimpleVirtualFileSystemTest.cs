// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System.IO;

using KOTORModSync.Core;
using KOTORModSync.Core.Services.FileSystem;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class SimpleVirtualFileSystemTest
    {
        [Test]
        public void Test_VirtualFileSystemProvider_BasicCreation()
        {
            var provider = new VirtualFileSystemProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider, Is.Not.Null, "VirtualFileSystemProvider should not be null");
                Assert.That(provider.IsDryRun, Is.True, "VirtualFileSystemProvider should be in dry-run mode");
                Assert.That(provider, Is.InstanceOf<VirtualFileSystemProvider>(), "Should be instance of VirtualFileSystemProvider");
            });
        }

        [Test]
        public void Test_RealFileSystemProvider_BasicCreation()
        {
            var provider = new RealFileSystemProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider, Is.Not.Null, "RealFileSystemProvider should not be null");
                Assert.That(provider.IsDryRun, Is.False, "RealFileSystemProvider should not be in dry-run mode");
                Assert.That(provider, Is.InstanceOf<RealFileSystemProvider>(), "Should be instance of RealFileSystemProvider");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_FileOperations()
        {
            var provider = new VirtualFileSystemProvider();

            provider.WriteFileAsync("test.txt", "content").Wait();

            Assert.Multiple(() =>
            {
                Assert.That(provider, Is.Not.Null, "Provider should not be null");
                Assert.That(provider.FileExists("test.txt"), Is.True, "File should exist after writing");
                Assert.That(provider.FileExists("nonexistent.txt"), Is.False, "Non-existent file should not exist");
                Assert.That(provider.FileExists(null), Is.False, "Null path should return false");
                Assert.That(provider.FileExists(""), Is.False, "Empty path should return false");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_ReadWriteOperations()
        {
            var provider = new VirtualFileSystemProvider();
            string testContent = "test content with special chars: !@#$%^&*()";
            string testPath = "test_read_write.txt";

            provider.WriteFileAsync(testPath, testContent).Wait();
            string readContent = provider.ReadFileAsync(testPath).Result;

            Assert.Multiple(() =>
            {
                Assert.That(provider.FileExists(testPath), Is.True, "File should exist after writing");
                Assert.That(readContent, Is.EqualTo(testContent), "Read content should match written content");
                Assert.That(readContent, Is.Not.Null, "Read content should not be null");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_DeleteOperations()
        {
            var provider = new VirtualFileSystemProvider();
            string testPath = "test_delete.txt";

            provider.WriteFileAsync(testPath, "content").Wait();
            Assert.That(provider.FileExists(testPath), Is.True, "File should exist before deletion");

            provider.DeleteFileAsync(testPath).Wait();

            Assert.Multiple(() =>
            {
                Assert.That(provider.FileExists(testPath), Is.False, "File should not exist after deletion");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_DirectoryOperations()
        {
            var provider = new VirtualFileSystemProvider();
            string testDir = "test_directory";
            string testFile = Path.Combine(testDir, "file.txt");

            provider.CreateDirectoryAsync(testDir).Wait();
            provider.WriteFileAsync(testFile, "content").Wait();

            Assert.Multiple(() =>
            {
                Assert.That(provider.DirectoryExists(testDir), Is.True, "Directory should exist after creation");
                Assert.That(provider.FileExists(testFile), Is.True, "File in directory should exist");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_EmptyContentHandling()
        {
            var provider = new VirtualFileSystemProvider();
            string testPath = "empty.txt";

            provider.WriteFileAsync(testPath, "").Wait();
            string readContent = provider.ReadFileAsync(testPath).Result;

            Assert.Multiple(() =>
            {
                Assert.That(provider.FileExists(testPath), Is.True, "Empty file should exist");
                Assert.That(readContent, Is.EqualTo(""), "Read content should be empty string");
                Assert.That(readContent, Is.Not.Null, "Read content should not be null (even if empty)");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_LargeContentHandling()
        {
            var provider = new VirtualFileSystemProvider();
            string testPath = "large.txt";
            string largeContent = new string('A', 100000); // 100KB

            provider.WriteFileAsync(testPath, largeContent).Wait();
            string readContent = provider.ReadFileAsync(testPath).Result;

            Assert.Multiple(() =>
            {
                Assert.That(provider.FileExists(testPath), Is.True, "Large file should exist");
                Assert.That(readContent, Is.EqualTo(largeContent), "Read content should match written large content");
                Assert.That(readContent.Length, Is.EqualTo(100000), "Read content length should match written length");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_UnicodeContentHandling()
        {
            var provider = new VirtualFileSystemProvider();
            string testPath = "unicode.txt";
            string unicodeContent = "测试内容_тест_テスト_🎮";

            provider.WriteFileAsync(testPath, unicodeContent).Wait();
            string readContent = provider.ReadFileAsync(testPath).Result;

            Assert.Multiple(() =>
            {
                Assert.That(provider.FileExists(testPath), Is.True, "Unicode file should exist");
                Assert.That(readContent, Is.EqualTo(unicodeContent), "Read content should match written Unicode content");
            });
        }

        [Test]
        public void Test_MainConfig_Initialization()
        {
            var config = new MainConfig();

            Assert.Multiple(() =>
            {
                Assert.That(config, Is.Not.Null, "MainConfig should not be null");
                Assert.That(config.caseInsensitivePathing, Is.TypeOf<bool>(), "caseInsensitivePathing should be boolean");
                Assert.That(config.useMultiThreadedIO, Is.False, "useMultiThreadedIO should default to false");
                Assert.That(config.sourcePath, Is.Null, "sourcePath should be null initially");
                Assert.That(config.destinationPath, Is.Null, "destinationPath should be null initially");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_NullPathHandling()
        {
            var provider = new VirtualFileSystemProvider();

            Assert.Multiple(() =>
            {
                Assert.That(() => provider.FileExists(null), Throws.Nothing, "FileExists with null should not throw");
                Assert.That(provider.FileExists(null), Is.False, "FileExists with null should return false");
            });
        }

        [Test]
        public void Test_VirtualFileSystemProvider_ConcurrentOperations()
        {
            var provider = new VirtualFileSystemProvider();
            var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();

            for (int i = 0; i < 10; i++)
            {
                int index = i;
                tasks.Add(provider.WriteFileAsync($"concurrent_{index}.txt", $"content_{index}"));
            }

            System.Threading.Tasks.Task.WaitAll(tasks.ToArray());

            Assert.Multiple(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    Assert.That(provider.FileExists($"concurrent_{i}.txt"), Is.True,
                        $"File concurrent_{i}.txt should exist after concurrent write");
                }
            });
        }
    }
}
