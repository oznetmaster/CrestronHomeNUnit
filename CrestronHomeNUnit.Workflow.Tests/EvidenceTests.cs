// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeNUnit.Workflow;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class EvidenceTests
	{
	[TestCase (0, 2, 2, "Passed", true)]
	[TestCase (1, 2, 2, "Passed", false)]
	[TestCase (0, 2, 3, "Passed", false)]
	[TestCase (0, 0, 0, "Passed", false)]
	[TestCase (0, 2, 2, "NotExecuted", false)]
	public void TrxRequiresSuccessfulExecutionAndMatchingIndividualResults (int exit, int passed, int total, string outcome, bool gate)
		{
		var file = Path.GetTempFileName ();
		try
			{
			File.WriteAllText (file, $"<TestRun><ResultSummary><Counters total='{total}' passed='{passed}' failed='0'/></ResultSummary><Results>"
				 + string.Concat (Enumerable.Range (0, passed).Select (_ => $"<UnitTestResult outcome='{outcome}'/>")) + "</Results></TestRun>");
			Assert.That (WorkflowEvidence.ReadTrx (file, exit, 1).MeetsGate, Is.EqualTo (gate));
			}
		finally { File.Delete (file); }
		}

	[TestCase ("Passed", 0, 2, true)]
	[TestCase ("Failed", 0, 1, false)]
	[TestCase ("NotExecuted", 0, 1, false)]
	[TestCase ("Passed", 1, 2, false)]
	[TestCase ("Passed", 0, 3, false)]
	public void EveryTargetFrameworkMustPassAndContributeItsResults (string secondOutcome, int exitCode, int minimum, bool gate)
		{
		var directory = Path.Combine (Path.GetTempPath (), "multi-target-results-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (directory);
		try
			{
			void Result (string framework, string outcome) => File.WriteAllText (Path.Combine (directory, "TestResult_" + framework + ".trx"),
				$"<TestRun><ResultSummary><Counters total='1' passed='{(outcome == "Passed" ? 1 : 0)}' failed='{(outcome == "Failed" ? 1 : 0)}'/></ResultSummary><Results><UnitTestResult testName='SameTest' outcome='{outcome}'/></Results></TestRun>");
			Result ("net472", "Passed");
			Result ("net10.0", secondOutcome);
			var result = WorkflowEvidence.ReadLocalResults (directory, exitCode, minimum);
			Assert.That (result.Passed, Is.EqualTo (secondOutcome == "Passed" ? 2 : 1));
			Assert.That (result.MeetsGate, Is.EqualTo (gate));
			}
		finally
			{
			foreach (var path in Directory.EnumerateFiles (directory)) File.Delete (path);
			Directory.Delete (directory);
			}
		}

	[Test]
	public void PreviousResultFilesCannotBeCountedByAnotherRun ()
		{
		var directory = Path.Combine (Path.GetTempPath (), "previous-results-" + Guid.NewGuid ().ToString ("N"));
		WorkflowEvidence.PrepareLocalResults (directory);
		var file = Path.Combine (directory, "TestResult_net472.trx");
		try
			{
			File.WriteAllText (file, "original evidence");
			Assert.Throws<InvalidOperationException> (() => WorkflowEvidence.PrepareLocalResults (directory));
			Assert.That (File.ReadAllText (file), Is.EqualTo ("original evidence"));
			}
		finally
			{
			File.Delete (file);
			Directory.Delete (directory);
			}
		}

	[Test]
	public void MissingFrameworkResultsCannotPass ()
		{
		var directory = Path.Combine (Path.GetTempPath (), "missing-target-results-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (directory);
		try { Assert.That (WorkflowEvidence.ReadLocalResults (directory, 0, 1).MeetsGate, Is.False); }
		finally { Directory.Delete (directory); }
		}

	[TestCase (true, false, true, true, false, TestName = "UnconfirmedRemovalRetainsProcessorLease")]
	[TestCase (true, false, true, false, true, TestName = "ConfirmedRemovalAllowsLeaseRelease")]
	[TestCase (true, false, false, true, true, TestName = "IntentionallyRetainedIdleHostAllowsLeaseRelease")]
	[TestCase (false, false, false, true, false, TestName = "UnknownRemoteExecutionRetainsProcessorLease")]
	[TestCase (true, true, false, false, false, TestName = "UncertainActivationRetainsProcessorLease")]
	[TestCase (true, false, true, false, true, TestName = "FailureBeforeInstallationDoesNotRequireRemoval")]
	public void LeaseReleaseRequiresConfirmedProcessorState (bool stopped, bool uncertain, bool remove, bool hasInstance, bool expected)
		{
		Assert.That (WorkflowRunner.CanReleaseLease (stopped, uncertain, remove, hasInstance), Is.EqualTo (expected));
		}

	[Test]
	public void PropertyChecksRejectWrongJsonTypeAndOutOfRangeReadings ()
		{
		var check = new PropertyCheck ("temperature", 1, "sensor", "temperature", Minimum: -40, Maximum: 85);
		Assert.That (WorkflowRunner.CheckProperty (check, JsonSerializer.SerializeToElement (21.5)), Is.True);
		Assert.That (WorkflowRunner.CheckProperty (check, JsonSerializer.SerializeToElement ("21.5")), Is.False);
		Assert.That (WorkflowRunner.CheckProperty (check, JsonSerializer.SerializeToElement (100)), Is.False);
		var ready = new PropertyCheck ("ready", 1, "sensor", "ready", JsonSerializer.SerializeToElement (true));
		Assert.That (WorkflowRunner.CheckProperty (ready, JsonSerializer.SerializeToElement (false)), Is.False);
		Assert.That (WorkflowRunner.CheckProperty (ready, JsonSerializer.SerializeToElement ("true")), Is.False);
		}

	[Test]
	public async Task SourceIdentityIncludesDirtyAndUntrackedEditsButAllowsDebugCounter ()
		{
		var directory = Path.Combine (Path.GetTempPath (), "workflow-evidence-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (directory);
		try
			{
			await WorkflowEvidence.ProcessAsync ("git", ["init", "--quiet"], directory, Path.Combine (directory, "ignored.log"), default);
			File.WriteAllText (Path.Combine (directory, ".gitignore"), "*.log\nprivate.json\n");
			var manifest = Path.Combine (directory, "driver.json");
			File.WriteAllText (manifest, "{\"DriverVersion\": \"2.0.000.0001\", \"VersionDate\":\"old\", \"Model\":\"Original\"}");
			var original = await WorkflowEvidence.SourceDigestAsync ([directory], default);
			File.WriteAllText (manifest, "{\"DriverVersion\": \"2.0.000.0002\", \"VersionDate\":\"new\", \"Model\":\"Original\"}");
			Assert.That (await WorkflowEvidence.SourceDigestAsync ([directory], default), Is.EqualTo (original));
			File.WriteAllText (Path.Combine (directory, "private.json"), "private test setting");
			Assert.That (await WorkflowEvidence.SourceDigestAsync ([directory], default), Is.EqualTo (original));
			File.WriteAllText (Path.Combine (directory, "new.cs"), "// new untracked source");
			Assert.That (await WorkflowEvidence.SourceDigestAsync ([directory], default), Is.Not.EqualTo (original));
			File.Delete (Path.Combine (directory, "new.cs"));
			File.WriteAllText (manifest, "{\"DriverVersion\": \"2.0.001.0002\", \"VersionDate\":\"new\", \"Model\":\"Original\"}");
			Assert.That (await WorkflowEvidence.SourceDigestAsync ([directory], default), Is.Not.EqualTo (original));
			}
		finally
			{
			var resolved = Path.GetFullPath (directory);
			if (resolved.StartsWith (Path.GetFullPath (Path.GetTempPath ()), StringComparison.OrdinalIgnoreCase)
				 && Path.GetFileName (resolved).StartsWith ("workflow-evidence-", StringComparison.Ordinal))
				Directory.Delete (resolved, true);
			}
		}
	}