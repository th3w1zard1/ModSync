// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KOTORModSync.Core;
using KOTORModSync.Core.Services;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class FullBuildMarkdownMergeRoundTripTests
    {
        private static readonly string[] Formats = { "TOML", "JSON", "YAML", "XML" };

        private static readonly (string Label, string MarkdownRelative, string TomlRelative, int ExpectedTomlCount)[] FullBuilds =
        {
            ("KOTOR1", Path.Combine("mod-builds", "content", "k1", "full.md"), Path.Combine("mod-builds", "TOMLs", "KOTOR1_Full.toml"), 189),
            ("KOTOR2", Path.Combine("mod-builds", "content", "k2", "full.md"), Path.Combine("mod-builds", "TOMLs", "KOTOR2_Full.toml"), 145),
        };

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

        private static List<ModComponent> LoadCanonicalToml(string tomlPath)
        {
            return ModComponentSerializationService
                .DeserializeModComponentFromString(File.ReadAllText(tomlPath), "toml")
                .ToList();
        }

        /// <summary>
        /// Two-source merge: TOML (existing) supplies instructions; markdown (incoming) supplies human metadata and ordering.
        /// </summary>
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

        [TestCaseSource(nameof(FullBuildMarkdownDeserializeCases))]
        public void FullBuild_Markdown_Deserializes(string buildLabel, string markdownRelative, string tomlRelative)
        {
            (string markdownPath, _) = ResolvePaths(markdownRelative, tomlRelative);

            IReadOnlyList<ModComponent> fromMarkdown = ModComponentSerializationService.DeserializeModComponentFromString(
                File.ReadAllText(markdownPath),
                "markdown");

            Assert.That(fromMarkdown, Is.Not.Null);
            Assert.That(fromMarkdown.Count, Is.GreaterThan(100), $"{buildLabel} markdown should parse a large component set");
        }

        [TestCaseSource(nameof(FullBuildMergeMatchCases))]
        public void FullBuild_Merge_MatchesTomlInstructionParity(
            string buildLabel,
            string markdownRelative,
            string tomlRelative,
            int expectedTomlCount)
        {
            (string markdownPath, string tomlPath) = ResolvePaths(markdownRelative, tomlRelative);

            List<ModComponent> canonical = LoadCanonicalToml(tomlPath);
            List<ModComponent> merged = MergeModBuildsFull(markdownPath, tomlPath);

            Assert.That(canonical.Count, Is.EqualTo(expectedTomlCount), $"{buildLabel} canonical TOML count");
            Assert.That(merged.Count, Is.EqualTo(expectedTomlCount), $"{buildLabel} merged count should match TOML");

            AssertInstructionParity(canonical, merged, $"{buildLabel}/merge");
        }

        [TestCaseSource(nameof(FullBuildMergeRoundTripCases))]
        public void FullBuild_Merged_RoundTrip_PreservesInstructions(
            string buildLabel,
            string markdownRelative,
            string tomlRelative,
            string format)
        {
            (string markdownPath, string tomlPath) = ResolvePaths(markdownRelative, tomlRelative);

            List<ModComponent> canonical = LoadCanonicalToml(tomlPath);
            List<ModComponent> merged = MergeModBuildsFull(markdownPath, tomlPath);

            string serialized = ModComponentSerializationService.SerializeModComponentAsString(merged, format);
            IReadOnlyList<ModComponent> roundTripped = ModComponentSerializationService.DeserializeModComponentFromString(serialized, format);

            AssertInstructionParity(canonical, roundTripped.ToList(), $"{buildLabel}/merged/{format}");
        }

        private static IEnumerable<TestCaseData> FullBuildMarkdownDeserializeCases()
        {
            foreach ((string label, string md, string toml, _) in FullBuilds)
            {
                yield return new TestCaseData(label, md, toml)
                    .SetName($"{label}_Markdown_Deserializes");
            }
        }

        private static IEnumerable<TestCaseData> FullBuildMergeMatchCases()
        {
            foreach ((string label, string md, string toml, int count) in FullBuilds)
            {
                yield return new TestCaseData(label, md, toml, count)
                    .SetName($"{label}_Merge_MatchesTomlInstructionParity");
            }
        }

        private static IEnumerable<TestCaseData> FullBuildMergeRoundTripCases()
        {
            foreach ((string label, string md, string toml, _) in FullBuilds)
            {
                foreach (string format in Formats)
                {
                    yield return new TestCaseData(label, md, toml, format)
                        .SetName($"{label}_Merged_RoundTrip_{format}");
                }
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
