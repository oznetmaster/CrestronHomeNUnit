// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class InstalledControlTests
	{
	private static JsonElement Value (bool state) => JsonSerializer.SerializeToElement (state);
	private static InstalledControlPlan Plan => new ()
		{
		Name = "Outlet",
		DeviceId = 2,
		Model = "Outlet",
		PhysicalIdentity = "test-device",
		Command = "setPower",
		Parameter = "value",
		StateProperty = "power",
		InvertBoolean = true,
		Probe = new (typeof (InstalledControlTests).Assembly.Location, AppContext.BaseDirectory, []),
		TimeoutSeconds = 1,
		RestoreTimeoutSeconds = 1,
		PollMilliseconds = 10
		};

	[TestCase ("same")]
	[TestCase ("missing")]
	[TestCase ("mixed")]
	[TestCase ("nonboolean")]
	public void RejectsAmbiguousBooleanCommands (string failure)
		{
		var plan = Plan with
			{
			Command = failure == "mixed" ? "setPower" : null,
			Parameter = null,
			BooleanCommands = new ("on", failure == "same" ? "on" : failure == "missing" ? "" : "off"),
			InvertBoolean = failure != "nonboolean",
			TestValue = failure == "nonboolean" ? JsonSerializer.SerializeToElement (3) : default
			};
		Assert.Throws<ArgumentException> (() => plan.Validate ());
		}

	[Test]
	public void BooleanPlanRoundTripsWithoutUndefinedJsonValue ()
		{
		var plan = Plan with
			{
			Command = null,
			Parameter = null,
			BooleanCommands = new ("on", "off")
			};
		var restored = JsonSerializer.Deserialize<InstalledControlPlan> (JsonSerializer.Serialize (plan))!;
		Assert.DoesNotThrow (() => restored.Validate ());
		Assert.That (restored.BooleanCommands, Is.EqualTo (plan.BooleanCommands));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ControlsAndRestoresEitherOriginalState (bool initial)
		{
		var session = new Session { Physical = initial, Home = initial };
		var result = await InstalledControlRunner.ExecuteAsync (Plan, session, default);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.EqualTo (new[] { !initial, initial }));
		Assert.That (session.Physical, Is.EqualTo (initial));
		Assert.That (session.Records, Is.EqualTo (new[] { "OriginalCaptured", "ControlIntent", "RestoreIntent", "Restored" }));
		}

	[Test]
	public async Task StartupCompletionDuringCaptureCausesFreshCaptureBeforeControl ()
		{
		var session = new Session { StartupDuringCapture = true };
		var result = await InstalledControlRunner.ExecuteAsync (Plan, session, default);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True);
		Assert.That (session.CapturesBeforeFirstCommand, Is.EqualTo (2));
		Assert.That (session.Writes, Is.EqualTo (new[] { true, false }));
		}

	[Test]
	public async Task OptimisticHomeStateDoesNotProvePhysicalSuccess ()
		{
		var session = new Session { IgnoreFirstPhysicalCommand = true };
		var result = await InstalledControlRunner.ExecuteAsync (Plan, session, default);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.EqualTo (new[] { true, false }));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task LostAcknowledgementOrCancellationRestoresAfterConfirmedCompletion (bool cancel)
		{
		using var cancellation = new CancellationTokenSource ();
		var session = new Session
			{
			AfterFirstSubmission = () =>
			{
				if (cancel)
					cancellation.Cancel ();
				throw new IOException ("private secret must never appear in results");
			}
			};
		var result = await InstalledControlRunner.ExecuteAsync (Plan, session, cancellation.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Physical, Is.False);
		Assert.That (result.Detail, Does.Not.Contain ("secret"));
		}

	[TestCase ("pending")]
	[TestCase ("restart")]
	[TestCase ("concurrent")]
	public async Task UncertainCompletionDoesNotSendAnOverlappingRestore (string failure)
		{
		var session = new Session { Failure = failure };
		var result = await InstalledControlRunner.ExecuteAsync (Plan, session, default);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Writes, Is.EqualTo (new[] { true }));
		Assert.That (session.Records.Last (), Is.EqualTo ("RecoveryRequired"));
		}

	[Test]
	public async Task FailedRestorationKeepsRecoveryRequired ()
		{
		var session = new Session { IgnoreRestore = true };
		var result = await InstalledControlRunner.ExecuteAsync (Plan, session, default);
		Assert.That (result.RestorationConfirmed || result.Passed, Is.False);
		Assert.That (session.Writes.Count, Is.EqualTo (2));
		}

	[TestCase ("busy")]
	[TestCase ("probe")]
	[TestCase ("journal")]
	[TestCase ("units")]
	public async Task InvalidPreflightOrJournalDoesNotControlAnything (string failure)
		{
		var session = new Session { PreflightFailure = failure };
		var result = await InstalledControlRunner.ExecuteAsync (Plan, session, default);
		Assert.That (session.Writes, Is.Empty);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		}

	[Test]
	public async Task OriginalStateCannotBeUsedAsEvidenceOfSuccessfulControl ()
		{
		var session = new Session ();
		var result = await InstalledControlRunner.ExecuteAsync (Plan with
			{
			InvertBoolean = false,
			TestValue = Value (false)
			}, session, default);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Writes, Is.Empty);
		}

	private sealed class Session : InstalledControlRunner.ISession
		{
		public bool Physical, Home, IgnoreFirstPhysicalCommand, IgnoreRestore, StartupDuringCapture;
		public int CapturesBeforeFirstCommand;
		private int _startupCompleted;
		public string? Failure, PreflightFailure;
		public Action? AfterFirstSubmission;
		public List<bool> Writes { get; } = [];
		public List<string> Records { get; } = [];
		public Task<ControlActivity> ActivityAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			return Task.FromResult (new ControlActivity (Failure == "restart" && Writes.Count > 0 ? "new" : "epoch",
				Failure == "concurrent" && Writes.Count > 0 ? 2 : Failure == "pending" ? 0 : Writes.Count + _startupCompleted,
				PreflightFailure == "busy" || Failure == "pending" && Writes.Count > 0 ? 1 : 0));
			}
		public Task<ControlObservation> ObserveAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			if (Writes.Count == 0)
				{
				CapturesBeforeFirstCommand++;
				if (StartupDuringCapture)
					_startupCompleted = 1;
				}
			if (PreflightFailure == "probe")
				throw new IOException ("private settings");
			return Task.FromResult (new ControlObservation ("request", "test-device", Value (Physical), Value (PreflightFailure == "units" || Physical)));
			}
		public Task SubmitAsync (JsonElement value, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Writes.Add (value.GetBoolean ());
			Home = value.GetBoolean ();
			if (!(IgnoreFirstPhysicalCommand && Writes.Count == 1) && !(IgnoreRestore && Writes.Count == 2))
				Physical = Home;
			if (Writes.Count == 1)
				AfterFirstSubmission?.Invoke ();
			return Task.CompletedTask;
			}
		public Task<bool> HomeMatchesAsync (JsonElement value, CancellationToken token) => Task.FromResult (value.GetBoolean () == Home);
		public Task RecordAsync (string phase, ControlObservation? original = null)
			{
			if (PreflightFailure == "journal")
				throw new IOException ("cannot persist original state");
			Records.Add (phase);
			return Task.CompletedTask;
			}
		}
	}