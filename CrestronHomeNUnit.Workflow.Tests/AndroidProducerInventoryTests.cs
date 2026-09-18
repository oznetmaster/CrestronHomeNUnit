// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class AndroidProducerInventoryTests
	{
	private string _root = null!, _assembly = null!, _manifest = null!;
	[SetUp]
	public void Prepare ()
		{
		_root = Path.Combine (Path.GetTempPath (), "android-producer-" + Guid.NewGuid ().ToString ("N"));
		_assembly = Path.Combine (_root, "assembly");
		_manifest = Path.Combine (_root, "producer-manifest.json");
		Directory.CreateDirectory (Path.Combine (_assembly, "fr"));
		File.WriteAllText (Path.Combine (_assembly, "Example.dll"), "main assembly");
		File.WriteAllText (Path.Combine (_assembly, "dependency.dll"), "dependency");
		File.WriteAllText (Path.Combine (_assembly, "Example.deps.json"), "{}");
		File.WriteAllText (Path.Combine (_assembly, "fr", "Example.resources.dll"), "localized resource");
		}
	[TearDown]
	public void Remove () => Directory.Delete (_root, recursive: true);
	[Test]
	public void RetainsEveryFileIncludingRuntimeSettingsAndNestedResources ()
		{
		var inventory = AndroidProducerInventory.Capture (_assembly, CancellationToken.None);
		inventory.Save (_manifest);
		inventory.RequireUnchanged (_assembly, _manifest, CancellationToken.None);
		using var manifest = JsonDocument.Parse (File.ReadAllBytes (_manifest));
		Assert.That (manifest.RootElement.GetProperty ("schemaVersion").GetInt32 (), Is.EqualTo (1));
		var files = manifest.RootElement.GetProperty ("files").EnumerateArray ().ToArray ();
		Assert.That (files.Select (f => f.GetProperty ("relativePath").GetString ()), Is.EquivalentTo
			(new[] { "Example.dll", "dependency.dll", "Example.deps.json", "fr/Example.resources.dll" }));
		foreach (var file in files)
			Assert.That (file.GetProperty ("sha256").GetString (), Is.EqualTo (Convert.ToHexString
				(SHA256.HashData (File.ReadAllBytes (Path.Combine (_assembly, file.GetProperty ("relativePath").GetString ()!))))));
		Assert.That (inventory.Sha256, Is.EqualTo (Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (_manifest)))));
		Assert.Throws<IOException> (() => inventory.Save (_manifest), "Do not overwrite existing evidence.");
		}
	[TestCase ("dependency.dll"), TestCase ("Example.deps.json"), TestCase ("fr/Example.resources.dll")]
	public void ReplacingBothDependencyAndManifestCannotReplaceTheCoordinatorPin (string name)
		{
		var original = AndroidProducerInventory.Capture (_assembly, CancellationToken.None);
		original.Save (_manifest);
		File.WriteAllText (Path.Combine (_assembly, name), "changed");
		File.Delete (_manifest);
		AndroidProducerInventory.Capture (_assembly, CancellationToken.None).Save (_manifest);
		Assert.Throws<InvalidDataException> (() => original.RequireUnchanged (_assembly, _manifest, CancellationToken.None));
		}
	[TestCase (true), TestCase (false)]
	public void MissingOrAdditionalDependencyIsRejected (bool missing)
		{
		var original = AndroidProducerInventory.Capture (_assembly, CancellationToken.None);
		original.Save (_manifest);
		if (missing)
			File.Delete (Path.Combine (_assembly, "dependency.dll"));
		else
			File.WriteAllText (Path.Combine (_assembly, "extra.dll"), "unlisted");
		Assert.Throws<InvalidDataException> (() => original.RequireUnchanged (_assembly, _manifest, CancellationToken.None));
		}
	[Test]
	public void OversizedFileIsRejectedBeforeHashing ()
		{
		using (var large = File.Create (Path.Combine (_assembly, "oversized.dll")))
			large.SetLength (32 * 1024 * 1024 + 1);
		Assert.Throws<InvalidDataException> (() => AndroidProducerInventory.Capture (_assembly, CancellationToken.None));
		}
	[Test]
	public void CancellationDoesNotProduceAnInventory ()
		{
		Assert.Throws<OperationCanceledException> (() => AndroidProducerInventory.Capture (_assembly, new CancellationToken (true)));
		Assert.That (File.Exists (_manifest), Is.False);
		}
	}