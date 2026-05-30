// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using KOTORModSync.Core;
using KOTORModSync.Core.CLI;
using KOTORModSync.Core.Services;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    /// <summary>
    /// Exercises the CLI install path against merged mod-builds full builds.
    /// Best-effort mode skips missing archives; a full archive-complete install remains manual/local.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("LongRunning")]
    public sealed class FullBuildInstallLongRunningTests
    {
        private static readonly (string Label, string MarkdownRelative, string TomlRelative, int ExpectedTomlCount)[] FullBuilds =
        {
            ("KOTOR1", Path.Combine("mod-builds", "content", "k1", "full.md"), Path.Combine("mod-builds", "TOMLs", "KOTOR1_Full.toml"), 189),
            ("KOTOR2", Path.Combine("mod-builds", "content", "k2", "full.md"), Path.Combine("mod-builds", "TOMLs", "KOTOR2_Full.toml"), 145),
        };

        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _previousMainConfig;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_FullBuildInstall_" + Guid.NewGuid());
            _modDirectory = Path.Combine(_testDirectory, "Mods");
            _kotorDirectory = Path.Combine(_testDirectory, "KOTOR");
            Directory.CreateDirectory(_modDirectory);
            Directory.CreateDirectory(_kotorDirectory);
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override"));
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "data"));
            File.WriteAllText(Path.Combine(_kotorDirectory, "swkotor.exe"), "fake exe");
            File.WriteAllText(Path.Combine(_kotorDirectory, "dialog.tlk"), "fake dialog");

            _previousMainConfig = MainConfig.Instance;
            MainConfig.Instance = new MainConfig
            {
                sourcePath = new DirectoryInfo(_modDirectory),
                destinationPath = new DirectoryInfo(_kotorDirectory),
            };

            EnsureHolopatcherInTestResources();
        }

        [TearDown]
        public void TearDown()
        {
            MainConfig.Instance = _previousMainConfig;

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

        [TestCaseSource(nameof(FullBuildInstallCases))]
        public void FullBuild_Merged_BestEffortInstall_ExitsZero_LongRunning(
            string buildLabel,
            string markdownRelative,
            string tomlRelative,
            int expectedTomlCount)
        {
            (string markdownPath, string tomlPath) = ResolvePaths(markdownRelative, tomlRelative);
            string mergedToml = Path.Combine(_testDirectory, $"{buildLabel}_merged.toml");

            int mergeExit = ModBuildConverter.Run(new[]
            {
                "merge",
                "--existing", tomlPath,
                "--incoming", markdownPath,
                "--use-existing-order",
                "--prefer-existing-instructions",
                "--prefer-existing-options",
                "--prefer-existing-modlinks",
                "-f", "toml",
                "-o", mergedToml,
            });

            Assert.That(mergeExit, Is.EqualTo(0), $"{buildLabel} merge should succeed");
            Assert.That(File.Exists(mergedToml), Is.True);

            var merged = ModComponentSerializationService
                .DeserializeModComponentFromString(File.ReadAllText(mergedToml), "toml")
                .ToList();

            Assert.That(merged.Count, Is.EqualTo(expectedTomlCount), $"{buildLabel} merged component count");
            Assert.That(merged.Sum(c => c.Instructions.Count), Is.GreaterThan(0));

            foreach (ModComponent component in merged)
            {
                component.IsSelected = true;
            }

            FileLoadingService.SaveToFile(merged, mergedToml);

            int installExit = ModBuildConverter.Run(new[]
            {
                "install",
                "-i", mergedToml,
                "-g", _kotorDirectory,
                "-s", _modDirectory,
                "--use-file-selection",
                "--best-effort",
                "--skip-validation",
                // Prevent best-effort from deselecting Nexus-only mods when no API key is configured.
                "--nexus-api-key", "test-placeholder-key",
            });

            Assert.That(installExit, Is.EqualTo(0),
                $"{buildLabel} best-effort install should complete (skipping mods without local archives)");
        }

        private static IEnumerable<TestCaseData> FullBuildInstallCases()
        {
            foreach ((string label, string md, string toml, int count) in FullBuilds)
            {
                yield return new TestCaseData(label, md, toml, count)
                    .SetName($"{label}_Merged_BestEffortInstall");
            }
        }

        private static (string markdownPath, string tomlPath) ResolvePaths(string markdownRelative, string tomlRelative)
        {
            string repoRoot = ResolveRepoRoot();
            string markdownPath = Path.Combine(repoRoot, markdownRelative);
            string tomlPath = Path.Combine(repoRoot, tomlRelative);

            if (!File.Exists(markdownPath) || !File.Exists(tomlPath))
            {
                Assert.Ignore($"mod-builds sources not found: {markdownPath} / {tomlPath}");
            }

            return (markdownPath, tomlPath);
        }

        private static string ResolveRepoRoot()
        {
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..")),
                Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..")),
                Path.GetFullPath(Environment.CurrentDirectory),
            };

            foreach (string candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                if (File.Exists(Path.Combine(candidate, "KOTORModSync.sln")))
                {
                    return candidate;
                }
            }

            throw new DirectoryNotFoundException("Could not locate repository root containing KOTORModSync.sln");
        }

        private static void EnsureHolopatcherInTestResources()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string resourcesDir = Path.Combine(baseDir, "Resources");
            Directory.CreateDirectory(resourcesDir);
            string holopatcherPath = Path.Combine(resourcesDir, "holopatcher");

            if (File.Exists(holopatcherPath) || Directory.Exists(holopatcherPath))
            {
                return;
            }

            string repoRoot = ResolveRepoRoot();
            string[] vendorCandidates =
            {
                Path.Combine(repoRoot, "vendor", "bin", "HoloPatcher_linux"),
                Path.Combine(repoRoot, "vendor", "bin", "HoloPatcher"),
            };

            foreach (string vendorPath in vendorCandidates)
            {
                if (!File.Exists(vendorPath))
                {
                    continue;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.Copy(vendorPath, holopatcherPath, overwrite: true);
                }
                else
                {
                    File.Copy(vendorPath, holopatcherPath, overwrite: true);
                    try
                    {
                        File.SetUnixFileMode(holopatcherPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    catch
                    {
                        // Ignore on platforms without Unix file modes
                    }
                }

                return;
            }
        }
    }
}
