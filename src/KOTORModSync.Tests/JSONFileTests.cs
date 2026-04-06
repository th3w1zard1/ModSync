// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using KOTORModSync.Core;
using KOTORModSync.Core.Services;

using Newtonsoft.Json;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class JsonFileTests
    {
        [SetUp]
        public void SetUp()
        {
            _filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            File.WriteAllText(_filePath, _exampleJson);
        }

        [TearDown]
        public void TearDown()
        {
            Assert.That(_filePath, Is.Not.Null, nameof(_filePath) + " != null");
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }

        private string _filePath = string.Empty;

        private readonly string _exampleJson = @"{
  ""components"": [
{
  ""name"": ""Example Dantooine Enhancement"",
  ""guid"": ""{B3525945-BDBD-45D8-A324-AAF328A5E13E}"",
  ""dependencies"": [
""{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"",
""{D0F371DA-5C69-4A26-8A37-76E3A6A2A50D}""
  ],
  ""installOrder"": 3,
  ""instructions"": [
{
  ""action"": ""extract"",
  ""source"": ""Example Dantooine Enhancement High Resolution - TPC Version-1103-2-1-1670680013.rar"",
  ""destination"": ""%temp%\\mod_files\\Dantooine HR"",
  ""overwrite"": true
},
{
  ""action"": ""delete"",
  ""paths"": [
""%temp%\\mod_files\\Dantooine HR\\DAN_wall03.tpc"",
""%temp%\\mod_files\\Dantooine HR\\DAN_NEW1.tpc"",
""%temp%\\mod_files\\Dantooine HR\\DAN_MWFl.tpc""
  ]
},
{
  ""action"": ""move"",
  ""source"": ""%temp%\\mod_files\\Dantooine HR\\"",
  ""destination"": ""%temp%\\Override""
}
  ]
},
{
  ""name"": ""Example Tweak Pack"",
  ""guid"": ""{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"",
  ""installOrder"": 1,
  ""dependencies"": [],
  ""instructions"": [
{
  ""action"": ""extract"",
  ""source"": ""URCMTP 1.3.rar"",
  ""destination"": ""%temp%\\mod_files\\Example Tweak Pack"",
  ""overwrite"": true
},
{
  ""action"": ""run"",
  ""path"": ""%temp%\\mod_files\\TSLPatcher.exe""
}
  ]
}
  ]
}";

        [Test]
        public void SaveAndLoadJSONFile_MatchingComponents()
        {
            Assert.That(_filePath, Is.Not.Null, nameof(_filePath) + " is null");

            List<ModComponent> originalComponents = FileLoadingService.LoadFromFile(_filePath).ToList();

            string modifiedFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            FileLoadingService.SaveToFile(originalComponents, modifiedFilePath);

            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(modifiedFilePath).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(originalComponents, Is.Not.Null, "Original components list should not be null");
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(File.Exists(modifiedFilePath), Is.True, "Modified file should exist");
                Assert.That(loadedComponents, Has.Count.EqualTo(originalComponents.Count), "Loaded components count should match original");
            });

            for (int i = 0; i < originalComponents.Count; i++)
            {
                ModComponent originalComponent = originalComponents[i];
                ModComponent loadedComponent = loadedComponents[i];

                Assert.Multiple(() =>
                {
                    Assert.That(originalComponent, Is.Not.Null, $"Original component at index {i} should not be null");
                    Assert.That(loadedComponent, Is.Not.Null, $"Loaded component at index {i} should not be null");
                });
                AssertComponentEquality(loadedComponent, originalComponent);
            }

            if (File.Exists(modifiedFilePath))
            {
                File.Delete(modifiedFilePath);
            }
        }

        [Test]
        public void SaveAndLoad_DefaultComponent()
        {
            var newComponent = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { "test.rar" },
                        Destination = "%temp%\\test",
                    },
                },
            };

            string jsonString = ModComponentSerializationService.SerializeModComponentAsJsonString(new List<ModComponent> { newComponent });
            ModComponent duplicateComponent = ModComponentSerializationService.DeserializeModComponentFromJsonString(jsonString)[0];

            Assert.Multiple(() =>
            {
                Assert.That(newComponent, Is.Not.Null, "New component should not be null");
                Assert.That(duplicateComponent, Is.Not.Null, "Duplicate component should not be null");
                Assert.That(jsonString, Is.Not.Null.And.Not.Empty, "JSON string should not be null or empty");
            });
            AssertComponentEquality(newComponent, duplicateComponent);
        }

        [Test]
        public void SaveAndLoadJSONFile_WhitespaceTests()
        {
            List<ModComponent> originalComponents = FileLoadingService.LoadFromFile(_filePath).ToList();

            Assert.That(_filePath, Is.Not.Null, nameof(_filePath) + " != null");
            string jsonContents = File.ReadAllText(_filePath);

            jsonContents = "    \r\n\t   \r\n\r\n\r\n" + jsonContents + "    \r\n\t   \r\n\r\n\r\n";

            string modifiedFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            File.WriteAllText(modifiedFilePath, jsonContents);

            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(modifiedFilePath).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(originalComponents, Is.Not.Null, "Original components list should not be null");
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(File.Exists(modifiedFilePath), Is.True, "Modified file should exist");
                Assert.That(loadedComponents, Has.Count.EqualTo(originalComponents.Count), "Loaded components count should match original after whitespace modification");
            });

            for (int i = 0; i < originalComponents.Count; i++)
            {
                ModComponent originalComponent = originalComponents[i];
                ModComponent loadedComponent = loadedComponents[i];

                Assert.Multiple(() =>
                {
                    Assert.That(originalComponent, Is.Not.Null, $"Original component at index {i} should not be null");
                    Assert.That(loadedComponent, Is.Not.Null, $"Loaded component at index {i} should not be null");
                });
                AssertComponentEquality(originalComponent, loadedComponent);
            }

            if (File.Exists(modifiedFilePath))
            {
                File.Delete(modifiedFilePath);
            }
        }

        [Test]
        public void SaveAndLoadJSONFile_EmptyComponentsList()
        {
            List<ModComponent> originalComponents = new List<ModComponent>();

            FileLoadingService.SaveToFile(originalComponents, _filePath);

            try
            {
                List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(_filePath).ToList()
                    ?? throw new InvalidDataException();

                Assert.Multiple(() =>
                {
                    Assert.That(_filePath, Is.Not.Null, "File path should not be null");
                    Assert.That(File.Exists(_filePath), Is.True, "File should exist after saving");
                    Assert.That(loadedComponents, Is.Null.Or.Empty, "Empty components list should load as null or empty");
                });
            }
            catch (InvalidDataException ex)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(ex, Is.Not.Null, "Exception should not be null");
                    Assert.That(_filePath, Is.Not.Null, "File path should not be null");
                });
                Logger.LogException(ex);
            }
        }

        [Test]
        public void SaveAndLoadJSONFile_DuplicateGuids()
        {
            List<ModComponent> originalComponents = new List<ModComponent>
            {
                new ModComponent
                {
                    Name = "ModComponent 1",
                    Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                },
                new ModComponent
                {
                    Name = "ModComponent 2",
                    Guid = Guid.Parse("{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"),
                },
                new ModComponent
                {
                    Name = "ModComponent 3",
                    Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                },
            };

            FileLoadingService.SaveToFile(originalComponents, _filePath);
            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(_filePath).ToList()
                ?? throw new InvalidDataException();

            Assert.That(loadedComponents, Has.Count.EqualTo(originalComponents.Count));

            for (int i = 0; i < originalComponents.Count; i++)
            {
                ModComponent originalComponent = originalComponents[i];
                ModComponent loadedComponent = loadedComponents[i];

                AssertComponentEquality(originalComponent, loadedComponent);
            }
        }

        [Test]
        public void SaveAndLoadJSONFile_ModifyComponents()
        {
            List<ModComponent> originalComponents = FileLoadingService.LoadFromFile(_filePath).ToList()
                ?? throw new InvalidDataException();

            originalComponents[0].Name = "Modified Name";

            FileLoadingService.SaveToFile(originalComponents, _filePath);
            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(_filePath).ToList()
                ?? throw new InvalidDataException();

            Assert.That(loadedComponents, Has.Count.EqualTo(originalComponents.Count));

            for (int i = 0; i < originalComponents.Count; i++)
            {
                ModComponent originalComponent = originalComponents[i];
                ModComponent loadedComponent = loadedComponents[i];

                AssertComponentEquality(loadedComponent, originalComponent);
            }
        }

        [Test]
        public void SaveAndLoadJSONFile_MultipleRounds()
        {
            // Each round in this test should be a List<ModComponent>, not a ModComponent
            var rounds = new List<List<ModComponent>>
            {
                new List<ModComponent>
                {
                    new ModComponent
                    {
                        Name = "ModComponent 1",
                        Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 2",
                        Guid = Guid.Parse("{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"),
                        IsSelected = true,
                    },
                },
                new List<ModComponent>
                {
                    new ModComponent
                    {
                        Name = "ModComponent 3",
                        Guid = Guid.Parse("{D0F371DA-5C69-4A26-8A37-76E3A6A2A50D}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 4",
                        Guid = Guid.Parse("{E7B27A19-9A81-4A20-B062-7D00F2603D5C}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 5",
                        Guid = Guid.Parse("{F1B05F5D-3C06-4B64-8E39-8BEC8D22BB0A}"),
                        IsSelected = true,
                    },
                },
            };

            foreach (List<ModComponent> components in rounds)
            {
                FileLoadingService.SaveToFile(components, _filePath);
                List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(_filePath).ToList()
                    ?? throw new InvalidDataException();

                Assert.That(loadedComponents, Has.Count.EqualTo(components.Count));

                for (int i = 0; i < components.Count; i++)
                {
                    ModComponent originalComponent = components[i];
                    ModComponent loadedComponent = loadedComponents[i];

                    AssertComponentEquality(originalComponent, loadedComponent);
                }
            }
        }

        [Test]
        public void JsonPrettyPrint_FormatsCorrectly()
        {
            var component = new ModComponent
            {
                Name = "Test Component",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { "test.rar" },
                        Overwrite = true,
                        Destination = "some/path",
                        Arguments = "some args",
                    },
                },
            };

            string jsonString = ModComponentSerializationService.SerializeModComponentAsJsonString(new List<ModComponent> { component });

            Assert.That(jsonString, Does.Contain("\n"), "Pretty-printed JSON should contain newlines");
            Assert.That(jsonString, Does.Contain("  "), "Pretty-printed JSON should contain indentation");
        }

        [Test]
        public void Instruction_ConditionalSerialization_OnlyRelevantFieldsAreIncluded()
        {
            var extractComponent = new ModComponent
            {
                Name = "Extract Test",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { "test.rar" },
                        Overwrite = true,
                        Destination = "some/path",
                    },
                },
            };
            string extractJson = ModComponentSerializationService.SerializeModComponentAsJsonString(new List<ModComponent> { extractComponent });
            Assert.Multiple(() =>
            {
                Assert.That(extractComponent, Is.Not.Null, "Extract component should not be null");
                Assert.That(extractJson, Is.Not.Null.And.Not.Empty, "Extract JSON string should not be null or empty");
                Assert.That(extractComponent.Instructions, Is.Not.Null, "Instructions list should not be null");
                Assert.That(extractComponent.Instructions, Has.Count.EqualTo(1), "Should have exactly one instruction");
                Assert.That(extractJson, Does.Not.Contain("overwrite"), "Extract should not serialize Overwrite");
                Assert.That(extractJson, Does.Not.Contain("destination"), "Extract should not serialize Destination");
                Assert.That(extractJson, Does.Not.Contain("arguments"), "Extract should not serialize Arguments");
            });
        }

        [Test]
        public void ModComponent_RuntimeFields_AreNotSerialized()
        {
            var component = new ModComponent
            {
                Name = "Test Mod",
                Guid = Guid.NewGuid(),
                IsDownloaded = true,
                InstallState = ModComponent.ComponentInstallState.Completed,
                IsSelected = true,
            };

            string jsonString = ModComponentSerializationService.SerializeModComponentAsJsonString(new List<ModComponent> { component });
            Assert.Multiple(() =>
            {
                Assert.That(jsonString, Does.Not.Contain("isDownloaded"), "JSON should not contain IsDownloaded");
                Assert.That(jsonString, Does.Not.Contain("installState"), "JSON should not contain InstallState");
                Assert.That(jsonString, Does.Not.Contain("lastStartedUtc"), "JSON should not contain LastStartedUtc");
                Assert.That(jsonString, Does.Not.Contain("lastCompletedUtc"), "JSON should not contain LastCompletedUtc");
            });
        }

        [Test]
        public void SaveAndLoadJSON_WithOptionsAndModLinkFilenames()
        {
            var testComponent = new ModComponent
            {
                Name = "Test Mod with Options",
                Guid = Guid.NewGuid(),
                Author = "Test Author",
                Description = "Test description",
                ResourceRegistry = new Dictionary<string, ResourceMetadata>(StringComparer.OrdinalIgnoreCase)
                {
                    ["https://example.com/mod.zip"] = new ResourceMetadata
                    {
                        Files = new Dictionary<string, bool?>(StringComparer.Ordinal) { { "mod_v1.0.zip", true }, { "mod_v2.0.zip", false }, { "mod_beta.zip", null } },
                    },
                    ["https://example.com/patch.rar"] = new ResourceMetadata
                    {
                        Files = new Dictionary<string, bool?>(StringComparer.Ordinal) { { "patch.rar", true } },
                    },
                },
                ExcludedDownloads = new List<string> { "debug.zip", "old_version.rar" },
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
            {
                new Instruction
                {
                    Action = Instruction.ActionType.Extract,
                    Source = new List<string> { "<<modDirectory>>\\mod*.zip" },
                    Overwrite = true,
                },
            },
                Options = new System.Collections.ObjectModel.ObservableCollection<Option>
            {
                new Option
                {
                    Guid = Guid.NewGuid(),
                    Name = "Optional Feature 1",
                    Description = "Adds feature 1",
                    IsSelected = false,
                    Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                    {
                        new Instruction
                        {
                            Action = Instruction.ActionType.Move,
                            Source = new List<string> { "<<modDirectory>>\\optional\\file1.txt" },
                            Destination = "<<kotorDirectory>>\\Override",
                            Overwrite = true,
                        },
                    },
                },
                new Option
                {
                    Guid = Guid.NewGuid(),
                    Name = "Optional Feature 2",
                    Description = "Adds feature 2",
                    IsSelected = true,
                    Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                    {
                        new Instruction
                        {
                            Action = Instruction.ActionType.Copy,
                            Source = new List<string> { "<<modDirectory>>\\optional\\file2.txt" },
                            Destination = "<<kotorDirectory>>\\Override",
                            Overwrite = false
                        },
                    },
                },
            },
            };

            // Save to JSON
            string jsonString = ModComponentSerializationService.SerializeModComponentAsJsonString(new List<ModComponent> { testComponent });

            // Verify JSON contains expected structures
            Assert.Multiple(() =>
            {
                Assert.That(testComponent, Is.Not.Null, "Test component should not be null");
                Assert.That(jsonString, Is.Not.Null.And.Not.Empty, "JSON string should not be null or empty");
                Assert.That(jsonString, Does.Contain("options"), "JSON should contain options");
                Assert.That(jsonString, Does.Contain("Optional Feature 1"), "JSON should contain option names");
                Assert.That(jsonString, Does.Contain("modLinkFilenames"), "JSON should contain modLinkFilenames");
                Assert.That(jsonString, Does.Contain("mod_v1.0.zip"), "JSON should contain filename keys");
                Assert.That(jsonString, Does.Contain("excludedDownloads"), "JSON should contain excludedDownloads");
            });

            // Load from JSON
            List<ModComponent> loadedComponents = ModComponentSerializationService.DeserializeModComponentFromJsonString(jsonString).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(loadedComponents, Has.Count.EqualTo(1), "Should load exactly one component");
            });

            ModComponent loadedComponent = loadedComponents[0];

            Assert.Multiple(() =>
            {
                Assert.That(loadedComponent, Is.Not.Null, "Loaded component should not be null");
                // Verify component properties
                Assert.That(loadedComponent.Name, Is.EqualTo(testComponent.Name), "Component name should match");
                Assert.That(loadedComponent.Options, Is.Not.Null, "Options list should not be null");
                Assert.That(loadedComponent.Options.Count, Is.EqualTo(2), "Should have 2 options");
            });

            Assert.Multiple(() =>
            {
                Assert.That(loadedComponent.Options[0], Is.Not.Null, "First option should not be null");
                Assert.That(loadedComponent.Options[1], Is.Not.Null, "Second option should not be null");
                Assert.That(loadedComponent.Options[0].Name, Is.EqualTo("Optional Feature 1"), "First option should have correct name");
                Assert.That(loadedComponent.Options[1].Name, Is.EqualTo("Optional Feature 2"), "Second option should have correct name");
                Assert.That(loadedComponent.Options[0].Instructions, Is.Not.Null, "First option instructions should not be null");
                Assert.That(loadedComponent.Options[1].Instructions, Is.Not.Null, "Second option instructions should not be null");
                Assert.That(loadedComponent.Options[0].Instructions.Count, Is.EqualTo(1), "Option 1 should have 1 instruction");
                Assert.That(loadedComponent.Options[1].Instructions.Count, Is.EqualTo(1), "Option 2 should have 1 instruction");
            });

            Assert.Multiple(() =>
            {
                // Verify ModLinkFilenames
                Assert.That(loadedComponent.ResourceRegistry, Is.Not.Null, "Resource registry should not be null");
                Assert.That(loadedComponent.ResourceRegistry.Count, Is.EqualTo(2), "Should have 2 URLs");
                Assert.That(loadedComponent.ResourceRegistry.ContainsKey("https://example.com/mod.zip"), Is.True, "Should contain first URL");
                Assert.That(loadedComponent.ResourceRegistry["https://example.com/mod.zip"], Is.Not.Null, "First URL entry should not be null");
                Assert.That(loadedComponent.ResourceRegistry["https://example.com/mod.zip"].Files, Is.Not.Null, "Files dictionary should not be null");
                Assert.That(loadedComponent.ResourceRegistry["https://example.com/mod.zip"].Files.Count, Is.EqualTo(3), "First URL should have 3 filenames");
                Assert.That(loadedComponent.ResourceRegistry["https://example.com/mod.zip"].Files["mod_v1.0.zip"], Is.EqualTo(true), "First filename should be true");
                Assert.That(loadedComponent.ResourceRegistry["https://example.com/mod.zip"].Files["mod_v2.0.zip"], Is.EqualTo(false), "Second filename should be false");
                Assert.That(loadedComponent.ResourceRegistry["https://example.com/mod.zip"].Files["mod_beta.zip"], Is.EqualTo(null), "Third filename should be null");
            });

            Assert.Multiple(() =>
            {
                // Verify ExcludedDownloads
                Assert.That(loadedComponent.ExcludedDownloads, Is.Not.Null, "Excluded downloads list should not be null");
                Assert.That(loadedComponent.ExcludedDownloads.Count, Is.EqualTo(2), "Should have 2 excluded downloads");
                Assert.That(loadedComponent.ExcludedDownloads, Does.Contain("debug.zip"), "Should contain first excluded download");
                Assert.That(loadedComponent.ExcludedDownloads, Does.Contain("old_version.rar"), "Should contain second excluded download");
            });
        }

        private static void AssertComponentEquality([CanBeNull] ModComponent comp1, [CanBeNull] ModComponent comp2)
        {
            if (ReferenceEquals(comp1, comp2))
            {
                return;
            }

            if (comp1 is null || comp2 is null)
            {
                return;
            }

            if (comp1.GetType() != comp2.GetType())
            {
                return;
            }

            if (comp1 is ModComponent modComp1 && comp2 is ModComponent modComp2)
            {
                string json1 = JsonConvert.SerializeObject(modComp1);
                string json2 = JsonConvert.SerializeObject(modComp2);

                ModComponent copy1 = JsonConvert.DeserializeObject<ModComponent>(json1)
                    ?? throw new InvalidOperationException();
                ModComponent copy2 = JsonConvert.DeserializeObject<ModComponent>(json2)
                    ?? throw new InvalidOperationException();

                string normalizedJson1 = JsonConvert.SerializeObject(copy1);
                string normalizedJson2 = JsonConvert.SerializeObject(copy2);

                Assert.That(normalizedJson1, Is.EqualTo(normalizedJson2));
            }
            else
            {
                string objJson = JsonConvert.SerializeObject(comp1);
                string anotherJson = JsonConvert.SerializeObject(comp2);

                Assert.That(objJson, Is.EqualTo(anotherJson));
            }
        }
    }
}
