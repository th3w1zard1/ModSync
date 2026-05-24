// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;

using KOTORModSync.Core;
using KOTORModSync.Core.Services;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
	[TestFixture]
	public class ResourceMetadataSerializationTests
	{
		[Test]
		public void SerializeResourceRegistry_WithSingleEntry_ProducesValidDict()
		{
			var registry = new Dictionary<string, ResourceMetadata>
(StringComparer.Ordinal)
			{
				["hash123"] = new ResourceMetadata
				{
					MetadataHash = "hash123",
					FileSize = 1024,
				}
			};

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(registry);

			Assert.That(serialized, Is.Not.Null);
			Assert.That(serialized.ContainsKey("resources"), Is.True);
		}

		[Test]
		public void DeserializeResourceRegistry_WithValidData_ReconstructsRegistry()
		{
			var original = new Dictionary<string, ResourceMetadata>
(StringComparer.Ordinal)
			{
				["hash123"] = new ResourceMetadata
				{
					MetadataHash = "hash123",
					FileSize = 1024,
					FirstSeen = DateTime.UtcNow,
				}
			};

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(original);
			var deserialized = ModComponentSerializationService.DeserializeResourceRegistry(serialized);

			Assert.Multiple(() =>
			{
				Assert.That(original, Is.Not.Null, "Original registry should not be null");
				Assert.That(original.Count, Is.EqualTo(1), "Original registry should contain exactly 1 entry");
				Assert.That(serialized, Is.Not.Null, "Serialized dictionary should not be null");
				Assert.That(deserialized, Is.Not.Null, "Deserialized registry should not be null");
				Assert.That(deserialized, Has.Count.EqualTo(1), "Deserialized registry should contain exactly 1 entry");
				Assert.That(deserialized.ContainsKey("hash123"), Is.True, "Deserialized registry should contain hash123 key");
				Assert.That(deserialized["hash123"], Is.Not.Null, "Deserialized metadata should not be null");
				Assert.That(deserialized["hash123"].MetadataHash, Is.EqualTo("hash123"), "MetadataHash should match original");
			});
		}

		[Test]
		public void SerializeResourceRegistry_WithMultipleEntries_PreservesAll()
		{
			var registry = new Dictionary<string, ResourceMetadata>
(StringComparer.Ordinal)
			{
				["hash1"] = new ResourceMetadata { MetadataHash = "hash1" },
				["hash2"] = new ResourceMetadata { MetadataHash = "hash2" },
				["hash3"] = new ResourceMetadata { MetadataHash = "hash3" }
			};

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(registry);
			var deserialized = ModComponentSerializationService.DeserializeResourceRegistry(serialized);

			Assert.Multiple(() =>
			{
				Assert.That(registry, Is.Not.Null, "Registry should not be null");
				Assert.That(registry.Count, Is.EqualTo(3), "Registry should contain exactly 3 entries");
				Assert.That(serialized, Is.Not.Null, "Serialized dictionary should not be null");
				Assert.That(deserialized, Is.Not.Null, "Deserialized registry should not be null");
				Assert.That(deserialized, Has.Count.EqualTo(3), "Deserialized registry should contain exactly 3 entries");
				Assert.That(deserialized.ContainsKey("hash1"), Is.True, "Deserialized registry should contain hash1");
				Assert.That(deserialized.ContainsKey("hash2"), Is.True, "Deserialized registry should contain hash2");
				Assert.That(deserialized.ContainsKey("hash3"), Is.True, "Deserialized registry should contain hash3");
				Assert.That(deserialized["hash1"], Is.Not.Null, "Hash1 metadata should not be null");
				Assert.That(deserialized["hash2"], Is.Not.Null, "Hash2 metadata should not be null");
				Assert.That(deserialized["hash3"], Is.Not.Null, "Hash3 metadata should not be null");
			});
		}

		[Test]
		public void SerializeResourceRegistry_WithEmptyRegistry_ProducesEmptyDict()
		{
			var registry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal);

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(registry);

			Assert.Multiple(() =>
			{
				Assert.That(registry, Is.Not.Null, "Registry should not be null");
				Assert.That(registry, Is.Empty, "Registry should be empty");
				Assert.That(serialized, Is.Not.Null, "Serialized dictionary should not be null");
				Assert.That(serialized.ContainsKey("resources"), Is.True, "Serialized dictionary should contain 'resources' key");
			});
		}

		[Test]
		public void DeserializeResourceRegistry_WithEmptyDict_ReturnsEmptyRegistry()
		{
			var emptyDict = new Dictionary<string, object>(StringComparer.Ordinal)
			{
				["resources"] = new List<object>()
			};

			var deserialized = ModComponentSerializationService.DeserializeResourceRegistry(emptyDict);

			Assert.Multiple(() =>
			{
				Assert.That(emptyDict, Is.Not.Null, "Empty dictionary should not be null");
				Assert.That(emptyDict.ContainsKey("resources"), Is.True, "Empty dictionary should contain 'resources' key");
				Assert.That(deserialized, Is.Not.Null, "Deserialized registry should not be null");
				Assert.That(deserialized, Is.Empty, "Deserialized registry should be empty");
			});
		}

		[Test]
		public void SerializeResourceRegistry_WithNullValues_HandlesGracefully()
		{
			var registry = new Dictionary<string, ResourceMetadata>
(StringComparer.Ordinal)
			{
				["hash1"] = new ResourceMetadata
				{
					MetadataHash = "hash1",
				}
			};

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(registry);
			var deserialized = ModComponentSerializationService.DeserializeResourceRegistry(serialized);

			Assert.Multiple(() =>
			{
				Assert.That(registry, Is.Not.Null, "Registry should not be null");
				Assert.That(registry.Count, Is.EqualTo(1), "Registry should contain exactly 1 entry");
				Assert.That(serialized, Is.Not.Null, "Serialized dictionary should not be null");
				Assert.That(deserialized, Is.Not.Null, "Deserialized registry should not be null");
				Assert.That(deserialized.ContainsKey("hash1"), Is.True, "Deserialized registry should contain hash1");
				Assert.That(deserialized["hash1"], Is.Not.Null, "Hash1 metadata should not be null");
			});
		}

		[Test]
		public void SerializeResourceRegistry_WithFiles_PreservesFilesDictionary()
		{
			var registry = new Dictionary<string, ResourceMetadata>
(StringComparer.Ordinal)
			{
				["hash1"] = new ResourceMetadata
				{
					MetadataHash = "hash1",
					Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase)
					{
						["file1.zip"] = true,
						["file2.zip"] = false
					}
				}
			};

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(registry);
			var deserialized = ModComponentSerializationService.DeserializeResourceRegistry(serialized);

			Assert.Multiple(() =>
			{
				Assert.That(registry, Is.Not.Null, "Registry should not be null");
				Assert.That(registry.Count, Is.EqualTo(1), "Registry should contain exactly 1 entry");
				Assert.That(serialized, Is.Not.Null, "Serialized dictionary should not be null");
				Assert.That(deserialized, Is.Not.Null, "Deserialized registry should not be null");
				Assert.That(deserialized.ContainsKey("hash1"), Is.True, "Deserialized registry should contain hash1");
				Assert.That(deserialized["hash1"], Is.Not.Null, "Hash1 metadata should not be null");
				Assert.That(deserialized["hash1"].Files, Is.Not.Null, "Files dictionary should not be null");
				Assert.That(deserialized["hash1"].Files, Has.Count.EqualTo(2), "Files dictionary should contain exactly 2 entries");
				Assert.That(deserialized["hash1"].Files.ContainsKey("file1.zip"), Is.True, "Files dictionary should contain file1.zip");
				Assert.That(deserialized["hash1"].Files.ContainsKey("file2.zip"), Is.True, "Files dictionary should contain file2.zip");
				Assert.That(deserialized["hash1"].Files["file1.zip"], Is.True, "file1.zip should have value true");
				Assert.That(deserialized["hash1"].Files["file2.zip"], Is.False, "file2.zip should have value false");
			});
		}

		[Test]
		public void SerializeResourceRegistry_WithHandlerMetadata_PreservesMetadata()
		{
			var registry = new Dictionary<string, ResourceMetadata>
(StringComparer.Ordinal)
			{
				["hash1"] = new ResourceMetadata
				{
					MetadataHash = "hash1",
					HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal)
					{
						["provider"] = "deadlystream",
						["fileId"] = "1234",
						["version"] = "1.0"
					}
				}
			};

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(registry);
			var deserialized = ModComponentSerializationService.DeserializeResourceRegistry(serialized);

			Assert.Multiple(() =>
			{
				Assert.That(registry, Is.Not.Null, "Registry should not be null");
				Assert.That(registry.Count, Is.EqualTo(1), "Registry should contain exactly 1 entry");
				Assert.That(serialized, Is.Not.Null, "Serialized dictionary should not be null");
				Assert.That(deserialized, Is.Not.Null, "Deserialized registry should not be null");
				Assert.That(deserialized.ContainsKey("hash1"), Is.True, "Deserialized registry should contain hash1");
				Assert.That(deserialized["hash1"], Is.Not.Null, "Hash1 metadata should not be null");
				Assert.That(deserialized["hash1"].HandlerMetadata, Is.Not.Null, "HandlerMetadata should not be null");
				Assert.That(deserialized["hash1"].HandlerMetadata.ContainsKey("provider"), Is.True, "HandlerMetadata should contain provider key");
				Assert.That(deserialized["hash1"].HandlerMetadata.ContainsKey("fileId"), Is.True, "HandlerMetadata should contain fileId key");
				Assert.That(deserialized["hash1"].HandlerMetadata["provider"], Is.EqualTo("deadlystream"), "Provider should match original");
				Assert.That(deserialized["hash1"].HandlerMetadata["fileId"], Is.EqualTo("1234"), "FileId should match original");
			});
		}

		[Test]
		public void RoundTrip_CompleteResourceMetadata_PreservesAllFields()
		{
			var original = new ResourceMetadata
			{
				ContentKey = "key789",
				MetadataHash = "hash789",
				FileSize = 2048,
				FirstSeen = DateTime.UtcNow,
				LastVerified = DateTime.UtcNow.AddHours(-1),
				Files = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase) { ["test.zip"] = true },
				HandlerMetadata = new Dictionary<string, object>(StringComparer.Ordinal) { ["key"] = "value" }
			};

			var registry = new Dictionary<string, ResourceMetadata>(StringComparer.Ordinal) { ["hash789"] = original };

			var serialized = ModComponentSerializationService.SerializeResourceRegistry(registry);
			var deserialized = ModComponentSerializationService.DeserializeResourceRegistry(serialized);
			var result = deserialized["hash789"];

			Assert.Multiple(() =>
			{
				Assert.That(original, Is.Not.Null, "Original metadata should not be null");
				Assert.That(registry, Is.Not.Null, "Registry should not be null");
				Assert.That(registry.Count, Is.EqualTo(1), "Registry should contain exactly 1 entry");
				Assert.That(serialized, Is.Not.Null, "Serialized dictionary should not be null");
				Assert.That(deserialized, Is.Not.Null, "Deserialized registry should not be null");
				Assert.That(deserialized.ContainsKey("hash789"), Is.True, "Deserialized registry should contain hash789");
				Assert.That(result, Is.Not.Null, "Result metadata should not be null");
				Assert.That(result.MetadataHash, Is.EqualTo(original.MetadataHash), "MetadataHash should match original");
				Assert.That(result.FileSize, Is.EqualTo(original.FileSize), "FileSize should match original");
			});
		}
	}
}
