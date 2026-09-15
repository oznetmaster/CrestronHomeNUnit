// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class PackageCleanupTests
	{
	private static readonly DriverPackageInfo Package = new ("d695856e-fb11-491f-8e3c-684f2b62b6dd", "Test package", "Example", "1.2.3.4");
	private static byte[] Manifest (string path, string? id = null, string version = "1.002.003.0004", string[]? aliases = null, int copies = 1)
		=> JsonSerializer.SerializeToUtf8Bytes (Enumerable.Range (0, copies).Select (_ => new
			{
			DriverPackageId = id ?? Package.DriverId, DriverVersion = version, LocalPath = path, SupportedModels = aliases ?? ["Test alias"]
			}));
	private const string Path = WorkflowPackageCleanup.Storage + "Tests.pkg";
	[Test]
	public void NewlyUploadedUninstalledPackageIsEligible ()
		{
		var candidate = WorkflowPackageCleanup.SelectCandidate (Manifest (Path), Package, new HashSet<string> (), ["Unrelated driver"]);
		Assert.That (candidate.Path, Is.EqualTo (Path));
		Assert.That (candidate.Protected, Is.False);
		Assert.That (candidate.Models, Does.Contain ("Test alias"));
		}
	[Test]
	public void PreExistingManualPathIsAlwaysPreserved ()
		{
		var candidate = WorkflowPackageCleanup.SelectCandidate (Manifest (Path), Package, new HashSet<string> { Path }, [Package.Model]);
		Assert.That (candidate.Protected, Is.True);
		}
	[TestCase ("Test package")]
	[TestCase ("test ALIAS")]
	[TestCase (" Test alias ")]
	public void InstalledModelOrAliasPreventsRemoval (string model)
		=> Assert.Throws<InvalidOperationException> (() => WorkflowPackageCleanup.SelectCandidate (Manifest (Path), Package, new HashSet<string> (), [model]));
	[TestCase ("/other/Tests.pkg")]
	[TestCase (WorkflowPackageCleanup.Storage + "../Tests.pkg")]
	[TestCase (WorkflowPackageCleanup.Storage + "sub/Tests.pkg")]
	[TestCase (WorkflowPackageCleanup.Storage + "sub\\Tests.pkg")]
	[TestCase (WorkflowPackageCleanup.Storage + ".pkg")]
	[TestCase (WorkflowPackageCleanup.Storage + "Tests.dll")]
	public void PathsOutsideImmediatePackageStorageAreRejected (string path)
		=> Assert.Throws<InvalidDataException> (() => WorkflowPackageCleanup.ValidatePath (path));
	[TestCase (0)]
	[TestCase (2)]
	public void MissingOrAmbiguousCatalogueIdentityIsRejected (int count)
		=> Assert.Throws<InvalidDataException> (() => WorkflowPackageCleanup.SelectCandidate (Manifest (Path, copies: count), Package, new HashSet<string> (), []));
	[Test]
	public void SameModelDifferentGuidIsNotOwnershipEvidence ()
		=> Assert.Throws<InvalidDataException> (() => WorkflowPackageCleanup.SelectCandidate (Manifest (Path, id: Guid.NewGuid ().ToString ()), Package, new HashSet<string> (), []));
	[Test]
	public void DifferentPackageVersionIsNotOwnershipEvidence ()
		=> Assert.Throws<InvalidDataException> (() => WorkflowPackageCleanup.SelectCandidate (Manifest (Path, version: "1.2.3.5"), Package, new HashSet<string> (), []));
	[Test]
	public void UnknownAliasFailsClosed ()
		=> Assert.Throws<InvalidDataException> (() => WorkflowPackageCleanup.SelectCandidate (Manifest (Path, aliases: [""]), Package, new HashSet<string> (), []));
	[Test]
	public void ChangedStoredBytesPreventRemoval ()
		=> Assert.Throws<InvalidDataException> (() => WorkflowPackageCleanup.VerifyHash ([1, 2, 3], [1, 2, 4]));
	[Test]
	public void ExactRetainedBytesAreAccepted () => WorkflowPackageCleanup.VerifyHash ([1, 2, 3], [1, 2, 3]);
	[TestCase (false, true)]
	[TestCase (true, false)]
	public async Task ManualOrFailedRunsNeverInvokePackageCleanup (bool enabled, bool passed)
		{
		int calls = 0;
		var initial = new ProcessorWorkflowResult ([new ("Processor", passed ? "Passed" : "Failed")], false, false);
		var result = await WorkflowPackageCleanup.AfterSuccessfulRunAsync (initial, enabled, () =>
			{
			calls++;
			return Task.FromResult (new WorkflowPackageCleanup.Result (true, true, 10, "Removed"));
			}, _ => { });
		Assert.That (calls, Is.Zero);
		Assert.That (result, Is.SameAs (initial));
		}
	[Test]
	public async Task FailedCleanupRetainsPassedTestResultsButFailsWorkflow ()
		{
		var initial = new ProcessorWorkflowResult ([new ("Processor", "Passed")], false, false);
		var result = await WorkflowPackageCleanup.AfterSuccessfulRunAsync (initial, true,
			 () => throw new IOException ("private diagnostic must not escape"), _ => { });
		Assert.That (result.Passed, Is.False);
		Assert.That (result.Stages[0].Outcome, Is.EqualTo ("Passed"));
		Assert.That (result.Stages[1].Detail, Does.Not.Contain ("private diagnostic"));
		}
	[Test]
	public async Task CachedCatalogueEntryDoesNotRequireRebootOrFailSuccessfulStorageCleanup ()
		{
		var initial = new ProcessorWorkflowResult ([new ("Processor", "Passed")], false, false);
		var result = await WorkflowPackageCleanup.AfterSuccessfulRunAsync (initial, true,
			 () => Task.FromResult (new WorkflowPackageCleanup.Result (true, true, 10, "Cache remains until planned reboot")), _ => { });
		Assert.That (result.Passed, Is.True);
		Assert.That (result.Stages[^1].Detail, Does.Contain ("planned reboot"));
		}
	[Test]
	public async Task CleanupReportingFailureFailsClosed ()
		{
		var initial = new ProcessorWorkflowResult ([new ("Processor", "Passed")], false, false);
		var result = await WorkflowPackageCleanup.AfterSuccessfulRunAsync (initial, true,
			 () => Task.FromResult (new WorkflowPackageCleanup.Result (true, false, 10, "Removed")), _ => throw new IOException ());
		Assert.That (result.Passed, Is.False);
		}
	private static WorkflowPlan Plan () => new ()
		{
		Host = "original", CertificateSha256 = "pinned-certificate", SshFingerprint = "pinned-ssh",
		SourceRoots = ["source"], LocalTests = [new ("project", 1)],
		TestPackage = new ("project", "package.pkg", "tests", 1), ProcessorSuites = [new ("unit", 1, [])]
		};
	[Test]
	public void PackageCleanupRequiresInstanceRemoval ()
		{
		var plan = Plan () with { RemoveTestPackageAfterSuccessfulRun = true };
		Assert.That (() => plan.Validate (), Throws.ArgumentException.With.Message.Contains ("test-instance removal"));
		Assert.That (Plan ().RemoveTestPackageAfterSuccessfulRun, Is.False, "Manual workflows retain packages unless cleanup is explicitly enabled.");
		}
	[TestCase (30)]
	[TestCase (120)]
	[TestCase (900)]
	public void ManagementRequestsUseTheConfiguredStageDeadline (int seconds)
		{
		var plan = Plan () with { StageTimeoutSeconds = seconds };
		var connection = WorkflowRunner.ConnectionOptions (plan, "resolved-host");
		Assert.That (connection.RequestTimeout, Is.EqualTo (TimeSpan.FromSeconds (seconds)));
		Assert.That (connection.Host, Is.EqualTo ("resolved-host"));
		Assert.That (connection.CertificateSha256, Is.EqualTo (plan.CertificateSha256));
		}
	}