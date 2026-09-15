// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class ArtifactReuseTests
	{
	private string _root = null!, _manifest = null!, _package = null!;
	private PackageReceipt _receipt = null!;
	private const string Source = "source", Inputs = "inputs";
	private readonly string _run = Guid.NewGuid ().ToString ("N");
	private static readonly DriverPackageInfo Identity = new ("11111111-1111-1111-1111-111111111111", "Example", "Example", "1.2.3.8");

	[SetUp]
	public async Task Prepare ()
		{
		_root = Path.Combine (Path.GetTempPath (), "artifact-tests-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (Path.Combine (_root, "packages", "processor"));
		_manifest = Path.Combine (_root, "Example.json");
		await Write (_manifest, new
			{
			GeneralInformation = new
				{
				Guid = Identity.DriverId,
				BaseModel = Identity.Model,
				DriverVersion = "1.2.3.99"
				}
			});
		_package = Path.Combine (_root, "packages", "processor", "Example.pkg");
		using (var zip = ZipFile.Open (_package, ZipArchiveMode.Create))
			{
			using (var writer = new StreamWriter (zip.CreateEntry ("Example.dat").Open ()))
				writer.Write (JsonSerializer.Serialize (new
					{
					driverId = Identity.DriverId,
					baseModel = Identity.Model,
					manufacturer = Identity.Manufacturer,
					driverVersion = Identity.Version
					}));
			using var assembly = new StreamWriter (zip.CreateEntry ("Example.dll").Open ());
			assembly.Write ("Not executed by artifact inspection");
			}
		_receipt = new (Identity, "1.2.3.7", Convert.ToHexString (SHA256.HashData (await File.ReadAllBytesAsync (_package))), Source, Inputs);
		await Write (Path.Combine (_root, "processor-package.json"), _receipt);
		await Write (Path.Combine (_root, "BuildIdentity.json"), new
			{
			SourceSha256 = Source,
			Configuration = "Debug"
			});
		await Write (Path.Combine (_root, "Lease.json"), new
			{
			RunId = _run,
			State = "Released"
			});
		await WriteResult ("Passed", true);
		}
	[TearDown] public void Remove () => Directory.Delete (_root, true);
	private static Task Write (string path, object value) => File.WriteAllTextAsync (path, JsonSerializer.Serialize (value));
	private Task WriteResult (string outcome, bool complete) => Write (Path.Combine (_root, "Workflow.json"),
		new ProcessorWorkflowResult ([new ("Local", outcome, new (1, 0, 0, complete)), new ("Processor", "Passed", new (1, 0, 0, true))], false, false));
	private Task<ReusedArtifact?> Reuse (string source = Source, string? inputs = Inputs, DriverInfo[]? catalogue = null)
		=> WorkflowArtifacts.TryReuseAsync (_root, "processor", "Example.pkg", _manifest, source, inputs,
			(_, _) => Task.FromResult<IReadOnlyList<DriverInfo>> (catalogue ?? []), CancellationToken.None);

	[Test]
	public async Task VerifiedBytesAreCopiedWithoutChangingOriginalNameOrUsingResults ()
		{
		var retained = await Reuse ();
		Assert.That (retained!.RunId, Is.EqualTo (_run));
		var copy = Path.Combine (_root, "new-run", "packages", "processor", "Example.pkg");
		await WorkflowArtifacts.CopyVerifiedAsync (retained, copy, CancellationToken.None);
		Assert.That (File.ReadAllBytes (copy), Is.EqualTo (File.ReadAllBytes (_package)));
		Assert.That (Directory.GetFiles (Path.Combine (_root, "new-run"), "Workflow.json", SearchOption.AllDirectories), Is.Empty);
		}
	[TestCase ("Failed", true)]
	[TestCase ("Passed", false)]
	public async Task FailedOrIncompleteTestsCannotAuthorizeReuse (string outcome, bool complete)
		{
		await WriteResult (outcome, complete);
		Assert.ThrowsAsync<InvalidDataException> (async () => await Reuse ());
		}
	[TestCase ("Held")]
	[TestCase ("ReleaseUnconfirmed")]
	public async Task RetainedOrUncertainLeaseRefusesReuse (string state)
		{
		await Write (Path.Combine (_root, "Lease.json"), new
			{
			RunId = _run,
			State = state
			});
		Assert.ThrowsAsync<InvalidDataException> (async () => await Reuse ());
		}
	[Test]
	public async Task SourceOrToolchainChangesRequestAFreshBuild ()
		{
		Assert.That (await Reuse (source: "changed"), Is.Null);
		Assert.That (await Reuse (inputs: "changed"), Is.Null);
		Assert.That (await Reuse (inputs: null), Is.Null);
		}
	[Test]
	public async Task InconsistentSourceReceiptIsRejected ()
		{
		await Write (Path.Combine (_root, "processor-package.json"), _receipt with
			{
			SourceSha256 = "different"
			});
		Assert.ThrowsAsync<InvalidDataException> (async () => await Reuse ());
		}
	[TestCase ("1.002.003.0008")]
	[TestCase ("1.2.3.9")]
	[TestCase ("2.0.0.0")]
	public async Task CatalogueEqualityOrNewerVersionRequiresFreshRevision (string version)
		=> Assert.That (await Reuse (catalogue: [new () { Id = "catalogue", Model = " example ", Version = version }]), Is.Null);
	[Test]
	public async Task OlderAndUnrelatedCatalogueEntriesAllowReuse ()
		=> Assert.That (await Reuse (catalogue: [new () { Id = "catalogue", Model = "Example", Version = "1.2.3.7" }, new () { Id = "catalogue", Model = "Other" }]), Is.Not.Null);
	[Test]
	public void UnreadableMatchingCatalogueVersionFailsClosed ()
		=> Assert.ThrowsAsync<InvalidDataException> (async () => await Reuse (catalogue: [new () { Id = "catalogue", Model = "Example", Version = "unknown" }]));
	[Test]
	public async Task ChangedBytesAreRejectedBeforeAndAfterSelection ()
		{
		var retained = await Reuse ();
		await File.AppendAllTextAsync (_package, "corruption");
		Assert.ThrowsAsync<InvalidDataException> (async () => await Reuse ());
		Assert.ThrowsAsync<InvalidDataException> (() => WorkflowArtifacts.CopyVerifiedAsync (retained!, Path.Combine (_root, "new.pkg"), CancellationToken.None));
		Assert.That (File.Exists (Path.Combine (_root, "new.pkg")), Is.False);
		}
	[TestCase ("Other", "1.2.3.8")]
	[TestCase ("Example", "1.2.4.0")]
	public async Task WrongModelOrSourceReleaseCannotBeReused (string model, string version)
		{
		await Write (_manifest, new
			{
			GeneralInformation = new
				{
				Guid = Identity.DriverId,
				BaseModel = model,
				DriverVersion = version
				}
			});
		Assert.ThrowsAsync<InvalidDataException> (async () => await Reuse ());
		}
	[Test]
	public async Task ChangedPackageIdentityReceiptIsRejected ()
		{
		await Write (Path.Combine (_root, "processor-package.json"), _receipt with
			{
			Package = Identity with
				{
				DriverId = Guid.NewGuid ().ToString ()
				}
			});
		Assert.ThrowsAsync<InvalidDataException> (async () => await Reuse ());
		}
	[Test]
	public async Task OlderReceiptsAndCustomManifestLayoutsBuildNormally ()
		{
		await Write (Path.Combine (_root, "processor-package.json"), _receipt with
			{
			BuildInputsSha256 = null
			});
		Assert.That (await Reuse (), Is.Null);
		File.Delete (_manifest);
		Assert.That (await Reuse (), Is.Null);
		}
	[Test]
	public void MissingEvidenceCannotAuthorizeReuse ()
		{
		File.Delete (Path.Combine (_root, "Workflow.json"));
		Assert.ThrowsAsync<FileNotFoundException> (async () => await Reuse ());
		}
	[Test]
	public void ActualDriverRequiresItsOwnSuccessfulPostDeploymentChecks ()
		=> Assert.ThrowsAsync<InvalidDataException> (async () => await WorkflowArtifacts.TryReuseAsync (_root, "actual", "Example.pkg", _manifest,
			Source, Inputs, (_, _) => throw new AssertionException ("Must not contact processor"), CancellationToken.None));

	[Test]
	public async Task BuildIdentityIncludesSdkReferencedRestoreGraphsAndExternalInputs ()
		{
		var project = Path.Combine (_root, "root", "Root.csproj");
		var dependency = Path.Combine (_root, "dependency", "Dependency.csproj");
		async Task Restore (string path, string[] references, string marker)
			{
			var dir = Path.Combine (Path.GetDirectoryName (path)!, "obj");
			Directory.CreateDirectory (dir);
			await Write (Path.Combine (dir, "project.assets.json"), new
				{
				marker,
				project = new
					{
					restore = new
						{
						projectPath = path,
						frameworks = new
							{
							net472 = new
								{
								projectReferences = references.ToDictionary (r => r, r => new { projectPath = r })
								}
							}
						}
					}
				});
			}
		await Restore (project, [dependency], "initial");
		await Restore (dependency, [], "initial");
		var external = Path.Combine (_root, "sdk.dll");
		await File.WriteAllTextAsync (external, "initial");
		Task<string?> Digest (string sdk = "10.0.401") => WorkflowArtifacts.DigestInputsAsync (project, [external], sdk, CancellationToken.None);
		var original = await Digest ();
		Assert.That (await Digest (), Is.EqualTo (original));
		Assert.That (await Digest ("10.0.402"), Is.Not.EqualTo (original));
		await Restore (dependency, [], "changed dependency");
		var changed = await Digest ();
		Assert.That (changed, Is.Not.EqualTo (original));
		await File.WriteAllTextAsync (external, "different SDK assembly");
		Assert.That (await Digest (), Is.Not.EqualTo (changed));
		File.Delete (Path.Combine (_root, "dependency", "obj", "project.assets.json"));
		Assert.That (await Digest (), Is.Null);
		}

	[Test]
	public async Task WorkflowBuildUsesRetainedBytesAndNeverInvokesTheInvalidBuildProject ()
		{
		var source = Path.Combine (_root, "source");
		Directory.CreateDirectory (Path.Combine (source, "obj"));
		var project = Path.Combine (source, "Example.csproj");
		await File.WriteAllTextAsync (project, "This is deliberately not a buildable project.");
		File.Copy (_manifest, Path.ChangeExtension (project, ".json"));
		await Write (Path.Combine (source, "obj", "project.assets.json"), new
			{
			project = new
				{
				restore = new
					{
					projectPath = project,
					frameworks = new
						{
						net472 = new
							{
							projectReferences = new Dictionary<string, object> ()
							}
						}
					}
				}
			});
		using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (60));
		Assert.That (await WorkflowEvidence.ProcessAsync ("git", ["init", "--quiet"], source, Path.Combine (_root, "git.log"), timeout.Token), Is.Zero);
		var sourceHash = await WorkflowEvidence.SourceDigestAsync ([source], timeout.Token);
		var inputs = await WorkflowArtifacts.InputsDigestAsync (project, [], timeout.Token);
		await Write (Path.Combine (_root, "BuildIdentity.json"), new
			{
			SourceSha256 = sourceHash,
			Configuration = "Debug"
			});
		await Write (Path.Combine (_root, "processor-package.json"), _receipt with
			{
			SourceSha256 = sourceHash,
			BuildInputsSha256 = inputs
			});
		var target = new PackageBuildPlan (project, Path.Combine (source, "bin", "Example.pkg"), "Example tests", 1);
		var plan = new WorkflowPlan
			{
			Host = "unused.invalid",
			CertificateSha256 = "pin",
			SshFingerprint = "pin",
			SourceRoots = [source],
			LocalTests = [new (project, 1)],
			TestPackage = target,
			ProcessorSuites = [new ("unit", 1, [])],
			ArtifactReuse = new (_root, [])
			};
		var results = Path.Combine (_root, "new-results");
		Directory.CreateDirectory (results);
		await using var client = new ConfigurationClient (new CatalogueConnection ());
		var type = typeof (WorkflowRunner).GetNestedType ("Operations", BindingFlags.NonPublic)!;
		await using var operations = (IAsyncDisposable)Activator.CreateInstance (type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			null, [plan, new NetworkCredential (), results, client, null, null], null)!;
		type.GetField ("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (operations, sourceHash);
		var path = await (Task<string>)type.GetMethod ("Build", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (operations, [target, true, timeout.Token])!;
		Assert.That (File.ReadAllBytes (path), Is.EqualTo (File.ReadAllBytes (_package)));
		Assert.That (File.Exists (Path.Combine (results, "processor-build.log")), Is.False);
		var receipt = JsonSerializer.Deserialize<PackageReceipt> (File.ReadAllText (Path.Combine (results, "processor-package.json")))!;
		Assert.That (receipt.ReusedFromRunId, Is.EqualTo (_run));
		Assert.That (receipt.Sha256, Is.EqualTo (_receipt.Sha256));
		}

	private sealed class CatalogueConnection : IConfigurationConnection
		{
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Assert.That (command, Is.EqualTo ("cp.platformDriverController:getDrivers"));
			return Task.FromResult (JsonSerializer.Deserialize<T> ("[]"));
			}
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default) => throw new AssertionException ("No device request expected");
		public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new AssertionException ("No mutation expected");
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}