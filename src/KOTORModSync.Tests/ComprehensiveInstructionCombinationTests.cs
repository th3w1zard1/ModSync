// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KOTORModSync.Core;
using KOTORModSync.Core.Installation;
using KOTORModSync.Core.Services.FileSystem;
using NUnit.Framework;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using RealFileSystemProvider = KOTORModSync.Core.Services.FileSystem.RealFileSystemProvider;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class ComprehensiveInstructionCombinationTests
    {
        private string _testDirectory;
        private string _modDirectory;
        private string _kotorDirectory;
        private MainConfig _config;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "KOTORModSync_ComprehensiveTests_" + Guid.NewGuid());
            _modDirectory = Path.Combine(_testDirectory, "Mods");
            _kotorDirectory = Path.Combine(_testDirectory, "KOTOR");
            Directory.CreateDirectory(_modDirectory);
            Directory.CreateDirectory(_kotorDirectory);
            Directory.CreateDirectory(Path.Combine(_kotorDirectory, "Override"));

            _config = new MainConfig
            {
                sourcePath = new DirectoryInfo(_modDirectory),
                destinationPath = new DirectoryInfo(_kotorDirectory)
            };
        }

        [TearDown]
        public void TearDown()
        {
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

        #region Real-World Mod Installation Scenarios

        [Test]
        public async Task RealWorldScenario_ExtractMoveCopyDeleteSequence_ExecutesCorrectly()
        {
            // Simulates a typical mod installation: extract archive, move files, copy additional files, delete old files
            string archivePath = CreateTestZip("mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "textures/texture1.tga", "texture content 1" },
                { "textures/texture2.tga", "texture content 2" },
                { "models/model1.mdl", "model content 1" }
            });

            File.WriteAllText(Path.Combine(_modDirectory, "extra.txt"), "extra content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "old_texture.tga"), "old content");

            var component = new ModComponent { Name = "Real World Mod", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract archive
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move textures
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/textures/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy models
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/extracted/models/*.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy extra file
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/extra.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Delete old texture
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Delete,
                Source = new List<string> { "<<kotorDirectory>>/Override/old_texture.tga" }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Complete installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "First texture should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "Second texture should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "model1.mdl")), Is.True, "Model should be copied");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "extra.txt")), Is.True, "Extra file should be copied");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "old_texture.tga")), Is.False, "Old texture should be deleted");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "extracted", "textures", "texture1.tga")), Is.False, "Source texture should be moved (not copied)");
            });
        }

        [Test]
        public async Task RealWorldScenario_ExtractRenameDelDuplicateSequence_ExecutesCorrectly()
        {
            // Simulates texture mod: extract, rename conflicting files, delete duplicates
            string archivePath = CreateTestZip("texture_mod.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "texture.tga", "new texture content" }
            });

            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tpc"), "old tpc content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga"), "old tga content");

            var component = new ModComponent { Name = "Texture Mod", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/texture_mod.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move new texture
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/texture.tga" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = true
            });

            // Delete duplicate TPC (KOTOR prioritizes TGA over TPC)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.DelDuplicate,
                Source = new List<string> { ".tpc", ".tga" },
                Destination = "<<kotorDirectory>>/Override",
                Arguments = ".tpc"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Texture mod installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.True, "New texture should exist");
                Assert.That(File.ReadAllText(Path.Combine(_kotorDirectory, "Override", "texture.tga")), Is.EqualTo("new texture content"), "New texture content should be correct");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture.tpc")), Is.False, "Duplicate TPC should be deleted");
            });
        }

        [Test]
        public async Task RealWorldScenario_MultipleArchivesWithDependencies_ExecutesInOrder()
        {
            // Simulates mod with multiple archives that must be extracted in order
            string archive1 = CreateTestZip("part1.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "base.txt", "base content" }
            });
            string archive2 = CreateTestZip("part2.zip", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "extension.txt", "extension content" }
            });

            var component = new ModComponent { Name = "Multi-Part Mod", Guid = Guid.NewGuid(), IsSelected = true };

            // Extract first archive
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/part1.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Extract second archive (overwrites/extends first)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Extract,
                Source = new List<string> { "<<modDirectory>>/part2.zip" },
                Destination = "<<modDirectory>>/extracted"
            });

            // Move all extracted files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extracted/*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multi-part mod installation should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "base.txt")), Is.True, "Base file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "extension.txt")), Is.True, "Extension file should exist");
            });
        }

        #endregion

        #region Complex Option Scenarios

        [Test]
        public async Task ComplexOptionScenario_MultipleOptionsWithDependencies_ExecutesCorrectly()
        {
            var component = new ModComponent { Name = "Multi-Option Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Base Option", Guid = Guid.NewGuid(), IsSelected = true };
            var option2 = new Option { Name = "Extended Option", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { option1.Guid }, IsSelected = true };
            var option3 = new Option { Name = "Alternative Option", Guid = Guid.NewGuid(), Restrictions = new List<Guid> { option1.Guid }, IsSelected = false };

            File.WriteAllText(Path.Combine(_modDirectory, "base.txt"), "base");
            File.WriteAllText(Path.Combine(_modDirectory, "extended.txt"), "extended");
            File.WriteAllText(Path.Combine(_modDirectory, "alternative.txt"), "alternative");

            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/base.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option2.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/extended.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            option3.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/alternative.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            component.Options.Add(option1);
            component.Options.Add(option2);
            component.Options.Add(option3);

            // Choose instruction selects options
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString(), option2.Guid.ToString(), option3.Guid.ToString() }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multi-option mod should install successfully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "base.txt")), Is.True, "Base option file should be installed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "extended.txt")), Is.True, "Extended option file should be installed (dependency met)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "alternative.txt")), Is.False, "Alternative option file should NOT be installed (restriction conflict)");
            });
        }

        [Test]
        public async Task ComplexOptionScenario_OptionWithInstructionDependencies_RespectsDependencies()
        {
            var component = new ModComponent { Name = "Dependent Options Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var depComponent = new ModComponent { Name = "Dependency Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "option1_file.txt"), "option1 content");

            // Option instruction depends on another mod
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Dependencies = new List<Guid> { depComponent.Guid }
            });

            component.Options.Add(option1);
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }
            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { depComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Dependent option mod should install successfully");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1_file.txt")), Is.True, "Option file should be installed when dependency is met");
            });
        }

        [Test]
        public async Task ComplexOptionScenario_OptionWithInstructionRestrictions_SkipsWhenRestricted()
        {
            var component = new ModComponent { Name = "Restricted Options Mod", Guid = Guid.NewGuid(), IsSelected = true };
            var restrictedComponent = new ModComponent { Name = "Restricted Mod", Guid = Guid.NewGuid(), IsSelected = true };

            var option1 = new Option { Name = "Option 1", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "option1_file.txt"), "option1 content");

            // Option instruction restricted by another mod
            option1.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/option1_file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Restrictions = new List<Guid> { restrictedComponent.Guid }
            });

            component.Options.Add(option1);
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Choose,
                Source = new List<string> { option1.Guid.ToString() }
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }
            foreach (var option in component.Options)
            {
                foreach (var instruction in option.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(component);
                }
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { restrictedComponent, component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Restricted option mod should complete (instruction skipped)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "option1_file.txt")), Is.False, "Option file should NOT be installed when restriction is active");
            });
        }

        #endregion

        #region Complex Dependency Scenarios

        [Test]
        public async Task ComplexDependencyScenario_InstallAfterInstallBefore_RespectsOrdering()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), InstallAfter = new List<Guid> { modA.Guid }, IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), InstallBefore = new List<Guid> { modB.Guid }, IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "modA.txt"), "A");
            File.WriteAllText(Path.Combine(_modDirectory, "modB.txt"), "B");
            File.WriteAllText(Path.Combine(_modDirectory, "modC.txt"), "C");

            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modB.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modC.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modC.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { modB, modC, modA }; // Intentionally wrong order
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var comp in ordered)
            {
                foreach (var instruction in comp.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(comp);
                }
            }

            // Execute in order
            foreach (var comp in ordered)
            {
                var result = await comp.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Mod {comp.Name} should install successfully");
            }

            Assert.Multiple(() =>
            {
                // Verify order: A should be first, C should be before B, B should be after A
                int indexA = ordered.FindIndex(c => c.Guid == modA.Guid);
                int indexB = ordered.FindIndex(c => c.Guid == modB.Guid);
                int indexC = ordered.FindIndex(c => c.Guid == modC.Guid);

                Assert.That(indexA, Is.LessThan(indexB), "Mod A should install before Mod B (InstallAfter)");
                Assert.That(indexC, Is.LessThan(indexB), "Mod C should install before Mod B (InstallBefore)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modA.txt")), Is.True, "Mod A file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modB.txt")), Is.True, "Mod B file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modC.txt")), Is.True, "Mod C file should exist");
            });
        }

        [Test]
        public async Task ComplexDependencyScenario_MixedDependenciesAndRestrictions_HandlesCorrectly()
        {
            var modA = new ModComponent { Name = "Mod A", Guid = Guid.NewGuid(), IsSelected = true };
            var modB = new ModComponent { Name = "Mod B", Guid = Guid.NewGuid(), Dependencies = new List<Guid> { modA.Guid }, IsSelected = true };
            var modC = new ModComponent { Name = "Mod C", Guid = Guid.NewGuid(), Restrictions = new List<Guid> { modA.Guid }, IsSelected = false }; // Not selected due to restriction

            File.WriteAllText(Path.Combine(_modDirectory, "modA.txt"), "A");
            File.WriteAllText(Path.Combine(_modDirectory, "modB.txt"), "B");
            File.WriteAllText(Path.Combine(_modDirectory, "modC.txt"), "C");

            modA.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modA.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modB.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modB.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            modC.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/modC.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var components = new List<ModComponent> { modA, modB, modC };
            var ordered = InstallCoordinator.GetOrderedInstallList(components);

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var comp in ordered)
            {
                foreach (var instruction in comp.Instructions)
                {
                    instruction.SetFileSystemProvider(fileSystemProvider);
                    instruction.SetParentComponent(comp);
                }
            }

            // Execute only selected mods
            foreach (var comp in ordered.Where(c => c.IsSelected))
            {
                var result = await comp.ExecuteInstructionsAsync(components, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), $"Selected mod {comp.Name} should install successfully");
            }

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modA.txt")), Is.True, "Mod A file should exist");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modB.txt")), Is.True, "Mod B file should exist (dependency met)");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "modC.txt")), Is.False, "Mod C file should NOT exist (restriction active)");
            });
        }

        #endregion

        #region Path Resolution and Sandboxing Edge Cases

        [Test]
        public async Task PathSandboxing_RelativePathWithPlaceholders_ResolvesCorrectly()
        {
            // Test that paths with placeholders are properly sandboxed
            File.WriteAllText(Path.Combine(_modDirectory, "test.txt"), "content");

            var component = new ModComponent { Name = "Sandbox Test", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/test.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Sandboxed path should resolve correctly");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "test.txt")), Is.True, "File should be moved to correct destination");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "test.txt")), Is.False, "Source file should be moved (not copied)");
            });
        }

        [Test]
        public async Task PathSandboxing_WildcardInSubdirectory_ResolvesCorrectly()
        {
            Directory.CreateDirectory(Path.Combine(_modDirectory, "subdir"));
            File.WriteAllText(Path.Combine(_modDirectory, "subdir", "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "subdir", "file2.txt"), "content2");

            var component = new ModComponent { Name = "Wildcard Subdir", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/subdir/*.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Wildcard in subdirectory should work");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "First file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Second file should be moved");
            });
        }

        #endregion

        #region Error Recovery Scenarios

        [Test]
        public async Task ErrorRecovery_PartialFailure_ContinuesWithRemainingInstructions()
        {
            var component = new ModComponent { Name = "Partial Failure Test", Guid = Guid.NewGuid(), IsSelected = true };

            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");

            // First instruction succeeds
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file1.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Second instruction fails (file doesn't exist)
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/nonexistent.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Third instruction should still execute
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file2.txt" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                // Note: The exact behavior depends on implementation - some may continue, some may stop
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "First file should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "Third file should be moved even after error");
            });
        }

        [Test]
        public async Task ErrorRecovery_OverwriteFalseWithExistingFile_SkipsGracefully()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file.txt"), "new content");
            File.WriteAllText(Path.Combine(_kotorDirectory, "Override", "file.txt"), "existing content");

            var component = new ModComponent { Name = "Overwrite Skip", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file.txt" },
                Destination = "<<kotorDirectory>>/Override",
                Overwrite = false
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Should succeed even when skipping overwrite");
                Assert.That(File.ReadAllText(Path.Combine(_kotorDirectory, "Override", "file.txt")), Is.EqualTo("existing content"), "Existing file should not be overwritten");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "file.txt")), Is.True, "Source file should remain when overwrite is false");
            });
        }

        #endregion

        #region Complex Wildcard Scenarios

        [Test]
        public async Task ComplexWildcard_MultiplePatternsInSequence_ProcessesAll()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "texture1.tga"), "tga1");
            File.WriteAllText(Path.Combine(_modDirectory, "texture2.tga"), "tga2");
            File.WriteAllText(Path.Combine(_modDirectory, "model1.mdl"), "mdl1");
            File.WriteAllText(Path.Combine(_modDirectory, "model2.mdl"), "mdl2");

            var component = new ModComponent { Name = "Multi-Wildcard", Guid = Guid.NewGuid(), IsSelected = true };

            // Move all TGA files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/*.tga" },
                Destination = "<<kotorDirectory>>/Override"
            });

            // Copy all MDL files
            component.Instructions.Add(new Instruction
            {
                Action = Instruction.ActionType.Copy,
                Source = new List<string> { "<<modDirectory>>/*.mdl" },
                Destination = "<<kotorDirectory>>/Override"
            });

            var fileSystemProvider = new RealFileSystemProvider();
            foreach (var instruction in component.Instructions)
            {
                instruction.SetFileSystemProvider(fileSystemProvider);
                instruction.SetParentComponent(component);
            }

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Multi-wildcard should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture1.tga")), Is.True, "First TGA should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "texture2.tga")), Is.True, "Second TGA should be moved");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "model1.mdl")), Is.True, "First MDL should be copied");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "model2.mdl")), Is.True, "Second MDL should be copied");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "texture1.tga")), Is.False, "TGA source should be moved");
                Assert.That(File.Exists(Path.Combine(_modDirectory, "model1.mdl")), Is.True, "MDL source should remain (copied)");
            });
        }

        [Test]
        public async Task ComplexWildcard_QuestionMarkPattern_MatchesSingleCharacter()
        {
            File.WriteAllText(Path.Combine(_modDirectory, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(_modDirectory, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(_modDirectory, "file10.txt"), "content10");

            var component = new ModComponent { Name = "Question Mark Wildcard", Guid = Guid.NewGuid(), IsSelected = true };
            var instruction = new Instruction
            {
                Action = Instruction.ActionType.Move,
                Source = new List<string> { "<<modDirectory>>/file?.txt" },
                Destination = "<<kotorDirectory>>/Override"
            };

            component.Instructions.Add(instruction);

            var fileSystemProvider = new RealFileSystemProvider();
            instruction.SetFileSystemProvider(fileSystemProvider);
            instruction.SetParentComponent(component);

            var result = await component.ExecuteInstructionsAsync(new List<ModComponent> { component }, fileSystemProvider, System.Threading.CancellationToken.None, fileSystemProvider);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(ModComponent.InstallExitCode.Success), "Question mark wildcard should succeed");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file1.txt")), Is.True, "file1.txt should match");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file2.txt")), Is.True, "file2.txt should match");
                Assert.That(File.Exists(Path.Combine(_kotorDirectory, "Override", "file10.txt")), Is.False, "file10.txt should NOT match (too many characters)");
            });
        }

        #endregion

        #region Helper Methods

        private string CreateTestZip(string fileName, Dictionary<string, string> files)
        {
            string zipPath = Path.Combine(_modDirectory, fileName);
            using (var archive = ZipArchive.Create())
            {
                foreach (var kvp in files)
                {
                    archive.AddEntry(kvp.Key, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kvp.Value)), true);
                }
                using (var stream = File.OpenWrite(zipPath))
                {
                    archive.SaveTo(stream, new WriterOptions(CompressionType.None));
                }
            }
            return zipPath;
        }

        #endregion
    }
}

