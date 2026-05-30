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
    [TestFixture]
    [Category("Integration")]
    public sealed class FullBuildCliPipelineTests
    {
        private static readonly string[] ExportFormats = { "json", "yaml", "xml" };

        private static readonly (string Label, string MarkdownRelative, string TomlRelative, string AliasFileName, int ExpectedTomlCount)[] FullBuilds =
        {
            ("KOTOR1", Path.Combine("mod-builds", "content", "k1", "full.md"), Path.Combine("mod-builds", "TOMLs", "KOTOR1_Full.toml"), "KOTOR1_FULL.md", 189),
            ("KOTOR2", Path.Combine("mod-builds", "content", "k2", "full.md"), Path.Combine("mod-builds", "TOMLs", "KOTOR2_Full.toml"), "KOTOR2_FULL.md", 145),
        };

        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _previousMainConfig;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_FullBuildCli_" + Guid.NewGuid());
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

        private static void EnsureHolopatcherInTestResources()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string resourcesDir = Path.Combine(baseDir, "Resources");
            Directory.CreateDirectory(resourcesDir);
            string targetPath = Path.Combine(resourcesDir, "holopatcher");
            if (File.Exists(targetPath))
            {
                return;
            }

            string vendorHolopatcher = Path.GetFullPath(Path.Combine(
                baseDir,
                "..", "..", "..", "..", "..",
                "vendor", "bin", "HoloPatcher_linux"));
            if (!File.Exists(vendorHolopatcher))
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.Copy(vendorHolopatcher, targetPath, overwrite: true);
            }
            else
            {
                File.CreateSymbolicLink(targetPath, vendorHolopatcher);
            }
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

        private static List<ModComponent> MergeModBuildsFull(string markdownPath, string tomlPath)
        {
            var mergeOptions = new MergeOptions
            {
                UseExistingOrder = true,
                PreferExistingInstructions = true,
                PreferExistingOptions = true,
                PreferExistingResourceRegistry = true,
            };

            return ComponentMergeService.MergeInstructionSets(
                tomlPath,
                markdownPath,
                mergeOptions);
        }

        private string WriteMergedToml(string buildLabel, string markdownRelative, string tomlRelative)
        {
            (string markdownPath, string tomlPath) = ResolvePaths(markdownRelative, tomlRelative);
            List<ModComponent> merged = MergeModBuildsFull(markdownPath, tomlPath);
            string outputPath = Path.Combine(_testDirectory, $"{buildLabel}_merged.toml");
            File.WriteAllText(outputPath, ModComponentSerializationService.SerializeModComponentAsString(merged, "TOML"));
            return outputPath;
        }

        [TestCaseSource(nameof(FullBuildCliCases))]
        public void FullBuild_Merged_ValidateDryRunOnly_ExitsZero(
            string buildLabel,
            string markdownRelative,
            string tomlRelative,
            int expectedTomlCount)
        {
            string mergedToml = WriteMergedToml(buildLabel, markdownRelative, tomlRelative);

            int exitCode = ModBuildConverter.Run(new[]
            {
                "validate",
                "--input", mergedToml,
                "--game-dir", _kotorDirectory,
                "--source-dir", _modDirectory,
                "--dry-run-only",
                "--errors-only",
            });

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(0), $"{buildLabel} validate --dry-run-only should pass on template dirs");
                Assert.That(File.ReadAllText(mergedToml), Does.Contain("thisMod"), $"{buildLabel} merged file should contain components");
            });

            var merged = ModComponentSerializationService
                .DeserializeModComponentFromString(File.ReadAllText(mergedToml), "toml")
                .ToList();
            Assert.That(merged.Count, Is.EqualTo(expectedTomlCount));
        }

        [TestCaseSource(nameof(FullBuildCliCases))]
        public void FullBuild_Merged_ConvertAllFormats_ExitZeroAndReload(
            string buildLabel,
            string markdownRelative,
            string tomlRelative,
            int expectedTomlCount)
        {
            (string markdownPath, string tomlPath) = ResolvePaths(markdownRelative, tomlRelative);
            List<ModComponent> canonical = ModComponentSerializationService
                .DeserializeModComponentFromString(File.ReadAllText(tomlPath), "toml")
                .ToList();

            string mergedToml = WriteMergedToml(buildLabel, markdownRelative, tomlRelative);

            foreach (string format in ExportFormats)
            {
                string exportPath = Path.Combine(_testDirectory, $"{buildLabel}_merged.{format}");
                int exitCode = ModBuildConverter.Run(new[]
                {
                    "convert",
                    "--input", mergedToml,
                    "-f", format,
                    "-o", exportPath,
                });

                Assert.Multiple(() =>
                {
                    Assert.That(exitCode, Is.EqualTo(0), $"{buildLabel} convert to {format}");
                    Assert.That(File.Exists(exportPath), Is.True, $"{buildLabel} export file {format}");
                });

                var reloaded = ModComponentSerializationService
                    .DeserializeModComponentFromString(File.ReadAllText(exportPath), format)
                    .ToList();

                Assert.That(reloaded.Count, Is.EqualTo(expectedTomlCount), $"{buildLabel}/{format} component count");
                AssertInstructionParity(canonical, reloaded, $"{buildLabel}/{format}");
            }
        }

        [TestCaseSource(nameof(FullBuildAliasCases))]
        public void FullBuild_MarkdownAlias_DeserializesSameCountAsCanonical(
            string buildLabel,
            string markdownRelative,
            string tomlRelative,
            string aliasFileName,
            int unusedExpectedTomlCount)
        {
            _ = unusedExpectedTomlCount;
            (string markdownPath, _) = ResolvePaths(markdownRelative, tomlRelative);

            string aliasPath = Path.Combine(_testDirectory, aliasFileName);
            File.Copy(markdownPath, aliasPath, overwrite: true);

            var fromCanonical = ModComponentSerializationService.DeserializeModComponentFromString(
                File.ReadAllText(markdownPath),
                "markdown");
            var fromAlias = ModComponentSerializationService.DeserializeModComponentFromString(
                File.ReadAllText(aliasPath),
                "markdown");

            Assert.Multiple(() =>
            {
                Assert.That(fromCanonical, Is.Not.Empty, $"{buildLabel} canonical markdown should deserialize");
                Assert.That(fromAlias.Count, Is.EqualTo(fromCanonical.Count), $"{buildLabel} alias {aliasFileName} should match canonical markdown count");
            });
        }

        private static IEnumerable<TestCaseData> FullBuildCliCases()
        {
            foreach ((string label, string md, string toml, _, int count) in FullBuilds)
            {
                yield return new TestCaseData(label, md, toml, count)
                    .SetName($"{label}_CliPipeline");
            }
        }

        private static IEnumerable<TestCaseData> FullBuildAliasCases()
        {
            foreach ((string label, string md, string toml, string alias, int count) in FullBuilds)
            {
                yield return new TestCaseData(label, md, toml, alias, count)
                    .SetName($"{label}_MarkdownAlias");
            }
        }

        private static void AssertInstructionParity(
            IReadOnlyList<ModComponent> expected,
            IReadOnlyList<ModComponent> actual,
            string context)
        {
            var expectedByName = expected
                .GroupBy(c => c.Name?.Trim() ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var actualByName = actual
                .GroupBy(c => c.Name?.Trim() ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            Assert.That(actualByName.Keys, Is.EquivalentTo(expectedByName.Keys), $"{context}: component names");

            foreach (string name in expectedByName.Keys)
            {
                ModComponent a = expectedByName[name];
                ModComponent b = actualByName[name];

                Assert.Multiple(() =>
                {
                    Assert.That(b.Instructions.Count, Is.EqualTo(a.Instructions.Count), $"{context}: instructions for '{name}'");
                    Assert.That(b.Options.Count, Is.EqualTo(a.Options.Count), $"{context}: options for '{name}'");
                });
            }
        }
    }
}
