// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Reflection;

using KOTORModSync.Core.Services;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class TelemetryServiceTests
    {
        [Test]
        public void GetAuthHeaders_WithValidSecret_IncludesSignedSessionHeaders()
        {
            using (TelemetryService service = CreateService(
                       new TelemetryConfiguration
                       {
                           IsEnabled = true,
                           UserConsented = true,
                           SessionId = "session-123",
                       },
                       new TelemetryAuthenticator("super-secret", "session-123")))
            {
                string headers = service.GetAuthHeaders("/v1/metrics");

                Assert.Multiple(() =>
                {
                    Assert.That(headers, Is.Not.Null.And.Not.Empty);
                    Assert.That(headers, Does.Contain("X-KMS-Signature="));
                    Assert.That(headers, Does.Contain("X-KMS-Timestamp="));
                    Assert.That(headers, Does.Contain("X-KMS-Session-ID=session-123"));
                    Assert.That(headers, Does.Contain("X-KMS-Client-Version="));
                });
            }
        }

        [Test]
        public void GetAuthHeaders_WithoutValidSecret_ReturnsEmpty()
        {
            using (TelemetryService service = CreateService(
                       new TelemetryConfiguration
                       {
                           SessionId = "session-123",
                       },
                       new TelemetryAuthenticator(string.Empty, "session-123")))
            {
                Assert.That(service.GetAuthHeaders("/v1/traces"), Is.Empty);
            }
        }

        [Test]
        public void BuildPrometheusUriPrefix_UsesLoopbackOnlyListener()
        {
            Assert.That(
                TelemetryService.BuildPrometheusUriPrefix(9555),
                Is.EqualTo("http://localhost:9555/"));
        }

        private static TelemetryService CreateService(TelemetryConfiguration config, TelemetryAuthenticator authenticator)
        {
            var service = (TelemetryService)Activator.CreateInstance(typeof(TelemetryService), nonPublic: true);
            SetPrivateField(service, "_config", config);
            SetPrivateField(service, "_authenticator", authenticator);
            return service;
        }

        private static void SetPrivateField(TelemetryService service, string fieldName, object value)
        {
            FieldInfo field = typeof(TelemetryService).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
            field.SetValue(service, value);
        }
    }
}
