// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class RollbackTests
	{
	private static ProcessorWorkflowResult Failed => new ([new ("Local", "Passed"), new ("Processor", "Passed"),
		new ("Update actual driver", "Passed"), new ("Deployed driver live", "Failed")], true, true);
	[Test]
	public void OnlyCompletedFailedInstalledChecksWithKnownStoppedExecutionPermitRollback ()
		{
		Assert.That (WorkflowRollback.ShouldAttempt (Failed, true, true), Is.True);
		Assert.That (WorkflowRollback.ShouldAttempt (Failed, false, true), Is.False);
		Assert.That (WorkflowRollback.ShouldAttempt (Failed, true, false), Is.False);
		Assert.That (WorkflowRollback.ShouldAttempt (Failed with
			{
			DriverUpdateVerified = false
			}, true, true), Is.False);
		Assert.That (WorkflowRollback.ShouldAttempt (Failed with
			{
			DriverUpdateAttempted = false
			}, true, true), Is.False);
		}
	[TestCase ("Passed")]
	[TestCase ("Error")]
	[TestCase ("Cancelled")]
	public void SuccessCancellationAndUncertainFailureNeverTriggerRollback (string outcome)
		=> Assert.That (WorkflowRollback.ShouldAttempt (new ([new ("Deployed driver live", outcome)], true, true), true, true), Is.False);
	[TestCase ("Error")]
	[TestCase ("Deferred")]
	[TestCase ("Cancelled")]
	public void UnconfirmedCleanupOrEvidenceBlocksRollback (string outcome)
		=> Assert.That (WorkflowRollback.ShouldAttempt (Failed with
			{
			Stages = Failed.Stages.Append (new ("Remove test instance", outcome)).ToArray ()
			}, true, true), Is.False);
	[Test]
	public async Task SuccessfulRollbackRequiresChecksBeforeEachMutationAndAfterRestoration ()
		{
		var session = new Session ();
		await WorkflowRollback.ExecuteAsync (session, CancellationToken.None);
		Assert.That (session.Events, Is.EqualTo (new[] { "Check1", "ImportIntent", "Import", "Check2", "SwapIntent", "Swap", "VerifyRestored", "Restored" }));
		var outcome = Failed with
			{
			Stages = Failed.Stages.Append (new ("Rollback actual driver", "Passed")).ToArray ()
			};
		Assert.That (outcome.Passed, Is.False, "Recovery must not make a failed deployment gate green.");
		}
	[TestCase ("Check1", 0, 0)]
	[TestCase ("ImportIntent", 0, 0)]
	[TestCase ("Import", 1, 0)]
	[TestCase ("Check2", 1, 0)]
	[TestCase ("SwapIntent", 1, 0)]
	[TestCase ("Swap", 1, 1)]
	[TestCase ("VerifyRestored", 1, 1)]
	[TestCase ("Restored", 1, 1)]
	public void FailureStopsWithoutRepeatingAnyMutation (string failure, int imports, int swaps)
		{
		var session = new Session (failure);
		Assert.ThrowsAsync<IOException> (() => WorkflowRollback.ExecuteAsync (session, CancellationToken.None));
		Assert.That (session.Events.Count (e => e == "Import"), Is.EqualTo (imports));
		Assert.That (session.Events.Count (e => e == "Swap"), Is.EqualTo (swaps));
		if (failure != "Restored")
			Assert.That (session.Events, Does.Not.Contain ("Restored"));
		}
	[Test]
	public void CancellationAfterIntentDoesNotSubmitAnImport ()
		{
		using var cancel = new CancellationTokenSource ();
		var session = new Session (cancel: cancel);
		Assert.ThrowsAsync<OperationCanceledException> (() => WorkflowRollback.ExecuteAsync (session, cancel.Token));
		Assert.That (session.Events, Does.Not.Contain ("Import"));
		}
	[TestCase (null, true)]
	[TestCase ("Check1", false)]
	[TestCase ("Import", false)]
	[TestCase ("Swap", false)]
	[TestCase ("VerifyRestored", false)]
	[TestCase ("Restored", false)]
	public async Task ExecutionGuardIsClearedOnlyAfterFullyVerifiedAndRecordedRestoration (string? failure, bool clears)
		{
		var session = new Session (failure);
		bool acquired = false, released = false;
		Task Begin (CancellationToken token)
			{
			acquired = true;
			return Task.CompletedTask;
			}
		Task End (CancellationToken token)
			{
			released = true;
			return Task.CompletedTask;
			}
		if (clears)
			await WorkflowRollback.ExecuteGuardedAsync (session, Begin, End, CancellationToken.None);
		else
			Assert.ThrowsAsync<IOException> (() => WorkflowRollback.ExecuteGuardedAsync (session, Begin, End, CancellationToken.None));
		Assert.That (acquired, Is.True);
		Assert.That (released, Is.EqualTo (clears));
		}
	[Test]
	public void BusyExecutionGuardPreventsAnyRecoveryWork ()
		{
		var session = new Session ();
		Assert.ThrowsAsync<IOException> (() => WorkflowRollback.ExecuteGuardedAsync (session, _ => throw new IOException ("Busy"),
			_ => throw new AssertionException ("Must not release someone else's guard"), CancellationToken.None));
		Assert.That (session.Events, Is.Empty);
		}
	private sealed class Session (string? failure = null, CancellationTokenSource? cancel = null) : WorkflowRollback.ISession
		{
		public List<string> Events { get; } = [];
		private int _checks;
		private Task Step (string name)
			{
			Events.Add (name);
			if (name == failure)
				throw new IOException ("Synthetic failure");
			if (name == "ImportIntent")
				cancel?.Cancel ();
			return Task.CompletedTask;
			}
		public Task VerifyCurrentAsync (CancellationToken token) => Step ("Check" + ++_checks);
		public Task RecordAsync (string phase) => Step (phase);
		public Task ImportPreviousAsync (CancellationToken token) => Step ("Import");
		public Task SwapOnceAsync (CancellationToken token) => Step ("Swap");
		public Task VerifyRestoredAsync (CancellationToken token) => Step ("VerifyRestored");
		}

	private static DeviceInfo Root () => new ()
		{
		Id = 12,
		ParentDeviceId = -6,
		Model = "Example",
		Name = "Test",
		LocationId = 5,
		PropertyValues = new ()
			{
			["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.2.003.0004"),
			["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement ("Loaded"),
			["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (false),
			["cp.driverConfiguration:isConfigured"] = JsonSerializer.SerializeToElement (true)
			}
		};
	[Test]
	public void ScopeIncludesEveryDescendantWithIdentityNameAndRoom ()
		{
		DeviceInfo[] all = [new () { Id = 15, ParentDeviceId = 13, Model = "Grandchild", Name = "Nested", LocationId = 6 }, Root (),
			new () { Id = 13, ParentDeviceId = 12, Model = "Child", Name = "Child", LocationId = 5 }, new () { Id = 16, ParentDeviceId = -6, Model = "Unrelated" }];
		var scope = WorkflowRollback.Scope (all, 12, "Example", "1.2.3.4");
		Assert.That (scope.Select (n => n.Id), Is.EqualTo (new[] { 12, 13, 15 }));
		Assert.That (scope[2], Is.EqualTo (new WorkflowRollback.Node (15, 13, "Grandchild", "Nested", 6)));
		}
	[TestCase ("cp.driverConfiguration:driverLoadingStatus", "Unloaded")]
	[TestCase ("cp.driverConfiguration:driverLoadingStatus", "Loading")]
	[TestCase ("cp.driverInformation:version", "1.2.3.5")]
	public void UnloadedOrUnexpectedVersionFailsClosed (string property, string value)
		{
		var root = Root ();
		root.PropertyValues[property] = JsonSerializer.SerializeToElement (value);
		Assert.Throws<InvalidOperationException> (() => WorkflowRollback.Scope ([root], 12, "Example", "1.2.3.4"));
		}
	[Test]
	public void RebootUnknownConfigurationAndDuplicateIdentityAreRejected ()
		{
		var root = Root ();
		root.PropertyValues["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (true);
		Assert.Throws<InvalidOperationException> (() => WorkflowRollback.Scope ([root], 12, "Example", "1.2.3.4"));
		root = Root ();
		root.PropertyValues.Remove ("cp.driverConfiguration:isConfigured");
		Assert.Throws<InvalidOperationException> (() => WorkflowRollback.Scope ([root], 12, "Example", "1.2.3.4"));
		Assert.Throws<InvalidDataException> (() => WorkflowRollback.Scope ([Root (), Root ()], 12, "Example", "1.2.3.4"));
		Assert.Throws<InvalidOperationException> (() => WorkflowRollback.Scope ([Root ()], 12, "Wrong", "1.2.3.4"));
		}

	[Test]
	public void CompatibilityEvidenceRequiresFreshIdentityExactPackageAndStableConfiguration ()
		{
		var hash = new string ('A', 64);
		var config = new string ('B', 64);
		var value = new RollbackConfigurationObservation ("nonce", 12, "Example", "1.2.3.4", hash, true, config);
		Assert.DoesNotThrow (() => WorkflowRollback.VerifyObservation (value, "nonce", 12, "Example", "1.2.003.0004", hash, config));
		foreach (var invalid in new[] { value with { RequestId = "stale" }, value with { DeviceId = 13 }, value with { Model = "Wrong" },
			value with { InstalledVersion = "1.2.3.5" }, value with { PreviousPackageSha256 = new string ('C', 64) },
			value with { CompatibleWithPreviousVersion = false }, value with { ConfigurationIdentity = new string ('D', 64) }, value with { ConfigurationIdentity = "invalid" } })
			Assert.Throws<InvalidDataException> (() => WorkflowRollback.VerifyObservation (invalid, "nonce", 12, "Example", "1.2.3.4", hash, config));
		}
	}