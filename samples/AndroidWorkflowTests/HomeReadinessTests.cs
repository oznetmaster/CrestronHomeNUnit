// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeNUnit.Android;

using NUnit.Framework;

namespace AndroidWorkflowTests;

// Own one session for this entire test project, not a new session per fixture.
[SetUpFixture]
public sealed class WorkflowSession
	{
	internal static AndroidWorkflowSession? Current { get; private set; }
	internal static CrestronHomeNavigation? Navigation { get; private set; }

	[OneTimeSetUp]
	public async Task Open ()
		{
		Current = null;
		Navigation = null;
		if (string.IsNullOrWhiteSpace (Environment.GetEnvironmentVariable (AndroidWorkflowSession.CONTEXT_VARIABLE)))
			Assert.Ignore ("Run this opt-in UI project through a processor workflow; ordinary discovery never connects to Android.");
		Current = await AndroidWorkflowSession.OpenFromEnvironmentAsync ();
		Navigation = new (Current);
		}

	[OneTimeTearDown]
	public async Task Complete ()
		{
		if (Current == null) return;
		bool restored = false;
		try
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
			await Navigation!.RestoreHomeAsync (cleanup.Token);
			restored = Navigation.HomeRestored;
			}
		finally
			{
			// This project navigates but never edits settings or operates physical devices.
			Current.Complete (restorationConfirmed: restored);
			}
		}
	}

[TestFixture]
[NonParallelizable]
public sealed class HomeReadinessTests
	{
	[TestCase (1)]
	[TestCase (2)]
	public async Task SavedLocalEndpointMatchesAndHomeIsRestored (int repetition)
		{
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (4));
		await WorkflowSession.Navigation!.VerifySavedEndpointAsync ($"connection-{repetition}", WorkflowSession.Current!.Context.Profile.LocalPort, timeout.Token);
		Assert.That (WorkflowSession.Navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task ExpectedHomeIsVisibleAndEvidenceCanBeCaptured ()
		{
		var session = WorkflowSession.Current!;
		await session.CaptureAsync ("home.readiness", hierarchy =>
			{
			CrestronHomePages.RequireHome (hierarchy, session.Context.Profile.ExpectedHomeText);
			});
		}
	}