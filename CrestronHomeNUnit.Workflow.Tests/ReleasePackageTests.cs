// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class ReleasePackageTests
	{
	private string _root = null!, _repo = null!, _project = null!, _package = null!;
	private ReleaseCandidatePlan _plan = null!;
	private PackageBuildPlan _actual = null!;
	private const string DriverGuid = "11111111-1111-1111-1111-111111111111";

	[SetUp]
	public async Task Prepare ()
		{
		_root = Path.Combine (Path.GetTempPath (), "release-package-" + Guid.NewGuid ().ToString ("N"));
		_repo = Path.Combine (_root, "source");
		Directory.CreateDirectory (_repo);
		_project = Path.Combine (_repo, "Example.csproj");
		// Deliberately not buildable: retaining a release must never invoke its project.
		await File.WriteAllTextAsync (_project, "This pinned project must not be built to retain the package.");
		await Git ("init", "--quiet");
		await Git ("add", "Example.csproj");
		await Git ("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Fixture source");
		_package = Path.Combine (_root, "NeilColvin_Example_IP.pkg");
		using (var zip = ZipFile.Open (_package, ZipArchiveMode.Create))
			{
			using (var writer = new StreamWriter (zip.CreateEntry ("NeilColvin_Example_IP.dat").Open ()))
				writer.Write (JsonSerializer.Serialize (new { driverId = DriverGuid, baseModel = "Example", manufacturer = "Example", driverVersion = "1.2.3.0" }));
			using var writer2 = new StreamWriter (zip.CreateEntry ("NeilColvin_Example_IP.dll").Open ());
			writer2.Write ("Structural identity fixture; never executed.");
			}
		_plan = new (Convert.ToHexString (SHA256.HashData (await File.ReadAllBytesAsync (_package))), DriverGuid, "1.2.3.0", _repo, await Git ("rev-parse", "HEAD"));
		_actual = new (_project, _package, "Example", 1);
		}

	[TearDown]
	public void Remove ()
		{
		var expected = Path.TrimEndingDirectorySeparator (Path.GetFullPath (Path.GetTempPath ())) + Path.DirectorySeparatorChar;
		if (!Path.GetFullPath (_root).StartsWith (expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName (_root).StartsWith ("release-package-", StringComparison.Ordinal))
			throw new InvalidOperationException ("Refusing cleanup outside the test's temporary directory.");
		// Git creates read-only object files on Windows.
		foreach (var path in Directory.EnumerateFiles (_root, "*", SearchOption.AllDirectories)) File.SetAttributes (path, FileAttributes.Normal);
		Directory.Delete (_root, recursive: true);
		}

	private async Task<string> Git (params string[] args)
		{
		var start = new ProcessStartInfo ("git") { WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var argument in args) start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		var output = process.StandardOutput.ReadToEndAsync ();
		var error = process.StandardError.ReadToEndAsync ();
		await process.WaitForExitAsync ();
		Assert.That (process.ExitCode, Is.Zero, await error);
		return (await output).Trim ();
		}

	private Task<WorkflowReleasePackage> Retain (ReleaseCandidatePlan? plan = null, string name = "results") =>
		WorkflowReleasePackage.PrepareAsync (plan ?? _plan, _actual, Path.Combine (_root, name), CancellationToken.None);

	[Test]
	public async Task RetainsExactReleaseBytesNameVersionAndCommitWithoutBuilding ()
		{
		var original = await File.ReadAllBytesAsync (_package);
		using var retained = await Retain ();
		Assert.That (File.ReadAllBytes (retained.Path), Is.EqualTo (original));
		Assert.That (Path.GetFileName (retained.Path), Is.EqualTo (Path.GetFileName (_package)));
		Assert.That (retained.Identity.Version, Is.EqualTo ("1.2.3.0"));
		Assert.That (retained.SourceCommit, Is.EqualTo (_plan.SourceCommit));
		File.WriteAllText (_package, "Original download changed after the copy");
		Assert.That (File.ReadAllBytes (retained.Path), Is.EqualTo (original));
		await retained.VerifySourceCommitAsync (CancellationToken.None);
		}

	[Test]
	public void ChangedPackageBytesCannotPassTheTrustedHash () =>
		Assert.ThrowsAsync<InvalidDataException> (async () => { using var retained = await Retain (_plan with { Sha256 = new ('0', 64) }); });

	[Test]
	public void WrongReleaseGuidIsRejected () =>
		Assert.ThrowsAsync<InvalidDataException> (async () => { using var retained = await Retain (_plan with { DriverGuid = Guid.NewGuid ().ToString () }); });

	[Test]
	public void WrongReleaseVersionIsRejected () =>
		Assert.ThrowsAsync<InvalidDataException> (async () => { using var retained = await Retain (_plan with { DriverVersion = "1.2.4.0" }); });

	[Test]
	public void DebugRevisionCannotBeDeclaredAsRelease () =>
		Assert.Throws<ArgumentException> (() => (_plan with { DriverVersion = "1.2.3.7" }).Validate (_actual));

	[Test]
	public void DifferentSourceCommitIsRejected () =>
		Assert.ThrowsAsync<InvalidDataException> (async () => { using var retained = await Retain (_plan with { SourceCommit = new ('a', 40) }); });

	[TestCase (true)]
	[TestCase (false)]
	public void DirtyOrUntrackedSourceCannotClaimTheReleaseCommit (bool tracked)
		{
		File.WriteAllText (tracked ? _project : Path.Combine (_repo, "Additional.cs"), "Changed source");
		Assert.ThrowsAsync<InvalidDataException> (async () => { using var retained = await Retain (); });
		}

	[Test]
	public async Task CommitChangeAfterPreparationIsRejected ()
		{
		using var retained = await Retain ();
		await Git ("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "--allow-empty", "-m", "Changed commit");
		Assert.ThrowsAsync<InvalidDataException> (() => retained.VerifySourceCommitAsync (CancellationToken.None));
		}

	[Test]
	public async Task WorkingTreeEditBetweenPreparationAndLocalTestsIsRejected ()
		{
		using var retained = await Retain ();
		File.AppendAllText (_project, "Changed between stages");
		Assert.ThrowsAsync<InvalidDataException> (() => retained.VerifyPristineSourceAsync (CancellationToken.None));
		}

	[Test]
	public async Task RepeatingPreparationCannotOverwriteEvidence ()
		{
		using var retained = await Retain ();
		Assert.ThrowsAsync<IOException> (async () => { using var second = await Retain (); });
		using var anotherRun = await Retain (name: "another-run");
		Assert.That (File.ReadAllBytes (anotherRun.Path), Is.EqualTo (File.ReadAllBytes (retained.Path)));
		}

	[Test]
	public async Task WorkflowUsesRetainedCandidateWithoutBuildOrCatalogueVersionReconciliation ()
		{
		using var retained = await Retain ();
		var source = await WorkflowEvidence.SourceDigestAsync ([_repo], CancellationToken.None);
		var results = Path.Combine (_root, "results");
		var plan = new WorkflowPlan
			{
			Host = "unused.invalid", CertificateSha256 = "unused", SshFingerprint = "unused", SourceRoots = [_repo],
			ActualDriver = _actual, TestPackage = new (_project, Path.Combine (_root, "Tests.pkg"), "Tests", 1),
			LocalTests = [new (_project, 1)], ProcessorSuites = [new ("unit", 1, [])], ReleaseCandidate = _plan
			};
		await using var client = new ConfigurationClient (new NoProcessorConnection ());
		var type = typeof (WorkflowRunner).GetNestedType ("Operations", BindingFlags.NonPublic)!;
		await using var operations = (IAsyncDisposable)Activator.CreateInstance (type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			null, [plan, new NetworkCredential (), results, client, null, null, null, retained], null)!;
		type.GetField ("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (operations, source);
		var path = await (Task<string>)type.GetMethod ("PrepareActualPackageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (operations, [CancellationToken.None])!;
		Assert.That (path, Is.EqualTo (retained.Path));
		Assert.That (File.Exists (Path.Combine (results, "actual-build.log")), Is.False);
		var receipt = JsonSerializer.Deserialize<PackageReceipt> (File.ReadAllText (Path.Combine (results, "actual-package.json")))!;
		Assert.That (receipt.ReleaseSourceCommit, Is.EqualTo (_plan.SourceCommit));
		Assert.That (receipt.Sha256, Is.EqualTo (_plan.Sha256));
		Assert.That (receipt.DebugRevisionBaseline, Is.Null);
		}

	private sealed class NoProcessorConnection : IConfigurationConnection
		{
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertionException ("Candidate preparation must not query or mutate the processor.");
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default) => throw new AssertionException ("No processor request permitted.");
		public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new AssertionException ("No processor operation permitted.");
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}

	[Test]
	public void AProjectOutsideReleaseRepositoryIsRejected () =>
		Assert.Throws<ArgumentException> (() => _plan.Validate (_actual with { Project = Path.Combine (_root, "elsewhere", "Example.csproj") }));
	}