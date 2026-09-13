// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class WorkflowRestartTests
	{
	private static DriverRebootRequest Request (DriverRebootMode mode) => new ("Update", 17, "Example", "1.1", mode);

	[TestCase (DriverRebootMode.ProcessorManaged, false, 0)]
	[TestCase (DriverRebootMode.ExplicitAfterOperation, true, 0)]
	[TestCase (DriverRebootMode.ExplicitAfterOperation, false, 1)]
	public async Task RequestsRebootOnlyWhenExplicitAndNotAlreadyDisconnected (DriverRebootMode mode, bool closed, int expectedWrites)
		{
		var connection = new Fake ();
		if (closed)
			connection.Closed.SetResult ();
		var writes = 0;
		var leaseChecked = false;
		var evidence = new List<string> ();
		await WorkflowRestart.CompleteAsync (Request (mode), new (connection), (state, _) => { evidence.Add (state); return Task.CompletedTask; },
			 _ => { Assert.That (evidence.Last (), Is.EqualTo ("RebootSubmissionAttempted")); writes++; return Task.FromResult (new ProcessorRebootResult (ProcessorRebootStatus.Accepted)); },
			 _ => Task.FromResult (new ConfigurationClient (new Fake ())), _ => { leaseChecked = true; return Task.CompletedTask; }, default);
		Assert.That (writes, Is.EqualTo (expectedWrites));
		Assert.That (leaseChecked, Is.True);
		Assert.That (evidence.Last (), Is.EqualTo ("ReconnectedAndLeaseVerified"));
		}

	[Test]
	public void EvidenceFailurePreventsRebootSubmission ()
		{
		Assert.ThrowsAsync<IOException> (async () => await WorkflowRestart.CompleteAsync (Request (DriverRebootMode.ExplicitAfterOperation), new (new Fake ()),
			 (_, _) => throw new IOException (), _ => throw new AssertionException ("Must not reboot"),
			 _ => throw new AssertionException ("Must not reconnect"), _ => throw new AssertionException ("Must not continue"), default));
		}

	[Test]
	public void UnconfirmedRebootDoesNotRetryOrProceed ()
		{
		var writes = 0;
		Assert.ThrowsAsync<IOException> (async () => await WorkflowRestart.CompleteAsync (Request (DriverRebootMode.ExplicitAfterOperation), new (new Fake ()),
			 (_, _) => Task.CompletedTask, _ => { writes++; return Task.FromResult (new ProcessorRebootResult (ProcessorRebootStatus.Unconfirmed)); },
			 _ => throw new AssertionException ("Must not continue"), _ => throw new AssertionException ("Must not continue"), default));
		Assert.That (writes, Is.EqualTo (1));
		}

	[Test]
	public void MissingLeaseBlocksContinuationAndDisposesFreshConnection ()
		{
		var fresh = new Fake ();
		var states = new List<string> ();
		Assert.ThrowsAsync<IOException> (async () => await WorkflowRestart.CompleteAsync (Request (DriverRebootMode.ProcessorManaged), new (new Fake ()),
			 (state, _) => { states.Add (state); return Task.CompletedTask; }, _ => throw new AssertionException ("No SSH reboot"),
			 _ => Task.FromResult (new ConfigurationClient (fresh, ownsConnection: true)), _ => throw new IOException (), default));
		Assert.That (fresh.Disposed, Is.True);
		Assert.That (states, Does.Not.Contain ("ReconnectedAndLeaseVerified"));
		}

	[Test]
	public void AbsentProcessorIsRetryableButAmbiguousNameIsNot ()
		{
		Assert.Throws<IOException> (() => WorkflowRestart.SelectRestartAddress ([], "example"));
		Assert.Throws<InvalidOperationException> (() => WorkflowRestart.SelectRestartAddress ([new ("example", "192.0.2.1", "MC4-R", "1"), new ("example", "192.0.2.2", "MC4-R", "1")], "example"));
		}

	[Test]
	public void RestartDiscoveryUsesTheCurrentAddress () =>
		 Assert.That (WorkflowRestart.SelectRestartAddress ([new ("example", "192.0.2.2", "MC4-R", "1")], "EXAMPLE"), Is.EqualTo ("192.0.2.2"));

	private sealed class Fake : IConfigurationConnection
		{
		public TaskCompletionSource Closed { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		public bool Disposed
			{
			get; private set;
			}
		public Task WaitForDisconnectAsync (CancellationToken token = default) => Closed.Task.WaitAsync (token);
		public Task<T?> GetAsync<T> (string path, CancellationToken token = default) => throw new NotSupportedException ();
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken token = default) => throw new NotSupportedException ();
		public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken token = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync ()
			{
			Disposed = true;
			return ValueTask.CompletedTask;
			}
		}
	}