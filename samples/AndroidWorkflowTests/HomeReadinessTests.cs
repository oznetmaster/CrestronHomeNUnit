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

	[OneTimeSetUp]
	public async Task Open ()
		{
		Current = null;
		if (string.IsNullOrWhiteSpace (Environment.GetEnvironmentVariable (AndroidWorkflowSession.CONTEXT_VARIABLE)))
			Assert.Ignore ("Run this opt-in UI project through a processor workflow; ordinary discovery never connects to Android.");
		Current = await AndroidWorkflowSession.OpenFromEnvironmentAsync ();
		}

	[OneTimeTearDown]
	public void Complete ()
		{
		// This example only reads the UI and sends no inputs or physical-device commands.
		// A project that adds controls must verify its own state restoration before reporting true.
		Current?.Complete (restorationConfirmed: true);
		}
	}

[TestFixture]
public sealed class HomeReadinessTests
	{
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