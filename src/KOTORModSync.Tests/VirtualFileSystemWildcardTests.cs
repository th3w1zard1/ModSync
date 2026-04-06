// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KOTORModSync.Core;
using KOTORModSync.Core.Services.FileSystem;
using KOTORModSync.Core.Utility;

using NUnit.Framework;

namespace KOTORModSync.Tests
{

    [TestFixture]
    public class VirtualFileSystemWildcardTests
    {
        private string _testRootDir = string.Empty;
        private string _sourceDir = string.Empty;
        private string _destinationDir = string.Empty;
        private string _virtualTestDir = string.Empty;
        private string _realTestDir = string.Empty;
        private string _sevenZipPath = string.Empty;

        [SetUp]
        public void SetUp()
        {

            _testRootDir = Path.Combine(Path.GetTempPath(), "KOTORModSync_Wildcard_Tests_" + Guid.NewGuid().ToString("N"));
            _virtualTestDir = Path.Combine(_testRootDir, "Virtual");
            _realTestDir = Path.Combine(_testRootDir, "Real");
            _sourceDir = Path.Combine(_testRootDir, "Source");
            _destinationDir = Path.Combine(_testRootDir, "Destination");

            _ = Directory.CreateDirectory(_testRootDir);
            _ = Directory.CreateDirectory(_virtualTestDir);
            _ = Directory.CreateDirectory(_realTestDir);
            _ = Directory.CreateDirectory(_sourceDir);
            _ = Directory.CreateDirectory(_destinationDir);

            _sevenZipPath = Find7Zip();

            _ = new MainConfig
            {
                sourcePath = new DirectoryInfo(_sourceDir),
                destinationPath = new DirectoryInfo(_destinationDir),
                caseInsensitivePathing = true,
                useMultiThreadedIO = false,
            };
        }

        [TearDown]
        public void Dispose()
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
        }

        private static string Find7Zip()
        {
            string[] paths = new string[]
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
            catch { }

            throw new InvalidOperationException("7-Zip not found. Please install 7-Zip to run these tests.");
        }

