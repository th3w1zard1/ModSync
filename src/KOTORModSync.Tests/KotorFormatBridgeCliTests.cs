// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class KotorFormatBridgeCliTests
    {
        private static string BridgeScriptPath =>
            Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "..",
                "tools", "godot-holocron", "bridge", "kotor_format_bridge.py"));

        private static string SampleTwoDaPath =>
            Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..",
                "Fixtures", "kotor", "sample.2da"));

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!File.Exists(BridgeScriptPath))
            {
                Assert.Ignore($"Bridge script not found at {BridgeScriptPath}");
            }

            if (!PyKotorImportWorks())
            {
                Assert.Ignore("PyKotor is not importable; skipping KotorFormatBridgeCliTests.");
            }
        }

        [Test]
        public void Probe_SampleTwoDa_ReturnsMetadata()
        {
            var result = RunBridge("probe", SampleTwoDaPath);
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("extension").GetString(), Is.EqualTo("2da"));
            Assert.That(result.GetProperty("resource_type").GetString(), Is.EqualTo("TwoDA"));
        }

        [Test]
        public void Read_SampleTwoDa_ReturnsRows()
        {
            var result = RunBridge("read", SampleTwoDaPath);
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            var payload = result.GetProperty("payload");
            Assert.That(payload.GetProperty("format").GetString(), Is.EqualTo("twoda"));
            var rows = payload.GetProperty("data").GetProperty("rows");
            Assert.That(rows.GetArrayLength(), Is.EqualTo(2));
        }

        [Test]
        public void Write_RoundTripsSampleTwoDa()
        {
            var readResult = RunBridge("read", SampleTwoDaPath);
            Assert.That(readResult.GetProperty("ok").GetBoolean(), Is.True);
            var payload = readResult.GetProperty("payload").GetRawText();

            string tempPath = Path.Combine(Path.GetTempPath(), "kotor_bridge_roundtrip_" + Guid.NewGuid() + ".2da");
            try
            {
                var writeResult = RunBridge("write", tempPath, "--payload", payload);
                Assert.That(writeResult.GetProperty("ok").GetBoolean(), Is.True);
                Assert.That(File.Exists(tempPath), Is.True);

                var reread = RunBridge("read", tempPath);
                Assert.That(reread.GetProperty("ok").GetBoolean(), Is.True);
                Assert.That(
                    reread.GetProperty("payload").GetProperty("data").GetProperty("rows").GetArrayLength(),
                    Is.EqualTo(2));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        [Test]
        public void SupportedTypes_ReturnsNonEmptyList()
        {
            var result = RunBridge("supported-types");
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("types").GetArrayLength(), Is.GreaterThan(10));
        }

        private static bool PyKotorImportWorks()
        {
            var probe = RunBridgeRaw("supported-types");
            return probe.ExitCode == 0 && probe.Json.TryGetProperty("ok", out var ok) && ok.GetBoolean();
        }

        private static JsonElement RunBridge(params string[] args)
        {
            var raw = RunBridgeRaw(args);
            Assert.That(raw.ExitCode, Is.EqualTo(0), () => raw.StdOut + raw.StdErr);
            return raw.Json;
        }

        private static (int ExitCode, string StdOut, string StdErr, JsonElement Json) RunBridgeRaw(params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(BridgeScriptPath);
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            Assert.That(process, Is.Not.Null);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            string jsonLine = ExtractJsonLine(stdout);
            using var doc = JsonDocument.Parse(jsonLine);
            return (process.ExitCode, stdout, stderr, doc.RootElement.Clone());
        }

        private static string ExtractJsonLine(string stdout)
        {
            string[] lines = stdout.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    return trimmed;
                }
            }

            throw new InvalidOperationException("Bridge did not emit JSON on stdout: " + stdout);
        }
    }
}
