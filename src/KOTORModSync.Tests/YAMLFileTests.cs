// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using KOTORModSync.Core;
using KOTORModSync.Core.Services;
using KOTORModSync.Core.Utility;

using Newtonsoft.Json;
using NUnit.Framework;

using YamlSerialization = YamlDotNet.Serialization;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class YamlFileTests
    {
        [SetUp]
        public void SetUp()
        {

            _filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");

            File.WriteAllText(_filePath, _exampleYaml);
        }

        [TearDown]
        public void TearDown()
        {

            Assert.That(_filePath, Is.Not.Null, nameof(_filePath) + " != null");
            File.Delete(_filePath);
        }

        private string _filePath = string.Empty;

        private readonly string _exampleYaml = @"---
Name: Example Dantooine Enhancement
Guid: B3525945-BDBD-45D8-A324-AAF328A5E13E
Dependencies:
  - C5418549-6B7E-4A8C-8B8E-4AA1BC63C732
  - D0F371DA-5C69-4A26-8A37-76E3A6A2A50D
Instructions:
  - Action: extract
    Source: Example Dantooine Enhancement High Resolution - TPC Version-1103-2-1-1670680013.rar
  - Action: delete
    Source:
      - '%temp%\mod_files\Dantooine HR\DAN_wall03.tpc'
      - '%temp%\mod_files\Dantooine HR\DAN_NEW1.tpc'
      - '%temp%\mod_files\Dantooine HR\DAN_MWFl.tpc'
  - Action: move
    Source: '%temp%\mod_files\Dantooine HR\'
    Destination: '%temp%\Override'
---
Name: Example Tweak Pack
Guid: C5418549-6B7E-4A8C-8B8E-4AA1BC63C732
Dependencies: []
Instructions:
  - Action: extract
    Source: URCMTP 1.3.rar
  - Action: run
    Source: '%temp%\mod_files\TSLPatcher.exe'
";

        [Test]
        public void SaveAndLoadYAMLFile_MatchingComponents()
        {
            // Create test components programmatically instead of using hardcoded YAML
            var originalComponents = new List<ModComponent>
            {
                new ModComponent
                {
                    Name = "Example Dantooine Enhancement",
                    Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                    IsSelected = true,
                    Dependencies = new List<Guid> { Guid.Parse("{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"), Guid.Parse("{D0F371DA-5C69-4A26-8A37-76E3A6A2A50D}") },
                    Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                    {
                        new Instruction
                        {
                            Action = Instruction.ActionType.Extract,
                            Source = new List<string> { "Example Dantooine Enhancement High Resolution - TPC Version-1103-2-1-1670680013.rar" },
                        },
                        new Instruction
                        {
                            Action = Instruction.ActionType.Delete,
                            Source = new List<string>
                            {
                                "%temp%\\mod_files\\Dantooine HR\\DAN_wall03.tpc",
                                "%temp%\\mod_files\\Dantooine HR\\DAN_NEW1.tpc",
                                "%temp%\\mod_files\\Dantooine HR\\DAN_MWFl.tpc",
                            },
                        },
                        new Instruction
                        {
                            Action = Instruction.ActionType.Move,
                            Source = new List<string> { "%temp%\\mod_files\\Dantooine HR\\" },
                            Destination = "%temp%\\Override",
                        },
                    },
                },
                new ModComponent
                {
                    Name = "Example Tweak Pack",
                    Guid = Guid.Parse("{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"),
                    IsSelected = true,
                    Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                    {
                        new Instruction
                        {
                            Action = Instruction.ActionType.Extract,
                            Source = new List<string> { "URCMTP 1.3.rar" },
                        },
                        new Instruction
                        {
                            Action = Instruction.ActionType.Run,
                            Source = new List<string> { "%temp%\\mod_files\\TSLPatcher.exe" },
                        },
                    },
                },
            };

            string serializedYaml = ModComponentSerializationService.SerializeModComponentAsYamlString(originalComponents);
            TestContext.Progress.WriteLine("=== SERIALIZED YAML ===");
            TestContext.Progress.WriteLine(serializedYaml);
            TestContext.Progress.WriteLine("=== END YAML ===");
            TestContext.Progress.WriteLine($"\nYAML Length: {serializedYaml.Length}");
            TestContext.Progress.WriteLine($"First 500 chars: {serializedYaml.Substring(0, Math.Min(500, serializedYaml.Length))}");

            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            File.WriteAllText(tempFilePath, serializedYaml);

            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(tempFilePath).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(originalComponents, Is.Not.Null, "Original components list should not be null");
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(File.Exists(tempFilePath), Is.True, "Temporary file should exist");
                Assert.That(serializedYaml, Is.Not.Null.And.Not.Empty, "Serialized YAML should not be null or empty");
                Assert.That(loadedComponents, Has.Count.EqualTo(originalComponents.Count), "Loaded components count should match original");
            });

            for (int i = 0; i < originalComponents.Count; i++)
            {
                ModComponent originalComponent = originalComponents[i];
                ModComponent loadedComponent = loadedComponents[i];

                AssertComponentEquality(loadedComponent, originalComponent);
            }
        }

        [Test]
        public void SaveAndLoad_DefaultComponent()
        {
            // Create a test component programmatically
            var newComponent = new ModComponent
            {
                Name = "test_mod_" + Path.GetRandomFileName(),
                Guid = Guid.NewGuid(),
                IsSelected = true,
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
                {
                    new Instruction
                    {
                        Action = Instruction.ActionType.Extract,
                        Source = new List<string> { "test.rar" },
                    },
                },
            };

            string yamlString = ModComponentSerializationService.SerializeModComponentAsYamlString(new List<ModComponent> { newComponent });

            List<ModComponent> loadedComponents = ModComponentSerializationService.DeserializeModComponentFromYamlString(yamlString).ToList() ?? throw new InvalidDataException();
            ModComponent duplicateComponent = loadedComponents[0];

            Assert.Multiple(() =>
            {
                Assert.That(newComponent, Is.Not.Null, "New component should not be null");
                Assert.That(duplicateComponent, Is.Not.Null, "Duplicate component should not be null");
                Assert.That(yamlString, Is.Not.Null.And.Not.Empty, "YAML string should not be null or empty");
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(loadedComponents, Has.Count.EqualTo(1), "Should load exactly one component");
            });
            AssertComponentEquality(newComponent, duplicateComponent);
        }

        [Test]
        [Ignore("not sure if I want to support")]
        public void SaveAndLoadYAMLFile_CaseInsensitive()
        {

            List<ModComponent> originalComponents = FileLoadingService.LoadFromFile(_filePath).ToList();

            Assert.That(_filePath, Is.Not.Null, nameof(_filePath) + " != null");
            string yamlContents = File.ReadAllText(_filePath);

            yamlContents = ConvertFieldNamesAndValuesToMixedCase(yamlContents);

            string modifiedFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            File.WriteAllText(modifiedFilePath, yamlContents);

            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(modifiedFilePath).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(originalComponents, Is.Not.Null, "Original components list should not be null");
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(File.Exists(modifiedFilePath), Is.True, "Modified file should exist");
                Assert.That(yamlContents, Is.Not.Null.And.Not.Empty, "YAML contents should not be null or empty");
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
        }

        [Test]
        public void SaveAndLoadYAMLFile_WhitespaceTests()
        {
            // Create test components programmatically
            var originalComponents = new List<ModComponent>
            {
                new ModComponent
                {
                    Name = "Test Component",
                    Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                    IsSelected = true,
                },
            };

            // Serialize to YAML
            string yamlContents = ModComponentSerializationService.SerializeModComponentAsYamlString(originalComponents);

            // Add extra whitespace around the content
            yamlContents = "    \r\n\t   \r\n\r\n\r\n" + yamlContents + "    \r\n\t   \r\n\r\n\r\n";

            string modifiedFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            File.WriteAllText(modifiedFilePath, yamlContents);

            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(modifiedFilePath).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(originalComponents, Is.Not.Null, "Original components list should not be null");
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(File.Exists(modifiedFilePath), Is.True, "Modified file should exist");
                Assert.That(yamlContents, Is.Not.Null.And.Not.Empty, "YAML contents should not be null or empty");
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
        }

        private static string ConvertFieldNamesAndValuesToMixedCase(string yamlContents)
        {
            var convertedContents = new StringBuilder();
            var random = new Random();

            bool isFieldName = true;

            foreach (char c in yamlContents)
            {
                char convertedChar = c;

                if (isFieldName)
                {
                    if (char.IsLetter(c))
                    {

                        convertedChar = random.Next(2) == 0
                            ? char.ToUpper(c)
                            : char.ToLower(c);
                    }
                    else if (c == ':')
                    {
                        isFieldName = false;
                    }
                }
                else
                {
                    if (char.IsLetter(c))
                    {

                        convertedChar = random.Next(2) == 0
                            ? char.ToUpper(c)
                            : char.ToLower(c);
                    }
                    else if (c == '\n' || c == '\r')
                    {
                        isFieldName = true;
                    }
                }

                _ = convertedContents.Append(convertedChar);
            }

            return convertedContents.ToString();
        }

        [Test]
        public void SaveAndLoadYAMLFile_EmptyComponentsList()
        {
            List<ModComponent> originalComponents = new List<ModComponent>();

            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            FileLoadingService.SaveToFile(originalComponents, tempFilePath);

            var exception = Assert.Throws<InvalidDataException>(() => FileLoadingService.LoadFromFile(tempFilePath));

            Assert.Multiple(() =>
            {
                Assert.That(exception, Is.Not.Null, "Empty components list should throw InvalidDataException");
                Assert.That(originalComponents, Is.Not.Null, "Original components list should not be null");
                Assert.That(originalComponents, Is.Empty, "Original components list should be empty");
                Assert.That(File.Exists(tempFilePath), Is.True, "Temporary file should exist");
            });
        }

        [Test]
        public void SaveAndLoadYAMLFile_DuplicateGuids()
        {
            List<ModComponent> originalComponents =
            new List<ModComponent>
            {
                new ModComponent
                {
                    Name = "ModComponent 1", Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                    IsSelected = true,
                },
                new ModComponent
                {
                    Name = "ModComponent 2", Guid = Guid.Parse("{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"),
                    IsSelected = true,
                },
                new ModComponent
                {
                    Name = "ModComponent 3", Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                    IsSelected = true,
                },
            };

            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            FileLoadingService.SaveToFile(originalComponents, tempFilePath);
            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(tempFilePath).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(originalComponents, Is.Not.Null, "Original components list should not be null");
                Assert.That(loadedComponents, Is.Not.Null, "Loaded components list should not be null");
                Assert.That(File.Exists(tempFilePath), Is.True, "Temporary file should exist");
                Assert.That(loadedComponents, Has.Count.EqualTo(originalComponents.Count), "Loaded components count should match original (duplicate GUIDs allowed)");
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
        }

        [Test]
        public void SaveAndLoadYAMLFile_ModifyComponents()
        {
            // Create test components programmatically
            var originalComponents = new List<ModComponent>
            {
                new ModComponent
                {
                    Name = "Original Name",
                    Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                    IsSelected = true,
                },
                new ModComponent
                {
                    Name = "Second Component",
                    Guid = Guid.Parse("{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"),
                    IsSelected = true,
                },
            };

            // Modify the first component
            originalComponents[0].Name = "Modified Name";

            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
            FileLoadingService.SaveToFile(originalComponents, tempFilePath);
            List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(tempFilePath).ToList();

            Assert.That(loadedComponents, Has.Count.EqualTo(originalComponents.Count));

            for (int i = 0; i < originalComponents.Count; i++)
            {
                ModComponent originalComponent = originalComponents[i];
                ModComponent loadedComponent = loadedComponents[i];

                AssertComponentEquality(loadedComponent, originalComponent);
            }
        }

        [Test]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
        public void SaveAndLoadYAMLFile_MultipleRounds()
        {

            List<List<ModComponent>> rounds =
            new List<List<ModComponent>>
            {
                new List<ModComponent>
                {
                    new ModComponent
                    {
                        Name = "ModComponent 1", Guid = Guid.Parse("{B3525945-BDBD-45D8-A324-AAF328A5E13E}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 2", Guid = Guid.Parse("{C5418549-6B7E-4A8C-8B8E-4AA1BC63C732}"),
                        IsSelected = true,
                    },
                },
                new List<ModComponent>
                {
                    new ModComponent
                    {
                        Name = "ModComponent 3", Guid = Guid.Parse("{D0F371DA-5C69-4A26-8A37-76E3A6A2A50D}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 4", Guid = Guid.Parse("{E7B27A19-9A81-4A20-B062-7D00F2603D5C}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 5", Guid = Guid.Parse("{F1B05F5D-3C06-4B64-8E39-8BEC8D22BB0A}"),
                        IsSelected = true,
                    },
                },
                new List<ModComponent>
                {
                    new ModComponent
                    {
                        Name = "ModComponent 6", Guid = Guid.Parse("{EF04A28E-5031-4A95-A85A-9A1B29A31710}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 7", Guid = Guid.Parse("{B0373F49-ED5A-43A1-91E0-5CEB85659282}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 8", Guid = Guid.Parse("{BBDB9C8D-DA44-4859-A641-0364D6F34D12}"),
                        IsSelected = true,
                    },
                    new ModComponent
                    {
                        Name = "ModComponent 9", Guid = Guid.Parse("{D6B5C60F-26A7-4595-A0E2-2DE567A376DE}"),
                        IsSelected = true,
                    },
                },
            };

            foreach (List<ModComponent> components in rounds)
            {
                FileLoadingService.SaveToFile(components, _filePath);
                List<ModComponent> loadedComponents = FileLoadingService.LoadFromFile(_filePath).ToList();

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<Pending>")]
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
                    Arguments = "some args",
                },
            },
            };
            string extractYaml = ModComponentSerializationService.SerializeModComponentAsYamlString(new List<ModComponent> { extractComponent });
            Assert.Multiple(() =>
            {
                Assert.That(extractComponent, Is.Not.Null, "Extract component should not be null");
                Assert.That(extractYaml, Is.Not.Null.And.Not.Empty, "Extract YAML string should not be null or empty");
                Assert.That(extractComponent.Instructions, Is.Not.Null, "Instructions list should not be null");
                Assert.That(extractComponent.Instructions, Has.Count.EqualTo(1), "Should have exactly one instruction");
                Assert.That(NetFrameworkCompatibility.Contains(extractYaml, "Overwrite", StringComparison.Ordinal), Is.False, "Extract should not serialize Overwrite");
                Assert.That(NetFrameworkCompatibility.Contains(extractYaml, "Destination", StringComparison.Ordinal), Is.False, "Extract should not serialize Destination");
                Assert.That(NetFrameworkCompatibility.Contains(extractYaml, "Arguments", StringComparison.Ordinal), Is.False, "Extract should not serialize Arguments");
            });

            var moveComponent = new ModComponent
            {
                Name = "Move Test",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
            {
                new Instruction
                {
                    Action = Instruction.ActionType.Move,
                    Source = new List<string> { "test.txt" },
                    Destination = "<<kotorDirectory>>\\Override",
                    Overwrite = false,  // Set to false so it gets serialized (different from default)
					Arguments = "should not appear",
                },
            },
            };
            string moveYaml = ModComponentSerializationService.SerializeModComponentAsYamlString(new List<ModComponent> { moveComponent });
            Assert.Multiple(() =>
            {
                Assert.That(moveYaml, Does.Contain("Overwrite"), "Move should serialize Overwrite");
                Assert.That(moveYaml, Does.Contain("Destination"), "Move should serialize Destination");
                Assert.That(moveYaml, Does.Not.Contain("Arguments"), "Move should not serialize Arguments");
            });

            var patcherComponent = new ModComponent
            {
                Name = "Patcher Test",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
            {
                new Instruction
                {
                    Action = Instruction.ActionType.Patcher,
                    Source = new List<string> { "tslpatchdata" },
                    Destination = "<<kotorDirectory>>",
                    Arguments = "0",
                    Overwrite = true,
                },
            },
            };
            string patcherYaml = ModComponentSerializationService.SerializeModComponentAsYamlString(new List<ModComponent> { patcherComponent });
            Assert.Multiple(() =>
            {
                Assert.That(patcherYaml, Does.Not.Contain("Overwrite"), "Patcher should not serialize Overwrite");
                Assert.That(patcherYaml, Does.Contain("Destination"), "Patcher should serialize Destination");
                Assert.That(patcherYaml, Does.Contain("Arguments"), "Patcher should serialize Arguments");
            });

            var executeComponent = new ModComponent
            {
                Name = "Execute Test",
                Guid = Guid.NewGuid(),
                Instructions = new System.Collections.ObjectModel.ObservableCollection<Instruction>
            {
                new Instruction
                {
                    Action = Instruction.ActionType.Execute,
                    Source = new List<string> { "setup.exe" },
                    Arguments = "/silent",
                    Overwrite = true,
                    Destination = "some/path",
                },
            },
            };
            string executeYaml = ModComponentSerializationService.SerializeModComponentAsYamlString(new List<ModComponent> { executeComponent });
            Assert.Multiple(() =>
            {
                Assert.That(executeYaml, Does.Not.Contain("Overwrite"), "Execute should not serialize Overwrite");
                Assert.That(executeYaml, Does.Not.Contain("Destination"), "Execute should not serialize Destination");
                Assert.That(executeYaml, Does.Contain("Arguments"), "Execute should serialize Arguments");
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

            string yamlString = ModComponentSerializationService.SerializeModComponentAsYamlString(new List<ModComponent> { component });
            Assert.Multiple(() =>
            {
                Assert.That(component, Is.Not.Null, "Component should not be null");
                Assert.That(yamlString, Is.Not.Null.And.Not.Empty, "YAML string should not be null or empty");
                Assert.That(yamlString, Does.Not.Contain("IsDownloaded"), "YAML should not contain IsDownloaded (runtime field)");
                Assert.That(yamlString, Does.Not.Contain("InstallState"), "YAML should not contain InstallState (runtime field)");
                Assert.That(yamlString, Does.Not.Contain("LastStartedUtc"), "YAML should not contain LastStartedUtc (runtime field)");
                Assert.That(yamlString, Does.Not.Contain("LastCompletedUtc"), "YAML should not contain LastCompletedUtc (runtime field)");
                Assert.That(yamlString, Does.Contain("IsSelected"), "YAML should contain IsSelected (persistent field)");
                Assert.That(yamlString, Does.Contain("Test Mod"), "YAML should contain component name");
            });
        }

        private static void AssertComponentEquality(object obj, object another)
        {
            if (ReferenceEquals(obj, another))
            {
                return;
            }

            if (obj is null || another is null)
            {
                return;
            }

            if (obj.GetType() != another.GetType())
            {
                return;
            }

            if (obj is ModComponent comp1 && another is ModComponent comp2)
            {

                string json1 = JsonConvert.SerializeObject(comp1);
                string json2 = JsonConvert.SerializeObject(comp2);

                ModComponent copy1 = JsonConvert.DeserializeObject<ModComponent>(json1) ?? throw new InvalidDataException();
                ModComponent copy2 = JsonConvert.DeserializeObject<ModComponent>(json2) ?? throw new InvalidDataException();

                string normalizedJson1 = JsonConvert.SerializeObject(copy1);
                string normalizedJson2 = JsonConvert.SerializeObject(copy2);

                Assert.That(normalizedJson1, Is.EqualTo(normalizedJson2));
            }
            else
            {
                string objJson = JsonConvert.SerializeObject(obj);
                string anotherJson = JsonConvert.SerializeObject(another);

                Assert.That(objJson, Is.EqualTo(anotherJson));
            }
        }
    }
}
