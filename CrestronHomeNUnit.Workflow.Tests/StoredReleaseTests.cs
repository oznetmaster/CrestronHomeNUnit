// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class StoredReleaseTests
	{
	private static readonly DriverPackageInfo Package = new ("22222222-2222-2222-2222-222222222222", "Example", "Maker", "2.1.2.0");
	private const string StoredPath = WorkflowPackageCleanup.Storage + "candidate.pkg";
	private static readonly byte[] Payload = [1, 2, 3, 4];
	private static string Hash => Convert.ToHexStringLower (SHA256.HashData (Payload));
	private static DriverInfo Driver (string version = "2.1.002.0000", string availability = "LocalByUser", string id = "catalogue") =>
	 new ()
		 {
		 Id = id,
		 Model = Package.Model,
		 Manufacturer = Package.Manufacturer,
		 Version = version,
		 AvailabilityState = availability
		 };
	private static byte[] Manifest (string path = StoredPath, string? guid = null, int count = 1, string model = "Example", string[]? aliases = null) =>
	 JsonSerializer.SerializeToUtf8Bytes (Enumerable.Range (0, count).Select (_ => new
		 {
		 DriverPackageId = guid ?? Package.DriverId,
		 DriverVersion = "2.1.002.0000",
		 ModelName = model,
		 Manufacturer = Package.Manufacturer,
		 SupportedModels = aliases ?? new[] { model },
		 LocalPath = path
		 }));

	[Test]
	public void ReuseRequiresExplicitOptIn () => Assert.That (new ReleaseCandidatePlan (Hash, Package.DriverId, Package.Version, "source", "commit").ReuseVerifiedStoredPackage, Is.False);

	[Test]
	public async Task ExactStoredCandidateIsVerifiedWithoutUploadingOrChangingItsVersion ()
		{
		var reads = new List<string> ();
		int catalogueReads = 0;
		var result = await WorkflowStoredRelease.VerifySnapshotAsync (Package, Hash, (path, limit, _) =>
		{
			reads.Add (path);
			byte[] data = path == StoredPath ? Payload : Manifest ();
			Assert.That (data.Length, Is.LessThanOrEqualTo (limit));
			return Task.FromResult (data);
		}, _ => { catalogueReads++; return Task.FromResult<IReadOnlyList<DriverInfo>> ([Driver ()]); }, _ => Task.FromResult<IReadOnlyList<DeviceInfo>> ([]), CancellationToken.None);
		Assert.That (result.Path, Is.EqualTo (StoredPath));
		Assert.That (result.Sha256, Is.EqualTo (Hash));
		Assert.That (result.CatalogueId, Is.EqualTo ("catalogue"));
		Assert.That (catalogueReads, Is.EqualTo (2));
		Assert.That (reads, Is.EqualTo (new[] { WorkflowStoredRelease.Manifest, StoredPath, WorkflowStoredRelease.Manifest }));
		}

	[TestCase ("2.1.3.0", "LocalByUser")]
	[TestCase ("2.1.2.0", "Available")]
	[TestCase ("2.1.2.0", "Unknown")]
	[TestCase ("2.1.1.0", "LocalByUser")]
	public void NonExactOrNonLocalCatalogueIsRejected (string version, string availability) =>
	 Assert.Throws<InvalidDataException> (() => WorkflowStoredRelease.SelectCatalogue (Package, [Driver (version, availability)]));

	[Test]
	public void AmbiguousCatalogueIsRejected () => Assert.Throws<InvalidDataException> (() => WorkflowStoredRelease.SelectCatalogue (Package, [Driver (), Driver (id: "another")]));

	[TestCase ("/other/candidate.pkg")]
	[TestCase (WorkflowPackageCleanup.Storage + "../candidate.pkg")]
	[TestCase (WorkflowPackageCleanup.Storage + "sub/candidate.pkg")]
	public void UnsafeStoragePathIsRejected (string path) => Assert.Throws<InvalidDataException> (() => WorkflowStoredRelease.SelectPath (Manifest (path), Package));

	[TestCase (0)]
	[TestCase (2)]
	public void MissingOrAmbiguousManifestIsRejected (int count) => Assert.Throws<InvalidDataException> (() => WorkflowStoredRelease.SelectPath (Manifest (count: count), Package));

	[Test]
	public void SameModelAndVersionWithDifferentGuidIsRejected () => Assert.Throws<InvalidDataException> (() => WorkflowStoredRelease.SelectPath (Manifest (guid: Guid.NewGuid ().ToString ()), Package));

	[Test]
	public void ManifestModelMismatchIsRejected () => Assert.Throws<InvalidDataException> (() => WorkflowStoredRelease.SelectPath (Manifest (model: "Other"), Package));

	[TestCase ("Example")]
	[TestCase (" alias ")]
	public void ExistingInstanceOrAliasCannotBeMistakenForVerifiedLoadedBytes (string model) =>
	 Assert.Throws<InvalidDataException> (() => WorkflowStoredRelease.VerifyUnused (Manifest (aliases: ["Example", "Alias"]), Package, [new DeviceInfo { Id = 17, Model = model }]));

	[Test]
	public async Task InstanceAppearingDuringVerificationIsRejected ()
		{
		int reads = 0;
		await Assert.ThrowsAsync<InvalidDataException> (async () => await WorkflowStoredRelease.VerifySnapshotAsync (Package, Hash,
		 (path, _, _) => Task.FromResult (path == StoredPath ? Payload : Manifest ()),
		 _ => Task.FromResult<IReadOnlyList<DriverInfo>> ([Driver ()]),
		 _ => Task.FromResult<IReadOnlyList<DeviceInfo>> (++reads == 1 ? [] : [new DeviceInfo { Id = 17, Model = Package.Model }]), CancellationToken.None));
		}

	[TestCase ("bytes")]
	[TestCase ("manifest")]
	[TestCase ("catalogue")]
	public async Task ChangedBytesOrSnapshotCannotBeReused (string change)
		{
		int manifestReads = 0, catalogueReads = 0;
		await Assert.ThrowsAsync<InvalidDataException> (async () => await WorkflowStoredRelease.VerifySnapshotAsync (Package, Hash, (path, _, _) =>
		{
			if (path == StoredPath)
				return Task.FromResult (change == "bytes" ? new byte[] { 9 } : Payload);
			manifestReads++;
			return Task.FromResult (change == "manifest" && manifestReads == 2 ? Manifest (path: WorkflowPackageCleanup.Storage + "changed.pkg") : Manifest ());
		}, _ => { catalogueReads++; return Task.FromResult<IReadOnlyList<DriverInfo>> ([Driver (id: change == "catalogue" && catalogueReads == 2 ? "changed" : "catalogue")]); }, _ => Task.FromResult<IReadOnlyList<DeviceInfo>> ([]), CancellationToken.None));
		}
	}