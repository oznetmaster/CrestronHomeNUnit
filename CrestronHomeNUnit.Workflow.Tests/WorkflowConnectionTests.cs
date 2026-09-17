// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class WorkflowConnectionTests
	{
	[Test]
	public async Task ExpiredConnectionIsReplacedWithoutReplayingCommands ()
		{
		var expired = new Fake ();
		var fresh = new Fake ();
		var checks = 0;
		await using var result = await WorkflowConnection.RefreshAsync (new (expired, true),
			_ => Task.FromResult (new ConfigurationClient (fresh, true)), _ => { checks++; return Task.CompletedTask; }, default);
		Assert.That (expired.Disposed, Is.True);
		Assert.That (fresh.Disposed, Is.False);
		Assert.That (checks, Is.EqualTo (2));
		Assert.That (await result.GetDevicesAsync (), Is.Empty);
		}

	[TestCase (1)]
	[TestCase (2)]
	public void OwnershipLossPreventsContinuation (int failureAt)
		{
		var previous = new Fake ();
		var fresh = new Fake ();
		int checks = 0, connections = 0;
		Assert.ThrowsAsync<IOException> (async () => await WorkflowConnection.RefreshAsync (new (previous, true),
			_ => { connections++; return Task.FromResult (new ConfigurationClient (fresh, true)); },
			_ => ++checks == failureAt ? throw new IOException () : Task.CompletedTask, default));
		Assert.That (previous.Disposed, Is.False);
		Assert.That (connections, Is.EqualTo (failureAt - 1));
		Assert.That (fresh.Disposed, Is.EqualTo (failureAt == 2));
		}

	[Test]
	public void FailedAuthenticationIsNotRetried ()
		{
		var previous = new Fake ();
		int attempts = 0;
		Assert.ThrowsAsync<IOException> (async () => await WorkflowConnection.RefreshAsync (new (previous, true),
			_ => { attempts++; throw new IOException (); }, _ => Task.CompletedTask, default));
		Assert.That (attempts, Is.EqualTo (1));
		Assert.That (previous.Disposed, Is.False);
		}

	[Test]
	public void CancellationBeforeRefreshDoesNotOpenAConnection ()
		{
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		Assert.ThrowsAsync<OperationCanceledException> (async () => await WorkflowConnection.RefreshAsync (new (new Fake (), true),
			_ => throw new AssertionException ("Unexpected connection"), _ => throw new AssertionException ("Unexpected lease access"), cancelled.Token));
		}

	private sealed class Fake : IConfigurationConnection
		{
		public bool Disposed { get; private set; }
		public Task<T?> GetAsync<T> (string path, CancellationToken token = default) =>
			Task.FromResult (System.Text.Json.JsonSerializer.Deserialize<T> ("{}"));
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken token = default) =>
			throw new AssertionException ("Refreshing a session must not submit or replay a command.");
		public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken token = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () { Disposed = true; return ValueTask.CompletedTask; }
		}
	}