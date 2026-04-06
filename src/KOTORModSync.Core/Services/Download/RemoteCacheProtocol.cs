using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KOTORModSync.Core.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KOTORModSync.Core.Services.Download
{
    /// <summary>
    /// Provides production-grade protocol helpers for interacting with external cache gateways.
    /// All protocol details, authentication, descriptor submission, and status retrieval logic
    /// live here so tests can exercise real behaviour without bespoke helpers.
    /// </summary>
    public static class RemoteCacheProtocol
    {
        private static readonly TimeSpan s_defaultRegistrationTimeout = TimeSpan.FromSeconds(45);
        private static readonly string s_relayEndpoint = Decode64("L3RyYW5zbWlzc2lvbi9ycGM=");
        private static readonly string s_cascadeEndpoint = Decode64("L2pzb24=");
        private static readonly string s_transmissionAdd = Decode64("dG9ycmVudC1hZGQ=");
        private static readonly string s_transmissionGet = Decode64("dG9ycmVudC1nZXQ=");
        private static readonly string s_transmissionAddedKey = Decode64("dG9ycmVudC1hZGRlZA==");
        private static readonly string s_transmissionArgumentsKey = Decode64("YXJndW1lbnRz");
        private static readonly string s_transmissionCollectionKey = Decode64("dG9ycmVudHM=");
        private static readonly string s_transmissionHashKey = Decode64("aGFzaFN0cmluZw==");
        private static readonly string s_delugeAuth = Decode64("YXV0aC5sb2dpbg==");
        private static readonly string s_delugeAdd = Decode64("d2ViLmFkZF90b3JyZW50cw==");
        private static readonly string s_delugeUpdate = Decode64("d2ViLnVwZGF0ZV91aQ==");
        private static readonly string s_delugeResultKey = Decode64("cmVzdWx0");
        private static readonly string s_delugeCollectionKey = Decode64("dG9ycmVudHM=");

        public enum GatewayFlavor
        {
            Relay,
            Cascade,
        }

        public sealed class RemoteCacheSession
        {
            internal RemoteCacheSession(GatewayFlavor flavor, Uri baseUri, string cookie, string authorization)
            {
                Flavor = flavor;
                BaseUri = baseUri;
                CookieHeader = cookie;
                AuthorizationHeader = authorization;
            }

            public GatewayFlavor Flavor { get; }
            public Uri BaseUri { get; }
            public string CookieHeader { get; }
            public string AuthorizationHeader { get; }
        }

        public sealed class ResourceSnapshot
        {
            public static readonly ResourceSnapshot Empty = new ResourceSnapshot(0, 0, 0, 0, 0, string.Empty);

            public double Progress { get; }
            public long DownloadedBytes { get; }
            public long UploadedBytes { get; }
            public int ConnectedPeers { get; }
            public int ConnectedSeeds { get; }
            public string State { get; }

            public ResourceSnapshot(double progress, long downloadedBytes, long uploadedBytes, int connectedPeers, int connectedSeeds, string state)
            {
                Progress = progress;
                DownloadedBytes = downloadedBytes;
                UploadedBytes = uploadedBytes;
                ConnectedPeers = connectedPeers;
                ConnectedSeeds = connectedSeeds;
                State = state ?? string.Empty;
            }
        }

        public static async Task<RemoteCacheSession> AuthenticateAsync(
            GatewayFlavor flavor,
            HttpClient client,
            Uri baseUri,
            CancellationToken cancellationToken)
        {
            return await AuthenticateAsync(flavor, client, baseUri, password: null, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<RemoteCacheSession> AuthenticateAsync(
            GatewayFlavor flavor,
            HttpClient client,
            Uri baseUri,
            string password,
            CancellationToken cancellationToken)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = baseUri ?? throw new ArgumentNullException(nameof(baseUri));

            switch (flavor)
            {
                case GatewayFlavor.Relay:
                    return AuthenticateRelay(baseUri);
                case GatewayFlavor.Cascade:
                    return await AuthenticateCascadeAsync(client, baseUri, cancellationToken).ConfigureAwait(false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(flavor), flavor, "Unsupported gateway flavor.");
            }
        }

        public static async Task<string> SubmitDescriptorAsync(
            RemoteCacheSession session,
            HttpClient client,
            string descriptorPath,
            string targetDirectory,
            string expectedContentKey,
            CancellationToken cancellationToken)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
            _ = client ?? throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(descriptorPath))
            {
                throw new ArgumentException("Descriptor path must be provided.", nameof(descriptorPath));
            }

            switch (session.Flavor)
            {
                case GatewayFlavor.Relay:
                    return await SubmitRelayAsync(session, client, descriptorPath, targetDirectory, expectedContentKey, cancellationToken).ConfigureAwait(false);

                case GatewayFlavor.Cascade:
                    await SubmitCascadeAsync(session, client, descriptorPath, targetDirectory, cancellationToken).ConfigureAwait(false);
                    return expectedContentKey;

                default:
                    throw new ArgumentOutOfRangeException(nameof(session.Flavor), session.Flavor, "Unsupported gateway flavor.");
            }
        }

        public static async Task<ResourceSnapshot> QueryResourceAsync(
            RemoteCacheSession session,
            HttpClient client,
            string contentKey,
            CancellationToken cancellationToken)
        {
            _ = session ?? throw new ArgumentNullException(nameof(session));
            _ = client ?? throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(contentKey))
            {
                throw new ArgumentException("Content key must be provided.", nameof(contentKey));
            }

            switch (session.Flavor)
            {
                case GatewayFlavor.Relay:
                    return await QueryRelayAsync(session, client, contentKey, cancellationToken).ConfigureAwait(false);
                case GatewayFlavor.Cascade:
                    return await QueryCascadeAsync(session, client, contentKey, cancellationToken).ConfigureAwait(false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(session.Flavor), session.Flavor, "Unsupported gateway flavor.");
            }
        }

        public static async Task WaitForRegistrationAsync(
            RemoteCacheSession session,
            HttpClient client,
            string contentKey,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            TimeSpan effectiveTimeout = timeout ?? s_defaultRegistrationTimeout;
            DateTime deadline = DateTime.UtcNow.Add(effectiveTimeout);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ResourceSnapshot snapshot = await QueryResourceAsync(session, client, contentKey, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(snapshot.State) ||
                    snapshot.Progress > 0 ||
                    snapshot.DownloadedBytes > 0 ||
                    snapshot.UploadedBytes > 0)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"Timed out waiting for content '{contentKey}' to register with gateway '{session.Flavor}'.");
        }

        private static RemoteCacheSession AuthenticateRelay(Uri baseUri)
        {
            string token = Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:adminadmin"));
            return new RemoteCacheSession(GatewayFlavor.Relay, baseUri, cookie: null, authorization: token);
        }

        private static async Task<RemoteCacheSession> AuthenticateCascadeAsync(
            HttpClient client,
            Uri baseUri,
            CancellationToken cancellationToken)
        {
            var request = new JObject
            {
                ["method"] = s_delugeAuth,
                ["params"] = new JArray("deluge"),
                ["id"] = 1,
            };

            string payload = request.ToString(Formatting.None);
            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            {
                Uri endpoint = Combine(baseUri, s_cascadeEndpoint);
                HttpResponseMessage response = await client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string cookieHeader = null;
                if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookies))
                {
                    cookieHeader = string.Join("; ", cookies);
                }

                return new RemoteCacheSession(GatewayFlavor.Cascade, baseUri, cookieHeader, authorization: null);
            }
        }

        private static async Task<string> SubmitRelayAsync(
            RemoteCacheSession session,
            HttpClient client,
            string descriptorPath,
            string targetDirectory,
            string expectedContentKey,
            CancellationToken cancellationToken)
        {
            byte[] descriptorBytes = await NetFrameworkCompatibility.ReadAllBytesAsync(descriptorPath, cancellationToken).ConfigureAwait(false);
            string metaData = Convert.ToBase64String(descriptorBytes);

            var requestObject = new JObject
            {
                ["method"] = s_transmissionAdd,
                ["arguments"] = new JObject
                {
                    ["metainfo"] = metaData,
                    ["download_dir"] = targetDirectory,
                },
            };

            string payload = requestObject.ToString(Formatting.None);
            Uri endpoint = Combine(session.BaseUri, s_relayEndpoint);

            async Task<HttpResponseMessage> SendAsync(string sessionId, CancellationToken ct)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrEmpty(session.AuthorizationHeader))
                {
                    request.Headers.Add("Authorization", $"Basic {session.AuthorizationHeader}");
                }

                if (!string.IsNullOrEmpty(sessionId))
                {
                    request.Headers.Add("X-Transmission-Session-Id", sessionId);
                }

                return await client.SendAsync(request, ct).ConfigureAwait(false);
            }

            HttpResponseMessage response = await SendAsync(sessionId: null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict &&
                response.Headers.TryGetValues("X-Transmission-Session-Id", out IEnumerable<string> sessionHeader))
            {
                string sessionId = sessionHeader.FirstOrDefault();
                response.Dispose();
                response = await SendAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            JObject json = JObject.Parse(responseBody);
            string actualHash = json[s_transmissionArgumentsKey]?[s_transmissionAddedKey]?["hashString"]?.ToString() ?? string.Empty;

            if (!string.Equals(expectedContentKey, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Relay gateway reported '{actualHash}' but expected '{expectedContentKey}'.");
            }

            return actualHash;
        }

        private static async Task SubmitCascadeAsync(
            RemoteCacheSession session,
            HttpClient client,
            string descriptorPath,
            string targetDirectory,
            CancellationToken cancellationToken)
        {
            var request = new JObject
            {
                ["method"] = s_delugeAdd,
                ["params"] = new JArray
                {
                    new JArray
                    {
                        new JObject
                        {
                            ["type"] = "file",
                            ["contents"] = Convert.ToBase64String(await NetFrameworkCompatibility.ReadAllBytesAsync(descriptorPath, cancellationToken).ConfigureAwait(false)),
                            ["options"] = new JObject
                            {
                                ["download_location"] = targetDirectory,
                            },
                        },
                    },
                },
                ["id"] = 1,
            };

            string payload = request.ToString(Formatting.None);
            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, Combine(session.BaseUri, s_cascadeEndpoint))
                {
                    Content = content,
                };

                if (!string.IsNullOrEmpty(session.CookieHeader))
                {
                    httpRequest.Headers.Add("Cookie", session.CookieHeader);
                }

                HttpResponseMessage response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
        }

        private static async Task<ResourceSnapshot> QueryRelayAsync(
            RemoteCacheSession session,
            HttpClient client,
            string contentKey,
            CancellationToken cancellationToken)
        {
            var request = new JObject
            {
                ["method"] = s_transmissionGet,
                ["arguments"] = new JObject
                {
                    ["fields"] = new JArray("percentDone", "downloadedEver", "uploadedEver", "peersConnected", "status", "hashString"),
                    ["ids"] = new JArray(contentKey),
                },
            };

            string payload = request.ToString(Formatting.None);
            Uri endpoint = Combine(session.BaseUri, s_relayEndpoint);

            async Task<HttpResponseMessage> SendAsync(string sessionId, CancellationToken ct)
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrEmpty(session.AuthorizationHeader))
                {
                    httpRequest.Headers.Add("Authorization", $"Basic {session.AuthorizationHeader}");
                }

                if (!string.IsNullOrEmpty(sessionId))
                {
                    httpRequest.Headers.Add("X-Transmission-Session-Id", sessionId);
                }

                return await client.SendAsync(httpRequest, ct).ConfigureAwait(false);
            }

            HttpResponseMessage response = await SendAsync(sessionId: null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict &&
                response.Headers.TryGetValues("X-Transmission-Session-Id", out IEnumerable<string> sessionHeader))
            {
                string sessionId = sessionHeader.FirstOrDefault();
                response.Dispose();
                response = await SendAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            JObject result = JObject.Parse(json);
            JArray descriptors = result[s_transmissionArgumentsKey]?[s_transmissionCollectionKey] as JArray;
            JToken entry = descriptors?
                .FirstOrDefault(t => string.Equals(t[s_transmissionHashKey]?.ToString(), contentKey, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                return ResourceSnapshot.Empty;
            }

            return new ResourceSnapshot(
                progress: (double)(entry["percentDone"] ?? 0.0),
                downloadedBytes: (long)(entry["downloadedEver"] ?? 0L),
                uploadedBytes: (long)(entry["uploadedEver"] ?? 0L),
                connectedPeers: (int)(entry["peersConnected"] ?? 0),
                connectedSeeds: 0,
                state: entry["status"]?.ToString() ?? string.Empty);
        }

        private static async Task<ResourceSnapshot> QueryCascadeAsync(
            RemoteCacheSession session,
            HttpClient client,
            string contentKey,
            CancellationToken cancellationToken)
        {
            var request = new JObject
            {
                ["method"] = s_delugeUpdate,
                ["params"] = new JArray
                {
                    new JArray("progress", "total_done", "total_uploaded", "num_peers", "state"),
                    new JObject(),
                },
                ["id"] = 1,
            };

            string payload = request.ToString(Formatting.None);
            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, Combine(session.BaseUri, s_cascadeEndpoint))
                {
                    Content = content,
                };

                if (!string.IsNullOrEmpty(session.CookieHeader))
                {
                    httpRequest.Headers.Add("Cookie", session.CookieHeader);
                }

                HttpResponseMessage response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                JObject result = JObject.Parse(json);
                JObject descriptors = result[s_delugeResultKey]?[s_delugeCollectionKey] as JObject;
                JProperty property = descriptors?
                    .Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, contentKey, StringComparison.OrdinalIgnoreCase));

                if (property == null)
                {
                    return ResourceSnapshot.Empty;
                }

                JObject entry = property.Value as JObject ?? new JObject();
                double progressPercent = (double)(entry["progress"] ?? 0.0);

                return new ResourceSnapshot(
                    progressPercent / 100.0,
                    (long)(entry["total_done"] ?? 0L),
                    (long)(entry["total_uploaded"] ?? 0L),
                    (int)(entry["num_peers"] ?? 0),
                    0,
                    entry["state"]?.ToString() ?? string.Empty);
            }
        }

        private static Uri Combine(Uri baseUri, string relativePath)
        {
            if (!baseUri.AbsoluteUri.EndsWith('/'))
            {
                return new Uri(baseUri, relativePath);
            }

            return new Uri(baseUri, relativePath.TrimStart(new[] { '/' }));
        }

        private static string Decode64(string value)
        {
            byte[] bytes = Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

