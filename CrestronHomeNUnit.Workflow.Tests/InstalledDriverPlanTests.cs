// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Net;
using System.Text.Json;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class InstalledDriverPlanTests
	{
	private string _root = null!;
	private InstalledDriverTestPlan _plan = null!;
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "installed-plan-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		var project = Path.Combine (_root, "Tests.csproj");
		File.WriteAllText (project, "<Project />");
		var adb = Path.Combine (_root, "fake-adb.exe");
		File.WriteAllText (adb, "never executed");
		var profile = Path.Combine (_root, "profile.json");
		File.WriteAllText (profile, JsonSerializer.Serialize (new AndroidSessionProfile (adb, "synthetic", "example.app", "Example Home", Path.Combine (_root, "android.lock"))));
		var package = Path.Combine (_root, "Example.pkg");
		File.WriteAllText (package, "synthetic package deliberately fails trusted hash before processor access");
		_plan = new ()
			{
			Host = "192.0.2.1", CertificateSha256 = new ('a', 64), SshFingerprint = "synthetic-only", PackagePath = package, PackageSha256 = new ('b', 64),
			SourceRoots = [_root], Target = new (42, -6, "Example", "Example Platform", 3, "1.2.3.0", "example.platform.ip.developer", "Developer", "tcpClient"),
			AndroidTests = new (project, profile)
			};
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);

	[Test]
	public void ValidPlanNeedsNoBuildDeploymentOrProcessorSuite () => Assert.DoesNotThrow (_plan.Validate);

	[TestCase ("hash")]
	[TestCase ("certificate")]
	[TestCase ("ssh")]
	[TestCase ("version")]
	[TestCase ("room")]
	[TestCase ("device")]
	[TestCase ("catalogue")]
	[TestCase ("source")]
	[TestCase ("timeout")]
	[TestCase ("wait")]
	[TestCase ("commit")]
	[TestCase ("selection")]
	public void MissingOrUnsafeInputsCannotStartWork (string fault)
		{
		var plan = fault switch
			{
			"hash" => _plan with { PackageSha256 = "computed-later" },
			"certificate" => _plan with { CertificateSha256 = "" },
			"ssh" => _plan with { SshFingerprint = "" },
			"version" => _plan with { Target = _plan.Target with { Version = "1.2.3" } },
			"room" => _plan with { Target = _plan.Target with { LocationId = 0 } },
			"device" => _plan with { Target = _plan.Target with { DeviceId = 0 } },
			"catalogue" => _plan with { Target = _plan.Target with { CatalogueId = "../other" } },
			"source" => _plan with { SourceRoots = [] },
			"timeout" => _plan with { TimeoutSeconds = 0 },
			"wait" => _plan with { LeaseWaitSeconds = -1 },
			"commit" => _plan with { PackageSourceCommit = "short" },
			_ => _plan with { AndroidTests = _plan.AndroidTests with { RequiredTests = [] } }
			};
		Assert.Throws<ArgumentException> (plan.Validate);
		Assert.That (File.Exists (Path.Combine (_root, "android.lock")), Is.False);
		}

	[Test]
	public void WrongCandidateHashStopsBeforeAnyReservationOrFixtureBuild ()
		{
		var results = Path.Combine (_root, "results");
		var exception = Assert.ThrowsAsync<InvalidDataException> (() => InstalledDriverTests.RunAsync (_plan, new NetworkCredential ("unused", "unused"), results));
		Assert.That (exception!.Message, Does.Contain ("trusted package receipt"));
		Assert.That (File.Exists (Path.Combine (results, "Phases.jsonl")), Is.False);
		Assert.That (Directory.Exists (Path.Combine (results, "AndroidUI")), Is.False);
		Assert.That (File.Exists (Path.Combine (_root, "android.lock")), Is.False);
		using var report = JsonDocument.Parse (File.ReadAllText (Path.Combine (results, "InstalledDriverTests.json")));
		Assert.That (report.RootElement.GetProperty ("State").GetString (), Is.EqualTo ("Failed"));
		}

	[Test]
	public void ExistingResultsCannotBeOverwritten ()
		{
		var results = Path.Combine (_root, "results");
		Directory.CreateDirectory (results);
		File.WriteAllText (Path.Combine (results, "retained.txt"), "previous evidence");
		Assert.ThrowsAsync<InvalidOperationException> (() => InstalledDriverTests.RunAsync (_plan, new NetworkCredential ("unused", "unused"), results));
		Assert.That (File.ReadAllText (Path.Combine (results, "retained.txt")), Is.EqualTo ("previous evidence"));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task CliRecognizesInstalledPhaseAndRefusesInvalidOrDeploymentInputs (bool deploymentInput)
		{
		var input = JsonSerializer.SerializeToNode (_plan)!.AsObject ();
		if (deploymentInput) input["actualDriver"] = new System.Text.Json.Nodes.JsonObject ();
		var path = Path.Combine (_root, "plan.json");
		File.WriteAllText (path, input.ToJsonString ());
		var results = Path.Combine (_root, "cli-results");
		var start = new ProcessStartInfo ("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add (Path.Combine (AppContext.BaseDirectory, "CrestronHomeNUnit.Cli.dll"));
		foreach (var argument in new[] { "installed-tests", "--plan", path, "--results", results }) start.ArgumentList.Add (argument);
		start.Environment["CRESTRON_HOME_USER"] = "synthetic";
		start.Environment["CRESTRON_HOME_PASSWORD"] = "synthetic";
		using var process = Process.Start (start)!;
		var output = process.StandardOutput.ReadToEndAsync ();
		var error = process.StandardError.ReadToEndAsync ();
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		try { await process.WaitForExitAsync (deadline.Token); }
		finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (); } }
		Assert.That (process.ExitCode, Is.EqualTo (2), await output + await error);
		Assert.That (File.Exists (Path.Combine (results, "Phases.jsonl")), Is.False);
		Assert.That (File.Exists (Path.Combine (results, "InstalledDriverTests.json")), Is.EqualTo (!deploymentInput));
		Assert.That (File.Exists (Path.Combine (_root, "android.lock")), Is.False);
		}
	}