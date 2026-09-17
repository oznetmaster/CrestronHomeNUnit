// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class AndroidStageTests
	{
	private string _root = null!, _package = null!, _project = null!, _owner = null!;
	private AndroidSessionProfile _profile = null!;

	[SetUp]
	public void Prepare ()
		{
		_root = Path.Combine (Path.GetTempPath (), "android-stage-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_project = Path.Combine (_root, "UiTests.csproj");
		File.WriteAllText (_project, "Not built: this test substitutes only the child process boundary.");
		_owner = Guid.NewGuid ().ToString ("N");
		_profile = new (Environment.ProcessPath!, "fixture-serial", "example.app", "Example Home", Path.Combine (_root, "android.lease"));
		_package = Path.Combine (_root, "Example.pkg");
		using var zip = ZipFile.Open (_package, ZipArchiveMode.Create);
		using (var writer = new StreamWriter (zip.CreateEntry ("Example.dat").Open ()))
			writer.Write ("{\"driverId\":\"11111111-1111-1111-1111-111111111111\",\"baseModel\":\"Example\",\"manufacturer\":\"Example\",\"driverVersion\":\"1.2.3.8\"}");
		using var assembly = new StreamWriter (zip.CreateEntry ("Example.dll").Open ());
		assembly.Write ("Not executable: package identity inspection only.");
		}

	[TearDown]
	public void Remove () => Directory.Delete (_root, recursive: true);

	[TestCase ("valid", 0, true, true)]
	[TestCase ("missing", 0, true, false)]
	[TestCase ("wrong-package", 0, true, false)]
	[TestCase ("unrestored", 0, true, false)]
	[TestCase ("valid", 1, false, true)]
	public async Task StageBindsChildToRetainedPackageAndRequiresBothTestAndRestorationResults (string completionMode, int exit, bool passes, bool restored)
		{
		using var lease = AndroidSessionLease.Acquire (_profile.LockPath, _owner);
		var output = Path.Combine (_root, "results");
		var outcome = await WorkflowAndroid.RunAsync (new (_project, "unused-by-stage"), _profile, _owner, "192.0.2.1", 7, _package,
			new ('B', 64), output, CancellationToken.None, async (executable, arguments, directory, log, token, environment) =>
				{
				Assert.That (executable, Is.EqualTo ("dotnet"));
				Assert.That (arguments, Does.Contain (_project).And.Contain ("-p:DeployAfterBuild=false").And.Contain ("-p:BuildForTests=true"));
				Assert.That (directory, Is.EqualTo (_root));
				if (arguments.Contains ("--list-tests"))
					{
					Assert.That (environment![AndroidWorkflowSession.CONTEXT_VARIABLE], Is.Empty, "Discovery must not receive an Android session.");
					var dumpDirectory = Path.Combine (output, "assembly", "Dump");
					Directory.CreateDirectory (dumpDirectory);
					await File.WriteAllTextAsync (Path.Combine (dumpDirectory, "D_Example.dll.dump"),
						"<NUnitXml><test-run runstate=\"Runnable\" testcasecount=\"1\"><test-case id=\"1\" fullname=\"Example.Home\" runstate=\"Runnable\" /></test-run></NUnitXml>", token);
					return 0;
					}
				Assert.That (arguments, Does.Contain ("--no-build").And.Contain ("--no-restore"));
				var context = AndroidWorkflowSession.Read<AndroidRunContext> (environment![AndroidWorkflowSession.CONTEXT_VARIABLE]);
				AndroidWorkflowSession.VerifyContext (context);
				Assert.That (context.ProcessorAddress, Is.EqualTo ("192.0.2.1"));
				Assert.That (context.InstalledDriverId, Is.EqualTo (7));
				Assert.That (context.RequireManagedDevice ("child").DeviceId, Is.EqualTo (19));
				Assert.That (context.DriverVersion, Is.EqualTo ("1.2.3.8"));
				Assert.That (context.PackageSha256, Is.EqualTo (Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (_package)))));
				Assert.That (context.SourceSha256, Is.EqualTo (new string ('B', 64)));
				Assert.That (context.ReleaseSourceCommit, Is.EqualTo (new string ('d', 40)));
				Assert.That (context.EvidenceDirectory, Is.EqualTo (output));
				if (completionMode != "missing")
					await File.WriteAllTextAsync (Path.Combine (output, "completion.json"), JsonSerializer.Serialize (new AndroidRunCompletion
						(1, context.RunId, completionMode == "wrong-package" ? new ('C', 64) : context.PackageSha256, completionMode != "unrestored")), token);
				await File.WriteAllTextAsync (Path.Combine (output, "TestResult.trx"),
					"<TestRun><ResultSummary outcome=\"Completed\"><Counters total=\"1\" passed=\"1\" failed=\"0\" /></ResultSummary><Results><UnitTestResult testId=\"1\" executionId=\"unique\" outcome=\"Passed\" /></Results><TestDefinitions><UnitTest id=\"1\" name=\"Home\"><TestMethod className=\"Example\" adapterTypeName=\"executor://nunit3testexecutor/\" /></UnitTest></TestDefinitions></TestRun>", token);
				return exit;
				}, releaseSourceCommit: new ('d', 40), managedDevices: [new ("child", 19, 7, "Example Child", "CI Child", 3)]);
		Assert.That (outcome.Tests.MeetsGate, Is.EqualTo (passes));
		Assert.That (outcome.RestorationConfirmed, Is.EqualTo (restored));
		Assert.That (File.Exists (_profile.LockPath), Is.True, "Only the coordinator may release after all workflow cleanup.");
		}

	[Test]
	public void EmptyManagedBindingsKeepTheLegacyContextWireContract ()
		{
		var context = new AndroidRunContext (1, _owner, Environment.MachineName, 1, 1,
			"192.0.2.1", 7, "11111111-1111-1111-1111-111111111111", "1.0.0.1", new ('a', 64), new ('b', 64), _profile, _root);
		using var legacy = JsonDocument.Parse (WorkflowAndroid.SerializeContext (context));
		Assert.That (legacy.RootElement.TryGetProperty ("ManagedDevices", out _), Is.False,
			"Old UI fixture packages reject this unknown field, even when it is empty.");
		using var managed = JsonDocument.Parse (WorkflowAndroid.SerializeContext (context with
			{ ManagedDevices = [new ("room", 19, 7, "Example Child", "CI Child", 3)] }));
		Assert.That (managed.RootElement.GetProperty ("ManagedDevices")[0].GetProperty ("DeviceId").GetInt32 (), Is.EqualTo (19));
		}

	[Test]
	public void FailedDiscoveryCannotStartExecution ()
		{
		using var lease = AndroidSessionLease.Acquire (_profile.LockPath, _owner);
		int calls = 0;
		Assert.ThrowsAsync<InvalidDataException> (() => WorkflowAndroid.RunAsync (new (_project, "unused"), _profile, _owner,
			"192.0.2.1", 7, _package, new ('B', 64), Path.Combine (_root, "failed-discovery"), CancellationToken.None,
			(_, arguments, _, _, _, environment) =>
				{
				calls++;
				Assert.That (arguments, Does.Contain ("--list-tests"));
				Assert.That (environment![AndroidWorkflowSession.CONTEXT_VARIABLE], Is.Empty);
				return Task.FromResult (1);
				}));
		Assert.That (calls, Is.EqualTo (1));
		Assert.That (File.Exists (_profile.LockPath), Is.True);
		}

	[Test]
	public async Task RealNUnitDiscoveryAndExecutionMatchWithoutAndroidAccess ()
		{
		var repository = new DirectoryInfo (TestContext.CurrentContext.TestDirectory);
		while (repository != null && !File.Exists (Path.Combine (repository.FullName, "CrestronHomeNUnit.sln"))) repository = repository.Parent;
		Assert.That (repository, Is.Not.Null, "This source acceptance test requires the repository checkout.");
		var project = Path.Combine (repository!.FullName, "CrestronHomeNUnit.Android.Tests", "CrestronHomeNUnit.Android.Tests.csproj");
		using var lease = AndroidSessionLease.Acquire (_profile.LockPath, _owner);
		using var deadline = new CancellationTokenSource (TimeSpan.FromMinutes (3));
		var output = Path.Combine (_root, "real-results");
		// Android.Tests uses only fake transports. It never opens a workflow UI session or sends ADB input.
		var result = await WorkflowAndroid.RunAsync (new (project, "unused"), _profile, _owner, "192.0.2.1", 7, _package, new ('B', 64), output, deadline.Token);
		Assert.That (result.Tests.MeetsGate, Is.True, "The real adapter's complete discovery and results must agree.");
		Assert.That (result.Tests.Passed, Is.EqualTo (AndroidTestCoverage.ReadDiscovery (Path.Combine (output, "discovery.dump")).Length));
		Assert.That (result.RestorationConfirmed, Is.False, "The offline suite produces no Android session completion.");
		Assert.That (File.Exists (Path.Combine (output, "coverage.json")), Is.True);
		}

	[Test]
	public void InterruptedChildRetainsReservationAndCannotCreateCompletion ()
		{
		using var lease = AndroidSessionLease.Acquire (_profile.LockPath, _owner);
		var output = Path.Combine (_root, "interrupted");
		Assert.ThrowsAsync<OperationCanceledException> (() => WorkflowAndroid.RunAsync (new (_project, "unused"), _profile, _owner,
			"192.0.2.1", 7, _package, new ('B', 64), output, CancellationToken.None,
			(_, _, _, _, _, _) => throw new OperationCanceledException ()));
		Assert.That (File.Exists (_profile.LockPath), Is.True);
		Assert.That (File.Exists (Path.Combine (output, "completion.json")), Is.False);
		}
	}