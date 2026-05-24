using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HoloPatcher.UI
{

    /// <summary>
    /// Configuration and version information for HoloPatcher.
    /// Equivalent to holopatcher/config.py
    /// </summary>
    public static class Config
    {
        private static readonly Dictionary<string, object> LocalProgramInfo = new Dictionary<string, object>()
        {
            ["currentVersion"] = "2.0.0a1",
            ["holopatcherLatestVersion"] = "1.5.2",
            ["holopatcherLatestBetaVersion"] = "1.7.0b1",
            ["updateInfoLink"] = "https://api.github.com/repos/th3w1zard1/KPatcher/contents/src/KPatcher.UI/Config.cs",
            ["updateBetaInfoLink"] = "https://api.github.com/repos/th3w1zard1/KPatcher/contents/src/KPatcher.UI/Config.cs?ref=bleeding-edge",
            ["holopatcherDownloadLink"] = "https://deadlystream.com/files/file/1982-holocron-holopatcher",
            ["holopatcherBetaDownloadLink"] = "https://github.com/th3w1zard1/KPatcher/releases/tag/v1.70-patcher-beta1",
            ["holopatcherDirectLinks"] = new Dictionary<string, Dictionary<string, List<string>>>
            {
                ["Darwin"] = new Dictionary<string, List<string>>
                {
                    ["32bit"] = new List<string>(),
                    ["64bit"] = new List<string> { "https://github.com/th3w1zard1/KPatcher/releases/download/{tag}/HoloPatcher_Mac.zip" }
                },
                ["Linux"] = new Dictionary<string, List<string>>
                {
                    ["32bit"] = new List<string>(),
                    ["64bit"] = new List<string> { "https://github.com/th3w1zard1/KPatcher/releases/download/{tag}/HoloPatcher_Linux.zip" }
                },
                ["Windows"] = new Dictionary<string, List<string>>
                {
                    ["32bit"] = new List<string> { "https://github.com/th3w1zard1/KPatcher/releases/download/{tag}/HoloPatcher_Windows.zip" },
                    ["64bit"] = new List<string> { "https://github.com/th3w1zard1/KPatcher/releases/download/{tag}/HoloPatcher_Windows.zip" }
                }
            },
            ["holopatcherLatestNotes"] = "",
            ["holopatcherLatestBetaNotes"] = ""
        };

        public static string CurrentVersion => (string)LocalProgramInfo["currentVersion"];

        /// <summary>
        /// Gets remote HoloPatcher update information from GitHub.
        /// </summary>
        public static async Task<Dictionary<string, object>> GetRemoteHolopatcherUpdateInfoAsync(bool useBetaChannel = false, bool silent = false)
        {
            string updateInfoLink = useBetaChannel
                ? (string)LocalProgramInfo["updateBetaInfoLink"]
                : (string)LocalProgramInfo["updateInfoLink"];

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(15);
                    string response = await httpClient.GetStringAsync(updateInfoLink);

                    // Parse JSON response from GitHub API
                    var jsonDoc = JsonDocument.Parse(response);
                    string base64Content = jsonDoc.RootElement.GetProperty("content").GetString();
                    if (string.IsNullOrEmpty(base64Content))
                    {
                        throw new InvalidOperationException("No content found in GitHub API response");
                    }

                    // Decode base64 content
                    byte[] decodedBytes = Convert.FromBase64String(base64Content);
                    string decodedContent = System.Text.Encoding.UTF8.GetString(decodedBytes);

                    // Extract JSON between markers
                    Match jsonMatch = Regex.Match(decodedContent, @"<---JSON_START--->\s*#\s*(.*?)\s*#\s*<---JSON_END--->", RegexOptions.Singleline);
                    if (!jsonMatch.Success)
                    {
                        throw new InvalidOperationException("JSON data not found or markers are incorrect");
                    }

                    string jsonStr = jsonMatch.Groups[1].Value;
                    Dictionary<string, object> remoteInfo = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonStr);
                    if (remoteInfo is null)
                    {
                        throw new InvalidOperationException("Failed to deserialize remote info");
                    }

                    return remoteInfo;
                }
            }
            catch (Exception)
            {
                if (silent)
                {
                    return LocalProgramInfo;
                }
                // In GUI mode, show error dialog - handled by caller
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Compares two version strings to determine if remote is newer.
        /// </summary>
        public static bool RemoteVersionNewer(string localVersion, string remoteVersion)
        {
            try
            {
                var local = new Version(localVersion);
                var remote = new Version(remoteVersion);
                return remote > local;
            }
            catch
            {
                // Fallback to string comparison if version parsing fails
                return string.Compare(remoteVersion, localVersion, StringComparison.Ordinal) > 0;
            }
        }
    }
}

