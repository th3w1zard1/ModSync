// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using JetBrains.Annotations;

using KOTORModSync.Core.Parsing;
using KOTORModSync.Core.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

using static KOTORModSync.Core.Instruction;

using YamlSerialization = YamlDotNet.Serialization;

namespace KOTORModSync.Core.Services
{
    public static class ModComponentSerializationService
    {
        #region Encoding Sanitization
        /// <summary>
        /// Sanitizes string content to handle problematic characters that break parsers.
        /// Uses the encoding specified in MainConfig.FileEncoding (default: utf-8)
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static string SanitizeUtf8(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            try
            {
                // Get the configured encoding (default to UTF-8)
                string encodingName = MainConfig.FileEncoding
                                      ?? "utf-8";
                Encoding targetEncoding;

                // Map encoding name to .NET Encoding
                if (encodingName.Equals("windows-1252", StringComparison.OrdinalIgnoreCase) ||
                    encodingName.Equals("cp-1252", StringComparison.OrdinalIgnoreCase) ||
                    encodingName.Equals("cp1252", StringComparison.OrdinalIgnoreCase))
                {
                    targetEncoding = Encoding.GetEncoding(1252);
                }
                else if (encodingName.Equals("utf-8", StringComparison.OrdinalIgnoreCase) ||
                           encodingName.Equals("utf8", StringComparison.OrdinalIgnoreCase))
                {
                    targetEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                }
                else
                {
                    // Try to get encoding by name, fallback to UTF-8
                    try
                    {
                        targetEncoding = Encoding.GetEncoding(encodingName);
                    }
                    catch
                    {
                        Logger.LogWarning($"Unknown encoding '{encodingName}', using UTF-8");
                        targetEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                    }
                }

                var result = new StringBuilder(input.Length);

                foreach (char c in input)
                {
                    try
                    {
                        // Try to encode this character to target encoding
                        targetEncoding.GetBytes(new[] { c });
                        // If successful, add it to result
                        result.Append(c);
                    }
                    catch
                    {
                        // If encoding fails, ignore/skip this character
                        int lineNumber = input.Substring(0, input.IndexOf(c)).Count(x => x == '\n') + 1;
                        int substringLength = input.IndexOf(c);
                        int columnNumber = substringLength - input.LastIndexOf('\n', substringLength - 1) + 1;
                        Logger.LogVerbose($"Failed to encode character `{c}` with encoding '{encodingName}' at line {lineNumber} column {columnNumber}, ignoring");
                    }
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to sanitize content with encoding (using original): {ex.Message}");
                return input;
            }
        }
        #endregion

        private static readonly JsonSerializerSettings DirectJsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
        };

        /// <summary>
        /// Populates ResourceRegistry for all components from DownloadCacheService cache.
        /// This ensures components have cached filename data immediately after loading.
        /// </summary>
        private static async Task PopulateResourceRegistryFromCacheAsync([NotNull][ItemNotNull] IReadOnlyList<ModComponent> components)
        {
            // Ensure resource index is loaded
            await Services.DownloadCacheService.LoadResourceIndexAsync().ConfigureAwait(false);

            foreach (ModComponent component in components)
            {
                if (component.ResourceRegistry == null || component.ResourceRegistry.Count == 0)
                {
                    continue;
                }

                // Create a list to track URLs we need to update
                List<string> urlsToUpdate = new List<string>(component.ResourceRegistry.Keys);

                foreach (string url in urlsToUpdate)
                {
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    // Try to get cached metadata for this URL
                    ResourceMetadata cachedMetadata = Services.DownloadCacheService.TryGetResourceMetadataByUrl(url);

                    if (cachedMetadata != null && cachedMetadata.Files != null && cachedMetadata.Files.Count > 0)
                    {
                        // Get existing metadata or create new
                        ResourceMetadata existingMetadata = component.ResourceRegistry.TryGetValue(url, out ResourceMetadata existing)
                            ? existing
                            : new ResourceMetadata();

                        // If the component's ResourceRegistry doesn't have files yet, populate from cache
                        if (existingMetadata.Files == null || existingMetadata.Files.Count == 0)
                        {
                            existingMetadata.Files = new Dictionary<string, bool?>(cachedMetadata.Files, StringComparer.OrdinalIgnoreCase);
                            await Logger.LogVerboseAsync($"[ModComponentSerializationService] Populated {cachedMetadata.Files.Count} cached filename(s) for URL: {url} in component: {component.Name}").ConfigureAwait(false);
                        }

                        // Also copy other useful metadata from cache
                        if (string.IsNullOrEmpty(existingMetadata.ContentId) && !string.IsNullOrEmpty(cachedMetadata.ContentId))
                        {
                            existingMetadata.ContentId = cachedMetadata.ContentId;
                        }
                        if (string.IsNullOrEmpty(existingMetadata.ContentHashSHA256) && !string.IsNullOrEmpty(cachedMetadata.ContentHashSHA256))
                        {
                            existingMetadata.ContentHashSHA256 = cachedMetadata.ContentHashSHA256;
                        }
                        if (string.IsNullOrEmpty(existingMetadata.MetadataHash) && !string.IsNullOrEmpty(cachedMetadata.MetadataHash))
                        {
                            existingMetadata.MetadataHash = cachedMetadata.MetadataHash;
                        }
                        if (existingMetadata.FileSize == 0 && cachedMetadata.FileSize > 0)
                        {
                            existingMetadata.FileSize = cachedMetadata.FileSize;
                        }

                        // Update the component's ResourceRegistry
                        component.ResourceRegistry[url] = existingMetadata;
                    }
                }
            }
        }

