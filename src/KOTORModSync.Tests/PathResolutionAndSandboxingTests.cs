// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KOTORModSync.Core;
using KOTORModSync.Core.Services.FileSystem;
using NUnit.Framework;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class PathResolutionAndSandboxingTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_PathTests_" + Guid.NewGuid());
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

        #region Path Placeholder Resolution

        [Test]
        public void SetRealPaths_WithModDirectoryPlaceholder_ResolvesCorrectly()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<modDirectory>>/output"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);
            instruction.SetRealPaths(skipExistenceCheck: true);

            var realSourcePathsProperty = typeof(Instruction).GetProperty("RealSourcePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var realSourcePaths = (List<string>)realSourcePathsProperty?.GetValue(instruction);

            Assert.Multiple(() =>
            {
                Assert.That(realSourcePaths, Is.Not.Null, "RealSourcePaths should not be null");
                Assert.That(realSourcePaths, Is.Not.Empty, "RealSourcePaths should not be empty");
                Assert.That(realSourcePaths[0], Does.Contain(_modDirectory), "Should resolve to mod directory");
                Assert.That(realSourcePaths[0], Does.Not.Contain("<<modDirectory>>"), "Placeholder should be replaced");
            });
        }

        [Test]
        public void SetRealPaths_WithKotorDirectoryPlaceholder_ResolvesCorrectly()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<kotorDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);
            instruction.SetRealPaths(skipExistenceCheck: true);

            var realSourcePathsProperty = typeof(Instruction).GetProperty("RealSourcePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var realSourcePaths = (List<string>)realSourcePathsProperty?.GetValue(instruction);

            Assert.Multiple(() =>
            {
                Assert.That(realSourcePaths, Is.Not.Null, "RealSourcePaths should not be null");
                Assert.That(realSourcePaths[0], Does.Contain(_kotorDirectory), "Should resolve to KOTOR directory");
                Assert.That(realSourcePaths[0], Does.Not.Contain("<<kotorDirectory>>"), "Placeholder should be replaced");
            });
        }

        [Test]
        public void SetRealPaths_WithMixedPlaceholders_ResolvesCorrectly()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string>
                {
                    "<<modDirectory>>/file1.txt",
                    "<<kotorDirectory>>/file2.txt"
                },
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);
            instruction.SetRealPaths(skipExistenceCheck: true);

            var realSourcePathsProperty = typeof(Instruction).GetProperty("RealSourcePaths", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var realSourcePaths = (List<string>)realSourcePathsProperty?.GetValue(instruction);

            Assert.Multiple(() =>
            {
                Assert.That(realSourcePaths, Is.Not.Null, "RealSourcePaths should not be null");
                Assert.That(realSourcePaths, Has.Count.EqualTo(2), "Should have two source paths");
                Assert.That(realSourcePaths[0], Does.Contain(_modDirectory), "First path should resolve to mod directory");
                Assert.That(realSourcePaths[1], Does.Contain(_kotorDirectory), "Second path should resolve to KOTOR directory");
            });
        }

        #endregion

        #region Sandboxing Security

        [Test]
        public void SetRealPaths_WithAbsoluteSystemPath_RejectsOrSanitizes()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "C:\\Windows\\System32\\file.txt" },
                Destination = "C:\\Windows\\System32"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            var exception = Assert.Throws<FileNotFoundException>(() =>
            {
                instruction.SetRealPaths();
            }, "Absolute system path should be rejected");

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Exception should be thrown");
                Assert.That(instruction.Source, Is.Not.Null, "Source should not be null");
            });
        }

        [Test]
        public void SetRealPaths_WithRelativePathOutsideSandbox_Rejects()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "../../../Windows/System32/file.txt" },
                Destination = "../../../Windows/System32"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.Throws<FileNotFoundException>(() =>
            {
                instruction.SetRealPaths();
            }, "Path outside sandbox should be rejected");
        }

        [Test]
        public void SetRealPaths_WithValidPlaceholderPath_Allows()
        {
            string testFile = Path.Combine(_modDirectory, "file.txt");
            File.WriteAllText(testFile, "content");

            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.DoesNotThrow(() =>
            {
                instruction.SetRealPaths();
            }, "Valid placeholder path should be allowed");
        }

        #endregion

        #region Path Edge Cases

        [Test]
        public void SetRealPaths_WithEmptySource_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string>(),
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.DoesNotThrow(() =>
            {
                instruction.SetRealPaths(skipExistenceCheck: true);
            }, "Empty source should be handled gracefully");
        }

        [Test]
        public void SetRealPaths_WithNullSource_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = null,
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.DoesNotThrow(() =>
            {
                instruction.SetRealPaths(skipExistenceCheck: true);
            }, "Null source should be handled gracefully");
        }

        [Test]
        public void SetRealPaths_WithWhitespacePath_HandlesGracefully()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "   " },
                Destination = "<<kotorDirectory>>/Override"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.DoesNotThrow(() =>
            {
                instruction.SetRealPaths(skipExistenceCheck: true);
            }, "Whitespace path should be handled gracefully");
        }

        [Test]
        public void SetRealPaths_WithNestedPlaceholders_ResolvesCorrectly()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/subdir/file.txt" },
                Destination = "<<kotorDirectory>>/Override/nested"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);
            instruction.SetRealPaths(skipExistenceCheck: true);

            var realDestinationPathProperty = typeof(Instruction).GetProperty("RealDestinationPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var realDestinationPath = (DirectoryInfo)realDestinationPathProperty?.GetValue(instruction);

            Assert.Multiple(() =>
            {
                Assert.That(realDestinationPath, Is.Not.Null, "RealDestinationPath should not be null");
                Assert.That(realDestinationPath.FullName, Does.Contain(_kotorDirectory), "Should resolve to KOTOR directory");
                Assert.That(realDestinationPath.FullName, Does.Contain("nested"), "Should preserve nested path");
            });
        }

        #endregion

        #region Path Normalization

        [Test]
        public void SetRealPaths_WithMixedSeparators_NormalizesCorrectly()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>\\subdir/file.txt" },
                Destination = "<<kotorDirectory>>/Override\\nested"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.DoesNotThrow(() =>
            {
                instruction.SetRealPaths(skipExistenceCheck: true);
            }, "Mixed separators should be normalized");
        }

        [Test]
        public void SetRealPaths_WithTrailingSeparators_HandlesCorrectly()
        {
            var component = new ModComponent { Name = "Test", Guid = Guid.NewGuid() };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt/" },
                Destination = "<<kotorDirectory>>/Override/"
            };

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);
            _config.sourcePath = new DirectoryInfo(_modDirectory);
            _config.destinationPath = new DirectoryInfo(_kotorDirectory);

            Assert.DoesNotThrow(() =>
            {
                instruction.SetRealPaths(skipExistenceCheck: true);
            }, "Trailing separators should be handled");
        }

        #endregion
    }
}

