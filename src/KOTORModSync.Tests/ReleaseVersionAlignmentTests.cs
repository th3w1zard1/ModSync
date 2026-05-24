// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.Text.Json;

using KOTORModSync.Core;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class ReleaseVersionAlignmentTests
    {
        private static string ResolveRepoRoot()
        {
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..")),
                Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..")),
                Path.GetFullPath(Environment.CurrentDirectory),
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "KOTORModSync.sln")))
                {
                    return candidate;
                }
            }

            Assert.Fail("Could not locate repository root containing KOTORModSync.sln");
            return string.Empty;
        }

        [Test]
        public void CurrentVersion_MatchesReleasePleaseManifest()
        {
            string repoRoot = ResolveRepoRoot();
            string manifestPath = Path.Combine(repoRoot, ".release-please-manifest.json");
            Assert.That(File.Exists(manifestPath), Is.True, $"Missing manifest at {manifestPath}");

            string manifestJson = File.ReadAllText(manifestPath);
            using JsonDocument document = JsonDocument.Parse(manifestJson);
            string manifestVersion = document.RootElement.GetProperty(".").GetString();

            Assert.That(
                MainConfig.CurrentVersion,
                Is.EqualTo(manifestVersion),
                "MainConfig.CurrentVersion must match .release-please-manifest.json so UI and releases stay aligned.");
        }
    }
}
