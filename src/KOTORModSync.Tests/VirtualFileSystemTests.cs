// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Services.FileSystem;
using KOTORModSync.Core.Utility;
using NUnit.Framework;

namespace KOTORModSync.Tests
{

    [TestFixture]
    public class VirtualFileSystemTests
    {
        private string _testRootDir;
        private string _sourceDir;
        private string _destinationDir;
        private string _sevenZipPath;
        private MainConfig _originalConfig;

        [OneTimeSetUp]
        public void OneTimeSetUp() => _sevenZipPath = Find7Zip();

        [SetUp]
        public void SetUp()
        {
            _testRootDir = Path.Combine(Path.GetTempPath(), "KOTORModSync_VFS_Tests_" + Guid.NewGuid().ToString("N"));
            _sourceDir = Path.Combine(_testRootDir, "Source");
            _destinationDir = Path.Combine(_testRootDir, "Dest");
            Directory.CreateDirectory(_sourceDir);
            Directory.CreateDirectory(_destinationDir);

            _originalConfig = new MainConfig();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_testRootDir))
                {
                    Directory.Delete(_testRootDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"Warning: Could not delete test directory: {ex.Message}");
            }

            _ = new MainConfig
            {
                sourcePath = _originalConfig?.sourcePath,
                destinationPath = _originalConfig?.destinationPath,
            };
        }

        internal static string Find7Zip()
        {
            string[] paths =
            new string[]
            {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
            };

            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = NetFrameworkCompatibility.IsWindows() ? "where" : "which",
                    Arguments = "7z",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                });
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        return output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("7-Zip not found. Please install 7-Zip to run these tests.", ex);
                throw;
            }

            throw new InvalidOperationException("7-Zip not found. Please install 7-Zip to run these tests.");
        }

        internal void CreateArchive(string archivePath, Dictionary<string, string> files)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "temp_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                foreach (KeyValuePair<string, string> file in files)
                {
                    string filePath = Path.Combine(tempDir, file.Key);
                    string fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir))
                    {
                        _ = Directory.CreateDirectory(fileDir);
                    }

                    File.WriteAllText(filePath, file.Value);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _sevenZipPath,
                    Arguments = $"a -tzip \"{archivePath}\" \"{Path.Combine(tempDir, "*")}\" -r",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process is null)
                    {
                        return;
                    }

                    process.WaitForExit();
                }
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        internal static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
            }

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                _ = file.CopyTo(targetFilePath, overwrite: true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        private async Task<(VirtualFileSystemProvider virtualProvider, string realDestDir)> RunBothProviders(
            List<Instruction> instructions,
            string sourceDir)
        {
            Debug.Assert(_testRootDir != null);
            string virtualRoot = Path.Combine(_testRootDir, "Virtual");
            string realRoot = Path.Combine(_testRootDir, "Real");

            _ = Directory.CreateDirectory(virtualRoot);
            _ = Directory.CreateDirectory(realRoot);

            CopyDirectory(sourceDir, virtualRoot);
            CopyDirectory(sourceDir, realRoot);

            DirectoryInfo originalSourcePath = MainConfig.SourcePath;
            DirectoryInfo originalDestPath = MainConfig.DestinationPath;

            var virtualInstructions = new List<Instruction>();
            var realInstructions = new List<Instruction>();
            foreach (Instruction instruction in instructions)
            {
                virtualInstructions.Add(new Instruction
                {
                    Action = instruction.Action,
                    Source = instruction.Source.ToList(),
                    Destination = instruction.Destination,
                    Overwrite = instruction.Overwrite,
                    Arguments = instruction.Arguments,
                });
                realInstructions.Add(new Instruction
                {
                    Action = instruction.Action,
                    Source = instruction.Source.ToList(),
                    Destination = instruction.Destination,
                    Overwrite = instruction.Overwrite,
                    Arguments = instruction.Arguments,
                });
            }

            try
            {

                _ = new MainConfig
                {
                    sourcePath = new DirectoryInfo(virtualRoot),
                    destinationPath = new DirectoryInfo(Path.Combine(virtualRoot, "dest")),
                };

                var virtualProvider = new VirtualFileSystemProvider();
                await virtualProvider.InitializeFromRealFileSystemAsync(virtualRoot);
                var virtualComponent = new ModComponent
                {
                    Name = "TestComponent",
                    Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>(virtualInstructions),
                };

                _ = await virtualComponent.ExecuteInstructionsAsync(virtualComponent.Instructions, new List<ModComponent>(), CancellationToken.None, virtualProvider);
                await TestContext.Progress.WriteLineAsync($"Virtual Provider - Files tracked: {virtualProvider.GetTrackedFiles().Count}");
                await TestContext.Progress.WriteLineAsync($"Virtual Provider - Issues: {virtualProvider.GetValidationIssues().Count}");

                _ = new MainConfig
                {
                    sourcePath = new DirectoryInfo(realRoot),
                    destinationPath = new DirectoryInfo(Path.Combine(realRoot, "dest")),
                };

                var realProvider = new RealFileSystemProvider();
                var realComponent = new ModComponent
                {
                    Name = "TestComponent",
                    Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>(realInstructions),
                };

                _ = await realComponent.ExecuteInstructionsAsync(realComponent.Instructions, new List<ModComponent>(), CancellationToken.None, realProvider);
                await TestContext.Progress.WriteLineAsync("Real Provider - Executed successfully");

                return (virtualProvider, Path.Combine(realRoot, "dest"));
            }
            finally
            {

                _ = new MainConfig
                {
                    sourcePath = originalSourcePath,
                    destinationPath = originalDestPath,
                };
            }
        }

        private static void AssertFileSystemsMatch(VirtualFileSystemProvider virtualProvider, string realDestDir)
        {

            string virtDestPath = Path.GetDirectoryName(Path.GetDirectoryName(realDestDir));
            string virtualDestPath = Path.Combine(virtDestPath, "Virtual", "dest");

            var virtualFiles = virtualProvider.GetTrackedFiles()
                .Where(f => f.StartsWith(virtualDestPath, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(virtualDestPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<string> realFiles = Directory.Exists(realDestDir)
                ? Directory.GetFiles(realDestDir, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(realDestDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TestContext.Progress.WriteLine($"\n=== Virtual Files ({virtualFiles.Count}) ===");
            foreach (string file in virtualFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                TestContext.Progress.WriteLine($"  {file}");
            }

            TestContext.Progress.WriteLine($"\n=== Real Files ({realFiles.Count}) ===");
            foreach (string file in realFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                TestContext.Progress.WriteLine($"  {file}");
            }

            var missingInReal = virtualFiles.Except(realFiles, StringComparer.OrdinalIgnoreCase).ToList();
            if (missingInReal.Count > 0)
            {
                TestContext.Progress.WriteLine($"\n=== Files in VIRTUAL but NOT in REAL ({missingInReal.Count}) ===");
                foreach (string file in missingInReal)
                {
                    TestContext.Progress.WriteLine($"  {file}");
                }
            }

            var missingInVirtual = realFiles.Except(virtualFiles, StringComparer.OrdinalIgnoreCase).ToList();
            if (missingInVirtual.Count > 0)
            {
                TestContext.Progress.WriteLine($"\n=== Files in REAL but NOT in VIRTUAL ({missingInVirtual.Count}) ===");
                foreach (string file in missingInVirtual)
                {
                    TestContext.Progress.WriteLine($"  {file}");
                }
            }
            Assert.Multiple(() =>
            {
                Assert.That(missingInReal, Is.Empty);
                Assert.That(missingInVirtual, Is.Empty);
                Assert.That(realFiles, Has.Count.EqualTo(virtualFiles.Count));
            });
        }

        #region Archive Operation Tests

        [Test]
        public async Task Test_ExtractArchive_Basic()
        {

            Debug.Assert(_sourceDir != null);
            string archivePath = Path.Combine(_sourceDir, "test.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "Content 1" },
                { "file2.txt", "Content 2" },
                { "subfolder/file3.txt", "Content 3" },
            });

            var instructions = new List<Instruction>
            {
                new Instruction
                {
                    Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\test.zip" },
                    Destination = "<<modDirectory>>",
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Extract archive operation should not produce validation errors");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should still exist after extraction");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        public async Task Test_MoveArchiveThenRenameThenExtract()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "chain_a.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "a.txt", "A" } });

            var instructions = new List<Instruction>
        {
            new Instruction { Action = Instruction.ActionType.Rename, Source = new List<string> { "<<modDirectory>>\\chain_a.zip" }, Destination = "chain_b.zip", Overwrite = true },
            new Instruction { Action = Instruction.ActionType.Rename, Source = new List<string> { "<<modDirectory>>\\chain_b.zip" }, Destination = "chain_final.zip", Overwrite = true },
            new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\chain_final.zip" }, Destination = "<<kotorDirectory>>" },
        };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_CopyArchiveTwiceThenExtractBoth()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "dup.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "d.txt", "D" } });

            var instructions = new List<Instruction>
        {

            new Instruction { Action = Instruction.ActionType.Copy, Source = new List<string> { "<<modDirectory>>\\dup.zip" }, Destination = "<<modDirectory>>\\copy1", Overwrite = true },
            new Instruction { Action = Instruction.ActionType.Rename, Source = new List<string> { "<<modDirectory>>\\copy1\\dup.zip" }, Destination = "dup_copy1.zip", Overwrite = true },
            new Instruction { Action = Instruction.ActionType.Copy, Source = new List<string> { "<<modDirectory>>\\dup.zip" }, Destination = "<<modDirectory>>\\copy2", Overwrite = true },
            new Instruction { Action = Instruction.ActionType.Rename, Source = new List<string> { "<<modDirectory>>\\copy2\\dup.zip" }, Destination = "dup_copy2.zip", Overwrite = true },
            new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\copy1\\dup_copy1.zip" }, Destination = "<<kotorDirectory>>\\extract1" },
            new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\copy2\\dup_copy2.zip" }, Destination = "<<kotorDirectory>>\\extract2" },
        };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_RenameArchiveIntoSubfolderThenExtract()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "sub.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "s.txt", "S" } });
            string subdir = Path.Combine(_sourceDir, "subdir"); _ = Directory.CreateDirectory(subdir);

            var instructions = new List<Instruction>
        {
            new Instruction { Action = Instruction.ActionType.Rename, Source = new List<string> { "<<modDirectory>>\\sub.zip" }, Destination = "subdir\\final.zip", Overwrite = true },
            new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\subdir\\final.zip" }, Destination = "<<kotorDirectory>>" },
        };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_ExtractThenMoveExtractedFolder()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "mv_extract.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "inner/x.txt", "X" } });
            var instructions = new List<Instruction>
        {
            new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\mv_extract.zip" }, Destination = "<<modDirectory>>" },
            new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { "<<modDirectory>>\\mv_extract\\inner\\x.txt" }, Destination = "<<kotorDirectory>>", Overwrite = true },
        };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_ExtractThenCopyWildcardSet()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "wc_set.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "pack/a.txt", "A" }, { "pack/b.log", "B" }, { "pack/c.txt", "C" } });
            Debug.Assert(_destinationDir != null);
            string outDir = Path.Combine(_destinationDir, "txts");
            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\wc_set.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Copy, Source = new List<string>
                {
                    @"<<modDirectory>>\wc_set\pack\*.txt",
                }, Destination = "<<kotorDirectory>>\\txts", Overwrite = true, },
            };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_RenameExtractedFileThenCopy()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "rn_copy.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "p/q.txt", "Q" } });
            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\rn_copy.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Rename, Source = new List<string> { "<<modDirectory>>\\rn_copy\\p\\q.txt" }, Destination = "qq.txt", Overwrite = true },
                new Instruction { Action = Instruction.ActionType.Copy, Source = new List<string> { "<<modDirectory>>\\rn_copy\\p\\qq.txt" }, Destination = "<<kotorDirectory>>\\copied", Overwrite = true },
            };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_DeleteExtractedFileThenVerifyMissing()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "del.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "rm.txt", "RM" } });
            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\del.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Delete, Source = new List<string> { "<<modDirectory>>\\del\\rm.txt" }, Destination = string.Empty },
            };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Delete extracted file operation should not produce validation errors");
                Assert.That(v.GetTrackedFiles(), Is.Not.Null, "Tracked files list should not be null");
                Assert.That(v.GetTrackedFiles().Any(p => p.EndsWith("del\\rm.txt", StringComparison.OrdinalIgnoreCase)), Is.False,
                    "Deleted file should not be in tracked files");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_MoveArchiveThenExtractTwoArchivesSequentially()
        {
            Debug.Assert(_sourceDir != null);
            string a1 = Path.Combine(_sourceDir, "a1.zip");
            string a2 = Path.Combine(_sourceDir, "a2.zip");
            CreateArchive(a1, new Dictionary<string, string>(StringComparer.Ordinal) { { "x.txt", "X" } });
            CreateArchive(a2, new Dictionary<string, string>(StringComparer.Ordinal) { { "y.txt", "Y" } });
            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Rename, Source = new List<string> { "<<modDirectory>>\\a1.zip" }, Destination = "a1_moved.zip", Overwrite = true },
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\a1_moved.zip" }, Destination = "<<kotorDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\a2.zip" }, Destination = "<<kotorDirectory>>" },
            };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public async Task Test_CopyThenMoveExtractedSetIntoNestedFolder()
        {
            Debug.Assert(_sourceDir != null);
            string src = Path.Combine(_sourceDir, "nest.zip");
            CreateArchive(src, new Dictionary<string, string>(StringComparer.Ordinal) { { "n/a.txt", "A" }, { "n/b.txt", "B" } });
            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\nest.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Copy, Source = new List<string> { "<<modDirectory>>\\nest\\n\\*.txt" }, Destination = "<<kotorDirectory>>\\nested\\deep", Overwrite = true },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { "<<kotorDirectory>>\\nested\\deep\\a.txt" }, Destination = "<<kotorDirectory>>\\final", Overwrite = true },
            };

            (VirtualFileSystemProvider v, string r) = await RunBothProviders(instructions, _sourceDir);
            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(v.GetValidationIssues(), Is.Empty, "Operation should not produce validation errors");
            });
            AssertFileSystemsMatch(v, r);
        }

        [Test]
        public void Test_MoveNonexistentFile_ShouldRecordErrorAndNotModifyReal()
        {
            var instructions = new List<Instruction>
            {
            new Instruction {
                Action = Instruction.ActionType.Move,
                    Source = new List<string> { "<<modDirectory>>\\nope.txt" },
                    Destination = "<<kotorDirectory>>\\anywhere.txt",
                    Overwrite = true,
                },
            };

            Debug.Assert(_sourceDir != null);
            Assert.ThrowsAsync<FileNotFoundException>(async () => await RunBothProviders(instructions, _sourceDir).ConfigureAwait(false));
        }

        [Test]
        public async Task Test_MoveArchiveThenExtract()
        {

            Debug.Assert(_sourceDir != null);
            string originalArchivePath = Path.Combine(_sourceDir, "original.zip");
            CreateArchive(originalArchivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "data.txt", "Important data" },
                { "config.ini", "[Settings]\nvalue=123" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\original.zip" },
                    Destination = "moved.zip",
                    Overwrite = true,
                },
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\moved.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Move and extract archive operation should not produce validation errors");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        public async Task Test_CopyArchiveThenExtractBoth()
        {

            Debug.Assert(_sourceDir != null);
            string originalArchivePath = Path.Combine(_sourceDir, "source.zip");
            CreateArchive(originalArchivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "shared.txt", "Shared content" },
            });

            var instructions = new List<Instruction>
            {

            new Instruction
            {
                Action = Instruction.ActionType.Copy,
                    Source = new List<string> { "<<modDirectory>>\\source.zip" },
                    Destination = "<<modDirectory>>\\archives",
                    Overwrite = true,
                },

            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\archives\\source.zip" },
                    Destination = "copy.zip",
                    Overwrite = true,
                },

            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\source.zip" },
                    Destination = "<<kotorDirectory>>\\original_extract",
                },

            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\archives\\copy.zip" },
                    Destination = "<<kotorDirectory>>\\copy_extract",
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Copy and extract archive operation should not produce validation errors");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        public async Task Test_RenameArchiveThenExtract()
        {

            Debug.Assert(_sourceDir != null);
            string originalArchivePath = Path.Combine(_sourceDir, "oldname.zip");
            CreateArchive(originalArchivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "readme.txt", "Read me first" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\oldname.zip" },
                    Destination = "newname.zip",
                    Overwrite = true,
                },
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\newname.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Rename and extract archive operation should not produce validation errors");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        public async Task Test_ExtractMultipleArchives()
        {

            Debug.Assert(_sourceDir != null);
            string archive1 = Path.Combine(_sourceDir, "mod1.zip");
            string archive2 = Path.Combine(_sourceDir, "mod2.zip");
            string archive3 = Path.Combine(_sourceDir, "mod3.zip");

            CreateArchive(archive1, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "mod1/data.txt", "Mod 1 data" },
            });

            CreateArchive(archive2, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "mod2/config.ini", "Mod 2 config" },
                { "mod2/assets/texture.tga", "Texture data" },
            });

            CreateArchive(archive3, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "mod3/script.ncs", "Script bytecode" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\mod1.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\mod2.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\mod3.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Extract multiple archives operation should not produce validation errors");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        #endregion

        #region File Operation Tests

        [Test]
        public async Task Test_MoveExtractedFiles()
        {

            Debug.Assert(_sourceDir != null);
            string archivePath = Path.Combine(_sourceDir, "files.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "Content 1" },
                { "file2.txt", "Content 2" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\files.zip" },
                    Destination = "<<modDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Move,
                    Source = new List<string> { "<<modDirectory>>\\files\\file1.txt" },
                    Destination = "<<kotorDirectory>>\\final",
                    Overwrite = true,
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Extract archive operation should not produce validation errors");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should still exist after extraction");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        public async Task Test_CopyExtractedFiles()
        {

            Debug.Assert(_sourceDir != null);
            string archivePath = Path.Combine(_sourceDir, "source.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "original.txt", "Original content" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\source.zip" },
                    Destination = "<<modDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Copy,
                    Source = new List<string> { "<<modDirectory>>\\source\\original.txt" },
                    Destination = "<<kotorDirectory>>\\backup",
                    Overwrite = true,
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Extract archive operation should not produce validation errors");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should still exist after extraction");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        public async Task Test_RenameExtractedFile()
        {

            Debug.Assert(_sourceDir != null);
            string archivePath = Path.Combine(_sourceDir, "content.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oldname.dat", "Data content" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\content.zip" },
                    Destination = "<<modDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\content\\oldname.dat" },
                    Destination = "newname.dat",
                    Overwrite = true,
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Extract archive operation should not produce validation errors");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should still exist after extraction");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        public async Task Test_DeleteExtractedFile()
        {

            Debug.Assert(_sourceDir != null);
            string archivePath = Path.Combine(_sourceDir, "cleanup.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "keep.txt", "Keep this" },
                { "delete.txt", "Delete this" },
                { "also_keep.txt", "Keep this too" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\cleanup.zip" },
                    Destination = "<<modDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Delete,
                    Source = new List<string> { "<<modDirectory>>\\cleanup\\delete.txt" },
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Extract archive operation should not produce validation errors");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should still exist after extraction");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        #endregion

        #region Validation Tests (Should Fail)

        [Test]
        public void Test_ExtractNonExistentArchive_ShouldFail()
        {

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\doesnotexist.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            };

            Debug.Assert(_sourceDir != null);
            var exception = Assert.ThrowsAsync<FileNotFoundException>(async () => await RunBothProviders(instructions, _sourceDir).ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(instructions, Is.Not.Null, "Instructions list should not be null");
                Assert.That(instructions, Has.Count.GreaterThan(0), "Should have at least one instruction");
                Assert.That(_sourceDir, Is.Not.Null, "Source directory should not be null");
                Assert.That(Directory.Exists(_sourceDir), Is.True, "Source directory should exist");
                Assert.That(exception, Is.Not.Null, "Extracting nonexistent archive should throw FileNotFoundException");
            });
        }

        [Test]
        public Task Test_MoveNonExistentFile_DetectedInDryRun()
        {

            Debug.Assert(_sourceDir != null);
            string archivePath = Path.Combine(_sourceDir, "test.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file.txt", "content" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\test.zip" },
                    Destination = "<<modDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Delete,
                    Source = new List<string> { "<<modDirectory>>\\test\\file.txt" },
                },
            new Instruction
            {
                Action = Instruction.ActionType.Move,
                    Source = new List<string> { "<<modDirectory>>\\test\\file.txt" },
                    Destination = "<<kotorDirectory>>\\moved.txt",
                },
            };

            var exception = Assert.ThrowsAsync<FileNotFoundException>(async () => await RunBothProviders(instructions, _sourceDir).ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(instructions, Is.Not.Null, "Instructions list should not be null");
                Assert.That(instructions, Has.Count.GreaterThan(0), "Should have at least one instruction");
                Assert.That(_sourceDir, Is.Not.Null, "Source directory should not be null");
                Assert.That(Directory.Exists(_sourceDir), Is.True, "Source directory should exist");
                Assert.That(File.Exists(archivePath), Is.True, "Archive should exist");
                Assert.That(exception, Is.Not.Null, "Moving deleted file should throw FileNotFoundException");
            });

            return Task.CompletedTask;
        }

        [Test]
        public async Task Test_ExtractMovedArchive_Success()
        {

            Debug.Assert(_sourceDir != null);
            string originalPath = Path.Combine(_sourceDir, "original.zip");
            CreateArchive(originalPath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "data/file.txt", "Important data" },
            });

            _ = Directory.CreateDirectory(Path.Combine(_sourceDir, "subdir"));

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\original.zip" },
                    Destination = "subdir\\moved.zip",
                    Overwrite = true,
                },
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\subdir\\moved.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Extract moved archive operation should not produce validation errors");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        #endregion

        #region Complex Scenarios

        [Test]
        public async Task Test_ComplexModInstallation_MultipleArchivesAndOperations()
        {

            Debug.Assert(_sourceDir != null);
            string mod1Archive = Path.Combine(_sourceDir, "mod1.zip");
            string mod2Archive = Path.Combine(_sourceDir, "mod2.zip");
            string patchArchive = Path.Combine(_sourceDir, "patch.zip");

            CreateArchive(mod1Archive, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "override/appearance.2da", "Mod1 appearance data" },
                { "override/dialog.dlg", "Mod1 dialog" },
                { "modules/module1.mod", "Module 1" },
            });

            CreateArchive(mod2Archive, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "override/appearance.2da", "Mod2 appearance data (conflicts!)" },
                { "override/spells.2da", "Mod2 spells" },
                { "lips/scene1.lip", "Lip sync data" },
            });

            CreateArchive(patchArchive, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "appearance.2da", "Patched appearance" },
                { "compatibility_fix.txt", "Instructions" },
            });

            var instructions = new List<Instruction>
            {

                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\mod1.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { "<<modDirectory>>\\mod1\\override\\*" }, Destination = "<<kotorDirectory>>\\override", Overwrite = true },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { "<<modDirectory>>\\mod1\\modules\\*" }, Destination = "<<kotorDirectory>>\\modules", Overwrite = true },

                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\mod2.zip" }, Destination = "<<modDirectory>>" },

                new Instruction { Action = Instruction.ActionType.Copy, Source = new List<string> { "<<kotorDirectory>>\\override\\appearance.2da" }, Destination = "<<kotorDirectory>>\\backup\\appearance.2da.mod1", Overwrite = true },

                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { "<<modDirectory>>\\mod2\\override\\*" }, Destination = "<<kotorDirectory>>\\override", Overwrite = true },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { "<<modDirectory>>\\mod2\\lips\\*" }, Destination = "<<kotorDirectory>>\\lips", Overwrite = true },

                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { "<<modDirectory>>\\patch.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { "<<modDirectory>>\\patch\\appearance.2da" }, Destination = "<<kotorDirectory>>\\override", Overwrite = true },

                new Instruction { Action = Instruction.ActionType.Delete, Source = new List<string> { "<<modDirectory>>\\patch\\compatibility_fix.txt" } },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Complex mod installation operation should not produce validation errors");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);

            List<string> virtualFiles = virtualProvider.GetTrackedFiles();
            Assert.Multiple(() =>
            {
                Assert.That(virtualFiles, Is.Not.Null, "Tracked files list should not be null");
                Assert.That(virtualFiles, Is.Not.Empty, "Complex mod installation should track files");
                Assert.That(virtualFiles.Any(f => f.EndsWith("override\\appearance.2da", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "Final appearance.2da should be tracked");
                Assert.That(virtualFiles.Any(f => f.EndsWith("override\\dialog.dlg", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "Dialog file should be tracked");
                Assert.That(virtualFiles.Any(f => f.EndsWith("override\\spells.2da", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "Spells file should be tracked");
                Assert.That(virtualFiles.Any(f => f.EndsWith("backup\\appearance.2da.mod1\\appearance.2da", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "Backup file should be tracked");
            });
        }

        [Test]
        public async Task Test_NestedArchiveOperations()
        {

            Debug.Assert(_sourceDir != null);
            string originalPath = Path.Combine(_sourceDir, "original.zip");
            CreateArchive(originalPath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "nested/deep/file.txt", "Deep content" },
            });

            var instructions = new List<Instruction>
            {
            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\original.zip" },
                    Destination = "temp1.zip",
                    Overwrite = true,
                },
            new Instruction
            {
                Action = Instruction.ActionType.Copy,
                    Source = new List<string> { "<<modDirectory>>\\temp1.zip" },
                    Destination = "<<modDirectory>>\\backup",
                    Overwrite = true,
                },
            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\backup\\temp1.zip" },
                    Destination = "temp2.zip",
                    Overwrite = true,
                },
            new Instruction
            {
                Action = Instruction.ActionType.Rename,
                    Source = new List<string> { "<<modDirectory>>\\backup\\temp2.zip" },
                    Destination = "final.zip",
                    Overwrite = true,
                },
            new Instruction
            {
                Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\backup\\final.zip" },
                    Destination = "<<kotorDirectory>>",
                },
            new Instruction
            {
                Action = Instruction.ActionType.Delete,
                    Source = new List<string> { "<<modDirectory>>\\temp1.zip" },
                },
            };

            (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(instructions, _sourceDir);

            Assert.Multiple(() =>
            {
                Assert.That(virtualProvider, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(virtualProvider.GetValidationIssues(), Is.Empty, "Nested archive operations should not produce validation errors");
            });
            AssertFileSystemsMatch(virtualProvider, realDestDir);
        }

        [Test]
        [Category("Integration")]
        [Explicit("Long-running integration test that downloads mods from the internet")]
        public async Task Test_FullModBuildInstallation_KOTOR1_Mobile_Full()
        {

            Debug.Assert(_testRootDir != null);
            string kotorRoot = Path.Combine(_testRootDir, "KOTOR_Install");
            CreateKotorDirectoryStructure(kotorRoot);

            string modDirectory = Path.Combine(_testRootDir, "Mods");
            _ = Directory.CreateDirectory(modDirectory);

            string[] possiblePaths =
            {
                Path.Combine(Environment.CurrentDirectory, "mod-builds", "TOMLs", "KOTOR1_Mobile_Full.toml"),
                Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "mod-builds", "TOMLs", "KOTOR1_Mobile_Full.toml"),
                Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "mod-builds", "TOMLs", "KOTOR1_Mobile_Full.toml"),
            };

            string tomlPath = null;
            foreach (string path in possiblePaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        tomlPath = fullPath;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    await TestContext.Progress.WriteLineAsync($"Failed to get full path for {path}: {ex.Message}");
                }
            }

            if (tomlPath is null)
            {
                Assert.Ignore($"TOML file not found. Tried locations:\n{string.Join("\n", possiblePaths)}");
                return;
            }

            await TestContext.Progress.WriteLineAsync($"Loading TOML from: {tomlPath}");

            List<ModComponent> components;
            try
            {
                components = await FileLoadingService.LoadFromFileAsync(tomlPath);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to parse TOML: {ex.Message}");
                return;
            }

            await TestContext.Progress.WriteLineAsync($"Loaded {components.Count} mods from TOML");

            _ = new MainConfig
            {
                sourcePath = new DirectoryInfo(modDirectory),
                destinationPath = new DirectoryInfo(kotorRoot),
            };
            await TestContext.Progress.WriteLineAsync("Attempting to download mods...");
            int downloadedCount = 0;
            int failedCount = 0;

            foreach (ModComponent component in components)
            {
                if (component.ResourceRegistry.Count == 0)
                {
                    await TestContext.Progress.WriteLineAsync($"  [{component.Name}] No download links available");
                    component.IsSelected = false;
                    failedCount++;
                    continue;
                }

                try
                {

                    bool downloaded = await TryDownloadModAsync(component, modDirectory);
                    if (downloaded)
                    {
                        await TestContext.Progress.WriteLineAsync($"  ✓ [{component.Name}] Downloaded successfully");
                        downloadedCount++;
                    }
                    else
                    {
                        await TestContext.Progress.WriteLineAsync($"  ✗ [{component.Name}] Download failed");
                        component.IsSelected = false;
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    await TestContext.Progress.WriteLineAsync($"  ✗ [{component.Name}] Download error: {ex.Message}");
                    component.IsSelected = false;
                    failedCount++;
                }
            }

            await TestContext.Progress.WriteLineAsync($"\nDownload summary: {downloadedCount} successful, {failedCount} failed");

            if (downloadedCount == 0)
            {
                Assert.Ignore("No mods were successfully downloaded - skipping installation test");
                return;
            }

            var selectedComponents = components.Where(c => c.IsSelected).ToList();
            await TestContext.Progress.WriteLineAsync($"\nInstalling {selectedComponents.Count} mods...");

            var allInstructions = new List<Instruction>();
            foreach (ModComponent component in selectedComponents)
            {
                allInstructions.AddRange(component.Instructions);
            }

            if (allInstructions.Count == 0)
            {
                Assert.Ignore("No instructions to execute");
                return;
            }

            await TestContext.Progress.WriteLineAsync($"Total instructions: {allInstructions.Count}");

            try
            {
                (VirtualFileSystemProvider virtualProvider, string realDestDir) = await RunBothProviders(
                    allInstructions,
                    modDirectory
                );

                var criticalIssues = virtualProvider.GetValidationIssues()
                    .Where(i => i.Severity >= ValidationSeverity.Error)
                    .ToList();
                await TestContext.Progress.WriteLineAsync("\nInstallation complete!");
                await TestContext.Progress.WriteLineAsync($"Total validation issues: {virtualProvider.GetValidationIssues().Count}");
                await TestContext.Progress.WriteLineAsync($"Critical issues: {criticalIssues.Count}");

                foreach (ValidationIssue issue in criticalIssues.Take(10))
                {
                    await TestContext.Progress.WriteLineAsync($"  {issue.Severity}: [{issue.Category}] {issue.Message}");
                }

                Assert.That(criticalIssues, Is.Empty, "Installation should complete without critical errors");

                AssertFileSystemsMatch(virtualProvider, realDestDir);
                await TestContext.Progress.WriteLineAsync("\n✓ Installation test passed!");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Installation failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void CreateKotorDirectoryStructure(string rootPath)
        {
            _ = Directory.CreateDirectory(rootPath);

            string[] directories =
            {
                "data",
                "lips",
                "modules\\extras",
                "movies",
                "Override",
                "rims",
                "streammusic",
                "streamsounds",
                "streamwaves\\globe",
                "TexturePacks",
                "utils\\swupdateskins",
            };

            foreach (string dir in directories)
            {
                _ = Directory.CreateDirectory(Path.Combine(rootPath, dir));
            }

            File.WriteAllText(Path.Combine(rootPath, "swkotor.exe"), "fake exe");
            File.WriteAllText(Path.Combine(rootPath, "dialog.tlk"), "fake dialog");
        }

        private static async Task<bool> TryDownloadModAsync(ModComponent component, string modDirectory)
        {

            await Task.CompletedTask;

            var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (component.Instructions.Count > 0)
            {
                foreach (Instruction instruction in component.Instructions)
                {
                    if (instruction.Action != Instruction.ActionType.Extract)
                    {
                        continue;
                    }

                    foreach (string cleanSource in instruction.Source.Select(source => NetFrameworkCompatibility.Replace(NetFrameworkCompatibility.Replace(source, "<<modDirectory>>\\", "", StringComparison.Ordinal), "<<modDirectory>>/", "", StringComparison.Ordinal)))
                    {

                        if (NetFrameworkCompatibility.Contains(cleanSource, '*', StringComparison.Ordinal))
                        {
                            try
                            {
                                string searchPattern = Path.GetFileName(cleanSource);
                                string searchDir = Path.GetDirectoryName(cleanSource) ?? "";
                                string fullSearchDir = Path.Combine(modDirectory, searchDir);

                                if (!Directory.Exists(fullSearchDir))
                                {
                                    continue;
                                }

                                string[] matchingFiles = Directory.GetFiles(fullSearchDir, searchPattern, SearchOption.TopDirectoryOnly);
                                if (matchingFiles.Length > 0)
                                {
                                    return true;
                                }
                            }
                            catch (Exception ex)
                            {
                                await TestContext.Progress.WriteLineAsync($"Failed to get full path for {cleanSource}: {ex.Message}");
                            }
                        }
                        else
                        {
                            _ = expectedFiles.Add(cleanSource);
                        }
                    }
                }
            }

            foreach (string expectedFile in expectedFiles)
            {
                string fullPath = Path.Combine(modDirectory, expectedFile);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                await TestContext.Progress.WriteLineAsync($"    Found existing file: {expectedFile}");
                return true;
            }

            return false;
        }

        #endregion
    }
}
