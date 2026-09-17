// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Client;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class ManagedAndroidTests
	{
	private static readonly AndroidManagedChildPlan CHILD = new ("room", "advertised-child", "CI Child", "Example Child", 3);
	private static readonly DriverInstanceReady ACTUAL = new (42, "Example Platform", "1.2.3.4", "Verified");
	private static AndroidTestPlan Plan (params AndroidManagedChildPlan[] children) => new ("unused", "unused") { ManagedChildren = children };
	private static Task Verify (CancellationToken token) { token.ThrowIfCancellationRequested (); return Task.CompletedTask; }

	[TestCase ("alias")]
	[TestCase ("duplicate-alias")]
	[TestCase ("duplicate-child")]
	[TestCase ("duplicate-room-name")]
	[TestCase ("room")]
	[TestCase ("name")]
	[TestCase ("model")]
	public void InvalidTargetsFailBeforeAnyLifecycleOrTest (string fault)
		{
		var children = fault switch
			{
			"alias" => new[] { CHILD with { Alias = "../child" } },
			"duplicate-alias" => [CHILD, CHILD with { Alias = "ROOM", ManagedDeviceId = "another", Name = "Second" }],
			"duplicate-child" => [CHILD, CHILD with { Alias = "second", Name = "Second" }],
			"duplicate-room-name" => [CHILD, CHILD with { Alias = "second", ManagedDeviceId = "another" }],
			"room" => [CHILD with { LocationId = 0 }],
			"name" => [CHILD with { Name = new ('x', 33) }],
			_ => [CHILD with { Model = "" }]
			};
		Assert.ThrowsAsync<ArgumentException> (() => WorkflowManagedAndroid.RunCoreAsync (Plan (children), ACTUAL, Verify,
			(_, _) => throw new AssertionException ("Tests must not start."), (_, _, _) => throw new AssertionException ("Lifecycle must not start.")));
		}

	[Test]
	public async Task ExistingPlansRunWithoutCreatingChildren ()
		{
		int ownership = 0;
		var result = await WorkflowManagedAndroid.RunCoreAsync (Plan (), ACTUAL,
			_ => { ownership++; return Task.CompletedTask; },
			(bindings, _) => { Assert.That (bindings, Is.Empty); return Task.FromResult (new AndroidTestOutcome (new (1, 0, 0, true), true)); },
			(_, _, _) => throw new AssertionException ("Existing plans must not commission children."));
		Assert.That (ownership, Is.EqualTo (2));
		Assert.That (result.Passed, Is.True);
		}

	[TestCase (true, true)]
	[TestCase (false, true)]
	[TestCase (true, false)]
	public async Task ActualParentAndReturnedChildIdsReachTestsWithIndependentRestoration (bool passed, bool restored)
		{
		var result = await WorkflowManagedAndroid.RunCoreAsync (Plan (CHILD), ACTUAL, Verify,
			(bindings, _) =>
				{
				Assert.That (bindings.Single (), Is.EqualTo (new AndroidManagedDeviceBinding ("room", 99, 42, "Example Child", "CI Child", 3)));
				return Task.FromResult (new AndroidTestOutcome (new (passed ? 1 : 0, passed ? 0 : 1, 0, true), restored));
				},
			async (targets, tests, token) =>
				{
				var target = targets.Single ();
				Assert.That (target.Request, Is.EqualTo (new ManagedDeviceRequest (42, "Example Platform", "1.2.3.4", "advertised-child", "CI Child", "Example Child", 3)));
				var bindings = new[] { new ManagedDeviceTestBinding (target.Alias, 99, target.Request) };
				var outcome = await tests (bindings, token);
				Assert.That (outcome, Is.EqualTo (new ManagedDeviceTestOutcome (passed, restored)));
				return new (passed && restored, restored, restored, bindings);
				});
		Assert.That (result.Passed, Is.EqualTo (passed && restored));
		Assert.That (result.SafeToRelease, Is.EqualTo (restored));
		Assert.That (result.Tests.Failed, Is.EqualTo (passed ? 0 : 1));
		}

	[Test]
	public async Task PendingConfigurationNeverClaimsTestsOrRelease ()
		{
		var result = await WorkflowManagedAndroid.RunCoreAsync (Plan (CHILD), ACTUAL, Verify,
			(_, _) => throw new AssertionException ("Pending configuration cannot run tests."),
			(_, _, _) => Task.FromResult (new ManagedDeviceValidationResult (false, false, false, [])));
		Assert.That (result.Passed, Is.False);
		Assert.That (result.SafeToRelease, Is.False);
		Assert.That (result.Tests.Complete, Is.False);
		}

	[Test]
	public void MismatchedCreatedIdentityIsNotGivenToTests () => Assert.ThrowsAsync<InvalidDataException> (() =>
		WorkflowManagedAndroid.RunCoreAsync (Plan (CHILD), ACTUAL, Verify,
			(_, _) => throw new AssertionException ("Changed child must not reach fixtures."),
			async (targets, tests, token) =>
				{
				await tests ([new ("room", 99, targets.Single ().Request with { ParentId = 43 })], token);
				throw new AssertionException ("Changed identity must stop the lifecycle.");
				}));

	[Test]
	public async Task UnconfirmedCleanupCannotReleaseEvenAfterPassingRestoredTest ()
		{
		var result = await WorkflowManagedAndroid.RunCoreAsync (Plan (CHILD), ACTUAL, Verify,
			(_, _) => Task.FromResult (new AndroidTestOutcome (new (1, 0, 0, true), true)),
			async (targets, tests, token) =>
				{
				var bindings = new[] { new ManagedDeviceTestBinding ("room", 99, targets.Single ().Request) };
				await tests (bindings, token);
				return new (false, true, false, bindings);
				});
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (result.CleanupConfirmed, Is.False);
		Assert.That (result.SafeToRelease, Is.False);
		Assert.That (result.Passed, Is.False);
		}

	[Test]
	public void LostReservationStopsBeforeCommissioning () => Assert.ThrowsAsync<IOException> (() =>
		WorkflowManagedAndroid.RunCoreAsync (Plan (CHILD), ACTUAL,
			_ => throw new IOException ("Reservation lost."),
			(_, _) => throw new AssertionException ("No tests allowed."),
			(_, _, _) => throw new AssertionException ("No setup allowed.")));
	}