        /// <summary>
        /// Synchronous wrapper for PopulateResourceRegistryFromCacheAsync.
        /// </summary>
        private static void PopulateResourceRegistryFromCache([NotNull][ItemNotNull] IReadOnlyList<ModComponent> components)
        {
            Task.Run(async () => await PopulateResourceRegistryFromCacheAsync(components).ConfigureAwait(false)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the DownloadCacheService cache from component ResourceRegistry before serialization.
        /// This ensures any runtime changes to ResourceRegistry are persisted to cache.
        /// </summary>
        private static async Task UpdateCacheFromResourceRegistryAsync([NotNull][ItemNotNull] IReadOnlyList<ModComponent> components)
        {
            // Ensure resource index is loaded
            await Services.DownloadCacheService.LoadResourceIndexAsync().ConfigureAwait(false);

            foreach (ModComponent component in components)
            {
                if (component.ResourceRegistry == null || component.ResourceRegistry.Count == 0)
                {
                    continue;
                }

                foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry)
                {
                    string url = kvp.Key;
                    ResourceMetadata metadata = kvp.Value;

                    if (string.IsNullOrWhiteSpace(url) || metadata == null)
                    {
                        continue;
                    }

                    // Only update cache if we have meaningful data (files list)
                    if (metadata.Files != null && metadata.Files.Count > 0)
                    {
                        await Services.DownloadCacheService.UpdateResourceMetadataWithFilenamesAsync(
                            component,
                            url,
                            metadata.Files.Keys.ToList()
                        ).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Synchronous wrapper for UpdateCacheFromResourceRegistryAsync.
        /// </summary>
        private static void UpdateCacheFromResourceRegistry([NotNull][ItemNotNull] IReadOnlyList<ModComponent> components)
        {
            Task.Run(async () => await UpdateCacheFromResourceRegistryAsync(components).ConfigureAwait(false)).GetAwaiter().GetResult();
        }

        #region Loading Functions
        [NotNull]
        [ItemNotNull]
        public static IReadOnlyList<ModComponent> DeserializeModComponentFromTomlString([NotNull] string tomlContent)
        {
            Logger.LogVerbose("Loading from TOML string");
            if (tomlContent is null)
            {
                throw new ArgumentNullException(nameof(tomlContent));
            }

            tomlContent = SanitizeUtf8(tomlContent);
            tomlContent = tomlContent
                .Replace(oldValue: "Instructions = []", string.Empty)
                .Replace(oldValue: "Options = []", string.Empty);
            if (string.IsNullOrWhiteSpace(tomlContent))
            {
                throw new InvalidDataException("TOML content is empty.");
            }

            tomlContent = Serializer.FixWhitespaceIssues(tomlContent);

            DocumentSyntax tomlDocument = Toml.Parse(tomlContent);
            if (tomlDocument.HasErrors)
            {
                foreach (DiagnosticMessage message in tomlDocument.Diagnostics)
                {
                    if (message != null)
                    {
                        Logger.LogError(message.Message);
                    }
                }
            }

            TomlTable tomlTable = tomlDocument.ToModel();
            ParseMetadataSection(tomlTable);

            if (!tomlTable.TryGetValue("thisMod", out object thisModObj))
            {
                throw new InvalidDataException("TOML content does not contain 'thisMod' array.");
            }

            IEnumerable<object> componentTables;

            if (thisModObj is TomlTableArray tomlTableArray)
            {
                componentTables = tomlTableArray;
            }
            else if (thisModObj is System.Collections.IList list)
            {
                componentTables = list.Cast<object>();
            }
            else
            {
                throw new InvalidDataException($"TOML 'thisMod' is not a valid array type. Got: {thisModObj.GetType().Name}");
            }

            // Collect all [[thisMod.Instructions]] entries from the root level
            var allInstructions = new List<object>();
            foreach (string key in tomlTable.Keys)
            {
                if (key.Contains("Instructions")
                    && !key.Contains("Options")
                    && tomlTable.TryGetValue(key, out object instructionsObj))
                {
                    if (instructionsObj is TomlTableArray instructionsTableArray)
                    {
                        Logger.LogVerbose($"Found {instructionsTableArray.Count} instructions at root level (TomlTableArray)");
                        foreach (TomlTable table in instructionsTableArray)
                        {
                            allInstructions.Add(table);
                        }
                    }
                    else if (instructionsObj is IList<object> instructionsList)
                    {
                        Logger.LogVerbose($"Found {instructionsList.Count} instructions at root level");
                        allInstructions.AddRange(instructionsList);
                    }
                    else
                    {
                        Logger.LogVerbose($"Instructions object type not handled: {instructionsObj?.GetType().Name ?? "null"}");
                    }
                }
            }

            // Collect all [[thisMod.Options]] entries from the root level
            var allOptions = new List<object>();
            Logger.LogVerbose($"TOML table keys: {string.Join(", ", tomlTable.Keys)}");
            foreach (string key in tomlTable.Keys)
            {
                Logger.LogVerbose($"Checking key: '{key}' - Contains Options: {key.IndexOf("Options", StringComparison.OrdinalIgnoreCase) >= 0}, Contains Instructions: {key.IndexOf("Instructions", StringComparison.OrdinalIgnoreCase) >= 0}");
                if (
                    key.IndexOf("Options", StringComparison.OrdinalIgnoreCase) >= 0
                    && key.IndexOf("Instructions", StringComparison.OrdinalIgnoreCase) < 0
                    && tomlTable.TryGetValue(key, out object optionsObj)
                )
                {
                    Logger.LogVerbose($"Found Options key: '{key}'");
                    Logger.LogVerbose($"Options object type: {optionsObj?.GetType().Name ?? "null"}");
                    if (optionsObj is IList<object> optionsList && !(optionsObj is TomlTableArray optionsTableArray))
                    {
                        Logger.LogVerbose($"Found {optionsList.Count} options at root level");
                        allOptions.AddRange(optionsList);
                    }
                    else
                    {
                        Logger.LogVerbose($"Options object type not handled: {optionsObj?.GetType().Name ?? "null"}");
                    }
                }
            }

            // Collect all [[thisMod.Options.Instructions]] entries from the root level
            var allOptionsInstructions = new List<object>();
            foreach (string key in tomlTable.Keys)
            {
                Logger.LogVerbose($"Checking key for Options Instructions: '{key}' - Contains Options: {key.IndexOf("Options", StringComparison.OrdinalIgnoreCase) >= 0}, Contains Instructions: {key.IndexOf("Instructions", StringComparison.OrdinalIgnoreCase) >= 0}");
                if (key.IndexOf("Options", StringComparison.OrdinalIgnoreCase) >= 0 && key.IndexOf("Instructions", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Logger.LogVerbose($"Found Options Instructions key: '{key}'");
                    if (tomlTable.TryGetValue(key, out object optionsInstructionsObj))
                    {
                        Logger.LogVerbose($"Options Instructions object type: {optionsInstructionsObj?.GetType().Name ?? "null"}");
                        if (optionsInstructionsObj is TomlTableArray optionsInstructionsTableArray)
                        {
                            Logger.LogVerbose($"Found {optionsInstructionsTableArray.Count} options instructions at root level (TomlTableArray)");
                            foreach (TomlTable table in optionsInstructionsTableArray)
                            {
                                allOptionsInstructions.Add(table);
                            }
                        }
                        else if (optionsInstructionsObj is IList<object> optionsInstructionsList)
                        {
                            Logger.LogVerbose($"Found {optionsInstructionsList.Count} options instructions at root level");
                            allOptionsInstructions.AddRange(optionsInstructionsList);
                        }
                        else
                        {
                            Logger.LogVerbose($"Options Instructions object type not handled: {optionsInstructionsObj?.GetType().Name ?? "null"}");
                        }
                    }
                }
            }

            var components = new List<ModComponent>();

            foreach (object tomlComponent in componentTables)
            {
                if (tomlComponent is null)
                {
                    continue;
                }

                try
                {
                    IDictionary<string, object> componentDict = tomlComponent as IDictionary<string, object>
                        ?? throw new InvalidCastException("Failed to cast TOML component to IDictionary<string, object>");

                    Logger.LogVerbose($"=== Processing TOML component ===");
                    Logger.LogVerbose($"tomlComponent type: {tomlComponent.GetType().Name}");
                    Logger.LogVerbose($"componentDict type: {componentDict.GetType().Name}");
                    Logger.LogVerbose($"componentDict keys: {string.Join(", ", componentDict.Keys)}");

                    // Check for Guid with case-insensitive lookup
                    string guidKey = componentDict.Keys.FirstOrDefault(k => string.Equals(k, "Guid", StringComparison.OrdinalIgnoreCase));
                    if (guidKey != null)
                    {
                        Logger.LogVerbose($"Found Guid key: '{guidKey}' (case-insensitive match)");
                    }
                    else
                    {
                        Logger.LogVerbose($"WARNING: Guid key not found in componentDict! Available keys: {string.Join(", ", componentDict.Keys)}");
                    }

                    // Check if this component has Instructions at the TOML level
                    if (componentDict.ContainsKey("Instructions"))
                    {
                        object tomlInstructions = componentDict["Instructions"];
                        Logger.LogVerbose($"TOML component has Instructions field: type={tomlInstructions?.GetType().Name ?? "null"}, value: {tomlInstructions}");
                        if (tomlInstructions is IList<object> instructionsList)
                        {
                            Logger.LogVerbose($"Instructions is IList with {instructionsList.Count} items");
                            for (int i = 0; i < Math.Min(instructionsList.Count, 3); i++)
                            {
                                Logger.LogVerbose($"  Instructions[{i}] type: {instructionsList[i]?.GetType().Name ?? "null"}, value: {instructionsList[i]}");
                            }
                        }
                    }
                    else
                    {
                        Logger.LogVerbose($"TOML component does NOT have Instructions field. Available keys: {string.Join(", ", componentDict.Keys)}");
                    }

                    ModComponent thisComponent = DeserializeComponent(componentDict);

                    // Assign collected instructions to this component
                    // Only if they weren't already deserialized from componentDict
                    if (allInstructions.Count > 0 && thisComponent.Instructions.Count == 0)
                    {
                        Logger.LogVerbose($"Assigning {allInstructions.Count} instructions to component '{thisComponent.Name}'");
                        thisComponent.Instructions = DeserializeInstructions(allInstructions, thisComponent);
                    }

                    // Assign collected options to this component
                    // Only if they weren't already deserialized from componentDict
                    if (allOptions.Count > 0 && thisComponent.Options.Count == 0)
                    {
                        Logger.LogVerbose($"Assigning {allOptions.Count} options to component '{thisComponent.Name}'");
                        thisComponent.Options = DeserializeOptions(allOptions);
                    }

                    // Assign collected options instructions to the appropriate options
                    if (allOptionsInstructions.Count > 0)
                    {
                        Logger.LogVerbose($"Assigning {allOptionsInstructions.Count} options instructions to component '{thisComponent.Name}'");

                        var instructionsByParent = new Dictionary<string, List<object>>(StringComparer.Ordinal);
                        foreach (object instrObj in allOptionsInstructions)
                        {
                            if (instrObj is IDictionary<string, object> instrDict &&
                                 instrDict.TryGetValue("Parent", out object parentObj))
                            {
                                string parentGuid = parentObj?.ToString();
                                if (!string.IsNullOrEmpty(parentGuid))
                                {
                                    if (!instructionsByParent.ContainsKey(parentGuid))
                                    {
                                        instructionsByParent[parentGuid] = new List<object>();
                                    }

                                    instructionsByParent[parentGuid].Add(instrObj);
                                }
                            }
                        }

                        foreach (Option option in thisComponent.Options)
                        {
                            string optionGuidStr = option.Guid.ToString();
                            if (instructionsByParent.TryGetValue(optionGuidStr, out List<object> instructions) && option.Instructions.Count == 0)
                            {
                                option.Instructions = DeserializeInstructions(instructions, option);
                            }
                        }
                    }

                    components.Add(thisComponent);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to deserialize component: {ex.Message}");
                    Logger.LogError($"Exception type: {ex.GetType().Name}");
                    Logger.LogError($"Stack trace: {ex.StackTrace}");
                }
            }

            if (components.Count == 0)
            {
                throw new InvalidDataException("No valid components found in TOML content.");
            }

            // Update cache from ResourceRegistry BEFORE returning
            UpdateCacheFromResourceRegistry(components);

            return components;
        }

        private static readonly string[] s_yamlSeparator = new[] { "---" };

        [NotNull]
        [ItemNotNull]
        public static IReadOnlyList<ModComponent> DeserializeModComponentFromYamlString([NotNull] string yamlContent)
        {
            Logger.LogVerbose("Loading from YAML string");
            if (yamlContent is null)
            {
                throw new ArgumentNullException(nameof(yamlContent));
            }

            yamlContent = SanitizeUtf8(yamlContent);
            var components = new List<ModComponent>();
            string[] yamlDocs = yamlContent.Split(s_yamlSeparator, StringSplitOptions.RemoveEmptyEntries);
            int docIndex = 0;
            foreach (string yamlDoc in yamlDocs)
            {
                docIndex++;
                if (string.IsNullOrWhiteSpace(yamlDoc))
                {
                    continue;
                }

                try
                {
                    // Check if this is a metadata document
                    if (IsYamlMetadataDocument(yamlDoc))
                    {
                        ParseYamlMetadataSection(yamlDoc.Trim());
                        Logger.LogVerbose($"Parsed YAML metadata document #{docIndex}");
                        continue;
                    }

                    ModComponent component = DeserializeYamlComponent(yamlDoc.Trim());
                    if (component != null)
                    {
                        components.Add(component);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to deserialize YAML document #{docIndex}: {ex.Message} - skipping this document");
                    Logger.LogVerbose($"YAML deserialization error details: {ex}");
                }
            }
            if (components.Count == 0)
            {
                throw new InvalidDataException("No valid components found in YAML content.");
            }

            // Update cache from ResourceRegistry BEFORE returning
            UpdateCacheFromResourceRegistry(components);

            return components;
        }

        private static bool IsYamlMetadataDocument(string yamlDoc)
        {
            if (string.IsNullOrWhiteSpace(yamlDoc))
            {
                return false;
            }

            // Metadata documents have "fileFormatVersion" or start with "# Metadata"
            return yamlDoc.Contains("fileFormatVersion:") ||
                   yamlDoc.TrimStart().StartsWith("# Metadata", StringComparison.OrdinalIgnoreCase);
        }

        private static void ParseYamlMetadataSection(string yamlDoc)
        {
            try
            {
                YamlSerialization.IDeserializer deserializer = new YamlSerialization.DeserializerBuilder()
                    .WithNamingConvention(YamlSerialization.NamingConventions.PascalCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                Dictionary<string, object> metadataDict = deserializer.Deserialize<Dictionary<string, object>>(yamlDoc);

                if (metadataDict is null)
                {
                    return;
                }

                var mainConfig = new MainConfig();
                if (metadataDict.TryGetValue("FileFormatVersion", out object versionObj) || metadataDict.TryGetValue("fileFormatVersion", out versionObj))
                {
                    mainConfig.fileFormatVersion = versionObj?.ToString() ?? "2.0";
                }

                if (metadataDict.TryGetValue("TargetGame", out object gameObj) || metadataDict.TryGetValue("targetGame", out gameObj))
                {
                    mainConfig.targetGame = gameObj?.ToString() ?? string.Empty;
                }

                if (metadataDict.TryGetValue("BuildName", out object nameObj) || metadataDict.TryGetValue("buildName", out nameObj))
                {
                    mainConfig.buildName = nameObj?.ToString() ?? string.Empty;
                }

                if (metadataDict.TryGetValue("BuildAuthor", out object authorObj) || metadataDict.TryGetValue("buildAuthor", out authorObj))
                {
                    mainConfig.buildAuthor = authorObj?.ToString() ?? string.Empty;
                }

                if (metadataDict.TryGetValue("BuildDescription", out object descObj) || metadataDict.TryGetValue("buildDescription", out descObj))
                {
                    mainConfig.buildDescription = descObj?.ToString() ?? string.Empty;
                }

                if (
                    (metadataDict.TryGetValue("LastModified", out object modifiedObj) ||
                    metadataDict.TryGetValue("lastModified", out modifiedObj)) &&
                    DateTime.TryParse(modifiedObj?.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate)
                )
                {
                    mainConfig.lastModified = parsedDate;
                }
                // Always load content sections if present
                if (metadataDict.TryGetValue("PreambleContent", out object preambleObj) || metadataDict.TryGetValue("preambleContent", out preambleObj))
                {
                    mainConfig.preambleContent = preambleObj?.ToString() ?? string.Empty;
                }

                if (metadataDict.TryGetValue("EpilogueContent", out object epilogueObj) || metadataDict.TryGetValue("epilogueContent", out epilogueObj))
                {
                    mainConfig.epilogueContent = epilogueObj?.ToString() ?? string.Empty;
                }

                if (metadataDict.TryGetValue("WidescreenWarningContent", out object widescreenObj) || metadataDict.TryGetValue("widescreenWarningContent", out widescreenObj))
                {
                    mainConfig.widescreenWarningContent = widescreenObj?.ToString() ?? string.Empty;
                }

                if (metadataDict.TryGetValue("AspyrExclusiveWarningContent", out object aspyrObj) || metadataDict.TryGetValue("aspyrExclusiveWarningContent", out aspyrObj))
                {
                    mainConfig.aspyrExclusiveWarningContent = aspyrObj?.ToString() ?? string.Empty;
                }

                if (metadataDict.TryGetValue("InstallationWarningContent", out object installationWarningObj) || metadataDict.TryGetValue("installationWarningContent", out installationWarningObj))
                {
                    mainConfig.installationWarningContent = installationWarningObj?.ToString() ?? string.Empty;
                }

                Logger.LogVerbose($"Loaded YAML metadata: Game={mainConfig.targetGame}, Version={mainConfig.fileFormatVersion}, Build={mainConfig.buildName}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to parse YAML metadata section (non-fatal): {ex.Message}");
            }
        }

        [NotNull]
        [ItemNotNull]
        public static IReadOnlyList<ModComponent> DeserializeModComponentFromMarkdownString([NotNull] string markdownContent)
        {
            Logger.LogVerbose("Loading from Markdown string");
            if (markdownContent is null)
            {
                throw new ArgumentNullException(nameof(markdownContent));
            }

            markdownContent = SanitizeUtf8(markdownContent);
            try
            {
                var profile = MarkdownImportProfile.CreateDefault();
                var parser = new MarkdownParser(profile);
                MarkdownParserResult result = parser.Parse(markdownContent);
                if (result.Components is null || result.Components.Count == 0)
                {
                    throw new InvalidDataException("No valid components found in Markdown content.");
                }

                // Update MainConfig with content sections from markdown
                var mainConfig = new MainConfig();
                if (!string.IsNullOrWhiteSpace(result.PreambleContent))
                {
                    mainConfig.preambleContent = result.PreambleContent;
                }

                if (!string.IsNullOrWhiteSpace(result.EpilogueContent))
                {
                    mainConfig.epilogueContent = result.EpilogueContent;
                }

                if (!string.IsNullOrWhiteSpace(result.WidescreenWarningContent))
                {
                    mainConfig.widescreenWarningContent = result.WidescreenWarningContent;
                }

                if (!string.IsNullOrWhiteSpace(result.AspyrExclusiveWarningContent))
                {
                    mainConfig.aspyrExclusiveWarningContent = result.AspyrExclusiveWarningContent;
                }

                if (!string.IsNullOrWhiteSpace(result.InstallationWarningContent))
                {
                    mainConfig.installationWarningContent = result.InstallationWarningContent;
                }

                var components = result.Components.ToList();

                // Update cache from ResourceRegistry BEFORE returning
                UpdateCacheFromResourceRegistry(components);

                return components;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to parse Markdown content: {ex.Message}");
                Logger.LogVerbose($"Markdown parsing error details: {ex}");
                throw new InvalidDataException("Failed to parse Markdown content.", ex);
            }
        }

        [NotNull]
        [ItemNotNull]
        public static IReadOnlyList<ModComponent> DeserializeModComponentFromJsonString([NotNull] string jsonContent)
        {
            Logger.LogVerbose("Loading from JSON string");
            if (jsonContent is null)
            {
                throw new ArgumentNullException(nameof(jsonContent));
            }

            jsonContent = SanitizeUtf8(jsonContent);

            List<ModComponent> components;
            try
            {
                // Deserialize JSON to JArray first, then convert each item to dictionary
                // This ensures we use the unified DeserializeComponent method which handles
                // Category and Language fallback logic properly
                JArray jsonArray = JArray.Parse(jsonContent);
                components = new List<ModComponent>();

                foreach (JToken token in jsonArray)
                {
                    if (token is JObject jobj)
                    {
                        Dictionary<string, object> componentDict = JTokenToDictionary(jobj);
                        ModComponent component = DeserializeComponent(componentDict);
                        components.Add(component);
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Failed to parse JSON content.", ex);
            }

            UpdateCacheFromResourceRegistry(components);

            return components;
        }

        [NotNull]
        [ItemNotNull]
        public static IReadOnlyList<ModComponent> DeserializeModComponentFromString(
            [NotNull] string content,
            [CanBeNull] string format = null)
        {
            Logger.LogVerbose($"Loading from string with format: {format ?? "auto-detect"}");
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            IReadOnlyList<ModComponent> components;

            if (!string.IsNullOrWhiteSpace(format))
            {
                string fmt = format.Trim().ToLowerInvariant();
                switch (fmt)
                {
                    case "toml":
                    case "tml":
                        components = DeserializeModComponentFromTomlString(content);
                        break;
                    case "json":
                        components = DeserializeModComponentFromJsonString(content);
                        break;
                    case "yaml":
                    case "yml":
                        components = DeserializeModComponentFromYamlString(content);
                        break;
                    case "md":
                    case "markdown":
                    case "mdown":
                    case "mkdn":
                    case "mkd":
                    case "mdtxt":
                    case "mdtext":
                    case "text":
                        components = DeserializeModComponentFromMarkdownString(content);
                        break;
                    default:
                        throw new ArgumentException($"Unknown format \"{format}\" passed to DeserializeModComponentFromString.", nameof(format));
                }
            }
            else
            {
                try
                {
                    components = DeserializeModComponentFromTomlString(content);
                }
                catch (Exception tomlEx)
                {
                    Logger.LogVerbose($"TOML parsing failed: {tomlEx.Message}");

                    try
                    {
                        components = DeserializeModComponentFromMarkdownString(content);
                    }
                    catch (Exception mdEx)
                    {
                        Logger.LogVerbose($"Markdown parsing failed: {mdEx.Message}");

                        try
                        {
                            components = DeserializeModComponentFromYamlString(content);
                        }
                        catch (Exception yamlEx)
                        {
                            Logger.LogVerbose($"YAML parsing failed: {yamlEx.Message}");

                            try
                            {
                                components = DeserializeModComponentFromTomlString(content);
                            }
                            catch (Exception tomlSecondEx)
                            {
                                Logger.LogVerbose($"TOML (second attempt) parsing failed: {tomlSecondEx.Message}");

                                components = DeserializeModComponentFromJsonString(content);

                            }
                        }
                    }
                }
            }

            // Remove duplicate options from all loaded components
            foreach (ModComponent component in components)
            {
                RemoveDuplicateOptions(component);
            }

            // Resolve dependencies and reorder components
            try
            {
                DependencyResolutionResult resolutionResult = DependencyResolverService.ResolveDependencies(components, ignoreErrors: false);
                if (resolutionResult.Success)
                {
                    components = resolutionResult.OrderedComponents;
                    Logger.LogVerbose($"Successfully resolved dependencies and reordered {components.Count} components");
                }
                else
                {
                    // Log dependency resolution errors but don't fail the load
                    Logger.LogWarning($"Dependency resolution failed with {resolutionResult.Errors.Count} errors:");
                    foreach (DependencyError error in resolutionResult.Errors)
                    {
                        Logger.LogWarning($"  - {error.ComponentName}: {error.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to resolve dependencies during loading: {ex.Message}");
            }

            // If all components are unselected, select all of them
            if (components.Count > 0 && !components.Any(c => c.IsSelected))
            {
                Logger.LogVerbose("All components are unselected, selecting all components automatically");
                foreach (ModComponent component in components)
                {
                    component.IsSelected = true;
                }
            }

            // Auto-fix config errors if enabled
            if (MainConfig.AttemptFixes && components.Count > 0)
            {
                Logger.LogVerbose("Auto-fix enabled, applying fixes to loaded components");
                AutoFixComponentIssues(components);
            }

            // Update cache from ResourceRegistry BEFORE returning
            UpdateCacheFromResourceRegistry(components);

            return components;
        }

        [NotNull]
        [ItemNotNull]
        public static Task<IReadOnlyList<ModComponent>> DeserializeModComponentFromStringAsync(
            [NotNull] string content,
            [CanBeNull] string format = null)
        {
            return Task.Run(() => DeserializeModComponentFromString(content, format));
        }
        #endregion

        #region Saving Functions
        [NotNull]
        public static string SerializeModComponentAsString(
            [NotNull] IReadOnlyList<ModComponent> components,
            [NotNull] string format = "toml",
            [CanBeNull] ComponentValidationContext validationContext = null
        )
        {
            Logger.LogVerbose($"Saving to string with format: {format}");
            if (components is null)
            {
                throw new ArgumentNullException(nameof(components));
            }

            if (format is null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            // Populate ResourceRegistry from cache BEFORE serialization
            PopulateResourceRegistryFromCache(components);

            switch (format.ToLowerInvariant())
            {
                case "toml":
                case "tml":
                    return SerializeModComponentAsTomlString(components, validationContext);
                case "yaml":
                case "yml":
                    return SerializeModComponentAsYamlString(components, validationContext);
                case "md":
                case "markdown":
                case "mdown":
                case "mkdn":
                case "mkd":
                case "mdtxt":
                case "mdtext":
                case "text":
                    return SerializeModComponentAsMarkdownString(components, validationContext);
                case "json":
                    return SerializeModComponentAsJsonString(components, validationContext);
                default:
                    throw new NotSupportedException($"Unsupported format: {format}");
            }
        }
        [NotNull]
        public static Task<string> SerializeModComponentAsStringAsync(
            [NotNull] IReadOnlyList<ModComponent> components,
            [NotNull] string format = "toml")
        {
            return Task.Run(() => SerializeModComponentAsString(components, format));
        }
        #endregion

        #region Public Helpers

        /// <summary>
        /// Preprocesses a component dictionary to handle duplicate fields by flattening nested collections.
        /// This handles cases where TOML/JSON/YAML might have duplicate field definitions.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static Dictionary<string, object> PreprocessComponentDictionary([NotNull] IDictionary<string, object> componentDict)
        {
            var processedDict = new Dictionary<string, object>(componentDict, StringComparer.OrdinalIgnoreCase);

            // Add debug logging for ModLinkFilenames
            if (componentDict.TryGetValue("ModLinkFilenames", out object modLinkFilenamesValue))
            {
                Logger.LogVerbose($"PreprocessComponentDictionary: Found ModLinkFilenames field, type: {modLinkFilenamesValue?.GetType().Name}, value: {modLinkFilenamesValue}");
            }

            // Handle any potential duplicate field issues by ensuring consistent structure
            foreach (KeyValuePair<string, object> kvp in componentDict)
            {
                // Special handling for ModLinkFilenames - preserve as dictionary, don't flatten
                if (kvp.Key.Equals("ModLinkFilenames", StringComparison.OrdinalIgnoreCase))
                {
                    processedDict[kvp.Key] = kvp.Value;
                    continue;
                }

                if (kvp.Value is System.Collections.IEnumerable enumerable && !(kvp.Value is string))
                {
                    // Flatten any nested collections that might result from duplicate fields
                    var flattenedList = new List<object>();
                    foreach (object item in enumerable)
                    {
                        if (item is System.Collections.IEnumerable nestedEnumerable && !(item is string))
                        {
                            // Flatten nested collections (handles duplicate Instructions, etc.)
                            foreach (object nestedItem in nestedEnumerable)
                            {
                                flattenedList.Add(nestedItem);
                            }
                        }
                        else
                        {
                            flattenedList.Add(item);
                        }
                    }
                    processedDict[kvp.Key] = flattenedList;
                }
                else
                {
                    processedDict[kvp.Key] = kvp.Value;
                }
            }

            // Handle YAML deserialization issues where Instructions/Options are created as KeyValuePair objects
            // instead of proper dictionaries
            ProcessInstructionsAndOptions(processedDict);

            // Add debug logging for ModLinkFilenames after processing
            if (processedDict.TryGetValue("ModLinkFilenames", out object modLinkFilenamesAfterValue))
            {
                Logger.LogVerbose($"PreprocessComponentDictionary: ModLinkFilenames after processing, type: {modLinkFilenamesAfterValue?.GetType().Name}, value: {modLinkFilenamesAfterValue}");
            }
            else
            {
                Logger.LogVerbose("PreprocessComponentDictionary: ModLinkFilenames field not found after processing");
            }

            return processedDict;
        }

        /// <summary>
        /// Processes Instructions and Options to handle YAML deserialization issues where
        /// they might be created as KeyValuePair objects instead of proper dictionaries.
        /// Also handles component-level properties that might be converted to KeyValuePair objects.
        /// </summary>
        private static void ProcessInstructionsAndOptions(Dictionary<string, object> processedDict)
        {
            // First, handle component-level properties that might be KeyValuePair objects
            ProcessComponentLevelProperties(processedDict);

            // Process Instructions
            if (processedDict.TryGetValue("Instructions", out object instructionsValue) && instructionsValue is List<object> instructionsList)
            {
                Logger.LogVerbose($"ProcessInstructionsAndOptions: Found {instructionsList.Count} instruction items");


                // Check if we have KeyValuePair objects (YAML deserialization issue)
                var keyValuePairs = instructionsList.Where(item => item.GetType().Name.StartsWith("KeyValuePair", StringComparison.Ordinal)).ToList();
                bool hasKeyValuePairs = keyValuePairs.Count > 0;

                if (hasKeyValuePairs)
                {
                    Logger.LogVerbose($"ProcessInstructionsAndOptions: Found {keyValuePairs.Count} KeyValuePair instruction items, grouping them");
                    List<object> processedInstructions = GroupKeyValuePairsIntoInstructions(keyValuePairs);
                    processedDict["Instructions"] = processedInstructions;
                }
                else
                {
                    Logger.LogVerbose("ProcessInstructionsAndOptions: No KeyValuePair instruction items, processing individually");
                    var processedInstructions = new List<object>();

                    var currentInstruction = new Dictionary<string, object>(StringComparer.Ordinal);

                    foreach (object item in instructionsList)
                    {
                        Logger.LogVerbose($"ProcessInstructionsAndOptions: Processing instruction item of type {item.GetType().Name}");

                        if (item is KeyValuePair<string, object> kvp)
                        {
                            Logger.LogVerbose($"ProcessInstructionsAndOptions: KeyValuePair - {kvp.Key} = {kvp.Value}");
                            currentInstruction[kvp.Key] = kvp.Value;

                            // Check if this completes an instruction (has Action field)
                            if (kvp.Key.Equals("Action", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrEmpty(kvp.Value?.ToString()))
                            {
                                processedInstructions.Add(new Dictionary<string, object>(currentInstruction, StringComparer.Ordinal));
                                currentInstruction.Clear();
                            }
                        }
                        else if (item is Dictionary<string, object> dict)
                        {
                            Logger.LogVerbose($"ProcessInstructionsAndOptions: Dictionary with {dict.Count} keys: {string.Join(", ", dict.Keys)}");
                            if (currentInstruction.Count > 0)

                            {
                                processedInstructions.Add(new Dictionary<string, object>(currentInstruction, StringComparer.Ordinal));
                                currentInstruction.Clear();
                            }
                            processedInstructions.Add(dict);
                        }
                        else
                        {
                            Logger.LogVerbose($"ProcessInstructionsAndOptions: Unknown item type {item.GetType().Name}: {item}");
                        }
                    }

                    if (currentInstruction.Count > 0)

                    {
                        processedInstructions.Add(new Dictionary<string, object>(currentInstruction, StringComparer.Ordinal));
                    }

                    Logger.LogVerbose($"ProcessInstructionsAndOptions: Processed {processedInstructions.Count} instructions");
                    processedDict["Instructions"] = processedInstructions;
                }
            }

            // Process Options
            if (processedDict.TryGetValue("Options", out object optionsValue) && optionsValue is List<object> optionsList)
            {
                Logger.LogVerbose($"ProcessInstructionsAndOptions: Found {optionsList.Count} option items");


                // Check if we have KeyValuePair objects (YAML deserialization issue)
                var keyValuePairs = optionsList.Where(item => item.GetType().Name.StartsWith("KeyValuePair", StringComparison.Ordinal)).ToList();
                bool hasKeyValuePairs = keyValuePairs.Count > 0;

                if (hasKeyValuePairs)
                {
                    Logger.LogVerbose($"ProcessInstructionsAndOptions: Found {keyValuePairs.Count} KeyValuePair option items, grouping them");
                    List<object> processedOptions = GroupKeyValuePairsIntoOptions(keyValuePairs);
                    processedDict["Options"] = processedOptions;
                }
                else
                {
                    Logger.LogVerbose("ProcessInstructionsAndOptions: No KeyValuePair option items, processing individually");
                    var processedOptions = new List<object>();

                    var currentOption = new Dictionary<string, object>(StringComparer.Ordinal);

                    foreach (object item in optionsList)
                    {
                        Logger.LogVerbose($"ProcessInstructionsAndOptions: Processing option item of type {item.GetType().Name}");

                        if (item is KeyValuePair<string, object> kvp)
                        {
                            Logger.LogVerbose($"ProcessInstructionsAndOptions: KeyValuePair - {kvp.Key} = {kvp.Value}");
                            currentOption[kvp.Key] = kvp.Value;

                            // Check if this completes an option (has Name field)
                            if (kvp.Key.Equals("Name", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrEmpty(kvp.Value?.ToString()))
                            {
                                processedOptions.Add(new Dictionary<string, object>(currentOption, StringComparer.Ordinal));
                                currentOption.Clear();
                            }
                        }
                        else if (item is Dictionary<string, object> dict)
                        {
                            Logger.LogVerbose($"ProcessInstructionsAndOptions: Dictionary with {dict.Count} keys: {string.Join(", ", dict.Keys)}");
                            if (currentOption.Count > 0)

                            {
                                processedOptions.Add(new Dictionary<string, object>(currentOption, StringComparer.Ordinal));
                                currentOption.Clear();
                            }
                            processedOptions.Add(dict);
                        }
                        else
                        {
                            Logger.LogVerbose($"ProcessInstructionsAndOptions: Unknown item type {item.GetType().Name}: {item}");
                        }
                    }

                    if (currentOption.Count > 0)

                    {
                        processedOptions.Add(new Dictionary<string, object>(currentOption, StringComparer.Ordinal));
                    }

                    Logger.LogVerbose($"ProcessInstructionsAndOptions: Processed {processedOptions.Count} options");
                    processedDict["Options"] = processedOptions;
                }
            }
        }

        /// <summary>
        /// Processes component-level properties that might be converted to KeyValuePair objects during YAML deserialization.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static void ProcessComponentLevelProperties(Dictionary<string, object> processedDict)
        {
            // Check if any component-level properties are KeyValuePair objects
            var keyValuePairKeys = new List<string>();
            foreach (KeyValuePair<string, object> kvp in processedDict)
            {
                if (kvp.Value != null && kvp.Value.GetType().Name.StartsWith("KeyValuePair", StringComparison.Ordinal))
                {
                    keyValuePairKeys.Add(kvp.Key);
                }
            }

            if (keyValuePairKeys.Count > 0)
            {
                Logger.LogVerbose($"ProcessComponentLevelProperties: Found {keyValuePairKeys.Count} component-level KeyValuePair properties: {string.Join(", ", keyValuePairKeys)}");

                foreach (string key in keyValuePairKeys)
                {
                    object kvp = processedDict[key];
                    Type kvpType = kvp.GetType();
                    System.Reflection.PropertyInfo keyProperty = kvpType.GetProperty("Key");
                    System.Reflection.PropertyInfo valueProperty = kvpType.GetProperty("Value");

                    if (keyProperty != null && valueProperty != null)
                    {
                        string kvpKey = keyProperty.GetValue(kvp)?.ToString();
                        object kvpValue = valueProperty.GetValue(kvp);

                        // Convert string values back to appropriate types
                        if (kvpValue is string stringValue)
                        {
                            if (bool.TryParse(stringValue, out bool boolValue))
                            {
                                kvpValue = boolValue;
                            }
                            else if (int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int intValue))
                            {
                                kvpValue = intValue;
                            }
                            else if (Guid.TryParse(stringValue, out Guid guidValue))
                            {
                                kvpValue = guidValue;
                            }
                        }

                        Logger.LogVerbose($"ProcessComponentLevelProperties: Processing {kvpKey} = {kvpValue}");

                        // Special handling for ModLinkFilenames - preserve the original structure
                        if (kvpKey?.Equals("ModLinkFilenames", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            Logger.LogVerbose($"ProcessComponentLevelProperties: Preserving ModLinkFilenames structure");
                            processedDict[kvpKey] = kvpValue;
                            // Don't remove the original entry for ModLinkFilenames to avoid losing it
                        }
                        else
                        {
                            processedDict[kvpKey] = kvpValue;
                            // Remove the original KeyValuePair entry to avoid duplication
                            processedDict.Remove(key);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Groups KeyValuePair objects into complete instruction dictionaries.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static List<object> GroupKeyValuePairsIntoInstructions(List<object> kvpList)
        {
            var instructions = new List<object>();

            var currentInstruction = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (object kvp in kvpList)
            {
                // Use reflection to get Key and Value from KeyValuePair
                Type kvpType = kvp.GetType();
                System.Reflection.PropertyInfo keyProperty = kvpType.GetProperty("Key");
                System.Reflection.PropertyInfo valueProperty = kvpType.GetProperty("Value");

                if (keyProperty != null && valueProperty != null)
                {
                    string key = keyProperty.GetValue(kvp)?.ToString();
                    object value = valueProperty.GetValue(kvp);

                    // Convert string values back to appropriate types
                    if (value is string stringValue)
                    {
                        if (bool.TryParse(stringValue, out bool boolValue))
                        {
                            value = boolValue;
                        }
                        else if (int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int intValue))
                        {
                            value = intValue;
                        }
                        else if (Guid.TryParse(stringValue, out Guid guidValue))
                        {
                            value = guidValue;
                        }
                    }

                    Logger.LogVerbose($"GroupKeyValuePairsIntoInstructions: Processing {key} = {value}");

                    // Check if this is a new instruction (Action field marks the start of a new instruction)
                    if (!(key is null) && key.Equals("Action", StringComparison.OrdinalIgnoreCase) && value != null)
                    {
                        // If we have a current instruction, save it before starting a new one
                        if (currentInstruction.Count > 0)
                        {
                            instructions.Add(new Dictionary<string, object>(currentInstruction, StringComparer.Ordinal));
                            currentInstruction.Clear();
                        }
                    }
                    currentInstruction[key] = value;
                }
            }

            // Add the final instruction if it has content
            if (currentInstruction.Count > 0)

            {
                instructions.Add(new Dictionary<string, object>(currentInstruction, StringComparer.Ordinal));
            }

            Logger.LogVerbose($"GroupKeyValuePairsIntoInstructions: Grouped {kvpList.Count} KeyValuePairs into {instructions.Count} instructions");
            return instructions;
        }

        /// <summary>
        /// Groups KeyValuePair objects into complete option dictionaries.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static List<object> GroupKeyValuePairsIntoOptions(List<object> kvpList)
        {
            var options = new List<object>();

            var currentOption = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (object kvp in kvpList)
            {
                // Use reflection to get Key and Value from KeyValuePair
                Type kvpType = kvp.GetType();
                System.Reflection.PropertyInfo keyProperty = kvpType.GetProperty("Key");
                System.Reflection.PropertyInfo valueProperty = kvpType.GetProperty("Value");

                if (keyProperty != null && valueProperty != null)
                {
                    string key = keyProperty.GetValue(kvp)?.ToString();
                    object value = valueProperty.GetValue(kvp);

                    // Convert string values back to appropriate types
                    if (value is string stringValue)
                    {
                        if (bool.TryParse(stringValue, out bool boolValue))
                        {
                            value = boolValue;
                        }
                        else if (int.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int intValue))
                        {
                            value = intValue;
                        }
                        else if (Guid.TryParse(stringValue, out Guid guidValue))
                        {
                            value = guidValue;
                        }
                    }

                    Logger.LogVerbose($"GroupKeyValuePairsIntoOptions: Processing {key} = {value}");
                    currentOption[key] = value;

                    // Check if this completes an option (has Name field and we've seen a Guid)
                    if (
                        key?.Equals("Name", StringComparison.OrdinalIgnoreCase) == true
                        && !string.IsNullOrEmpty(value?.ToString())
                        && currentOption.ContainsKey("Guid")
                        && currentOption.Count > 0
                    )
                    {
                        Logger.LogVerbose($"GroupKeyValuePairsIntoOptions: Completed option with {currentOption.Count} fields");

                        options.Add(new Dictionary<string, object>(currentOption, StringComparer.Ordinal));
                        currentOption.Clear();
                    }
                }
            }

            // Add any remaining option
            if (currentOption.Count > 0)
            {
                Logger.LogVerbose($"GroupKeyValuePairsIntoOptions: Adding final option with {currentOption.Count} fields");
                options.Add(new Dictionary<string, object>(currentOption, StringComparer.Ordinal));
            }

            Logger.LogVerbose($"GroupKeyValuePairsIntoOptions: Grouped {kvpList.Count} KeyValuePairs into {options.Count} options");
            return options;
        }

        /// <summary>
        /// Deserializes a component from a dictionary with all conditional logic unified.
        /// This is the migrated version from ModComponent.DeserializeComponent.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static ModComponent DeserializeComponent([NotNull] IDictionary<string, object> componentDict)
        {
            var component = new ModComponent();

            component.Guid = GetRequiredValue<Guid>(componentDict, key: "Guid");
            component.Name = GetRequiredValue<string>(componentDict, key: "Name");
            _ = Logger.LogVerboseAsync($" == Deserialize next component '{component.Name}' ==");
            component.Author = GetValueOrDefault<string>(componentDict, key: "Author") ?? string.Empty;
            component.Heading = GetValueOrDefault<string>(componentDict, key: "Heading") ?? string.Empty;
            component.Category = GetValueOrDefault<List<string>>(componentDict, key: "Category") ?? new List<string>();
            if (component.Category.Count == 0)
            {
                string categoryStr = GetValueOrDefault<string>(componentDict, key: "Category") ?? string.Empty;
                if (!string.IsNullOrEmpty(categoryStr))
                {
                    component.Category = categoryStr.Split(
                        new[] { ",", ";" },
                        StringSplitOptions.RemoveEmptyEntries
                    ).Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
                }
            }
            else if (component.Category.Count == 1)
            {
                string singleCategory = component.Category[0];
                if (!string.IsNullOrEmpty(singleCategory) &&
                     (NetFrameworkCompatibility.Contains(singleCategory, ',', StringComparison.Ordinal) || NetFrameworkCompatibility.Contains(singleCategory, ';', StringComparison.Ordinal)))
                {
                    component.Category = singleCategory.Split(
                        new[] { ",", ";" },
                        StringSplitOptions.RemoveEmptyEntries
                    ).Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
                }
                else if (string.IsNullOrWhiteSpace(singleCategory))
                {
                    component.Category = new List<string>();
                }
            }
            component.Tier = GetValueOrDefault<string>(componentDict, key: "Tier") ?? string.Empty;
            component.Description = GetValueOrDefault<string>(componentDict, key: "Description") ?? string.Empty;
            component.DescriptionSpoilerFree = GetValueOrDefault<string>(componentDict, key: "DescriptionSpoilerFree") ?? string.Empty;
            component.InstallationMethod = GetValueOrDefault<string>(componentDict, key: "InstallationMethod") ?? string.Empty;
            component.Directions = GetValueOrDefault<string>(componentDict, key: "Directions") ?? string.Empty;
            component.DirectionsSpoilerFree = GetValueOrDefault<string>(componentDict, key: "DirectionsSpoilerFree") ?? string.Empty;
            component.DownloadInstructions = GetValueOrDefault<string>(componentDict, key: "DownloadInstructions") ?? string.Empty;
            component.DownloadInstructionsSpoilerFree = GetValueOrDefault<string>(componentDict, key: "DownloadInstructionsSpoilerFree") ?? string.Empty;
            component.UsageWarning = GetValueOrDefault<string>(componentDict, key: "UsageWarning") ?? string.Empty;
            component.UsageWarningSpoilerFree = GetValueOrDefault<string>(componentDict, key: "UsageWarningSpoilerFree") ?? string.Empty;
            component.Screenshots = GetValueOrDefault<string>(componentDict, key: "Screenshots") ?? string.Empty;
            component.ScreenshotsSpoilerFree = GetValueOrDefault<string>(componentDict, key: "ScreenshotsSpoilerFree") ?? string.Empty;
            component.Language = GetValueOrDefault<List<string>>(componentDict, key: "Language") ?? new List<string>();
            if (component.Language.Count == 0)
            {
                string languageStr = GetValueOrDefault<string>(componentDict, key: "Language") ?? string.Empty;
                if (!string.IsNullOrEmpty(languageStr))
                {
                    component.Language = languageStr.Split(
                        new[] { ",", ";" },
                        StringSplitOptions.RemoveEmptyEntries
                    ).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
                }
            }
            else if (component.Language.Count == 1)
            {
                string singleLanguage = component.Language[0];
                if (!string.IsNullOrEmpty(singleLanguage) &&
                     (NetFrameworkCompatibility.Contains(singleLanguage, ',', StringComparison.Ordinal) || NetFrameworkCompatibility.Contains(singleLanguage, ';', StringComparison.Ordinal)))
                {
                    component.Language = singleLanguage.Split(
                        new[] { ",", ";" },
                        StringSplitOptions.RemoveEmptyEntries
                    ).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
                }
                else if (string.IsNullOrWhiteSpace(singleLanguage))
                {
                    component.Language = new List<string>();
                }
            }
            component.ExcludedDownloads = GetValueOrDefault<List<string>>(componentDict, key: "ExcludedDownloads") ?? new List<string>();

            // Load ResourceRegistry (primary source, replaces ModLinkFilenames)
            component.ResourceRegistry = DeserializeResourceRegistry(componentDict);

            // Ensure ResourceRegistry exists
            if (component.ResourceRegistry == null)
            {
                component.ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);
            }

            // Load legacy ModLink format and migrate directly to ResourceRegistry (not ModLinkFilenames)
            List<string> legacyModLink = GetValueOrDefault<List<string>>(componentDict, key: "ModLink") ?? new List<string>();
            if (legacyModLink.Count == 0)
            {
                string modLink = GetValueOrDefault<string>(componentDict, key: "ModLink") ?? string.Empty;
                if (!string.IsNullOrEmpty(modLink))
                {
                    legacyModLink = modLink.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                }
            }

            // Migrate legacy ModLink directly to ResourceRegistry
            if (legacyModLink.Count > 0)
            {
                Logger.LogVerbose($"Migrating legacy ModLink to ResourceRegistry for component '{component.Name}' ({legacyModLink.Count} URLs)");
                var registryDict = new Dictionary<string, ResourceMetadata>(component.ResourceRegistry, StringComparer.OrdinalIgnoreCase);

                foreach (string url in legacyModLink)
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        // Check if ResourceRegistry already has an entry for this URL (keyed by URL directly)
                        bool urlExistsInRegistry = registryDict.ContainsKey(url);

                        if (!urlExistsInRegistry)
                        {
                            // Create minimal ResourceMetadata entry from legacy ModLink
                            string normalizedUrl = UrlNormalizer.Normalize(url);
                            byte[] urlHashBytes;
#if NET48
                            using (var sha1 = System.Security.Cryptography.SHA1.Create())
                            {
                                urlHashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalizedUrl));
                            }
#else
                            urlHashBytes = NetFrameworkCompatibility.HashDataSHA1(System.Text.Encoding.UTF8.GetBytes(normalizedUrl));
#endif
                            string tempKey = System.BitConverter.ToString(urlHashBytes).Replace("-", string.Empty).ToLowerInvariant();

                            var meta = new ResourceMetadata
                            {
                                ContentKey = tempKey,
                                MetadataHash = tempKey, // Temporary, will be updated when metadata is available
                                Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase), // Empty - files will be discovered during download
                                HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal),
                                FileSize = 0,
                                TrustLevel = MappingTrustLevel.Unverified,
                                FirstSeen = DateTime.UtcNow,
                            };

                            registryDict[url] = meta; // Key by URL directly
                            Logger.LogVerbose($"Created ResourceRegistry entry from legacy ModLink for URL: {url}");
                        }
                    }
                }

                component.ResourceRegistry = registryDict;
            }

            // DEPRECATED: Load ModLinkFilenames for backward compatibility and migrate to ResourceRegistry
            // This inline deserialization replaces the removed DeserializeModLinkFilenames function
            var deserializedFilenames = new Dictionary<string, Dictionary<string, bool?>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if ((componentDict.TryGetValue("ModLinkFilenames", out object modLinkFilenamesObj) ||
                     componentDict.TryGetValue("modLinkFilenames", out modLinkFilenamesObj)) && modLinkFilenamesObj != null)
                {
                    Logger.LogVerbose($"Migrating deprecated ModLinkFilenames to ResourceRegistry for component '{component.Name}'");

                    if (modLinkFilenamesObj is IDictionary<string, object> urlDict)
                    {
                        foreach (KeyValuePair<string, object> kvp in urlDict)
                        {
                            string url = kvp.Key;
                            var filenameDict = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

                            if (kvp.Value is IDictionary<string, object> filenameObj)
                            {
                                foreach (KeyValuePair<string, object> fileKvp in filenameObj)
                                {
                                    string filename = fileKvp.Key;
                                    bool? shouldDownload = null;

                                    if (fileKvp.Value is bool boolVal)
                                    {
                                        shouldDownload = boolVal;
                                    }
                                    else if (fileKvp.Value != null)
                                    {
                                        string valueStr = fileKvp.Value.ToString();
                                        if (!string.IsNullOrEmpty(valueStr) &&
                                            !valueStr.Equals("null", StringComparison.OrdinalIgnoreCase) &&
                                            bool.TryParse(valueStr, out bool parsedBool))
                                        {
                                            shouldDownload = parsedBool;
                                        }
                                        // else remains null (default)
                                    }

                                    filenameDict[filename] = shouldDownload;
                                }
                            }
                            else if (kvp.Value is IDictionary<object, object> objectFilenameDict)
                            {
                                foreach (KeyValuePair<object, object> fileKvp in objectFilenameDict)
                                {
                                    string filename = fileKvp.Key?.ToString();
                                    if (string.IsNullOrEmpty(filename))
                                    {
                                        continue;
                                    }


                                    bool? shouldDownload = null;
                                    if (fileKvp.Value is bool boolVal)
                                    {
                                        shouldDownload = boolVal;
                                    }
                                    else if (fileKvp.Value != null)
                                    {
                                        string valueStr = fileKvp.Value.ToString();
                                        if (!string.IsNullOrEmpty(valueStr) &&
                                            !valueStr.Equals("null", StringComparison.OrdinalIgnoreCase) &&
                                            bool.TryParse(valueStr, out bool parsedBool))
                                        {
                                            shouldDownload = parsedBool;
                                        }
                                    }

                                    filenameDict[filename] = shouldDownload;
                                }
                            }

                            if (filenameDict.Count > 0)
                            {
                                deserializedFilenames[url] = filenameDict;
                            }
                        }
                    }
                    else if (modLinkFilenamesObj is IDictionary<object, object> objectDict)
                    {
                        foreach (KeyValuePair<object, object> kvp in objectDict)
                        {
                            string url = kvp.Key?.ToString();
                            if (string.IsNullOrEmpty(url))
                            {
                                continue;
                            }


                            var filenameDict = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

                            if (kvp.Value is IDictionary<object, object> filenameObj)
                            {
                                foreach (KeyValuePair<object, object> fileKvp in filenameObj)
                                {
                                    string filename = fileKvp.Key?.ToString();
                                    if (string.IsNullOrEmpty(filename))
                                    {
                                        continue;
                                    }


                                    bool? shouldDownload = null;
                                    if (fileKvp.Value is bool boolVal)
                                    {
                                        shouldDownload = boolVal;
                                    }
                                    else if (fileKvp.Value != null)
                                    {
                                        string valueStr = fileKvp.Value.ToString();
                                        if (!string.IsNullOrEmpty(valueStr) &&
                                            !valueStr.Equals("null", StringComparison.OrdinalIgnoreCase) &&
                                            bool.TryParse(valueStr, out bool parsedBool))
                                        {
                                            shouldDownload = parsedBool;
                                        }
                                    }

                                    filenameDict[filename] = shouldDownload;
                                }
                            }

                            if (filenameDict.Count > 0)
                            {
                                deserializedFilenames[url] = filenameDict;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to deserialize deprecated ModLinkFilenames (non-fatal): {ex.Message}");
            }

            // Migrate deserialized ModLinkFilenames entries to ResourceRegistry if not already present
            if (deserializedFilenames.Count > 0)
            {
                Logger.LogVerbose($"Migrating {deserializedFilenames.Count} deprecated ModLinkFilenames entries to ResourceRegistry for component '{component.Name}'");
                var registryDict = new Dictionary<string, ResourceMetadata>(component.ResourceRegistry, StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, Dictionary<string, bool?>> kvp in deserializedFilenames)
                {
                    string url = kvp.Key;
                    Dictionary<string, bool?> filenames = kvp.Value;

                    // Check if ResourceRegistry already has an entry for this URL (keyed by URL directly)
                    bool urlExistsInRegistry = registryDict.ContainsKey(url);

                    if (!urlExistsInRegistry && filenames != null && filenames.Count > 0)
                    {
                        // Create minimal ResourceMetadata entry from ModLinkFilenames
                        string normalizedUrl = UrlNormalizer.Normalize(url);
                        byte[] urlHashBytes;
#if NET48
                        using (var sha1 = System.Security.Cryptography.SHA1.Create())
                        {
                            urlHashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalizedUrl));
                        }
#else
                        urlHashBytes = NetFrameworkCompatibility.HashDataSHA1(System.Text.Encoding.UTF8.GetBytes(normalizedUrl));
#endif
                        string tempKey = BitConverter.ToString(urlHashBytes).Replace("-", string.Empty).ToLowerInvariant();

                        var meta = new ResourceMetadata
                        {
                            ContentKey = tempKey,
                            MetadataHash = tempKey, // Temporary, will be updated when metadata is available
                            Files = new Dictionary<string, bool?>(filenames, StringComparer.Ordinal),
                            HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal),
                            FileSize = 0,
                            TrustLevel = MappingTrustLevel.Unverified,
                            FirstSeen = DateTime.UtcNow,
                        };

                        registryDict[url] = meta; // Key by URL directly
                        Logger.LogVerbose($"Created ResourceRegistry entry from deprecated ModLinkFilenames for URL: {url}");
                    }
                }

                component.ResourceRegistry = registryDict;
            }

            component.Dependencies = GetValueOrDefault<List<Guid>>(componentDict, key: "Dependencies") ?? new List<Guid>();
            component.Restrictions = GetValueOrDefault<List<Guid>>(componentDict, key: "Restrictions") ?? new List<Guid>();
            component.InstallBefore = GetValueOrDefault<List<Guid>>(componentDict, key: "InstallBefore") ?? new List<Guid>();
            component.InstallAfter = GetValueOrDefault<List<Guid>>(componentDict, key: "InstallAfter") ?? new List<Guid>();
            component.IsSelected = GetValueOrDefault<bool>(componentDict, key: "IsSelected");

            Logger.LogVerbose($"=== Processing Instructions for component '{component.Name}' ===");
            Logger.LogVerbose($"componentDict contains 'Instructions' key: {componentDict.ContainsKey("Instructions")}");
            IList<object> componentInstructions = null;
            if (componentDict.ContainsKey("Instructions"))
            {
                object instructionsObj = componentDict["Instructions"];
                Logger.LogVerbose($"Instructions object type: {instructionsObj?.GetType().Name ?? "null"}, value: {instructionsObj}");
                if (instructionsObj is TomlTableArray instructionsTableArray)
                {
                    Logger.LogVerbose($"Instructions is TomlTableArray with {instructionsTableArray.Count} items");
                    var instructionsList = new List<object>();
                    foreach (object item in instructionsTableArray)
                    {
                        instructionsList.Add(item);
                    }
                    componentInstructions = instructionsList;
                }
                else if (instructionsObj is IList<object> instructionsList)
                {
                    Logger.LogVerbose($"Instructions is IList<object> with {instructionsList.Count} items");
                    componentInstructions = instructionsList;
                }
                else
                {
                    Logger.LogVerbose($"Instructions is NOT IList<object>, actual type: {instructionsObj?.GetType().Name ?? "null"}, actual value: {instructionsObj}");
                }
            }
            else
            {
                Logger.LogVerbose($"componentDict does NOT contain 'Instructions' key. Available keys: {string.Join(", ", componentDict.Keys)}");
            }

            component.Instructions = DeserializeInstructions(componentInstructions, component);
            component.Options = DeserializeOptions(GetValueOrDefault<IList<object>>(componentDict, key: "Options"));
            _ = Logger.LogVerboseAsync($"Successfully deserialized component '{component.Name}'");

            return component;
        }

        /// <summary>
        /// Removes duplicate options from a component. Options are considered duplicates if they have identical instructions.
        /// Keeps the first occurrence and removes subsequent duplicates.
        /// </summary>
        private static void RemoveDuplicateOptions([NotNull] ModComponent component)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (component.Options.Count <= 1)
            {
                return;
            }

            var optionsToRemove = new List<int>();
            var guidsToRemove = new HashSet<Guid>();

            // Compare each option with all subsequent options
            for (int i = 0; i < component.Options.Count; i++)
            {
                if (optionsToRemove.Contains(i))
                {
                    continue;
                }

                Option option1 = component.Options[i];

                for (int j = i + 1; j < component.Options.Count; j++)
                {
                    if (optionsToRemove.Contains(j))
                    {
                        continue;
                    }

                    Option option2 = component.Options[j];

                    // Compare instructions
                    if (AreInstructionsIdentical(option1.Instructions, option2.Instructions))
                    {
                        // Mark option2 for removal (keep the earlier one)
                        optionsToRemove.Add(j);
                        guidsToRemove.Add(option2.Guid);
                        Logger.LogWarning($"Component '{component.Name}': Duplicate option detected - '{option2.Name}' (GUID: {option2.Guid}) has identical instructions to '{option1.Name}' (GUID: {option1.Guid}). Removing duplicate.");
                    }
                }
            }

            // Remove options in reverse order to maintain indices
            if (optionsToRemove.Count > 0)
            {
                foreach (int index in optionsToRemove.OrderByDescending(x => x))
                {
                    component.Options.RemoveAt(index);
                }

                // Remove GUIDs from Choose instructions
                RemoveGuidsFromChooseInstructions(component, guidsToRemove);
            }
        }

        /// <summary>
        /// Compares two instruction collections to determine if they are identical.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static bool AreInstructionsIdentical(
            [NotNull][ItemNotNull] System.Collections.ObjectModel.ObservableCollection<Instruction> instructions1,
            [NotNull][ItemNotNull] System.Collections.ObjectModel.ObservableCollection<Instruction> instructions2)
        {
            if (instructions1 is null || instructions2 is null)
            {
                return false;
            }

            if (instructions1.Count != instructions2.Count)
            {
                return false;
            }

            for (int i = 0; i < instructions1.Count; i++)
            {
                Instruction instr1 = instructions1[i];
                Instruction instr2 = instructions2[i];

                if (instr1.Action != instr2.Action)
                {
                    return false;
                }

                if (!string.Equals(instr1.Destination, instr2.Destination, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(instr1.Arguments, instr2.Arguments, StringComparison.Ordinal))
                {
                    return false;
                }

                if (instr1.Overwrite != instr2.Overwrite)
                {
                    return false;
                }

                // Compare Source lists
                if (instr1.Source.Count != instr2.Source.Count)
                {
                    return false;
                }

                for (int s = 0; s < instr1.Source.Count; s++)
                {
                    if (!string.Equals(instr1.Source[s], instr2.Source[s], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                // Compare Dependencies
                if (instr1.Dependencies.Count != instr2.Dependencies.Count)
                {
                    return false;
                }

                if (!instr1.Dependencies.SequenceEqual(instr2.Dependencies))
                {
                    return false;
                }

                // Compare Restrictions
                if (instr1.Restrictions.Count != instr2.Restrictions.Count)
                {
                    return false;
                }

                if (!instr1.Restrictions.SequenceEqual(instr2.Restrictions))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Removes specified GUIDs from all Choose instruction Source lists in a component.
        /// </summary>
        private static void RemoveGuidsFromChooseInstructions([NotNull] ModComponent component, [NotNull] HashSet<Guid> guidsToRemove)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (guidsToRemove is null)
            {
                throw new ArgumentNullException(nameof(guidsToRemove));
            }

            foreach (Instruction instruction in component.Instructions)
            {
                if (instruction.Action == ActionType.Choose)
                {
                    // Source contains GUIDs as strings
                    var originalSource = instruction.Source.ToList();
                    var filteredSource = new List<string>();

                    foreach (string guidStr in originalSource)
                    {
                        if (Guid.TryParse(guidStr, out Guid guid))
                        {
                            if (!guidsToRemove.Contains(guid))
                            {
                                filteredSource.Add(guidStr);
                            }
                            else
                            {
                                Logger.LogVerbose($"Removed GUID {guid} from Choose instruction");
                            }
                        }
                        else
                        {
                            // Keep non-GUID strings as-is
                            filteredSource.Add(guidStr);
                        }
                    }

                    // Update the Source property with the filtered list
                    instruction.Source = filteredSource;
                }
            }
        }

        public static void ParseMetadataSection(TomlTable tomlTable)
        {
            if (tomlTable is null)
            {
                return;
            }

            var mainConfig = new MainConfig();

            mainConfig.fileFormatVersion = "2.0";
            mainConfig.targetGame = string.Empty;
            mainConfig.buildName = string.Empty;
            mainConfig.buildAuthor = string.Empty;
            mainConfig.buildDescription = string.Empty;
            mainConfig.lastModified = null;
            mainConfig.preambleContent = string.Empty;
            mainConfig.epilogueContent = string.Empty;
            mainConfig.widescreenWarningContent = string.Empty;
            mainConfig.aspyrExclusiveWarningContent = string.Empty;
            mainConfig.installationWarningContent = string.Empty;
            try
            {
                if (!tomlTable.TryGetValue("metadata", out object metadataObj) || !(metadataObj is TomlTable metadataTable))
                {
                    return;
                }

                if (metadataTable.TryGetValue("fileFormatVersion", out object versionObj))
                {
                    mainConfig.fileFormatVersion = versionObj.ToString() ?? "2.0";
                }

                if (metadataTable.TryGetValue("targetGame", out object gameObj))
                {
                    mainConfig.targetGame = gameObj.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("buildName", out object nameObj))
                {
                    mainConfig.buildName = nameObj.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("buildAuthor", out object authorObj))
                {
                    mainConfig.buildAuthor = authorObj.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("buildDescription", out object descObj))
                {
                    mainConfig.buildDescription = descObj.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("lastModified", out object modifiedObj) &&
                    DateTime.TryParse(modifiedObj.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                {
                    mainConfig.lastModified = parsedDate;
                }
                // Always load content sections if present (check both Pascal and camel case for backward compatibility)
                if (metadataTable.TryGetValue("preambleContent", out object preambleObj) || metadataTable.TryGetValue("PreambleContent", out preambleObj))
                {
                    mainConfig.preambleContent = preambleObj?.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("epilogueContent", out object epilogueObj) || metadataTable.TryGetValue("EpilogueContent", out epilogueObj))
                {
                    mainConfig.epilogueContent = epilogueObj?.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("widescreenWarningContent", out object widescreenObj) || metadataTable.TryGetValue("WidescreenWarningContent", out widescreenObj))
                {
                    mainConfig.widescreenWarningContent = widescreenObj?.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("aspyrExclusiveWarningContent", out object aspyrObj) || metadataTable.TryGetValue("AspyrExclusiveWarningContent", out aspyrObj))
                {
                    mainConfig.aspyrExclusiveWarningContent = aspyrObj?.ToString() ?? string.Empty;
                }

                if (metadataTable.TryGetValue("installationWarningContent", out object installationWarningObj) || metadataTable.TryGetValue("InstallationWarningContent", out installationWarningObj))
                {
                    mainConfig.installationWarningContent = installationWarningObj?.ToString() ?? string.Empty;
                }

                Logger.LogVerbose($"Loaded metadata: Game={MainConfig.TargetGame}, Version={MainConfig.FileFormatVersion}, Build={MainConfig.BuildName}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to parse metadata section (non-fatal): {ex.Message}");
            }
        }

        [ItemNotNull]
        [NotNull]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        internal static System.Collections.ObjectModel.ObservableCollection<Instruction> DeserializeInstructions(
                [CanBeNull][ItemCanBeNull] IList<object> instructionsSerializedList,
                object parentComponent
            )
        {
            string componentName;
            if (parentComponent is ModComponent mc)
            {
                componentName = mc.Name;
            }
            else if (parentComponent is Option opt)
            {
                componentName = opt.Name;
            }
            else
            {
                componentName = "Unknown";
            }

            if (instructionsSerializedList is null || instructionsSerializedList.Count == 0)
            {
                _ = Logger.LogWarningAsync($"No instructions found for component '{componentName}'");
                return new System.Collections.ObjectModel.ObservableCollection<Instruction>();
            }

            Logger.LogVerbose($"DeserializeInstructions called for '{componentName}' with {instructionsSerializedList.Count} items");
            for (int i = 0; i < Math.Min(instructionsSerializedList.Count, 3); i++)
            {
                Logger.LogVerbose($"  instructionsSerializedList[{i}] type: {instructionsSerializedList[i]?.GetType().Name ?? "null"}, value: {instructionsSerializedList[i]}");
            }

            // Check if we're dealing with individual instruction fields (KeyValuePair objects) that need to be grouped
            bool needsGrouping = instructionsSerializedList.Count > 0 && instructionsSerializedList[0] is KeyValuePair<string, object>;

            if (needsGrouping)
            {
                Logger.LogVerbose($"Detected individual instruction fields, using GroupInstructionFieldsIntoInstructions");
                return GroupInstructionFieldsIntoInstructions(instructionsSerializedList, parentComponent);
            }

            var instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>();
            for (int index = 0; index < instructionsSerializedList.Count; index++)
            {
                Logger.LogVerbose($"Processing instruction {index + 1} for '{componentName}': {instructionsSerializedList[index]}");
                Dictionary<string, object> instructionDict =
                    Serializer.SerializeIntoDictionary(instructionsSerializedList[index]);
                Logger.LogVerbose($"Serialized instruction dict: {string.Join(", ", instructionDict.Keys)}");

                // Only deserialize paths for non-Choose instructions (Choose instructions have GUIDs as sources)
                string strAction = GetValueOrDefault<string>(instructionDict, key: "Action");
                if (!string.Equals(strAction, "Choose", StringComparison.OrdinalIgnoreCase))
                {
                    Serializer.DeserializePathInDictionary(instructionDict, key: "Source");
                }

                Serializer.DeserializeGuidDictionary(instructionDict, key: "Restrictions");
                Serializer.DeserializeGuidDictionary(instructionDict, key: "Dependencies");
                var instruction = new Instruction();

                // Ignore GUID if present (for backward compatibility)
                _ = instructionDict.TryGetValue("Guid", out _);
                if (string.Equals(GetValueOrDefault<string>(instructionDict, key: "Action"), "TSLPatcher", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(GetValueOrDefault<string>(instructionDict, key: "Action"), "HoloPatcher", StringComparison.OrdinalIgnoreCase))
                {
                    instruction.Action = ActionType.Patcher;
                    _ = Logger.LogVerboseAsync($" -- Deserialize instruction #{index + 1} action '{strAction}' -> Patcher (backward compatibility)");
                }
                else if (Enum.TryParse(GetValueOrDefault<string>(instructionDict, key: "Action"), ignoreCase: true, out ActionType action))
                {
                    instruction.Action = action;
                    _ = Logger.LogVerboseAsync($" -- Deserialize instruction #{index + 1} action '{action}'");
                }
                else
                {
                    _ = Logger.LogErrorAsync(
                        $"{Environment.NewLine} -- Missing/invalid action for instruction #{index + 1}"
                    );
                    instruction.Action = ActionType.Unset;
                }
                instruction.Arguments = GetValueOrDefault<string>(instructionDict, key: "Arguments") ?? string.Empty;
                // Default Overwrite behavior: Delete defaults to false (lenient), others default to true
                if (instructionDict.ContainsKey("Overwrite"))
                {
                    instruction.Overwrite = GetValueOrDefault<bool>(instructionDict, key: "Overwrite");
                }
                else
                {
                    instruction.Overwrite = instruction.Action != ActionType.Delete;
                }
                instruction.Restrictions = GetValueOrDefault<List<Guid>>(instructionDict, key: "Restrictions")
                    ?? new List<Guid>();
                instruction.Dependencies = GetValueOrDefault<List<Guid>>(instructionDict, key: "Dependencies")
                    ?? new List<Guid>();
                instruction.Source = GetValueOrDefault<List<string>>(instructionDict, key: "Source")
                    ?? new List<string>();
                instruction.Destination = GetValueOrDefault<string>(instructionDict, key: "Destination") ?? string.Empty;
                instructions.Add(instruction);
                if (parentComponent is ModComponent parentMc)
                {
                    instruction.SetParentComponent(parentMc);
                }
            }
            return instructions;
        }

        /// <summary>
        /// Groups individual KeyValuePair fields into complete instruction dictionaries.
        /// This handles the case where Tomlyn parses [[thisMod.Instructions]] as separate field entries.
        /// </summary>
        [ItemNotNull]
        [NotNull]
        private static System.Collections.ObjectModel.ObservableCollection<Instruction> GroupInstructionFieldsIntoInstructions(
            [NotNull] IList<object> instructionFields,
            object parentComponent)
        {
            string componentName;
            if (parentComponent is ModComponent mc)
            {
                componentName = mc.Name;
            }
            else
            {
                componentName = "Unknown";
            }

            Logger.LogVerbose($"=== GroupInstructionFieldsIntoInstructions for '{componentName}' ===");
            Logger.LogVerbose($"Processing {instructionFields.Count} individual instruction fields");

            var instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>();

            var currentInstruction = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (object fieldObj in instructionFields)
            {
                if (fieldObj is KeyValuePair<string, object> kvp)
                {
                    string key = kvp.Key;
                    object value = kvp.Value;

                    Logger.LogVerbose($"Processing field: {key} = {value} (type: {value?.GetType().Name ?? "null"})");

                    // Convert Tomlyn types to standard .NET types
                    object convertedValue = ConvertTomlynValue(value);

                    // If this is a Guid field and we already have fields in the current instruction, it's a new instruction
                    if (key.Equals("Guid", StringComparison.OrdinalIgnoreCase) && currentInstruction.Count > 0)
                    {
                        // We have a complete instruction, process it
                        Logger.LogVerbose($"Found complete instruction with Guid: {(currentInstruction.ContainsKey("Guid") ? currentInstruction["Guid"] : "unknown")}");
                        ProcessCompleteInstruction(currentInstruction, instructions, parentComponent);
                        currentInstruction = new Dictionary<string, object>(StringComparer.Ordinal);
                    }

                    currentInstruction[key] = convertedValue;
                }
            }

            // Process the last instruction if there is one
            if (currentInstruction.Count > 0)
            {
                Logger.LogVerbose($"Processing final instruction with {currentInstruction.Count} fields");
                ProcessCompleteInstruction(currentInstruction, instructions, parentComponent);
            }

            Logger.LogVerbose($"Grouped {instructionFields.Count} fields into {instructions.Count} complete instructions");
            return instructions;
        }

        /// <summary>
        /// Converts Tomlyn-specific types to standard .NET types that can be processed by the instruction deserializer.
        /// </summary>
        private static object ConvertTomlynValue(object value)
        {
            if (value is null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            // Handle TomlTableArray - preserve it for further processing (e.g., Instructions)
            if (value is TomlTableArray)
            {
                return value;
            }

            // Handle TomlArray by converting to List<string>
            if (value is TomlArray tomlArray)
            {
                var list = new List<string>();
                foreach (object item in tomlArray)
                {
                    list.Add(item?.ToString() ?? string.Empty);
                }
                return list;
            }

            // Handle other Tomlyn types by converting to string
            if (value.GetType().Namespace?.StartsWith("Tomlyn", StringComparison.Ordinal) == true)
            {
                return value.ToString();
            }

            return value;
        }

        /// <summary>
        /// Converts a TomlTable to a Dictionary&lt;string, object&gt; recursively, handling nested TomlTable objects.
        /// </summary>
        private static Dictionary<string, object> ConvertTomlTableToDictionary(TomlTable tomlTable)
        {
            if (tomlTable == null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kvp in tomlTable)
            {
                // Recursively convert nested TomlTable objects
                if (kvp.Value is TomlTable nestedTable)
                {
                    result[kvp.Key] = ConvertTomlTableToDictionary(nestedTable);
                }
                // Handle TomlArray (list of nested tables)
                else if (kvp.Value is TomlArray tomlArray)
                {
                    var list = new List<object>();
                    foreach (object item in tomlArray)
                    {
                        if (item is TomlTable arrayTable)
                        {
                            list.Add(ConvertTomlTableToDictionary(arrayTable));
                        }
                        else
                        {
                            list.Add(item);
                        }
                    }
                    result[kvp.Key] = list;
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// Processes a complete instruction dictionary and adds it to the instructions collection.
        /// </summary>
        private static void ProcessCompleteInstruction(
            Dictionary<string, object> instructionDict,
            System.Collections.ObjectModel.ObservableCollection<Instruction> instructions,
            object parentComponent)
        {
            Logger.LogVerbose($"Processing complete instruction with keys: {string.Join(", ", instructionDict.Keys)}");

            // Only deserialize paths for non-Choose instructions (Choose instructions have GUIDs as sources)
            string strAction = GetValueOrDefault<string>(instructionDict, key: "Action");
            if (!string.Equals(strAction, "Choose", StringComparison.OrdinalIgnoreCase))
            {
                Serializer.DeserializePathInDictionary(instructionDict, key: "Source");
            }

            Serializer.DeserializeGuidDictionary(instructionDict, key: "Restrictions");
            Serializer.DeserializeGuidDictionary(instructionDict, key: "Dependencies");

            var instruction = new Instruction();

            // Ignore GUID if present (for backward compatibility)
            _ = instructionDict.TryGetValue("Guid", out _);
            if (string.Equals(strAction, "TSLPatcher", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(strAction, "HoloPatcher", StringComparison.OrdinalIgnoreCase))
            {
                instruction.Action = ActionType.Patcher;
                Logger.LogVerbose($"Instruction action '{strAction}' -> Patcher (backward compatibility)");
            }
            else if (Enum.TryParse(strAction, ignoreCase: true, out ActionType action))
            {
                instruction.Action = action;
                Logger.LogVerbose($"Instruction action: '{action}'");
            }
            else
            {
                Logger.LogError($"Missing/invalid action for instruction: {strAction}");
                instruction.Action = ActionType.Unset;
            }

            instruction.Arguments = GetValueOrDefault<string>(instructionDict, key: "Arguments") ?? string.Empty;
            if (instructionDict.ContainsKey("Overwrite"))
            {
                instruction.Overwrite = GetValueOrDefault<bool>(instructionDict, key: "Overwrite");
            }
            else
            {
                instruction.Overwrite = instruction.Action != ActionType.Delete;
            }

            instruction.Restrictions = GetValueOrDefault<List<Guid>>(instructionDict, key: "Restrictions") ?? new List<Guid>();
            instruction.Dependencies = GetValueOrDefault<List<Guid>>(instructionDict, key: "Dependencies") ?? new List<Guid>();
            instruction.Source = GetValueOrDefault<List<string>>(instructionDict, key: "Source") ?? new List<string>();
            instruction.Destination = GetValueOrDefault<string>(instructionDict, key: "Destination") ?? string.Empty;

            instructions.Add(instruction);
            if (parentComponent is ModComponent parentMc)
            {
                instruction.SetParentComponent(parentMc);
            }

            Logger.LogVerbose($"Successfully created instruction: Action={instruction.Action}, Source.Count={instruction.Source.Count}, Destination='{instruction.Destination}'");
        }

        [ItemNotNull]
        [NotNull]
        internal static System.Collections.ObjectModel.ObservableCollection<Option> DeserializeOptions(
            [CanBeNull][ItemCanBeNull] IList<object> optionsSerializedList
        )
        {
            if (optionsSerializedList is null || optionsSerializedList.Count == 0)
            {
                return new System.Collections.ObjectModel.ObservableCollection<Option>();
            }

            Logger.LogVerbose($"DeserializeOptions called with {optionsSerializedList.Count} items");
            for (int i = 0; i < Math.Min(optionsSerializedList.Count, 3); i++)
            {
                Logger.LogVerbose($"  optionsSerializedList[{i}] type: {optionsSerializedList[i]?.GetType().Name ?? "null"}, value: {optionsSerializedList[i]}");
            }

            // Check if we're dealing with individual option fields (KeyValuePair objects) that need to be grouped
            bool needsGrouping = optionsSerializedList[0] is KeyValuePair<string, object>;

            if (needsGrouping)
            {
                Logger.LogVerbose($"Detected individual option fields, using GroupOptionFieldsIntoOptions");
                return GroupOptionFieldsIntoOptions(optionsSerializedList);
            }

            var options = new System.Collections.ObjectModel.ObservableCollection<Option>();
            for (int index = 0; index < optionsSerializedList.Count; index++)
            {
                // Handle both KeyValuePair<string, object> (from TOML array of tables) and direct IDictionary
                IDictionary<string, object> optionsDict;
                if (optionsSerializedList[index] is KeyValuePair<string, object> kvp)
                {
                    optionsDict = kvp.Value as IDictionary<string, object>;
                }
                else
                {
                    optionsDict = optionsSerializedList[index] as IDictionary<string, object>;
                }

                if (optionsDict is null)
                {
                    continue;
                }

                Serializer.DeserializeGuidDictionary(optionsDict, key: "Restrictions");
                Serializer.DeserializeGuidDictionary(optionsDict, key: "Dependencies");
                var option = new Option();
                _ = Logger.LogVerboseAsync($"-- Deserialize option #{index + 1}");
                option.Name = GetRequiredValue<string>(optionsDict, key: "Name");
                option.Description = GetValueOrDefault<string>(optionsDict, key: "Description") ?? string.Empty;
                _ = Logger.LogVerboseAsync($" == Deserialize next option '{option.Name}' ==");
                option.Guid = GetRequiredValue<Guid>(optionsDict, key: "Guid");
                option.Restrictions =
                    GetValueOrDefault<List<Guid>>(optionsDict, key: "Restrictions") ?? new List<Guid>();
                option.Dependencies =
                    GetValueOrDefault<List<Guid>>(optionsDict, key: "Dependencies") ?? new List<Guid>();
                option.Instructions = DeserializeInstructions(
                    GetValueOrDefault<IList<object>>(optionsDict, key: "Instructions"), option
                );
                option.IsSelected = GetValueOrDefault<bool>(optionsDict, key: "IsSelected");
                options.Add(option);
            }
            return options;
        }

        /// <summary>
        /// Groups individual KeyValuePair fields into complete option dictionaries.
        /// This handles the case where Tomlyn parses [[thisMod.Options]] as separate field entries.
        /// </summary>
        [ItemNotNull]
        [NotNull]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static System.Collections.ObjectModel.ObservableCollection<Option> GroupOptionFieldsIntoOptions(
            [NotNull] IList<object> optionFields)
        {
            Logger.LogVerbose($"=== GroupOptionFieldsIntoOptions ===");
            Logger.LogVerbose($"Processing {optionFields.Count} individual option fields");

            var options = new System.Collections.ObjectModel.ObservableCollection<Option>();

            var currentOption = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (object fieldObj in optionFields)
            {
                if (fieldObj is KeyValuePair<string, object> kvp)
                {
                    string key = kvp.Key;
                    object value = kvp.Value;

                    Logger.LogVerbose($"Processing option field: {key} = {value} (type: {value?.GetType().Name ?? "null"})");

                    // Special handling for Instructions field - preserve complex objects
                    object convertedValue;
                    if (key.Equals("Instructions", StringComparison.OrdinalIgnoreCase) && value is TomlArray instructionsArray)
                    {
                        // Convert TomlArray containing instruction dictionaries to List<object>
                        var instructionsList = new List<object>();
                        foreach (object item in instructionsArray)
                        {
                            if (item is TomlTable table)
                            {
                                instructionsList.Add(table);
                            }
                            else if (item is IDictionary<string, object> dict)
                            {
                                instructionsList.Add(dict);
                            }
                            else
                            {
                                // Convert other types to dictionary if possible
                                Dictionary<string, object> converted = Serializer.SerializeIntoDictionary(item);
                                instructionsList.Add(converted);
                            }
                        }
                        convertedValue = instructionsList;
                    }
                    else
                    {
                        // Convert Tomlyn types to standard .NET types
                        convertedValue = ConvertTomlynValue(value);
                    }

                    // If this is a Guid field and we already have fields in the current option, it's a new option
                    if (key.Equals("Guid", StringComparison.OrdinalIgnoreCase) && currentOption.Count > 0)
                    {
                        // We have a complete option, process it
                        Logger.LogVerbose($"Found complete option with Guid: {(currentOption.TryGetValue("Guid", out object guidValue) ? guidValue : "unknown")}");
                        ProcessCompleteOption(currentOption, options);

                        currentOption = new Dictionary<string, object>(StringComparer.Ordinal);
                    }

                    currentOption[key] = convertedValue;
                }
            }

            // Process the last option if there is one
            if (currentOption.Count > 0)
            {
                Logger.LogVerbose($"Processing final option with {currentOption.Count} fields");
                ProcessCompleteOption(currentOption, options);
            }

            Logger.LogVerbose($"Grouped {optionFields.Count} fields into {options.Count} complete options");
            return options;
        }

        /// <summary>
        /// Processes a complete option dictionary and adds it to the options collection.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static void ProcessCompleteOption(
            Dictionary<string, object> optionDict,
            System.Collections.ObjectModel.ObservableCollection<Option> options)
        {
            Logger.LogVerbose($"Processing complete option with keys: {string.Join(", ", optionDict.Keys)}");

            Serializer.DeserializeGuidDictionary(optionDict, key: "Restrictions");
            Serializer.DeserializeGuidDictionary(optionDict, key: "Dependencies");

            var option = new Option();
            option.Name = GetRequiredValue<string>(optionDict, key: "Name");
            option.Description = GetValueOrDefault<string>(optionDict, key: "Description") ?? string.Empty;
            option.Guid = GetRequiredValue<Guid>(optionDict, key: "Guid");
            option.Restrictions = GetValueOrDefault<List<Guid>>(optionDict, key: "Restrictions") ?? new List<Guid>();
            option.Dependencies = GetValueOrDefault<List<Guid>>(optionDict, key: "Dependencies") ?? new List<Guid>();
            option.IsSelected = GetValueOrDefault<bool>(optionDict, key: "IsSelected");

            // Process option instructions if present
            if (optionDict.ContainsKey("Instructions"))
            {
                object instructionsObj = optionDict["Instructions"];
                Logger.LogVerbose($"Option '{option.Name}' has Instructions field: type={instructionsObj?.GetType().Name ?? "null"}");

                IList<object> instructionsList = null;

                // Handle TomlTableArray (from Tomlyn parser)
                if (instructionsObj is TomlTableArray optionInstructionsTableArray)
                {
                    Logger.LogVerbose($"Option Instructions is TomlTableArray with {optionInstructionsTableArray.Count} items");
                    var convertedList = new List<object>();
                    foreach (TomlTable item in optionInstructionsTableArray)
                    {
                        convertedList.Add(item);
                    }
                    instructionsList = convertedList;
                }
                // Handle inline array of instruction dictionaries (legacy format)
                else if (instructionsObj is System.Collections.IEnumerable instructionsEnumerable && !(instructionsObj is string))
                {
                    var convertedList = new List<object>();
                    foreach (object item in instructionsEnumerable)
                    {
                        // Convert Tomlyn types to dictionaries
                        if (item is IDictionary<string, object> dict)
                        {
                            convertedList.Add(dict);
                        }
                        else if (item is TomlTable table)
                        {
                            convertedList.Add(table);
                        }
                        else
                        {
                            // Try to convert to dictionary using SerializeIntoDictionary
                            Dictionary<string, object> converted = Serializer.SerializeIntoDictionary(item);
                            convertedList.Add(converted);
                        }
                    }
                    if (convertedList.Count > 0)
                    {
                        instructionsList = convertedList;
                    }
                }
                else if (instructionsObj is IList<object> directList)
                {
                    Logger.LogVerbose($"Option Instructions is IList<object> with {directList.Count} items");
                    instructionsList = directList;
                }

                if (instructionsList != null)
                {
                    option.Instructions = DeserializeInstructions(instructionsList, option);
                    Logger.LogVerbose($"Option '{option.Name}' now has {option.Instructions.Count} instructions");
                }
                else
                {
                    Logger.LogVerbose($"Option Instructions could not be converted, actual type: {instructionsObj?.GetType().Name ?? "null"}");
                }
            }

            options.Add(option);
            Logger.LogVerbose($"Successfully created option: Name='{option.Name}', Guid={option.Guid}, Instructions.Count={option.Instructions.Count}");
        }

        [NotNull]
        internal static T GetRequiredValue<T>(
            [NotNull] IDictionary<string, object> dict,
            [NotNull] string key)
        {
            T value = GetValue<T>(dict, key, required: true);
            return object.Equals(value, default(T))
                ? throw new InvalidOperationException("GetValue cannot return null for a required value.")
                : value;
        }

        [CanBeNull]
        internal static T GetValueOrDefault<T>(
            [NotNull] IDictionary<string, object> dict,
            [NotNull] string key) =>
            GetValue<T>(dict, key, required: false);

        [CanBeNull]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        internal static T GetValue<T>(
            [NotNull] IDictionary<string, object> dict,
            [NotNull] string key, bool required)
        {
            try
            {
                if (dict is null)
                {
                    throw new ArgumentNullException(nameof(dict));
                }

                if (key is null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                Logger.LogVerbose($"GetValue<{typeof(T).Name}>: Attempting to get key '{key}' from dict with {dict.Count} keys");

                // Handle duplicate keys by consolidating values

                // First try exact key match
                if (dict.TryGetValue(key, out object value))
                {
                    Logger.LogVerbose($"GetValue: Found key '{key}', type={value?.GetType().Name ?? "null"}, value={value}");
                    // Check if this is a collection that might contain duplicates
                    if (value is System.Collections.IEnumerable valueEnumerable && !(value is string))
                    {
                        // For collections, consolidate duplicates by flattening nested collections
                        var consolidatedList = new List<object>();
                        foreach (object item in valueEnumerable)
                        {
                            if (item is System.Collections.IEnumerable nestedEnumerable && !(item is string))
                            {
                                // Flatten nested collections (handles duplicate Instructions, etc.)
                                foreach (object nestedItem in nestedEnumerable)
                                {
                                    consolidatedList.Add(nestedItem);
                                }
                            }
                            else
                            {
                                consolidatedList.Add(item);
                            }
                        }
                        value = consolidatedList;
                    }
                }
                else
                {
                    // Try case-insensitive match
                    string caseInsensitiveKey = dict.Keys.FirstOrDefault(
                        k => !(k is null) && k.Equals(key, StringComparison.OrdinalIgnoreCase)
                    );
                    if (
                        caseInsensitiveKey != null
                        && dict.TryGetValue(caseInsensitiveKey, out value)
                        && value is System.Collections.IEnumerable caseInsensitiveEnumerable
                        && !(value is string))
                    {
                        // For collections, consolidate duplicates by flattening nested collections
                        var consolidatedList = new List<object>();
                        foreach (object item in caseInsensitiveEnumerable)
                        {
                            if (item is System.Collections.IEnumerable nestedEnumerable && !(item is string))
                            {
                                // Flatten nested collections (handles duplicate Instructions, etc.)
                                foreach (object nestedItem in nestedEnumerable)
                                {
                                    consolidatedList.Add(nestedItem);
                                }
                            }
                            else
                            {
                                consolidatedList.Add(item);
                            }
                        }
                        value = consolidatedList;
                        // else: value is already set from TryGetValue for non-collection types
                    }
                }

                if (value is null)
                {
                    if (!required)
                    {
                        return default;
                    }

                    throw new KeyNotFoundException($"[Error] Missing or invalid '{key}' field.");
                }
                Type targetType = typeof(T);
                switch (value)
                {
                    case null:
                        throw new KeyNotFoundException($"[Error] Missing or invalid '{key}' field.");
                    case T t:
                        return t;
                    case string valueStr:
                        Logger.LogVerbose($"GetValue<{typeof(T).Name}>: key='{key}', type=string, value='{valueStr}'");
                        if (string.IsNullOrEmpty(valueStr))
                        {
                            return required
                                ? throw new KeyNotFoundException($"'{key}' field cannot be empty.")
                                : default(T);
                        }
                        if (targetType == typeof(Guid))
                        {
                            string guidStr = Serializer.FixGuidString(valueStr);
                            T result;
                            if (!string.IsNullOrEmpty(guidStr) && Guid.TryParse(guidStr, out Guid guid))
                            {
                                result = (T)(object)guid;
                            }
                            else
                            {
                                Logger.LogError($"GUID parsing failed for key '{key}': original='{valueStr}', fixed='{guidStr}', empty={string.IsNullOrEmpty(guidStr)}");
                                if (required)
                                {
                                    throw new ArgumentException($"'{key}' field is not a valid Guid!", nameof(key));
                                }

                                result = (T)(object)Guid.Empty;
                            }

                            return result;
                        }
                        if (targetType == typeof(string))
                        {
#pragma warning disable CS8600
                            // ReSharper disable once Possible System.InvalidCastException
                            return (T)(object)valueStr;
#pragma warning restore CS8600
                        }
                        break;
                }

                // Handle TomlTable explicitly (must come before IDictionary check since TomlTable might not implement IDictionary<string, object> directly)
                if (value is TomlTable tomlTable)
                {
                    try
                    {
                        // Convert TomlTable to Dictionary<string, object>
                        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (KeyValuePair<string, object> kvp in tomlTable)
                        {
                            // Recursively convert nested TomlTable objects
                            if (kvp.Value is TomlTable nestedTable)
                            {
                                result[kvp.Key] = ConvertTomlTableToDictionary(nestedTable);
                            }
                            else
                            {
                                result[kvp.Key] = kvp.Value;
                            }
                        }

                        // Return as target type
                        if (targetType == typeof(Dictionary<string, object>))
                        {
                            return (T)(object)result;
                        }

                        // If target is generic Dictionary, continue to generic handling
                        Type genericDictDefinition2 = targetType.IsGenericType
                            ? targetType.GetGenericTypeDefinition()
                            : null;
                        if (genericDictDefinition2 == typeof(Dictionary<,>))
                        {
                            // Convert the Dictionary<string, object> to the target generic type
                            Type[] genericArgs = typeof(T).GetGenericArguments();
                            Type dictKeyType = genericArgs.Length > 0 ? genericArgs[0] : typeof(string);
                            Type dictValueType = genericArgs.Length > 1 ? genericArgs[1] : typeof(object);

                            Type dictType = typeof(Dictionary<,>).MakeGenericType(dictKeyType, dictValueType);
                            var genericResult = (T)Activator.CreateInstance(dictType, StringComparer.OrdinalIgnoreCase);
                            System.Reflection.MethodInfo addMethod = genericResult?.GetType().GetMethod("Add");

                            foreach (KeyValuePair<string, object> kvp in result)
                            {
                                object convertedKey = kvp.Key;
                                if (dictKeyType != typeof(string))
                                {
                                    convertedKey = Convert.ChangeType(kvp.Key, dictKeyType, System.Globalization.CultureInfo.InvariantCulture);
                                }

                                object convertedValue = null;
                                if (dictValueType == typeof(bool?))
                                {
                                    if (kvp.Value == null)
                                    {
                                        convertedValue = null;
                                    }
                                    else if (kvp.Value is bool b)
                                    {
                                        convertedValue = b;
                                    }
                                    else if (kvp.Value is string str && string.Equals(str, "null", StringComparison.OrdinalIgnoreCase))
                                    {
                                        convertedValue = null;
                                    }
                                    else if (bool.TryParse(kvp.Value?.ToString(), out bool parsedBool))
                                    {
                                        convertedValue = parsedBool;
                                    }
                                    else
                                    {
                                        convertedValue = null;
                                    }
                                }
                                else
                                {
                                    convertedValue = Convert.ChangeType(kvp.Value, dictValueType, System.Globalization.CultureInfo.InvariantCulture);
                                }

                                _ = addMethod?.Invoke(genericResult, new[] { convertedKey, convertedValue });
                            }

                            return genericResult;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to convert TomlTable to Dictionary for '{key}': {ex.Message} - using default value");
                        Logger.LogVerbose($"TomlTable conversion error details for '{key}': {ex}");
                        return default;
                    }
                }

                // Handle Dictionary<string, object> types (including TomlTable and IDictionary)
                if (targetType == typeof(Dictionary<string, object>))
                {
                    // Handle direct dictionary
                    if (value is IDictionary<string, object> dictValue)
                    {
                        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (KeyValuePair<string, object> kvp in dictValue)
                        {
                            // Recursively convert nested TomlTable objects
                            if (kvp.Value is TomlTable nestedTable)
                            {
                                result[kvp.Key] = ConvertTomlTableToDictionary(nestedTable);
                            }
                            else if (kvp.Value is IDictionary<string, object> nestedDict)
                            {
                                // Recursively convert nested dictionaries
                                result[kvp.Key] = ConvertNestedDictionary(nestedDict);
                            }
                            else
                            {
                                result[kvp.Key] = kvp.Value;
                            }
                        }
                        return (T)(object)result;
                    }
                    // Handle IDictionary<object, object> (from some serializers)
                    else if (value is IDictionary<object, object> objectDict)
                    {
                        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (KeyValuePair<object, object> kvp in objectDict)
                        {
                            string dictKey = kvp.Key?.ToString();
                            if (string.IsNullOrEmpty(dictKey))
                            {
                                continue;
                            }

                            if (kvp.Value is TomlTable nestedTable)
                            {
                                result[dictKey] = ConvertTomlTableToDictionary(nestedTable);
                            }
                            else if (kvp.Value is IDictionary<object, object> nestedObjectDict)
                            {
                                var nestedDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                                foreach (KeyValuePair<object, object> nestedKvp in nestedObjectDict)
                                {
                                    string nestedKey = nestedKvp.Key?.ToString();
                                    if (!string.IsNullOrEmpty(nestedKey))
                                    {
                                        nestedDict[nestedKey] = nestedKvp.Value;
                                    }
                                }
                                result[dictKey] = nestedDict;
                            }
                            else
                            {
                                result[dictKey] = kvp.Value;
                            }
                        }
                        return (T)(object)result;
                    }
                }

                // Handle generic Dictionary types (e.g., Dictionary<string, bool?>)
                Type genericDictDefinition = targetType.IsGenericType
                    ? targetType.GetGenericTypeDefinition()
                    : null;
                if (genericDictDefinition == typeof(Dictionary<,>))
                {
                    // Handle TomlTable for generic dictionaries
                    if (value is TomlTable tomlTableForGeneric)
                    {
                        try
                        {
                            Type[] genericArgs = typeof(T).GetGenericArguments();
                            Type dictKeyType = genericArgs.Length > 0 ? genericArgs[0] : typeof(string);
                            Type dictValueType = genericArgs.Length > 1 ? genericArgs[1] : typeof(object);

                            Type dictType = typeof(Dictionary<,>).MakeGenericType(dictKeyType, dictValueType);
                            var result = (T)Activator.CreateInstance(dictType, StringComparer.OrdinalIgnoreCase);
                            System.Reflection.MethodInfo addMethod = result?.GetType().GetMethod("Add");

                            foreach (KeyValuePair<string, object> kvp in tomlTableForGeneric)
                            {
                                object convertedKey = kvp.Key;
                                if (dictKeyType != typeof(string))
                                {
                                    convertedKey = Convert.ChangeType(kvp.Key, dictKeyType, System.Globalization.CultureInfo.InvariantCulture);
                                }

                                object convertedValue = ConvertDictionaryValue(kvp.Value, dictValueType);
                                _ = addMethod?.Invoke(result, new[] { convertedKey, convertedValue });
                            }

                            return result;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to convert TomlTable to generic Dictionary for '{key}': {ex.Message} - using default value");
                            return default;
                        }
                    }
                    // Handle IDictionary<string, object> for generic dictionaries
                    else if (value is IDictionary<string, object> genericDictValue)
                    {
                        try
                        {
                            Type[] genericArgs = typeof(T).GetGenericArguments();
                            Type dictKeyType = genericArgs.Length > 0 ? genericArgs[0] : typeof(string);
                            Type dictValueType = genericArgs.Length > 1 ? genericArgs[1] : typeof(object);

                            Type dictType = typeof(Dictionary<,>).MakeGenericType(dictKeyType, dictValueType);
                            var result = (T)Activator.CreateInstance(dictType, StringComparer.OrdinalIgnoreCase);
                            System.Reflection.MethodInfo addMethod = result?.GetType().GetMethod("Add");

                            foreach (KeyValuePair<string, object> kvp in genericDictValue)
                            {
                                // Convert key if needed
                                object convertedKey = kvp.Key;
                                if (dictKeyType != typeof(string))
                                {
                                    convertedKey = Convert.ChangeType(kvp.Key, dictKeyType, System.Globalization.CultureInfo.InvariantCulture);
                                }

                                // Handle nested TomlTable objects
                                object valueToConvert = kvp.Value;
                                if (kvp.Value is TomlTable nestedTable)
                                {
                                    valueToConvert = ConvertTomlTableToDictionary(nestedTable);
                                }

                                // Convert value based on target type using helper method
                                object convertedValue = ConvertDictionaryValue(valueToConvert, dictValueType);

                                _ = addMethod?.Invoke(result, new[] { convertedKey, convertedValue });
                            }

                            return result;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to deserialize Dictionary for '{key}': {ex.Message} - using default value");
                            return default;
                        }
                    }
                    // Handle IDictionary<object, object> for generic dictionaries
                    else if (value is IDictionary<object, object> objectDictForGeneric)
                    {
                        try
                        {
                            Type[] genericArgs = typeof(T).GetGenericArguments();
                            Type dictKeyType = genericArgs.Length > 0 ? genericArgs[0] : typeof(string);
                            Type dictValueType = genericArgs.Length > 1 ? genericArgs[1] : typeof(object);

                            Type dictType = typeof(Dictionary<,>).MakeGenericType(dictKeyType, dictValueType);
                            var result = (T)Activator.CreateInstance(dictType, StringComparer.OrdinalIgnoreCase);
                            System.Reflection.MethodInfo addMethod = result?.GetType().GetMethod("Add");

                            foreach (KeyValuePair<object, object> kvp in objectDictForGeneric)
                            {
                                string keyStr = kvp.Key?.ToString();
                                if (string.IsNullOrEmpty(keyStr))
                                {
                                    continue;
                                }

                                object convertedKey = keyStr;
                                if (dictKeyType != typeof(string))
                                {
                                    convertedKey = Convert.ChangeType(keyStr, dictKeyType, System.Globalization.CultureInfo.InvariantCulture);
                                }

                                object convertedValue = ConvertDictionaryValue(kvp.Value, dictValueType);
                                _ = addMethod?.Invoke(result, new[] { convertedKey, convertedValue });
                            }

                            return result;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to deserialize Dictionary<object,object> to generic Dictionary for '{key}': {ex.Message} - using default value");
                            return default;
                        }
                    }
                }

                // Backwards/forwards compatibility: String <-> List<string> conversion
                Type genericListDefinition = targetType.IsGenericType
                    ? targetType.GetGenericTypeDefinition()
                    : null;

                // Converting string to List<string> (backwards compatibility)
                if ((genericListDefinition == typeof(List<>) || genericListDefinition == typeof(IList<>))
                    && value is string delimitedString)
                {
                    // Check if list element type is string
                    Type[] genericArgs = typeof(T).GetGenericArguments();
                    Type listElementType = genericArgs.Length > 0 ? genericArgs[0] : typeof(string);

                    if (listElementType == typeof(string))
                    {
                        try
                        {
                            Type listType = typeof(List<>).MakeGenericType(listElementType);
                            var list = (T)Activator.CreateInstance(listType);
                            System.Reflection.MethodInfo addMethod = list?.GetType().GetMethod(name: "Add");

                            // Split by semicolon or comma for delimited strings
                            string[] parts = delimitedString.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string part in parts)
                            {
                                string trimmed = part.Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                {
                                    _ = addMethod?.Invoke(list, new object[] { trimmed });
                                }
                            }

                            return list;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Failed to convert string to List<string> for '{key}': {ex.Message} - using default value");
                            return default;
                        }
                    }
                }

                // Converting List<string> (or any IEnumerable<string>) to string (forwards compatibility)
                if (targetType == typeof(string) &&
                    value is System.Collections.IEnumerable enumerable &&
                    !(value is string))
                {
                    try
                    {
                        // Try to join collection items as strings
                        var items = new List<string>();
                        foreach (object item in enumerable)
                        {
                            string itemStr = item?.ToString();
                            if (!string.IsNullOrWhiteSpace(itemStr))
                            {
                                items.Add(itemStr);
                            }
                        }

                        if (items.Count > 0)
                        {
                            // Join with comma separator
                            string result = string.Join(", ", items);
                            return (T)(object)result;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to convert collection to string for '{key}': {ex.Message} - using default value");
                        return default;
                    }
                }

                if (genericListDefinition == typeof(List<>) || genericListDefinition == typeof(IList<>))
                {
                    try
                    {
                        Type[] genericArgs = typeof(T).GetGenericArguments();
                        Type listElementType = genericArgs.Length > 0
                            ? genericArgs[0]
                            : typeof(string);
                        Type listType = typeof(List<>).MakeGenericType(listElementType);
                        var list = (T)Activator.CreateInstance(listType);
                        System.Reflection.MethodInfo addMethod = list?.GetType().GetMethod(name: "Add");

                        // Handle any IEnumerable (not just IEnumerable<object>)
                        if (value is System.Collections.IEnumerable collectionValue && !(value is string))
                        {
                            foreach (object item in collectionValue)
                            {
                                if (listElementType == typeof(Guid)
                                    && Guid.TryParse(item?.ToString(), out Guid guidItem))
                                {
                                    _ = addMethod?.Invoke(
                                        list,
                                        new[]
                                        {
                                    (object)guidItem,
                                        }
                                    );
                                }
                                else if (listElementType == typeof(string))
                                {
                                    switch (item)
                                    {
                                        case IEnumerable<object> nestedCollection when true:
                                            {
                                                foreach (object nestedItem in nestedCollection)
                                                {
                                                    string nestedStringValue = nestedItem?.ToString() ?? string.Empty;
                                                    if (!string.IsNullOrWhiteSpace(nestedStringValue))
                                                    {
                                                        _ = addMethod?.Invoke(
                                                            list,
                                                            new[]
                                                            {
                                                            (object)nestedStringValue,
                                                            }
                                                        );
                                                    }
                                                }

                                                break;
                                            }
                                        case string strItem:
                                            {
                                                if (!string.IsNullOrWhiteSpace(strItem))
                                                {
                                                    _ = addMethod?.Invoke(
                                                        list,
                                                        new[]
                                                        {
                                                        (object)strItem,
                                                        }
                                                    );
                                                }

                                                break;
                                            }
                                        default:
                                            {
                                                string stringValue = item?.ToString() ?? string.Empty;
                                                if (!string.IsNullOrWhiteSpace(stringValue))
                                                {
                                                    _ = addMethod?.Invoke(
                                                        list,
                                                        new[]
                                                        {
                                                        (object)stringValue,
                                                        }
                                                    );
                                                }

                                                break;
                                            }
                                    }
                                }
                                else
                                {
                                    _ = addMethod?.Invoke(
                                        list,
                                        new[]
                                        {
                                    item,
                                        }
                                    );
                                }
                            }
                        }
                        else
                        {
                            _ = addMethod?.Invoke(
                                list,
                                new[]
                                {
                            value,
                                }
                            );
                        }
                        return list;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to deserialize list field '{key}': {ex.Message} - using default value");
                        return default;
                    }
                }
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"Could not deserialize key '{key}': {e.Message} - using default value");
                    Logger.LogVerbose($"Deserialization error details for '{key}': {e}");
                    return default;
                }
            }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
            {
                Logger.LogWarning($"Runtime binding error for key '{key}': {ex.Message} - using default value");
                return default;
            }
            catch (InvalidCastException ex)
            {
                Logger.LogWarning($"Invalid cast for key '{key}': {ex.Message} - using default value");
                return default;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Unexpected error deserializing key '{key}': {ex.Message} - using default value");
                return default;
            }
        }

        /// <summary>
        /// Recursively converts nested dictionaries to ensure proper type handling
        /// </summary>
        private static Dictionary<string, object> ConvertNestedDictionary(IDictionary<string, object> dict)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kvp in dict)
            {
                if (kvp.Value is TomlTable nestedTable)
                {
                    result[kvp.Key] = ConvertTomlTableToDictionary(nestedTable);
                }
                else if (kvp.Value is IDictionary<string, object> nestedDict)
                {
                    result[kvp.Key] = ConvertNestedDictionary(nestedDict);
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// Converts a dictionary value to the target type, handling bool?, dictionaries, and other types
        /// </summary>
        private static object ConvertDictionaryValue(object value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            // Handle bool? specifically
            if (targetType == typeof(bool?))
            {
                if (value is bool b)
                {
                    return b;
                }

                if (value is string str && string.Equals(str, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (bool.TryParse(value.ToString(), out bool parsedBool))
                {
                    return parsedBool;
                }

                return null;
            }
            // Handle Dictionary<string, bool?> for nested dictionaries like Files
            else if (targetType == typeof(Dictionary<string, bool?>))
            {
                if (value is Dictionary<string, object> nestedDict)
                {
                    var filesDict = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, object> fileKvp in nestedDict)
                    {
                        bool? shouldDownload = null;
                        if (fileKvp.Value == null)
                        {
                            shouldDownload = null;
                        }
                        else if (fileKvp.Value is bool b)
                        {
                            shouldDownload = b;
                        }
                        else if (fileKvp.Value is string str && string.Equals(str, "null", StringComparison.OrdinalIgnoreCase))
                        {
                            shouldDownload = null;
                        }
                        else if (bool.TryParse(fileKvp.Value?.ToString(), out bool parsedBool))
                        {
                            shouldDownload = parsedBool;
                        }
                        filesDict[fileKvp.Key] = shouldDownload;
                    }
                    return filesDict;
                }
                else if (value is IDictionary<object, object> objectDict)
                {
                    var filesDict = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<object, object> fileKvp in objectDict)
                    {
                        string filename = fileKvp.Key?.ToString();
                        if (string.IsNullOrEmpty(filename))
                        {
                            continue;
                        }

                        bool? shouldDownload = null;
                        if (fileKvp.Value == null)
                        {
                            shouldDownload = null;
                        }
                        else if (fileKvp.Value is bool b)
                        {
                            shouldDownload = b;
                        }
                        else if (fileKvp.Value is string str && string.Equals(str, "null", StringComparison.OrdinalIgnoreCase))
                        {
                            shouldDownload = null;
                        }
                        else if (bool.TryParse(fileKvp.Value?.ToString(), out bool parsedBool))
                        {
                            shouldDownload = parsedBool;
                        }
                        filesDict[filename] = shouldDownload;
                    }
                    return filesDict;
                }
                else if (value is TomlTable tomlTable)
                {
                    var filesDict = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, object> fileKvp in tomlTable)
                    {
                        bool? shouldDownload = null;
                        if (fileKvp.Value == null)
                        {
                            shouldDownload = null;
                        }
                        else if (fileKvp.Value is bool b)
                        {
                            shouldDownload = b;
                        }
                        else if (fileKvp.Value is string str && string.Equals(str, "null", StringComparison.OrdinalIgnoreCase))
                        {
                            shouldDownload = null;
                        }
                        else if (bool.TryParse(fileKvp.Value?.ToString(), out bool parsedBool))
                        {
                            shouldDownload = parsedBool;
                        }
                        filesDict[fileKvp.Key] = shouldDownload;
                    }
                    return filesDict;
                }
            }
            // Handle Dictionary<string, object>
            else if (targetType == typeof(Dictionary<string, object>))
            {
                if (value is IDictionary<string, object> dictValue)
                {
                    return ConvertNestedDictionary(dictValue);
                }

                if (value is IDictionary<object, object> objectDict)
                {
                    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<object, object> kvp in objectDict)
                    {
                        string key = kvp.Key?.ToString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            result[key] = kvp.Value;
                        }
                    }
                    return result;
                }

                if (value is TomlTable tomlTable)
                {
                    return ConvertTomlTableToDictionary(tomlTable);
                }
            }

            // Default: try Convert.ChangeType for other types
            try
            {
                return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                // If conversion fails, return the value as-is (might be already the correct type)
                return value;
            }
        }

        private static Dictionary<string, object> ConvertToStringObjectDictionary(object value)
        {
            if (value is null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            if (value is Dictionary<string, object> stringObjectDict)
            {
                return new Dictionary<string, object>(stringObjectDict, StringComparer.OrdinalIgnoreCase);
            }

            if (value is IDictionary<object, object> objectDict)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<object, object> kvp in objectDict)
                {
                    string key = kvp.Key?.ToString();
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    result[key] = kvp.Value;
                }
                return result;
            }

            if (value is TomlTable tomlTable)
            {
                return ConvertTomlTableToDictionary(tomlTable);
            }

            if (value is JObject jobj)
            {
                return JTokenToDictionary(jobj);
            }

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, bool?> ConvertToStringBoolNullableDictionary(object value)
        {
            if (value is null)
            {
                return new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            }

            if (value is Dictionary<string, bool?> boolDict)
            {
                return new Dictionary<string, bool?>(boolDict, StringComparer.OrdinalIgnoreCase);
            }

            if (value is Dictionary<string, object> stringObjectDict)
            {
                return ConvertStringObjectToBoolDictionary(stringObjectDict);
            }

            if (value is IDictionary<object, object> objectDict)
            {
                var temp = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<object, object> kvp in objectDict)
                {
                    string key = kvp.Key?.ToString();
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    temp[key] = kvp.Value;
                }
                return ConvertStringObjectToBoolDictionary(temp);
            }

            if (value is TomlTable tomlTable)
            {
                Dictionary<string, object> converted = ConvertTomlTableToDictionary(tomlTable);
                return ConvertStringObjectToBoolDictionary(converted);
            }

            if (value is JObject jobj)
            {
                return ConvertStringObjectToBoolDictionary(JTokenToDictionary(jobj));
            }

            return new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, bool?> ConvertStringObjectToBoolDictionary(Dictionary<string, object> source)
        {
            var result = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kvp in source)
            {
                bool? shouldDownload = null;
                object value = kvp.Value;

                if (value is null)
                {
                    shouldDownload = null;
                }
                else if (value is bool boolVal)
                {
                    shouldDownload = boolVal;
                }
                else if (value is string strVal && string.Equals(strVal, "null", StringComparison.OrdinalIgnoreCase))
                {
                    shouldDownload = null;
                }
                else if (value is string strBool && bool.TryParse(strBool, out bool parsedBool))
                {
                    shouldDownload = parsedBool;
                }
                else if (value is IConvertible convertible)
                {
                    try
                    {
                        shouldDownload = convertible.ToBoolean(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        shouldDownload = null;
                    }
                }

                result[kvp.Key] = shouldDownload;
            }

            return result;
        }

        [CanBeNull]
        public static ModComponent DeserializeYamlComponent([NotNull] string yamlString)
        {
            if (yamlString is null)
            {
                throw new ArgumentNullException(nameof(yamlString));
            }

            try
            {
                YamlSerialization.IDeserializer deserializer = new YamlSerialization.DeserializerBuilder()
                    .WithNamingConvention(YamlSerialization.NamingConventions.PascalCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                Dictionary<string, object> yamlDict = deserializer.Deserialize<Dictionary<string, object>>(yamlString);
                if (yamlDict is null)
                {
                    Logger.LogError("Failed to deserialize YAML: result was null");
                    return null;
                }

                // Debug: Log ModLinkFilenames structure after YAML deserialization
                if (yamlDict.TryGetValue("ModLinkFilenames", out object modLinkFilenamesValue))
                {
                    Logger.LogVerbose($"DeserializeYamlComponent: Found ModLinkFilenames field, type: {modLinkFilenamesValue?.GetType().Name}, value: {modLinkFilenamesValue}");
                    if (modLinkFilenamesValue is Dictionary<object, object> modLinkDict)
                    {
                        Logger.LogVerbose($"DeserializeYamlComponent: ModLinkFilenames is Dictionary<object, object> with {modLinkDict.Count} entries");
                        foreach (KeyValuePair<object, object> kvp in modLinkDict)
                        {
                            Logger.LogVerbose($"DeserializeYamlComponent:   URL: {kvp.Key} (type: {kvp.Key?.GetType().Name}), Files: {kvp.Value} (type: {kvp.Value?.GetType().Name})");
                        }
                    }
                }

                // Pre-process the component dictionary to handle duplicate fields
                yamlDict = PreprocessComponentDictionary(yamlDict);

                ModComponent component = DeserializeComponent(yamlDict);
                return component;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to deserialize YAML component");
                return null;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static string SerializeModComponentAsTomlString(
            IReadOnlyList<ModComponent> components,
            ComponentValidationContext validationContext = null)
        {
            // Populate ResourceRegistry from cache BEFORE serialization
            PopulateResourceRegistryFromCache(components);

            Logger.LogVerbose("===== SerializeModComponentAsTomlString START =====");
            Logger.LogVerbose($"Serializing {components.Count} component(s) to TOML");
            for (int i = 0; i < Math.Min(components.Count, 5); i++)
            {
                ModComponent c = components[i];
                Logger.LogVerbose($"  Component #{i + 1}: '{c.Name}' (GUID={c.Guid}), ResourceRegistry={c.ResourceRegistry?.Count ?? 0}");
            }
            if (components.Count > 5)
            {
                Logger.LogVerbose($"  ... and {components.Count - 5} more components");
            }
            var result = new StringBuilder();

            var metadataTable = new TomlTable();
            // Only include fileFormatVersion if it's not the default "2.0"
            if (!string.Equals(MainConfig.FileFormatVersion, "2.0", StringComparison.Ordinal))
            {
                metadataTable["fileFormatVersion"] = MainConfig.FileFormatVersion ?? "2.0";
            }
            else if (!string.IsNullOrWhiteSpace(MainConfig.FileFormatVersion))
            {
                metadataTable["fileFormatVersion"] = MainConfig.FileFormatVersion;
            }

            if (!string.IsNullOrWhiteSpace(MainConfig.TargetGame))
            {
                metadataTable["targetGame"] = MainConfig.TargetGame;
            }

            if (!string.IsNullOrWhiteSpace(MainConfig.BuildName))
            {
                metadataTable["buildName"] = MainConfig.BuildName;
            }

            if (!string.IsNullOrWhiteSpace(MainConfig.BuildAuthor))
            {
                metadataTable["buildAuthor"] = MainConfig.BuildAuthor;
            }

            if (!string.IsNullOrWhiteSpace(MainConfig.BuildDescription))
            {
                metadataTable["buildDescription"] = MainConfig.BuildDescription;
            }

            if (MainConfig.LastModified.HasValue)
            {
                metadataTable["lastModified"] = MainConfig.LastModified.Value;
            }
            // Always serialize content sections, even if empty
            metadataTable["preambleContent"] = MainConfig.PreambleContent ?? string.Empty;
            metadataTable["epilogueContent"] = MainConfig.EpilogueContent ?? string.Empty;
            metadataTable["widescreenWarningContent"] = MainConfig.WidescreenWarningContent ?? string.Empty;
            metadataTable["aspyrExclusiveWarningContent"] = MainConfig.AspyrExclusiveWarningContent ?? string.Empty;
            metadataTable["installationWarningContent"] = MainConfig.InstallationWarningContent ?? string.Empty;

            var metadataRoot = new Dictionary<string, object>(StringComparer.Ordinal) { ["metadata"] = metadataTable };
            _ = result.AppendLine(Toml.FromModel(metadataRoot));

            bool isFirst = true;
            foreach (ModComponent component in components)
            {
                if (!isFirst)
                {
                    _ = result.AppendLine();
                    _ = result.AppendLine();
                }
                isFirst = false;

                // TOML-specific: Add validation comments for component issues
                if (validationContext != null && validationContext.HasIssues(component.Guid))
                {
                    IReadOnlyList<string> componentIssues = validationContext.GetComponentIssues(component.Guid);
                    if (componentIssues.Count > 0)
                    {
                        _ = result.AppendLine("# VALIDATION ISSUES:");
                        foreach (string issue in componentIssues)
                        {
                            _ = result.Append("# ").Append(issue).AppendLine();
                        }
                    }
                }

                // TOML-specific: Add URL failure comments
                if (validationContext != null && component.ResourceRegistry != null && component.ResourceRegistry.Count > 0)
                {
                    foreach (string url in component.ResourceRegistry.Keys)
                    {
                        List<string> urlFailures = validationContext.GetUrlFailures(url);
                        if (urlFailures.Count > 0)
                        {
                            _ = result.Append("# URL RESOLUTION FAILURE: ").Append(url).AppendLine();
                            foreach (string failure in urlFailures)
                            {
                                _ = result.Append("# ").Append(failure).AppendLine();
                            }
                        }
                    }
                }

                // Use unified serialization
                Dictionary<string, object> componentDict = SerializeComponentToDictionary(component, validationContext, duplicateNameAsHeadingWhenEmpty: false);

                Logger.LogVerbose($"[SerializeToml] Component '{component.Name}': SerializeComponentToDictionary returned {componentDict.Count} keys");
                Logger.LogVerbose($"[SerializeToml] Component '{component.Name}': Has ResourceRegistry in dict = {componentDict.ContainsKey("ResourceRegistry")}");
                Logger.LogVerbose($"[SerializeToml] Component '{component.Name}' runtime state: ResourceRegistry.Count={component.ResourceRegistry?.Count ?? 0}");

                var nestedContent = new StringBuilder();
                (Dictionary<string, object> modLinkFilenamesDict, Dictionary<string, object> resourceRegistryDict) = FixSerializedTomlDict(
                    componentDict,
                    nestedContent,
                    validationContext,
                    component
                );

                Logger.LogVerbose($"[SerializeToml] After FixSerializedTomlDict: resourceRegistryDict={(resourceRegistryDict?.Count ?? 0)} entries, modLinkFilenamesDict={(modLinkFilenamesDict?.Count ?? 0)} URLs");

                var rootTable = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["thisMod"] = componentDict,
                };
                // Limit regex evaluation time to 30 seconds to prevent ReDoS (MA0009)
                string componentToml = Regex.Replace(
                    Toml.FromModel(rootTable),
                    Regex.Escape("[thisMod]"),
                    "[[thisMod]]",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(30)
                );

                // Insert ResourceRegistry inline if present (NEW FORMAT - replaces ModLinkFilenames)
                if (resourceRegistryDict != null && resourceRegistryDict.Count > 0)
                {
                    Logger.LogVerbose($"[SerializeToml] Generating inline TOML for ResourceRegistry with {resourceRegistryDict.Count} entries");
                    var rrBuilder = new StringBuilder();
                    _ = rrBuilder.Append("ResourceRegistry = { ");

                    bool firstEntry = true;
                    foreach (KeyValuePair<string, object> registryEntry in resourceRegistryDict)
                    {
                        if (!firstEntry)
                        {
                            _ = rrBuilder.Append(", ");
                        }
                        firstEntry = false;

                        string contentKey = registryEntry.Key;
                        _ = rrBuilder.Append('"');
                        _ = rrBuilder.Append(contentKey.Replace("\"", "\\\""));
                        _ = rrBuilder.Append("\" = { ");

                        if (registryEntry.Value is Dictionary<string, object> metaDict)
                        {
                            bool firstField = true;
                            foreach (KeyValuePair<string, object> metaField in metaDict)
                            {
                                if (!firstField)
                                {
                                    _ = rrBuilder.Append(", ");
                                }
                                firstField = false;

                                _ = rrBuilder.Append('"');
                                _ = rrBuilder.Append(metaField.Key.Replace("\"", "\\\""));
                                _ = rrBuilder.Append("\" = ");

                                if (metaField.Value is string strVal)
                                {
                                    _ = rrBuilder.Append('"');
                                    _ = rrBuilder.Append(strVal.Replace("\"", "\\\""));
                                    _ = rrBuilder.Append('"');
                                }
                                else if (metaField.Value is bool boolVal)
                                {
                                    _ = rrBuilder.Append(boolVal ? "true" : "false");
                                }
                                else if (metaField.Value is long || metaField.Value is int)
                                {
                                    _ = rrBuilder.Append(metaField.Value);
                                }
                                else if (metaField.Value is Dictionary<string, object> nestedDict)
                                {
                                    // Serialize nested dictionaries as inline tables
                                    _ = rrBuilder.Append("{ ");
                                    bool firstNestedField = true;
                                    foreach (KeyValuePair<string, object> nestedKvp in nestedDict)
                                    {
                                        if (!firstNestedField)
                                        {
                                            _ = rrBuilder.Append(", ");
                                        }
                                        firstNestedField = false;

                                        _ = rrBuilder.Append('"');
                                        _ = rrBuilder.Append(nestedKvp.Key.Replace("\"", "\\\""));
                                        _ = rrBuilder.Append("\" = ");

                                        if (nestedKvp.Value is string nestedStr)
                                        {
                                            _ = rrBuilder.Append('"');
                                            _ = rrBuilder.Append(nestedStr.Replace("\"", "\\\""));
                                            _ = rrBuilder.Append('"');
                                        }
                                        else if (nestedKvp.Value is bool nestedBool)
                                        {
                                            _ = rrBuilder.Append(nestedBool ? "true" : "false");
                                        }
                                        else if (nestedKvp.Value == null)
                                        {
                                            _ = rrBuilder.Append("\"null\"");
                                        }
                                        else
                                        {
                                            _ = rrBuilder.Append('"');
                                            _ = rrBuilder.Append(nestedKvp.Value.ToString().Replace("\"", "\\\""));
                                            _ = rrBuilder.Append('"');
                                        }
                                    }
                                    _ = rrBuilder.Append(" }");
                                }
                                else if (metaField.Value == null)
                                {
                                    _ = rrBuilder.Append("\"null\"");
                                }
                                else
                                {
                                    _ = rrBuilder.Append('"');
                                    _ = rrBuilder.Append(metaField.Value.ToString().Replace("\"", "\\\""));
                                    _ = rrBuilder.Append('"');
                                }
                            }
                        }

                        _ = rrBuilder.Append(" }");
                    }

                    _ = rrBuilder.AppendLine(" }");

                    // Insert after the [[thisMod]] line
                    int insertPos = componentToml.IndexOf('\n');
                    if (insertPos > 0)
                    {
                        componentToml = componentToml.Insert(insertPos + 1, rrBuilder.ToString());
                        Logger.LogVerbose($"[SerializeToml] Inserted ResourceRegistry inline TOML at position {insertPos}, length: {rrBuilder.Length}");
                    }
                    else
                    {
                        Logger.LogWarning($"[SerializeToml] Could not find insertion point for ResourceRegistry inline TOML");
                    }
                }
                else
                {
                    Logger.LogVerbose($"[SerializeToml] ResourceRegistry dict is null or empty, skipping inline TOML generation");
                }

                // Insert ModLinkFilenames inline if present (DEPRECATED - for backward compatibility)
                if (modLinkFilenamesDict != null && modLinkFilenamesDict.Count > 0)
                {
                    Logger.LogVerbose($"[SerializeToml] Generating inline TOML for ModLinkFilenames with {modLinkFilenamesDict.Count} URLs");
                    var mlf = new StringBuilder();
                    _ = mlf.Append("ModLinkFilenames = { ");

                    bool firstUrl = true;
                    foreach (KeyValuePair<string, object> urlEntry in modLinkFilenamesDict)
                    {
                        if (!firstUrl)
                        {
                            _ = mlf.Append(", ");
                        }

                        firstUrl = false;

                        string url = urlEntry.Key;
                        _ = mlf.Append('"');
                        _ = mlf.Append(url.Replace("\"", "\\\""));
                        _ = mlf.Append("\" = { ");

                        if (urlEntry.Value is Dictionary<string, object> filenamesDict && filenamesDict.Count > 0)
                        {
                            bool firstFile = true;
                            foreach (KeyValuePair<string, object> fileEntry in filenamesDict)
                            {
                                if (!firstFile)
                                {
                                    _ = mlf.Append(", ");
                                }

                                firstFile = false;

                                string filename = fileEntry.Key;
                                _ = mlf.Append('"');
                                _ = mlf.Append(filename.Replace("\"", "\\\""));
                                _ = mlf.Append("\" = ");

                                if (fileEntry.Value is bool boolVal)
                                {
                                    _ = mlf.Append(boolVal ? "true" : "false");
                                }
                                else if (fileEntry.Value is string strVal && string.Equals(strVal, "null", StringComparison.OrdinalIgnoreCase))
                                {
                                    _ = mlf.Append("\"null\"");
                                }
                                else if (fileEntry.Value is string strVal2)
                                {
                                    _ = mlf.Append('"').Append(strVal2.Replace("\"", "\\\"")).Append('"');
                                }
                                else if (fileEntry.Value == null)
                                {
                                    _ = mlf.Append("\"null\"");
                                }
                                else
                                {
                                    _ = mlf.Append('"').Append(fileEntry.Value).Append('"');
                                }
                            }
                        }

                        _ = mlf.Append(" }");
                    }

                    _ = mlf.AppendLine(" }");

                    // Insert after the [[thisMod]] line
                    int insertPos = componentToml.IndexOf('\n');
                    if (insertPos > 0)
                    {
                        componentToml = componentToml.Insert(insertPos + 1, mlf.ToString());
                        Logger.LogVerbose($"[SerializeToml] Inserted ModLinkFilenames inline TOML at position {insertPos}, length: {mlf.Length}");
                    }
                    else
                    {
                        Logger.LogWarning($"[SerializeToml] Could not find insertion point for ModLinkFilenames inline TOML");
                    }
                }
                else
                {
                    Logger.LogVerbose($"[SerializeToml] ModLinkFilenames dict is null or empty, skipping inline TOML generation");
                }

                _ = result.Append(componentToml.TrimEnd());

                if (nestedContent.Length <= 0)
                {
                    continue;
                }

                _ = result.AppendLine();
                _ = result.Append(nestedContent);
            }

            return SanitizeUtf8(Serializer.FixWhitespaceIssues(result.ToString()));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static (Dictionary<string, object> modLinkFilenames, Dictionary<string, object> resourceRegistry) FixSerializedTomlDict(
            Dictionary<string, object> serializedComponentDict,
            StringBuilder nestedContent,
            ComponentValidationContext validationContext = null,
            ModComponent component = null
        )
        {
            if (serializedComponentDict is null)
            {
                throw new ArgumentNullException(nameof(serializedComponentDict));
            }

            if (nestedContent is null)
            {
                throw new ArgumentNullException(nameof(nestedContent));
            }

            // Remove metadata fields that were added by unified serialization (they're for format-specific use only)
            serializedComponentDict.Remove("_ValidationIssues");
            serializedComponentDict.Remove("_UrlFailures");
            serializedComponentDict.Remove("_HasInstructions");

            if (serializedComponentDict.TryGetValue("Instructions", out object val))
            {
                List<Dictionary<string, object>> instructionsList = null;

                if (val is List<Dictionary<string, object>> list)
                {
                    instructionsList = list;
                }
                else if (val is IEnumerable<Dictionary<string, object>> enumerable)
                {
                    instructionsList = enumerable.ToList();
                }

                if (instructionsList != null && instructionsList.Count > 0)
                {
                    int instructionIndex = 0;
                    foreach (Dictionary<string, object> item in instructionsList)
                    {
                        if (item is null || item.Count == 0)
                        {
                            continue;
                        }

                        // Add validation comments for instruction issues
                        if (validationContext != null && component != null && instructionIndex < component.Instructions.Count)
                        {
                            List<string> instructionIssues = validationContext.GetInstructionIssues(component.Guid, instructionIndex);
                            if (instructionIssues.Count > 0)
                            {
                                nestedContent.AppendLine();
                                nestedContent.AppendLine("# INSTRUCTION VALIDATION ISSUES:");
                                foreach (string issue in instructionIssues)
                                {
                                    nestedContent.Append("# ").Append(issue).AppendLine();
                                }
                            }
                        }

                        var model = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            {
                                "thisMod", new Dictionary<string, object>(StringComparer.Ordinal) {
                                    { "Instructions", item },
                                }
                            },
                        };
                        nestedContent.AppendLine();
                        nestedContent.Append(Regex.Replace(
                            Toml.FromModel(model),
                            Regex.Escape("thisMod.Instructions"),
                            "[thisMod.Instructions]",
                            RegexOptions.IgnoreCase | RegexOptions.Compiled,
                            TimeSpan.FromSeconds(15)
                        ));
                        instructionIndex++;
                    }
                }

                serializedComponentDict.Remove("Instructions");
            }

            // Extract ModLinkFilenames - we'll add it manually as inline TOML after the main TOML generation
            Dictionary<string, object> modLinkFilenamesDict = null;
            if (serializedComponentDict.TryGetValue("ModLinkFilenames", out object modLinkFilenamesVal) &&
                modLinkFilenamesVal is Dictionary<string, object> mlf)
            {
                modLinkFilenamesDict = mlf;
                serializedComponentDict.Remove("ModLinkFilenames");
                Logger.LogVerbose($"[FixSerializedTomlDict] Extracted ModLinkFilenames with {modLinkFilenamesDict.Count} URL entries for inline TOML");
            }

            // Extract ResourceRegistry for special TOML formatting (inline table format)
            Dictionary<string, object> resourceRegistryDict = null;
            if (serializedComponentDict.TryGetValue("ResourceRegistry", out object resourceRegistryVal) &&
                resourceRegistryVal is Dictionary<string, object> rr)
            {
                resourceRegistryDict = rr;
                serializedComponentDict.Remove("ResourceRegistry");
                Logger.LogVerbose($"[FixSerializedTomlDict] Extracted ResourceRegistry with {resourceRegistryDict.Count} entries for inline TOML");
            }

            bool hasOptions = serializedComponentDict.ContainsKey("Options");
            bool hasOptionsInstructions = serializedComponentDict.ContainsKey("OptionsInstructions");

            if (
                hasOptions && hasOptionsInstructions &&
                serializedComponentDict["Options"] is List<Dictionary<string, object>> optionsList &&
                serializedComponentDict["OptionsInstructions"] is List<Dictionary<string, object>> optionsInstructionsList &&
                optionsInstructionsList.Count > 0)
            {
                var instructionsByParent = optionsInstructionsList
                    .Where(instr => instr != null && instr.ContainsKey("Parent"))

                    .GroupBy(instr => instr["Parent"]?.ToString(), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

                foreach (Dictionary<string, object> optionDict in optionsList)
                {
                    if (optionDict is null || optionDict.Count == 0)
                    {
                        continue;
                    }

                    // CRITICAL: Remove the Instructions field from optionDict before serializing
                    // We use [[thisMod.Options.Instructions]] sections instead of inline arrays
                    optionDict.Remove("Instructions");
                    // Remove internal metadata field
                    optionDict.Remove("_HasInstructions");

                    var optionModel = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "thisMod", new Dictionary<string, object>(StringComparer.Ordinal) {
                                { "Options", optionDict },
                            }
                        },
                    };
                    nestedContent.AppendLine();
                    nestedContent.Append(Regex.Replace(
                        Toml.FromModel(optionModel),
                        Regex.Escape("thisMod.Options"),
                        "[thisMod.Options]",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled,
                        TimeSpan.FromSeconds(15)
                    ));

                    if (!optionDict.TryGetValue("Guid", out object guidObj))
                    {
                        continue;
                    }

                    string optionGuid = guidObj?.ToString();
                    if (string.IsNullOrEmpty(optionGuid) || !instructionsByParent.TryGetValue(optionGuid, out List<Dictionary<string, object>> instructions))
                    {
                        continue;
                    }
                    // Find the option in the component
                    Option currentOption = null;
                    if (component != null && Guid.TryParse(optionGuid, out Guid optGuid))
                    {
                        currentOption = component.Options.FirstOrDefault(opt => opt.Guid == optGuid);
                    }

                    int optionInstrIndex = 0;
                    foreach (Dictionary<string, object> instruction in instructions.Where(instruction => instruction != null && instruction.Count != 0))
                    {
                        // Add validation comments for option instruction issues
                        if (validationContext != null && currentOption != null && optionInstrIndex < currentOption.Instructions.Count)
                        {
                            List<string> instructionIssues = validationContext.GetInstructionIssues(component.Guid, optionInstrIndex);
                            if (instructionIssues.Count > 0)
                            {
                                nestedContent.AppendLine();
                                nestedContent.AppendLine("# OPTION INSTRUCTION VALIDATION ISSUES:");
                                foreach (string issue in instructionIssues)
                                {
                                    nestedContent.Append("# ").Append(issue).AppendLine();
                                }
                            }
                        }

                        var instrModel = new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            {
                                "thisMod", new Dictionary<string, object>(StringComparer.Ordinal) {
                                    { "OptionsInstructions", instruction },
                                }
                            },
                        };
                        nestedContent.Append(Regex.Replace(
                            Toml.FromModel(instrModel),
                            Regex.Escape("thisMod.OptionsInstructions"),
                            "[thisMod.Options.Instructions]",
                            RegexOptions.IgnoreCase | RegexOptions.Compiled,
                            TimeSpan.FromSeconds(15)
                        ));
                        optionInstrIndex++;
                    }
                }

                serializedComponentDict.Remove("Options");
                serializedComponentDict.Remove("OptionsInstructions");
            }

            var keysCopy = serializedComponentDict.Keys.ToList();
            foreach (string key in keysCopy)
            {
                object value = serializedComponentDict[key];

                List<Dictionary<string, object>> listItems = null;
                if (value is List<Dictionary<string, object>> list)
                {
                    listItems = list;
                }
                else if (value is IEnumerable<Dictionary<string, object>> enumerable)
                {
                    listItems = enumerable.ToList();
                }

                if (listItems is null || listItems.Count == 0)
                {
                    continue;
                }

                foreach (Dictionary<string, object> item in listItems.Where(item => item != null && item.Count != 0))
                {
                    var model = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                    {
                        "thisMod", new Dictionary<string, object>(StringComparer.Ordinal) {
                            { key, item },
                        }
                    },
                };
                    nestedContent.AppendLine();
                    _ = nestedContent.Append(Regex.Replace(
                        Toml.FromModel(model),
                        Regex.Escape($"thisMod.{key}"),
                        $"[thisMod.{key}]",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled,
                        TimeSpan.FromSeconds(15)
                    ));
                }

                serializedComponentDict.Remove(key);
            }

            return (modLinkFilenamesDict, resourceRegistryDict);
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static string SerializeModComponentAsYamlString(
                IReadOnlyList<ModComponent> components,
                ComponentValidationContext validationContext = null
            )
        {
            // Populate ResourceRegistry from cache BEFORE serialization
            PopulateResourceRegistryFromCache(components);

            Logger.LogVerbose("Saving to YAML string");
            var sb = new StringBuilder();

            // Write metadata section
            WriteYamlMetadataSection(sb);

            YamlSerialization.ISerializer serializer = new YamlSerialization.SerializerBuilder()
                .WithNamingConvention(YamlSerialization.NamingConventions.PascalCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(YamlSerialization.DefaultValuesHandling.OmitNull)
                .DisableAliases()
                .Build();
            foreach (ModComponent component in components)
            {
                sb.AppendLine("---");

                // Use unified serialization
                Dictionary<string, object> dict = SerializeComponentToDictionary(component, validationContext, duplicateNameAsHeadingWhenEmpty: false);

                // YAML-specific: Render validation comments from metadata
                if (dict.TryGetValue("_ValidationIssues", out object validationIssuesValue) && validationIssuesValue is List<string> componentIssues)
                {
                    sb.AppendLine("# VALIDATION ISSUES:");
                    foreach (string issue in componentIssues)
                    {
                        sb.Append("# ").Append(issue).AppendLine();
                    }
                    dict.Remove("_ValidationIssues");
                }

                // YAML-specific: Render URL failure comments from metadata
                if (dict.TryGetValue("_UrlFailures", out object urlFailuresValue) && urlFailuresValue is Dictionary<string, List<string>> urlFailures)
                {
                    foreach (KeyValuePair<string, List<string>> kvp in urlFailures)
                    {
                        sb.Append("# URL RESOLUTION FAILURE: ").Append(kvp.Key).AppendLine();
                        foreach (string failure in kvp.Value)
                        {
                            sb.Append("# ").Append(failure).AppendLine();
                        }
                    }
                    dict.Remove("_UrlFailures");
                }

                // YAML-specific: Remove internal metadata and convert action to lowercase
                dict.Remove("_HasInstructions");
                dict.Remove("OptionsInstructions"); // YAML doesn't use separate OptionsInstructions

                // YAML-specific: Convert Action strings to lowercase
                if (dict.TryGetValue("Instructions", out object instructionsValue) && instructionsValue is List<Dictionary<string, object>> instructions)
                {
                    foreach (Dictionary<string, object> instr in instructions)
                    {
                        if (instr.TryGetValue("Action", out object instructionActionValue) && instructionActionValue is string action)
                        {
                            instr["Action"] = action.ToLowerInvariant();
                        }
                        instr.Remove("_ValidationWarnings"); // YAML handles these as embedded fields in unified serialization
                    }
                }

                if (dict.TryGetValue("Options", out object optionsValue) && optionsValue is List<Dictionary<string, object>> options)
                {
                    foreach (Dictionary<string, object> opt in options)
                    {
                        // Remove internal metadata fields from options
                        opt.Remove("_HasInstructions");

                        if (opt.ContainsKey("Instructions") && opt["Instructions"] is List<Dictionary<string, object>> optInstructions)
                        {
                            foreach (Dictionary<string, object> instr in optInstructions)
                            {
                                if (instr.TryGetValue("Action", out object optionInstructionActionValue) && optionInstructionActionValue is string action)
                                {
                                    instr["Action"] = action.ToLowerInvariant();
                                }
                                instr.Remove("_ValidationWarnings");
                            }
                        }
                    }
                }

                sb.AppendLine(serializer.Serialize(dict));
            }
            return SanitizeUtf8(sb.ToString());
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static void WriteYamlMetadataSection(StringBuilder sb)
        {
            bool hasAnyMetadata = !string.IsNullOrWhiteSpace(MainConfig.TargetGame)
                || !string.IsNullOrWhiteSpace(MainConfig.BuildName)
                || !string.IsNullOrWhiteSpace(MainConfig.BuildAuthor)
                || !string.IsNullOrWhiteSpace(MainConfig.BuildDescription)
                || !string.IsNullOrWhiteSpace(MainConfig.PreambleContent)
                || !string.IsNullOrWhiteSpace(MainConfig.EpilogueContent)
                || !string.IsNullOrWhiteSpace(MainConfig.WidescreenWarningContent)
                || !string.IsNullOrWhiteSpace(MainConfig.AspyrExclusiveWarningContent)
                || !string.IsNullOrWhiteSpace(MainConfig.InstallationWarningContent)
                || MainConfig.LastModified.HasValue;


            if (!hasAnyMetadata && string.Equals(MainConfig.FileFormatVersion, "2.0", StringComparison.Ordinal))
            {
                return;
            }

            sb.AppendLine("---");
            sb.AppendLine("# Metadata");
            sb.Append("fileFormatVersion: \"").Append(MainConfig.FileFormatVersion).Append('"').AppendLine();

            if (!string.IsNullOrWhiteSpace(MainConfig.TargetGame))
            {
                sb.Append("targetGame: \"").Append(MainConfig.TargetGame).Append('"').AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(MainConfig.BuildName))
            {
                sb.Append("buildName: \"").Append(MainConfig.BuildName).Append('"').AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(MainConfig.BuildAuthor))
            {
                sb.Append("buildAuthor: \"").Append(MainConfig.BuildAuthor).Append('"').AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(MainConfig.BuildDescription))
            {
                string escapedDescription = MainConfig.BuildDescription
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
                sb.Append("buildDescription: \"").Append(escapedDescription).Append('"').AppendLine();
            }

            if (MainConfig.LastModified.HasValue)
            {
                sb.Append("lastModified: \"").AppendFormat("{0:O}", MainConfig.LastModified.Value).Append('"').AppendLine();
            }

            // Always serialize content sections, even if empty
            string escapedBefore = EscapeYamlString(MainConfig.PreambleContent ?? string.Empty);
            sb.Append("preambleContent: \"").Append(escapedBefore).Append('"').AppendLine();

            string escapedAfter = EscapeYamlString(MainConfig.EpilogueContent ?? string.Empty);
            sb.Append("epilogueContent: \"").Append(escapedAfter).Append('"').AppendLine();

            string escapedWidescreen = EscapeYamlString(MainConfig.WidescreenWarningContent ?? string.Empty);
            sb.Append("widescreenWarningContent: \"").Append(escapedWidescreen).Append('"').AppendLine();

            string escapedAspyr = EscapeYamlString(MainConfig.AspyrExclusiveWarningContent ?? string.Empty);
            sb.Append("aspyrExclusiveWarningContent: \"").Append(escapedAspyr).Append('"').AppendLine();

            string escapedInstallationWarning = EscapeYamlString(MainConfig.InstallationWarningContent ?? string.Empty);
            sb.Append("installationWarningContent: \"").Append(escapedInstallationWarning).Append('"').AppendLine();

            sb.AppendLine();
        }

        private static string EscapeYamlString(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        public static string SerializeModComponentAsMarkdownString(
            IReadOnlyList<ModComponent> components,
            ComponentValidationContext validationContext = null)
        {
            // Populate ResourceRegistry from cache BEFORE serialization
            PopulateResourceRegistryFromCache(components);

            Logger.LogVerbose("Saving to Markdown string");
            return GenerateModDocumentation(
                components,
                MainConfig.PreambleContent,
                MainConfig.EpilogueContent,
                MainConfig.WidescreenWarningContent,
                MainConfig.AspyrExclusiveWarningContent,
                validationContext);
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static string SerializeModComponentAsJsonString(
            IReadOnlyList<ModComponent> components,
            ComponentValidationContext validationContext = null)
        {
            PopulateResourceRegistryFromCache(components);

            Logger.LogVerbose("Saving to JSON string");

            _ = validationContext;

            string json = JsonConvert.SerializeObject(components, Formatting.Indented, DirectJsonSerializerSettings);
            return SanitizeUtf8(json);
        }

        /// <summary>
        /// Serializes a single ModComponent to TOML string.
        /// </summary>
        [NotNull]
        public static string SerializeSingleComponentAsTomlString([NotNull] ModComponent component)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            // Populate ResourceRegistry from cache BEFORE serialization
            PopulateResourceRegistryFromCache(new List<ModComponent> { component });

            try
            {
                // Use the existing serialization infrastructure to ensure correct format
                var components = new List<ModComponent> { component };
                return SerializeModComponentAsTomlString(components);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to serialize component to TOML");
                throw;
            }
        }
        public static async Task<string> GenerateModDocumentationAsync(
            [NotNull] string filePath,
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            [CanBeNull] string preambleContent = null,
            [CanBeNull] string epilogueContent = null,
            [CanBeNull] string widescreenWarningContent = null,
            [CanBeNull] string aspyrExclusiveWarningContent = null,
            [CanBeNull] string installationWarningContent = null,
            [CanBeNull] ComponentValidationContext validationContext = null)
        {
            return await Task.Run(() => GenerateModDocumentation(
                componentsList,
                preambleContent,
                epilogueContent,
                widescreenWarningContent,
                aspyrExclusiveWarningContent,
                validationContext
            )).ConfigureAwait(false);
        }

        [NotNull]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static string GenerateModDocumentation(
            [NotNull][ItemNotNull] IReadOnlyList<ModComponent> componentsList,
            [CanBeNull] string preambleContent = null,
            [CanBeNull] string epilogueContent = null,
            [CanBeNull] string widescreenWarningContent = null,
            [CanBeNull] string aspyrExclusiveWarningContent = null,
            [CanBeNull] ComponentValidationContext validationContext = null)
        {
            if (componentsList is null)
            {
                throw new ArgumentNullException(nameof(componentsList));
            }

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(preambleContent))
            {
                _ = sb.Append(preambleContent);
                if (!preambleContent.EndsWith("\n", StringComparison.Ordinal))
                {
                    _ = sb.AppendLine();
                }
                _ = sb.AppendLine();
            }

            _ = sb.AppendLine("## Mod List");

            var guidToName = componentsList.ToDictionary(c => c.Guid, c => c.Name);

            bool widescreenHeaderWritten = false;
            bool aspyrHeaderWritten = false;

            for (int i = 0; i < componentsList.Count; i++)
            {
                ModComponent component = componentsList[i];

                if (component.AspyrExclusive == true && !aspyrHeaderWritten && !string.IsNullOrWhiteSpace(aspyrExclusiveWarningContent))
                {
                    _ = sb.AppendLine();
                    _ = sb.AppendLine(aspyrExclusiveWarningContent.TrimEnd());
                    _ = sb.AppendLine();
                    aspyrHeaderWritten = true;
                }

                if (
                    component.WidescreenOnly &&
                    !widescreenHeaderWritten &&
                    !string.IsNullOrWhiteSpace(widescreenWarningContent) &&
                    !string.Equals(widescreenWarningContent, "Please install manually the widescreen implementations, e.g. uniws, before continuing.", StringComparison.Ordinal)
                )
                {
                    _ = sb.AppendLine();
                    _ = sb.AppendLine(widescreenWarningContent.TrimEnd());
                    _ = sb.AppendLine();
                    widescreenHeaderWritten = true;
                }

                if (i > 0)
                {
                    _ = sb.AppendLine("___");
                    _ = sb.AppendLine();
                }
                else
                {
                    _ = sb.AppendLine();
                }

                // Add validation warnings for component
                if (validationContext != null)
                {
                    IReadOnlyList<string> componentIssues = validationContext.GetComponentIssues(component.Guid);
                    if (componentIssues.Count > 0)
                    {
                        _ = sb.AppendLine("> **⚠️ VALIDATION WARNINGS:**");
                        foreach (string issue in componentIssues)
                        {
                            _ = sb.Append("> - ").Append(issue).AppendLine();
                        }
                        _ = sb.AppendLine();
                    }

                    // Add URL failure warnings
                    if (component.ResourceRegistry != null && component.ResourceRegistry.Count > 0)
                    {
                        foreach (string url in component.ResourceRegistry.Keys)
                        {
                            List<string> urlFailures = validationContext.GetUrlFailures(url);
                            if (urlFailures.Count > 0)
                            {
                                _ = sb.Append("> **⚠️ URL RESOLUTION FAILURE:** `").Append(url).Append('`').AppendLine();
                                foreach (string failure in urlFailures)
                                {
                                    _ = sb.Append("> - ").Append(failure).AppendLine();
                                }
                                _ = sb.AppendLine();
                            }
                        }
                    }
                }

                string heading = !string.IsNullOrWhiteSpace(component.Heading) ? component.Heading : component.Name;
                _ = sb.Append("### ").AppendLine(heading);
                _ = sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(component.NameFieldContent))
                {
                    _ = sb.Append("**Name:** ").AppendLine(component.NameFieldContent);
                }
                else if (component.ResourceRegistry?.Count > 0)
                {
                    var urls = component.ResourceRegistry.Keys.ToList();
                    if (urls.Count > 0 && !string.IsNullOrWhiteSpace(urls[0]))
                    {
                        _ = sb.Append("**Name:** [").Append(component.Name).Append("](")
                            .Append(urls[0]).Append(')');

                        for (int linkIdx = 1; linkIdx < urls.Count; linkIdx++)
                        {
                            if (!string.IsNullOrWhiteSpace(urls[linkIdx]))
                            {
                                _ = sb.Append(" and [**Patch**](").Append(urls[linkIdx]).Append(')');
                            }
                        }

                        _ = sb.AppendLine();
                    }
                    else
                    {
                        _ = sb.Append("**Name:** ").AppendLine(component.Name);
                    }
                }
                else
                {
                    _ = sb.Append("**Name:** ").AppendLine(component.Name);
                }

                _ = sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(component.Author))
                {
                    _ = sb.Append("**Author:** ").AppendLine(component.Author);
                    _ = sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(component.Description))
                {
                    _ = sb.Append("**Description:** ").AppendLine(component.Description);
                    _ = sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(component.Screenshots))
                {
                    _ = sb.Append("**Screenshots:** ").AppendLine(component.Screenshots);
                    _ = sb.AppendLine();
                }

                string categoryStr;
                if (component.Category?.Count > 0)
                {
                    if (component.Category.Count == 1)
                    {
                        categoryStr = component.Category[0];
                    }
                    else if (component.Category.Count == 2)
                    {
                        categoryStr = $"{component.Category[0]} & {component.Category[1]}";
                    }
                    else
                    {
                        IEnumerable<string> allButLast = component.Category.Take(component.Category.Count - 1);
                        string last = component.Category[component.Category.Count - 1];
                        categoryStr = $"{string.Join(", ", allButLast)} & {last}";
                    }
                }
                else
                {
                    categoryStr = "Uncategorized";
                }
                string tierStr = !string.IsNullOrWhiteSpace(component.Tier) ? component.Tier : "Unspecified";
                _ = sb.Append("**Category & Tier:** ").Append(categoryStr).Append(" / ").AppendLine(tierStr);
                _ = sb.AppendLine();

                string languageSupport = GetNonEnglishFunctionalityText(new List<string>(component.Language));

                if (!string.Equals(languageSupport, "UNKNOWN", StringComparison.Ordinal))
                {
                    _ = sb.Append("**Non-English Functionality:** ").AppendLine(languageSupport);
                    _ = sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(component.InstallationMethod))
                {
                    _ = sb.Append("**Installation Method:** ").AppendLine(component.InstallationMethod);
                }

                if (!string.IsNullOrWhiteSpace(component.KnownBugs))
                {
                    _ = sb.AppendLine();
                    _ = sb.Append("**Known Bugs:** ").AppendLine(component.KnownBugs);
                }

                if (!string.IsNullOrWhiteSpace(component.InstallationWarning))
                {
                    _ = sb.AppendLine();
                    _ = sb.Append("**Installation Warning:** ").AppendLine(component.InstallationWarning);
                }

                if (!string.IsNullOrWhiteSpace(component.CompatibilityWarning))
                {
                    _ = sb.AppendLine();
                    _ = sb.Append("**Compatibility Warning:** ").AppendLine(component.CompatibilityWarning);
                }

                if (!string.IsNullOrWhiteSpace(component.SteamNotes))
                {
                    _ = sb.AppendLine();
                    _ = sb.Append("**Steam Notes:** ").AppendLine(component.SteamNotes);
                }

                if (component.Dependencies?.Count > 0)
                {
                    var masterNames = component.Dependencies
                        .Select(guid =>
                        {
                            if (component.DependencyGuidToOriginalName.TryGetValue(guid, out string originalName))
                            {
                                return originalName;
                            }

                            if (guidToName.TryGetValue(guid, out string nameFromGuid))
                            {
                                return nameFromGuid;
                            }

                            return null;
                        })
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToList();

                    if (masterNames.Count > 0)
                    {
                        _ = sb.AppendLine();
                        _ = sb.Append("**Masters:** ");
                        for (int masterIndex = 0; masterIndex < masterNames.Count; masterIndex++)
                        {
                            if (masterIndex > 0) sb.Append(", ");
                            sb.Append(masterNames[masterIndex]);
                        }
                        sb.AppendLine();
                    }
                }

                if (!string.IsNullOrWhiteSpace(component.DownloadInstructions))
                {
                    _ = sb.AppendLine();
                    _ = sb.Append("**Download Instructions:** ").AppendLine(component.DownloadInstructions);
                }

                if (!string.IsNullOrWhiteSpace(component.Directions))
                {
                    _ = sb.AppendLine();
                    _ = sb.Append("**Installation Instructions:** ").AppendLine(component.Directions);
                }

                if (!string.IsNullOrWhiteSpace(component.UsageWarning))
                {
                    _ = sb.AppendLine();
                    _ = sb.Append("**Usage Warning:** ").AppendLine(component.UsageWarning);
                }

                _ = sb.AppendLine();

                if (component.Instructions.Count > 0 || component.Options.Count > 0)
                {
                    GenerateModSyncMetadata(sb, component);
                }
            }

            if (string.IsNullOrWhiteSpace(epilogueContent))
            {
                return sb.ToString();
            }

            _ = sb.AppendLine();
            _ = sb.Append(epilogueContent);
            if (!epilogueContent.EndsWith("\n", StringComparison.Ordinal))
            {
                _ = sb.AppendLine();
            }

            return SanitizeUtf8(sb.ToString());
        }

        private static void GenerateModSyncMetadata(
            [NotNull] StringBuilder sb,
            [NotNull] ModComponent component)
        {
            if (component.Instructions.Count == 0 && component.Options.Count == 0)
            {
                return;
            }

            _ = sb.AppendLine("<!--<<ModSync>>");

            try
            {
                // Serialize to YAML for markdown HTML comments instead of TOML
                string yaml = SerializeSingleComponentAsYamlString(component);
                _ = sb.Append(yaml);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to serialize component for ModSync metadata");
                _ = sb.Append("Guid: ").Append(component.Guid).AppendLine();
            }

            _ = sb.AppendLine("-->");
            _ = sb.AppendLine();
        }

        /// <summary>
        /// Serializes a single component to YAML string without metadata or document separators.
        /// This is used for embedding component data in markdown HTML comments.
        /// </summary>
        private static string SerializeSingleComponentAsYamlString(ModComponent component)
        {
            Logger.LogVerbose("Saving single component to YAML string");

            YamlSerialization.ISerializer serializer = new YamlDotNet.Serialization.SerializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.PascalCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(YamlDotNet.Serialization.DefaultValuesHandling.OmitNull)
                .DisableAliases()
                .Build();

            // Use unified serialization
            Dictionary<string, object> dict = SerializeComponentToDictionary(component, validationContext: null, duplicateNameAsHeadingWhenEmpty: true);

            // YAML-specific: Remove internal metadata and convert action to lowercase
            dict.Remove("_HasInstructions");
            dict.Remove("OptionsInstructions"); // YAML doesn't use separate OptionsInstructions

            // YAML-specific: Convert Action strings to lowercase
            if (dict.TryGetValue("Instructions", out object instructionsValue) && instructionsValue is List<Dictionary<string, object>> instructions)
            {
                foreach (Dictionary<string, object> instr in instructions)
                {
                    if (instr.TryGetValue("Action", out object instructionActionValue) && instructionActionValue is string action)
                    {
                        instr["Action"] = action.ToLowerInvariant();
                    }
                    instr.Remove("_ValidationWarnings"); // YAML handles these as embedded fields in unified serialization
                }
            }

            if (dict.TryGetValue("Options", out object optionsValue) && optionsValue is List<Dictionary<string, object>> options)
            {
                foreach (Dictionary<string, object> opt in options)
                {
                    // Remove internal metadata fields from options
                    opt.Remove("_HasInstructions");

                    if (opt.ContainsKey("Instructions") && opt["Instructions"] is List<Dictionary<string, object>> optInstructions)
                    {
                        foreach (Dictionary<string, object> instr in optInstructions)
                        {
                            if (instr.TryGetValue("Action", out object optionInstructionActionValue) && optionInstructionActionValue is string action)
                            {
                                instr["Action"] = action.ToLowerInvariant();
                            }
                            instr.Remove("_ValidationWarnings");
                        }
                    }
                }
            }

            return serializer.Serialize(dict);
        }

        [NotNull]
        private static string GetNonEnglishFunctionalityText([CanBeNull][ItemCanBeNull] List<string> languages)
        {
            if (languages is null || languages.Count == 0)
            {
                return "UNKNOWN";
            }

            if (languages.Count == 1 && languages.Exists(lang =>
                string.Equals(lang, "UNKNOWN", StringComparison.OrdinalIgnoreCase)))
            {
                return "UNKNOWN";
            }

            if (languages.Exists(lang => string.Equals(lang, b: "All", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lang, b: "YES", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lang, b: "Universal", StringComparison.OrdinalIgnoreCase)))
            {
                return "YES";
            }

            if (languages.Count == 1 && languages.Exists(lang =>
                string.Equals(lang, b: "English", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lang, b: "EN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(lang, b: "NO", StringComparison.OrdinalIgnoreCase)))
            {
                return "NO";
            }

            if (languages.Exists(lang => string.Equals(lang, b: "Partial", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(lang) && lang.IndexOf("Partial", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return "PARTIAL - Some text will be blank or in English";
            }

            if (languages.Count > 1 && languages.Exists(lang =>
                string.Equals(lang, b: "English", StringComparison.OrdinalIgnoreCase)))
            {
                return "PARTIAL - Supported languages: " + string.Join(", ", languages);
            }

            if (languages.Count == 1)
            {
                string singleLang = languages[0];
                if (string.IsNullOrEmpty(singleLang))
                {
                    return "Supported languages: " + string.Join(", ", languages);
                }

                string trimmed = singleLang.TrimStart();
                if (trimmed.StartsWith("YES", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("NO", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("PARTIAL", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.IndexOf("ONLY", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return singleLang;
                }
            }

            return "Supported languages: " + string.Join(", ", languages);
        }

        #region Unified Serialization Functions

        // ============================================================================
        // UNIFIED SERIALIZATION ARCHITECTURE
        // ============================================================================
        // These functions centralize ALL conditional logic for serialization:
        // - ActionType checking for Arguments, Destination, Overwrite
        // - Field presence/null/empty checks
        // - Validation context handling
        //
        // CURRENT STATUS:
        // ✓ TOML - Uses unified serialization (with special pre/post processing)
        // ✓ YAML - Uses unified serialization
        // ✓ JSON  - Uses unified serialization (with DictionaryToJObject conversion)
        //
        // BENEFITS:
        // - Single source of truth for field serialization rules
        // - Consistent behavior across all formats
        // - Easier maintenance (change once, applies everywhere)
        // - No more hunting for duplicated ActionType checks across 12 locations
        // ============================================================================

        /// <summary>
        /// Serializes an instruction to a dictionary with all conditional logic unified.
        /// Handles: ActionType-specific fields (Arguments, Destination, Overwrite), Dependencies, Restrictions, Validation warnings.
        /// </summary>
        private static Dictionary<string, object> SerializeInstructionToDictionary(
            [NotNull] Instruction instr)
        {
            var instrDict = new Dictionary<string, object>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(instr.ActionString))
            {
                instrDict["Action"] = instr.ActionString;
            }

            if (instr.Source.Count > 0)
            {
                instrDict["Source"] = instr.Source;
            }

            // Only serialize Destination for actions that use it
            if (!string.IsNullOrWhiteSpace(instr.Destination) &&
                (instr.Action == ActionType.Move ||
                 instr.Action == ActionType.Copy ||
                 instr.Action == ActionType.Rename ||
                 instr.Action == ActionType.DelDuplicate))  // DO NOT add ActionType.Patcher here!
            {
                instrDict["Destination"] = instr.Destination;
            }

            // Serialize Overwrite when it differs from default:
            // Delete default is false, so serialize when true
            // Move/Copy/Rename default is true, so serialize when false
            if (instr.Action == ActionType.Delete && instr.Overwrite)
            {
                instrDict["Overwrite"] = instr.Overwrite;
            }
            else if (
                  !instr.Overwrite
                  &&
                  (
                      instr.Action == ActionType.Move
                      || instr.Action == ActionType.Copy
                      || instr.Action == ActionType.Rename
                  )
              )
            {
                instrDict["Overwrite"] = instr.Overwrite;
            }

            // Serialize Arguments for specific action types
            if (
                !string.IsNullOrWhiteSpace(instr.Arguments)
                &&
                (
                    instr.Action == ActionType.DelDuplicate
                    || instr.Action == ActionType.Execute
                    || instr.Action == ActionType.Patcher
                )
            )
            {
                instrDict["Arguments"] = instr.Arguments;
            }

            if (instr.Dependencies.Count > 0)
            {
                instrDict["Dependencies"] = instr.Dependencies.Select(g => g.ToString()).ToList();
            }

            if (instr.Restrictions.Count > 0)
            {
                instrDict["Restrictions"] = instr.Restrictions.Select(g => g.ToString()).ToList();
            }

            // Instruction validation is no longer supported (instructions don't have GUIDs)

            return instrDict;
        }

        /// <summary>
        /// Serializes an option to a dictionary with all conditional logic unified
        /// </summary>
        private static Dictionary<string, object> SerializeOptionToDictionary(
            [NotNull] Option opt)
        {
            var optDict = new Dictionary<string, object>(StringComparer.Ordinal);

            // Always serialize Guid for Options (required field)
            optDict["Guid"] = opt.Guid.ToString();

            if (!string.IsNullOrWhiteSpace(opt.Name))
            {
                optDict["Name"] = opt.Name;
            }

            if (!string.IsNullOrWhiteSpace(opt.Description))
            {
                optDict["Description"] = opt.Description;
            }

            optDict["IsSelected"] = opt.IsSelected;
            if (opt.Restrictions.Count > 0)
            {
                optDict["Restrictions"] = opt.Restrictions.Select(g => g.ToString()).ToList();
            }

            if (opt.Dependencies.Count > 0)
            {
                optDict["Dependencies"] = opt.Dependencies.Select(g => g.ToString()).ToList();
            }

            // Serialize instructions if present
            if (opt.Instructions.Count <= 0)
            {
                return optDict;
            }

            var instructionsList = opt.Instructions.Select(instr => SerializeInstructionToDictionary(instr)).ToList();
            optDict["Instructions"] = instructionsList;

            return optDict;
        }

        /// <summary>
        /// Serializes a component to a dictionary with all conditional logic unified
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        private static Dictionary<string, object> SerializeComponentToDictionary(
            [NotNull] ModComponent component,
            [CanBeNull] ComponentValidationContext validationContext = null,
            bool duplicateNameAsHeadingWhenEmpty = false)
        {
            // Log component state at START of serialization to diagnose issues
            Logger.LogVerbose($"[SerializeComponentToDictionary] START for component '{component.Name}' (GUID={component.Guid})");
            Logger.LogVerbose($"[SerializeComponentToDictionary] Component state: ResourceRegistry={(component.ResourceRegistry?.Count ?? 0)} entries");
            if (component.ResourceRegistry != null && component.ResourceRegistry.Count > 0)
            {
                foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry.Take(3))
                {
                    Logger.LogVerbose($"[SerializeComponentToDictionary]   ResourceRegistry[URL: {kvp.Key}] = Files:{kvp.Value.Files?.Count ?? 0}");
                }
            }

            var componentDict = new Dictionary<string, object>(StringComparer.Ordinal);

            // Guid is a required field, always serialize it
            componentDict["Guid"] = component.Guid.ToString();

            if (!string.IsNullOrWhiteSpace(component.Name))
            {
                componentDict["Name"] = component.Name;
            }

            if (!string.IsNullOrWhiteSpace(component.Author))
            {
                componentDict["Author"] = component.Author;
            }

            if (!string.IsNullOrWhiteSpace(component.Tier))
            {
                componentDict["Tier"] = component.Tier;
            }

            if (!string.IsNullOrWhiteSpace(component.Description))
            {
                componentDict["Description"] = component.Description;
            }

            if (!string.IsNullOrWhiteSpace(component._descriptionSpoilerFree) && !string.Equals(component._descriptionSpoilerFree, component.Description, StringComparison.Ordinal))
            {
                componentDict["DescriptionSpoilerFree"] = component._descriptionSpoilerFree;
            }

            if (!string.IsNullOrWhiteSpace(component.InstallationMethod))
            {
                componentDict["InstallationMethod"] = component.InstallationMethod;
            }

            if (!string.IsNullOrWhiteSpace(component.Directions))
            {
                componentDict["Directions"] = component.Directions;
            }

            if (!string.IsNullOrWhiteSpace(component._directionsSpoilerFree) && !string.Equals(component._directionsSpoilerFree, component.Directions, StringComparison.Ordinal))
            {
                componentDict["DirectionsSpoilerFree"] = component._directionsSpoilerFree;
            }

            if (!string.IsNullOrWhiteSpace(component.DownloadInstructions))
            {
                componentDict["DownloadInstructions"] = component.DownloadInstructions;
            }

            if (!string.IsNullOrWhiteSpace(component._downloadInstructionsSpoilerFree) && !string.Equals(component._downloadInstructionsSpoilerFree, component.DownloadInstructions, StringComparison.Ordinal))
            {
                componentDict["DownloadInstructionsSpoilerFree"] = component._downloadInstructionsSpoilerFree;
            }

            if (!string.IsNullOrWhiteSpace(component.UsageWarning))
            {
                componentDict["UsageWarning"] = component.UsageWarning;
            }

            if (!string.IsNullOrWhiteSpace(component._usageWarningSpoilerFree) && !string.Equals(component._usageWarningSpoilerFree, component.UsageWarning, StringComparison.Ordinal))
            {
                componentDict["UsageWarningSpoilerFree"] = component._usageWarningSpoilerFree;
            }

            if (!string.IsNullOrWhiteSpace(component.Screenshots))
            {
                componentDict["Screenshots"] = component.Screenshots;
            }

            if (!string.IsNullOrWhiteSpace(component._screenshotsSpoilerFree) && !string.Equals(component._screenshotsSpoilerFree, component.Screenshots, StringComparison.Ordinal))
            {
                componentDict["ScreenshotsSpoilerFree"] = component._screenshotsSpoilerFree;
            }

            if (!string.IsNullOrWhiteSpace(component.KnownBugs))
            {
                componentDict["KnownBugs"] = component.KnownBugs;
            }

            if (!string.IsNullOrWhiteSpace(component.InstallationWarning))
            {
                componentDict["InstallationWarning"] = component.InstallationWarning;
            }

            if (!string.IsNullOrWhiteSpace(component.CompatibilityWarning))
            {
                componentDict["CompatibilityWarning"] = component.CompatibilityWarning;
            }

            if (!string.IsNullOrWhiteSpace(component.SteamNotes))
            {
                componentDict["SteamNotes"] = component.SteamNotes;
            }

            // TOML/YAML/JSON: only persist Heading when explicitly set (empty stays absent).
            // Markdown embedded YAML: duplicate Name into Heading when Heading is empty so <!--ModSync>> metadata round-trips.
            if (!string.IsNullOrWhiteSpace(component.Heading))
            {
                componentDict["Heading"] = component.Heading;
            }
            else if (duplicateNameAsHeadingWhenEmpty && !string.IsNullOrWhiteSpace(component.Name))
            {
                componentDict["Heading"] = component.Name;
            }

            componentDict["IsSelected"] = component.IsSelected;
            if (component.WidescreenOnly)
            {
                componentDict["WidescreenOnly"] = component.WidescreenOnly;
            }

            if (component.Category.Count > 0)
            {
                componentDict["Category"] = component.Category;
            }

            if (component.Language.Count > 0)
            {
                componentDict["Language"] = component.Language;
            }

            // DEPRECATED: ModLinkFilenames migration should happen during DESERIALIZATION, not serialization!
            // During serialization, we should just save whatever is already in ResourceRegistry.
            // If there's nothing in ResourceRegistry but ModLinkFilenames exists, it means the component
            // hasn't been properly migrated yet (old format). We'll serialize ModLinkFilenames as fallback.

            // CRITICAL: Ensure ResourceRegistry is populated from ModLinkFilenames if empty
            // ResourceRegistry should already be populated by this point

            // Serialize ResourceRegistry (new format)
            if (component.ResourceRegistry != null && component.ResourceRegistry.Count > 0)
            {
                IReadOnlyDictionary<string, object> serializedRegistry = SerializeResourceRegistry(component.ResourceRegistry);
                var registryDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in serializedRegistry)
                {
                    registryDict[kvp.Key] = kvp.Value;
                }
                componentDict["ResourceRegistry"] = registryDict;
                Logger.LogVerbose($"[SerializeComponent] Added ResourceRegistry to componentDict: {serializedRegistry.Count} entries");
            }
            else
            {
                Logger.LogVerbose($"[SerializeComponent] ResourceRegistry is null or empty (null={component.ResourceRegistry == null}, count={component.ResourceRegistry?.Count ?? 0}) - NOT adding to componentDict");
            }

            // DEPRECATED: ModLinkFilenames - serialize for backward compatibility alongside ResourceRegistry
            // Old tools/parsers may only understand ModLinkFilenames, so we keep both during transition
            // Extract ModLinkFilenames from ResourceRegistry.Files for backward compatibility
            if (component.ResourceRegistry != null && component.ResourceRegistry.Count > 0)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, ResourceMetadata> kvp in component.ResourceRegistry)
                {
                    string url = kvp.Key;
                    ResourceMetadata meta = kvp.Value;

                    if (meta.Files is null || meta.Files.Count == 0)
                    {
                        // Empty dictionary means auto-discover files
                        result[url] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        continue;
                    }

                    var serializedFilenames = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, bool?> fileKvp in meta.Files)
                    {
                        string filename = fileKvp.Key;
                        bool? shouldDownload = fileKvp.Value;

                        // Serialize: null = "null", true = true, false = false
                        if (shouldDownload.HasValue)
                        {
                            serializedFilenames[filename] = shouldDownload.Value;
                        }
                        else
                        {
                            serializedFilenames[filename] = "null";
                        }
                    }
                    result[url] = serializedFilenames;
                }
                componentDict["ModLinkFilenames"] = result;
                Logger.LogVerbose($"[SerializeComponent] Added ModLinkFilenames to componentDict for backward compatibility: {result.Count} URLs (extracted from ResourceRegistry.Files)");
            }

            if (component.ExcludedDownloads.Count > 0)
            {
                componentDict["ExcludedDownloads"] = component.ExcludedDownloads;
            }

            if (component.Dependencies.Count > 0)
            {
                componentDict["Dependencies"] = component.Dependencies.Select(g => g.ToString()).ToList();
            }

            if (component.Restrictions.Count > 0)
            {
                componentDict["Restrictions"] = component.Restrictions.Select(g => g.ToString()).ToList();
            }

            if (component.InstallAfter.Count > 0)
            {
                componentDict["InstallAfter"] = component.InstallAfter.Select(g => g.ToString()).ToList();
            }

            if (component.InstallBefore.Count > 0)
            {
                componentDict["InstallBefore"] = component.InstallBefore.Select(g => g.ToString()).ToList();
            }

            // Serialize instructions
            if (component.Instructions.Count > 0)
            {
                var instructionsList = component.Instructions.Select(instr => SerializeInstructionToDictionary(instr)).ToList();
                componentDict["Instructions"] = instructionsList;
            }

            // Serialize options
            if (component.Options.Count > 0)
            {
                var optionsList = new List<Dictionary<string, object>>();
                var optionsInstructionsList = new List<Dictionary<string, object>>();

                foreach (Option opt in component.Options)
                {
                    Dictionary<string, object> optDict = SerializeOptionToDictionary(opt);

                    // For TOML format, we need to separate option instructions
                    if (optDict.ContainsKey("Instructions") && optDict["Instructions"] is List<Dictionary<string, object>> optInstructions)
                    {
                        // Add Parent field for TOML's OptionsInstructions format
                        foreach (Dictionary<string, object> instrDict in optInstructions)
                        {
                            var instrDictWithParent = new Dictionary<string, object>(instrDict, StringComparer.Ordinal);
                            instrDictWithParent["Parent"] = opt.Guid.ToString();
                            optionsInstructionsList.Add(instrDictWithParent);
                        }

                        // Remove Instructions from optDict for TOML (they'll be in OptionsInstructions)
                        // But keep them for other formats - each format can decide what to do
                        optDict["_HasInstructions"] = true;
                    }

                    optionsList.Add(optDict);
                }

                componentDict["Options"] = optionsList;
                if (optionsInstructionsList.Count > 0)
                {
                    componentDict["OptionsInstructions"] = optionsInstructionsList;
                }
            }

            // Store validation context metadata (format-specific rendering will use this)
            if (validationContext != null)
            {
                IReadOnlyList<string> componentIssues = validationContext.GetComponentIssues(component.Guid);
                if (componentIssues.Count > 0)
                {
                    componentDict["_ValidationIssues"] = componentIssues;
                }

                if (component.ResourceRegistry != null && component.ResourceRegistry.Count > 0)
                {
                    var urlFailures = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    foreach (string url in component.ResourceRegistry.Keys)
                    {
                        List<string> failures = validationContext.GetUrlFailures(url);
                        if (failures.Count > 0)
                        {
                            urlFailures[url] = failures;
                        }
                    }
                    if (urlFailures.Count > 0)
                    {
                        componentDict["_UrlFailures"] = urlFailures;
                    }
                }
            }

            return componentDict;
        }

        #endregion

        #region Auto-Fix Functionality

        /// <summary>
        /// Automatically fixes common config issues in components after deserialization.
        /// Only runs if MainConfig.AttemptFixes is enabled.
        /// </summary>
        private static void AutoFixComponentIssues(IReadOnlyList<ModComponent> components)
        {
            if (components is null || components.Count == 0)
            {
                return;
            }

            var componentList = components.ToList();
            int fixesApplied = 0;

            foreach (ModComponent component in componentList)
            {
                if (component is null)
                {
                    continue;
                }

                // Auto-fix missing dependencies
                if (component.Dependencies.Count > 0)
                {
                    List<ModComponent> dependencyComponents = ModComponent.FindComponentsFromGuidList(
                        component.Dependencies,
                        componentList
                    );

                    foreach (ModComponent dep in dependencyComponents)
                    {
                        if (dep != null && !dep.IsSelected)
                        {
                            dep.IsSelected = true;
                            fixesApplied++;
                            Logger.LogVerbose($"Auto-fix: Selected missing dependency '{dep.Name}' (required by '{component.Name}')");
                        }
                    }
                }

                // Auto-fix conflicting mods (deselect restrictions if component is selected)
                if (component.IsSelected && component.Restrictions.Count > 0)
                {
                    List<ModComponent> restrictionComponents = ModComponent.FindComponentsFromGuidList(
                        component.Restrictions,
                        componentList
                    );

                    foreach (ModComponent restriction in restrictionComponents)
                    {
                        if (restriction != null && restriction.IsSelected)
                        {
                            restriction.IsSelected = false;
                            fixesApplied++;
                            Logger.LogVerbose($"Auto-fix: Deselected conflicting mod '{restriction.Name}' (conflicts with '{component.Name}')");
                        }
                    }
                }
            }

            if (fixesApplied > 0)
            {
                Logger.LogVerbose($"Auto-fix completed: Applied {fixesApplied} fix(es) to {componentList.Count} component(s)");
            }
        }

        #endregion

        #region Format-Specific Conversion Helpers

        /// <summary>
        /// Converts a JToken to a Dictionary recursively, handling nested JObjects and JArrays.
        /// This ensures proper deserialization of nested structures like Options with Instructions.
        /// </summary>
        private static Dictionary<string, object> JTokenToDictionary(JToken token)
        {
            if (token is null || token.Type == JTokenType.Null)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }

            if (!(token is JObject jobj))
            {
                throw new ArgumentException("Token must be a JObject", nameof(token));
            }

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (JProperty prop in jobj.Properties())
            {
                string key = prop.Name;
                JToken value = prop.Value;

                // Convert key to PascalCase to match expected format
                if (!string.IsNullOrEmpty(key))
                {
                    key = char.ToUpperInvariant(key[0]) + key.Substring(1);
                }

                switch (value.Type)
                {
                    case JTokenType.Null:
                        result[key] = null;
                        break;
                    case JTokenType.Object:
                        // Recursively convert nested objects
                        result[key] = JTokenToDictionary(value);
                        break;
                    case JTokenType.Array:
                        // Convert arrays to List<object>
                        var list = new List<object>();
                        foreach (JToken item in (JArray)value)
                        {
                            if (item.Type == JTokenType.Object)
                            {
                                list.Add(JTokenToDictionary(item));
                            }
                            else
                            {
                                list.Add(((JValue)item).Value);
                            }
                        }
                        result[key] = list;
                        break;
                    case JTokenType.Integer:
                    case JTokenType.Float:
                    case JTokenType.String:
                    case JTokenType.Boolean:
                    case JTokenType.Date:
                    case JTokenType.Bytes:
                    case JTokenType.Guid:
                    case JTokenType.Uri:
                    case JTokenType.TimeSpan:
                        result[key] = ((JValue)value).Value;
                        break;
                    default:
                        Logger.LogWarning($"Unexpected JSON token type for key '{key}': {value.Type}");
                        result[key] = value.ToString();
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Converts a dictionary to a JObject, recursively handling nested structures
        /// </summary>
        private static JObject DictionaryToJObject(Dictionary<string, object> dict)
        {
            var jobj = new JObject();
            foreach (KeyValuePair<string, object> kvp in dict)
            {
                string key = kvp.Key;

                // Skip metadata fields starting with underscore (they're for format-specific use)
                if (key.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                // Convert to camelCase for JSON
                key = char.ToLowerInvariant(key[0]) + key.Substring(1);

                object value = kvp.Value;

                if (value is null)
                {
                    jobj[key] = null;
                }
                else if (value is Dictionary<string, object> nestedDict)
                {
                    // Special handling for ModLinkFilenames - preserve original case for URL keys
                    if (key.Equals("modLinkFilenames", StringComparison.OrdinalIgnoreCase))
                    {
                        var modLinkObj = new JObject();
                        foreach (KeyValuePair<string, object> nestedKvp in nestedDict)
                        {
                            // Preserve original case for URL keys in ModLinkFilenames
                            modLinkObj[nestedKvp.Key] = JToken.FromObject(nestedKvp.Value);
                        }
                        jobj[key] = modLinkObj;
                    }
                    else
                    {
                        jobj[key] = DictionaryToJObject(nestedDict);
                    }
                }
                else if (value is List<Dictionary<string, object>> listOfDicts)
                {
                    var jarray = new JArray();
                    foreach (Dictionary<string, object> d in listOfDicts)
                    {
                        jarray.Add(DictionaryToJObject(d));
                    }
                    jobj[key] = jarray;
                }
                else if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    // Build array manually to avoid Newtonsoft edge cases when enumerable items are heterogeneous
                    var array = new JArray();
                    foreach (object item in enumerable)
                    {
                        if (item is Dictionary<string, object> itemDict)
                        {
                            array.Add(DictionaryToJObject(itemDict));
                        }
                        else
                        {
                            array.Add(item is null ? JValue.CreateNull() : JToken.FromObject(item));
                        }
                    }
                    jobj[key] = array;
                }
                else
                {
                    jobj[key] = JToken.FromObject(value);
                }
            }
            return jobj;
        }

        #endregion

        public static IReadOnlyDictionary<string, object> SerializeResourceRegistry(IReadOnlyDictionary<string, ResourceMetadata> registry)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ResourceMetadata> kvp in registry)
            {
                // Convert Files dictionary to serializable format
                var serializedFiles = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (kvp.Value.Files != null)
                {
                    foreach (KeyValuePair<string, bool?> fileKvp in kvp.Value.Files)
                    {
                        if (fileKvp.Value.HasValue)
                        {
                            serializedFiles[fileKvp.Key] = fileKvp.Value.Value;
                        }
                        else
                        {
                            serializedFiles[fileKvp.Key] = "null";
                        }
                    }
                }

                var metaDict = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["ContentKey"] = kvp.Value.ContentKey,
                    ["MetadataHash"] = kvp.Value.MetadataHash,
                    ["HandlerMetadata"] = kvp.Value.HandlerMetadata,
                    ["Files"] = serializedFiles,
                    ["FileSize"] = kvp.Value.FileSize,
                    ["TrustLevel"] = kvp.Value.TrustLevel.ToString(),
                };

                // Only serialize post-download fields if present
                if (kvp.Value.ContentId != null)
                {
                    metaDict["ContentId"] = kvp.Value.ContentId;
                }

                if (kvp.Value.ContentHashSHA256 != null)
                {
                    metaDict["ContentHashSHA256"] = kvp.Value.ContentHashSHA256;
                }

                if (kvp.Value.PieceLength > 0)
                {
                    metaDict["PieceLength"] = kvp.Value.PieceLength;
                }

                if (kvp.Value.PieceHashes != null)
                {
                    metaDict["PieceHashes"] = kvp.Value.PieceHashes;
                }

                if (kvp.Value.FirstSeen.HasValue)
                {
                    metaDict["FirstSeen"] = kvp.Value.FirstSeen.Value.ToString("O");
                }

                if (kvp.Value.LastVerified.HasValue)
                {
                    metaDict["LastVerified"] = kvp.Value.LastVerified.Value.ToString("O");
                }

                result[kvp.Key] = metaDict;
            }
            return result;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public static Dictionary<string, ResourceMetadata> DeserializeResourceRegistry(IDictionary<string, object> componentDict)
        {
            var result = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if ((!componentDict.TryGetValue("ResourceRegistry", out object registryObj) &&
                    !componentDict.TryGetValue("resourceRegistry", out registryObj)) || registryObj is null)
                {
                    Logger.LogVerbose("DeserializeResourceRegistry: No ResourceRegistry field found in componentDict");
                    return result;
                }

                Logger.LogVerbose($"DeserializeResourceRegistry: Found ResourceRegistry field, type: {registryObj.GetType().Name}");

                // Convert TomlTable to Dictionary if needed
                IDictionary<string, object> registryDict = null;
                if (registryObj is TomlTable tomlTable)
                {
                    registryDict = ConvertTomlTableToDictionary(tomlTable);
                }
                else if (registryObj is IDictionary<string, object> dict)
                {
                    registryDict = dict;
                }

                if (registryDict != null)
                {
                    Logger.LogVerbose($"DeserializeResourceRegistry: ResourceRegistry is IDictionary<string, object> with {registryDict.Count} entries");
                    foreach (KeyValuePair<string, object> kvp in registryDict)
                    {
                        IDictionary<string, object> metaDict = null;
                        if (kvp.Value is TomlTable table)
                        {
                            // Convert TomlTable to Dictionary<string, object>
                            metaDict = ConvertTomlTableToDictionary(table);
                        }
                        else if (kvp.Value is IDictionary<string, object> dict)
                        {
                            metaDict = dict;
                        }

                        if (metaDict == null)
                        {
                            continue;
                        }

                        var meta = new ResourceMetadata
                        {
                            ContentKey = GetValueOrDefault<string>(metaDict, "ContentKey"),
                            ContentId = GetValueOrDefault<string>(metaDict, "ContentId"),
                            ContentHashSHA256 = GetValueOrDefault<string>(metaDict, "ContentHashSHA256"),
                            MetadataHash = GetValueOrDefault<string>(metaDict, "MetadataHash"),
                            FileSize = GetValueOrDefault<long>(metaDict, "FileSize"),
                            PieceLength = GetValueOrDefault<int>(metaDict, "PieceLength"),
                            PieceHashes = GetValueOrDefault<string>(metaDict, "PieceHashes"),
                        };

                        Dictionary<string, object> handlerMetadata =
                            ConvertToStringObjectDictionary(GetValueOrDefault<object>(metaDict, "HandlerMetadata"));
                        meta.HandlerMetadata = handlerMetadata ?? new Dictionary<string, object>(StringComparer.Ordinal);

                        Dictionary<string, bool?> filesDictionary =
                            ConvertToStringBoolNullableDictionary(GetValueOrDefault<object>(metaDict, "Files"));
                        meta.Files = filesDictionary ?? new Dictionary<string, bool?>(StringComparer.Ordinal);

                        if (Enum.TryParse(GetValueOrDefault<string>(metaDict, "TrustLevel"), out MappingTrustLevel trustLevel))
                        {
                            meta.TrustLevel = trustLevel;
                        }

                        if (DateTime.TryParse(GetValueOrDefault<string>(metaDict, "FirstSeen"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime firstSeen))
                        {
                            meta.FirstSeen = firstSeen;
                        }

                        if (DateTime.TryParse(GetValueOrDefault<string>(metaDict, "LastVerified"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime lastVerified))
                        {
                            meta.LastVerified = lastVerified;
                        }

                        // Migration: Use PrimaryUrl as key if it exists (backward compatibility), otherwise use the existing key (which should be URL in new format)
                        string registryKey = GetValueOrDefault<string>(metaDict, "PrimaryUrl");
                        if (string.IsNullOrEmpty(registryKey))
                        {
                            registryKey = kvp.Key; // New format: key is already the URL
                        }
                        // Old format: key was a hash, PrimaryUrl was the URL - use PrimaryUrl as the key

                        result[registryKey] = meta;
                    }
                }
                else if (registryObj is IDictionary<object, object> objectDict)
                {
                    Logger.LogVerbose($"DeserializeResourceRegistry: ResourceRegistry is IDictionary<object, object> with {objectDict.Count} entries");
                    foreach (KeyValuePair<object, object> kvp in objectDict)
                    {
                        string key = kvp.Key?.ToString();
                        if (string.IsNullOrEmpty(key))
                        {
                            continue;
                        }

                        IDictionary<object, object> metaObjDict = null;
                        if (kvp.Value is TomlTable table)
                        {
                            // Convert TomlTable to Dictionary<object, object> for processing
                            Dictionary<string, object> converted = ConvertTomlTableToDictionary(table);
                            metaObjDict = new Dictionary<object, object>();
                            foreach (KeyValuePair<string, object> convertedKvp in converted)
                            {
                                metaObjDict[convertedKvp.Key] = convertedKvp.Value;
                            }
                        }
                        else if (kvp.Value is IDictionary<object, object> dict)
                        {
                            metaObjDict = dict;
                        }

                        if (metaObjDict == null)
                        {
                            continue;
                        }

                        // Convert IDictionary<object, object> to IDictionary<string, object>
                        var metaDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (KeyValuePair<object, object> metaKvp in metaObjDict)
                        {
                            string metaKey = metaKvp.Key?.ToString();
                            if (!string.IsNullOrEmpty(metaKey))
                            {
                                // Convert TomlTable objects to dictionaries
                                if (metaKvp.Value is TomlTable nestedTable)
                                {
                                    metaDict[metaKey] = ConvertTomlTableToDictionary(nestedTable);
                                }
                                else
                                {
                                    metaDict[metaKey] = metaKvp.Value;
                                }
                            }
                        }

                        var meta = new ResourceMetadata
                        {
                            ContentKey = GetValueOrDefault<string>(metaDict, "ContentKey"),
                            ContentId = GetValueOrDefault<string>(metaDict, "ContentId"),
                            ContentHashSHA256 = GetValueOrDefault<string>(metaDict, "ContentHashSHA256"),
                            MetadataHash = GetValueOrDefault<string>(metaDict, "MetadataHash"),
                            FileSize = GetValueOrDefault<long>(metaDict, "FileSize"),
                            PieceLength = GetValueOrDefault<int>(metaDict, "PieceLength"),
                            PieceHashes = GetValueOrDefault<string>(metaDict, "PieceHashes"),
                        };

                        Dictionary<string, object> handlerMetadata =
                            ConvertToStringObjectDictionary(GetValueOrDefault<object>(metaDict, "HandlerMetadata"));
                        meta.HandlerMetadata = handlerMetadata ?? new Dictionary<string, object>(StringComparer.Ordinal);

                        Dictionary<string, bool?> filesDictionary =
                            ConvertToStringBoolNullableDictionary(GetValueOrDefault<object>(metaDict, "Files"));
                        meta.Files = filesDictionary ?? new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

                        if (Enum.TryParse(GetValueOrDefault<string>(metaDict, "TrustLevel"), out MappingTrustLevel trustLevel))
                        {
                            meta.TrustLevel = trustLevel;
                        }

                        if (DateTime.TryParse(GetValueOrDefault<string>(metaDict, "FirstSeen"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime firstSeen))
                        {
                            meta.FirstSeen = firstSeen;
                        }

                        if (DateTime.TryParse(GetValueOrDefault<string>(metaDict, "LastVerified"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime lastVerified))
                        {
                            meta.LastVerified = lastVerified;
                        }

                        // Migration: Use PrimaryUrl as key if it exists (backward compatibility), otherwise use the existing key (which should be URL in new format)
                        string registryKey = GetValueOrDefault<string>(metaDict, "PrimaryUrl");
                        if (string.IsNullOrEmpty(registryKey))
                        {
                            registryKey = key; // New format: key is already the URL
                        }
                        // Old format: key was a hash, PrimaryUrl was the URL - use PrimaryUrl as the key

                        result[registryKey] = meta;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to deserialize ResourceRegistry (non-fatal): {ex.Message}");
            }

            return result;
        }

        #endregion
    }

    public class ComponentValidationContext
    {
        public Dictionary<Guid, List<string>> ComponentIssues { get; set; } = new Dictionary<Guid, List<string>>();
        public Dictionary<(Guid ComponentGuid, int InstructionIndex), List<string>> InstructionIssues { get; set; } = new Dictionary<(Guid ComponentGuid, int InstructionIndex), List<string>>();
        public Dictionary<string, List<string>> UrlFailures { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public void AddModComponentIssue(Guid componentGuid, string issue)
        {
            if (!ComponentIssues.ContainsKey(componentGuid))
            {
                ComponentIssues[componentGuid] = new List<string>();
            }

            ComponentIssues[componentGuid].Add(issue);
        }

        public void AddInstructionIssue(Guid componentGuid, int instructionIndex, string issue)
        {
            (Guid componentGuid, int instructionIndex) key = (componentGuid, instructionIndex);
            if (!InstructionIssues.ContainsKey(key))
            {
                InstructionIssues[key] = new List<string>();
            }

            InstructionIssues[key].Add(issue);
        }

        public void AddUrlFailure(string url, string error)
        {
            if (!UrlFailures.ContainsKey(url))
            {
                UrlFailures[url] = new List<string>();
            }

            UrlFailures[url].Add(error);
        }

        public IReadOnlyList<string> GetComponentIssues(Guid componentGuid)
        {
            return ComponentIssues.TryGetValue(componentGuid, out List<string> issues) ? issues : new List<string>();
        }

        public List<string> GetInstructionIssues(Guid componentGuid, int instructionIndex)
        {
            (Guid componentGuid, int instructionIndex) key = (componentGuid, instructionIndex);
            return InstructionIssues.TryGetValue(key, out List<string> issues) ? issues : new List<string>();
        }

        public List<string> GetUrlFailures(string url)
        {
            return UrlFailures.TryGetValue(url, out List<string> failures) ? failures : new List<string>();
        }

        public bool HasIssues(Guid componentGuid)
        {
            return ComponentIssues.ContainsKey(componentGuid);
        }

        public bool HasInstructionIssues(Guid componentGuid, int instructionIndex)
        {
            (Guid componentGuid, int instructionIndex) key = (componentGuid, instructionIndex);
            return InstructionIssues.ContainsKey(key);
        }

        public bool HasUrlFailures(string url)
        {
            return UrlFailures.ContainsKey(url);
        }
    }
}