        private void CreateArchive(string archivePath, Dictionary<string, string> files)
        {
            string tempDir = Path.Combine(_testRootDir, "temp_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(tempDir);

            try
            {
                foreach (KeyValuePair<string, string> kvp in files)
                {
                    string filePath = Path.Combine(tempDir, kvp.Key);
                    Assert.That(filePath, Is.Not.Null);
                    string fileDir = Path.GetDirectoryName(filePath);
                    Assert.That(fileDir, Is.Not.Null);
                    if (!string.IsNullOrEmpty(fileDir))
                    {
                        _ = Directory.CreateDirectory(fileDir);
                    }

                    File.WriteAllText(filePath, kvp.Value);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _sevenZipPath,
                    Arguments = $"a -tzip \"{archivePath}\" \"{tempDir}\\*\" -r",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(startInfo))
                {
                    Assert.That(process, Is.Not.Null);
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"7-Zip failed: {process.StandardError.ReadToEnd()}");
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private async Task<(VirtualFileSystemProvider virtualProvider, RealFileSystemProvider realProvider)> RunBothProviders(
            List<Instruction> instructions,
            string realSourcePath,
            string realDestPath)
        {

            string virtualSourceCopy = Path.Combine(_virtualTestDir, "source");
            string realSourceCopy = Path.Combine(_realTestDir, "source");
            string virtualDestCopy = Path.Combine(_virtualTestDir, "dest");
            string realDestCopy = Path.Combine(_realTestDir, "dest");

            _ = Directory.CreateDirectory(virtualSourceCopy);
            _ = Directory.CreateDirectory(realSourceCopy);
            _ = Directory.CreateDirectory(virtualDestCopy);
            _ = Directory.CreateDirectory(realDestCopy);

            CopyDirectory(realSourcePath, virtualSourceCopy);
            CopyDirectory(realSourcePath, realSourceCopy);
            CopyDirectory(realDestPath, virtualDestCopy);
            CopyDirectory(realDestPath, realDestCopy);

            DirectoryInfo originalSourcePath = MainConfig.SourcePath;
            DirectoryInfo originalDestPath = MainConfig.DestinationPath;

            try
            {

                _ = new MainConfig
                {
                    sourcePath = new DirectoryInfo(virtualSourceCopy),
                    destinationPath = new DirectoryInfo(virtualDestCopy),
                };

                var virtualProvider = new VirtualFileSystemProvider();
                var virtualComponent = new ModComponent { Name = "TestComponent" };

                var virtualInstructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>(instructions);
                _ = await virtualComponent.ExecuteInstructionsAsync(
                    virtualInstructions,
                    new List<ModComponent>(),
                    CancellationToken.None,
                    virtualProvider
                );
                await TestContext.Progress.WriteLineAsync($"Virtual Provider - Files tracked: {virtualProvider.GetTrackedFiles().Count}");
                await TestContext.Progress.WriteLineAsync($"Virtual Provider - Issues: {virtualProvider.GetValidationIssues().Count}");

                _ = new MainConfig
                {
                    sourcePath = new DirectoryInfo(realSourceCopy),
                    destinationPath = new DirectoryInfo(realDestCopy),
                };

                var realProvider = new RealFileSystemProvider();
                var realComponent = new ModComponent { Name = "TestComponent" };

                var realInstructions = new List<Instruction>(instructions);
                foreach (Instruction instruction in instructions)
                {
                    var newInstruction = new Instruction
                    {
                        Action = instruction.Action,
                        Source = instruction.Source.ToList(),
                        Destination = instruction.Destination,
                        Overwrite = instruction.Overwrite,
                        Arguments = instruction.Arguments,
                    };
                    realInstructions.Add(newInstruction);
                }

                var realInstructionsObservable = new System.Collections.ObjectModel.ObservableCollection<Instruction>(realInstructions);
                _ = await realComponent.ExecuteInstructionsAsync(
                    realInstructionsObservable,
                    new List<ModComponent>(),
                    CancellationToken.None,
                    realProvider
                );
                await TestContext.Progress.WriteLineAsync("Real Provider - Executed successfully");

                return (virtualProvider, realProvider);
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

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            if (!Directory.Exists(sourceDir))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destFile = Path.Combine(destDir, relativePath);
                string destFileDir = Path.GetDirectoryName(destFile);

                if (!string.IsNullOrEmpty(destFileDir))
                {
                    _ = Directory.CreateDirectory(destFileDir);
                }

                File.Copy(file, destFile, overwrite: true);
            }
        }

        private static void AssertFileSystemsMatch(VirtualFileSystemProvider virtualProvider, string realDir, string subfolder = "dest")
        {

            string virtBasePath = Path.GetDirectoryName(Path.GetDirectoryName(realDir));
            Assert.That(virtBasePath, Is.Not.Null);
            string virtualPath = Path.Combine(virtBasePath, "Virtual", subfolder);

            var virtualFiles = virtualProvider.GetTrackedFiles()
                .Where(f => f.StartsWith(virtualPath, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(virtualPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            HashSet<string> realFiles = Directory.Exists(realDir)
                ? Directory.GetFiles(realDir, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(realDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TestContext.Progress.WriteLine($"\n=== Virtual Files ({virtualFiles.Count}) in '{subfolder}' ===");
            foreach (string file in virtualFiles.OrderBy(f => f, StringComparer.Ordinal))
            {
                TestContext.Progress.WriteLine($"  {file}");
            }

            TestContext.Progress.WriteLine($"\n=== Real Files ({realFiles.Count}) in '{subfolder}' ===");
            foreach (string file in realFiles.OrderBy(f => f, StringComparer.Ordinal))
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

        [Test]
        public async Task Test_WildcardMove_StarPattern()
        {

            string archivePath = Path.Combine(_sourceDir, "files.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "Content 1" },
                { "file2.txt", "Content 2" },
                { "file3.txt", "Content 3" },
                { "readme.md", "Readme" },
                { "data.dat", "Data" },
            });

            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { @"<<modDirectory>>\files.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { @"<<modDirectory>>\files\*.txt" }, Destination = "<<kotorDirectory>>", Overwrite = true },
            };

            (VirtualFileSystemProvider v, _) = await RunBothProviders(instructions, _sourceDir, _destinationDir);

            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues().Count(issue => issue.Severity >= ValidationSeverity.Error), Is.EqualTo(0),
                    "Wildcard move with star pattern should not produce validation errors");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should exist");
            });
            AssertFileSystemsMatch(v, Path.Combine(_realTestDir, "dest"));
        }

        [Test]
        public async Task Test_WildcardCopy_QuestionMarkPattern()
        {

            string archivePath = Path.Combine(_sourceDir, "similar.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "1" },
                { "file2.txt", "2" },
                { "file3.txt", "3" },
                { "fileA.txt", "A" },
                { "fileAB.txt", "AB" },
            });

            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { @"<<modDirectory>>\similar.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Copy, Source = new List<string> { @"<<modDirectory>>\similar\file?.txt" }, Destination = "<<kotorDirectory>>", Overwrite = true },
            };

            (VirtualFileSystemProvider v, _) = await RunBothProviders(instructions, _sourceDir, _destinationDir);

            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues().Count(issue => issue.Severity >= ValidationSeverity.Error), Is.EqualTo(0),
                    "Wildcard move with star pattern should not produce validation errors");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should exist");
            });
            AssertFileSystemsMatch(v, Path.Combine(_realTestDir, "dest"));
        }

        [Test]
        public async Task Test_WildcardDelete_ComplexPattern()
        {

            string archivePath = Path.Combine(_sourceDir, "mixed.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "data_backup_2023.txt", "Old backup" },
                { "data_backup_2024.txt", "New backup" },
                { "data_current.txt", "Current" },
                { "logs_backup_2023.log", "Log backup" },
                { "config.txt", "Config" },
            });

            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { @"<<modDirectory>>\mixed.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Delete, Source = new List<string> { @"<<modDirectory>>\mixed\data_backup_*.txt" } },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { @"<<modDirectory>>\mixed\*" }, Destination = "<<kotorDirectory>>", Overwrite = true },
            };

            (VirtualFileSystemProvider v, _) = await RunBothProviders(instructions, _sourceDir, _destinationDir);

            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues().Count(issue => issue.Severity >= ValidationSeverity.Error), Is.EqualTo(0),
                    "Wildcard move with star pattern should not produce validation errors");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should exist");
            });
            AssertFileSystemsMatch(v, Path.Combine(_realTestDir, "dest"));
        }

        [Test]
        public async Task Test_WildcardInArchiveName()
        {

            string archive1 = Path.Combine(_sourceDir, "mod_v1.0.zip");
            string archive2 = Path.Combine(_sourceDir, "mod_v2.0.zip");
            string archive3 = Path.Combine(_sourceDir, "other.zip");

            CreateArchive(archive1, new Dictionary<string, string>(StringComparer.Ordinal)
            {
            { "version.txt", "2.0" },
        });

            CreateArchive(archive2, new Dictionary<string, string>(StringComparer.Ordinal)
            {
            { "version.txt", "2.0" },
        });

            CreateArchive(archive3, new Dictionary<string, string>(StringComparer.Ordinal)
            {
            { "data.txt", "other" },
        });

            var instructions = new List<Instruction>
        {
            new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { @"<<modDirectory>>\mod_*.zip" }, Destination = "<<modDirectory>>" },
        };

            (VirtualFileSystemProvider v, _) = await RunBothProviders(instructions, _sourceDir, _destinationDir);

            Assert.That(v.GetValidationIssues().Count(issue => issue.Severity >= ValidationSeverity.Error), Is.EqualTo(0));

            AssertFileSystemsMatch(v, Path.Combine(_realTestDir, "source"), "source");

            string virtualSourcePath = Path.Combine(_virtualTestDir, "source");
            var extractedFiles = v.GetTrackedFiles()
                .Where(f => f.StartsWith(virtualSourcePath, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Replace(virtualSourcePath + Path.DirectorySeparatorChar, ""))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(extractedFiles, Is.Not.Null, "Extracted files list should not be null");
                Assert.That(extractedFiles, Is.Not.Empty, "Extracted files list should not be empty");
                Assert.That(extractedFiles.Count(x => x.EndsWith("version.txt", StringComparison.Ordinal)), Is.EqualTo(2),
                    "Should extract version.txt from both matching archives");
                Assert.That(extractedFiles, Has.Some.EqualTo(@"mod_v1.0\version.txt"),
                    "Should extract version.txt from mod_v1.0 archive");
                Assert.That(extractedFiles, Has.Some.EqualTo(@"mod_v2.0\version.txt"),
                    "Should extract version.txt from mod_v2.0 archive");
                Assert.That(extractedFiles.Any(x => x.Contains("data.txt")), Is.False,
                    "File from non-matching archive should not be extracted");
                Assert.That(extractedFiles.Any(x => x.Contains("other")), Is.False,
                    "Files from non-matching archive should not be extracted");
            });
        }

        [Test]
        public async Task Test_WildcardMultiplePatterns()
        {

            string archivePath = Path.Combine(_sourceDir, "files.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "script1.ncs", "Script 1" },
                { "script2.ncs", "Script 2" },
                { "dialog1.dlg", "Dialog 1" },
                { "dialog2.dlg", "Dialog 2" },
                { "appearance.2da", "Appearance" },
                { "portraits.2da", "Portraits" },
                { "readme.txt", "Readme" },
            });

            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { @"<<modDirectory>>\files.zip" }, Destination = "<<modDirectory>>" },
                new Instruction
                {
                    Action = Instruction.ActionType.Copy,
                    Source = new List<string>
                    {
                        @"<<modDirectory>>\files\*.ncs",
                        @"<<modDirectory>>\files\*.dlg",
                        @"<<modDirectory>>\files\*.2da",
                    },
                    Destination = @"<<kotorDirectory>>\override",
                    Overwrite = true,
                },
            };

            (VirtualFileSystemProvider v, _) = await RunBothProviders(instructions, _sourceDir, _destinationDir);

            Assert.Multiple(() =>
            {
                Assert.That(v, Is.Not.Null, "Virtual file system provider should not be null");
                Assert.That(v.GetValidationIssues().Count(issue => issue.Severity >= ValidationSeverity.Error), Is.EqualTo(0),
                    "Wildcard move with star pattern should not produce validation errors");
                Assert.That(v.GetValidationIssues(), Is.Not.Null, "Validation issues list should not be null");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should exist");
            });
            AssertFileSystemsMatch(v, Path.Combine(_realTestDir, "dest"));

            string virtualDestPath = Path.Combine(_virtualTestDir, "dest", "override");
            var copiedFiles = v.GetTrackedFiles()
                .Where(f => f.StartsWith(virtualDestPath, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(copiedFiles, Is.Not.Null, "Copied files list should not be null");
                Assert.That(copiedFiles, Has.Count.EqualTo(6),
                    "Should copy 6 files matching wildcard patterns (2 .ncs, 2 .dlg, 2 .2da)");
                Assert.That(copiedFiles, Does.Contain("script1.ncs"), "Should contain first .ncs file");
                Assert.That(copiedFiles, Does.Contain("script2.ncs"), "Should contain second .ncs file");
                Assert.That(copiedFiles, Does.Contain("dialog1.dlg"), "Should contain first .dlg file");
                Assert.That(copiedFiles, Does.Contain("dialog2.dlg"), "Should contain second .dlg file");
                Assert.That(copiedFiles, Does.Contain("appearance.2da"), "Should contain first .2da file");
                Assert.That(copiedFiles, Does.Contain("portraits.2da"), "Should contain second .2da file");
                Assert.That(copiedFiles, Does.Not.Contain("readme.txt"),
                    "Files not matching wildcard patterns should not be copied");
            });
        }

        [Test]
        public void Test_WildcardNoMatches_ShouldProduceValidationError()
        {

            string archivePath = Path.Combine(_sourceDir, "empty.zip");
            CreateArchive(archivePath, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "file1.txt", "1" },
                { "file2.txt", "2" },
            });

            var instructions = new List<Instruction>
            {
                new Instruction { Action = Instruction.ActionType.Extract, Source = new List<string> { @"<<modDirectory>>\empty.zip" }, Destination = "<<modDirectory>>" },
                new Instruction { Action = Instruction.ActionType.Move, Source = new List<string> { @"<<modDirectory>>\empty\*.dat" }, Destination = "<<kotorDirectory>>", Overwrite = true },
            };

            var exception = Assert.ThrowsAsync<Core.Exceptions.WildcardPatternNotFoundException>(
                async () => await RunBothProviders(instructions, _sourceDir, _destinationDir).ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Wildcard pattern with no matches should throw WildcardPatternNotFoundException");
                Assert.That(File.Exists(archivePath), Is.True, "Source archive should exist");
            });
        }
    }
}
