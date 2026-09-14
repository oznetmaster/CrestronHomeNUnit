// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeNUnit.Client.Tests;

public sealed class ProcessorLeaseTests
	{
	[Test]
	public void BusyWithoutWait_DoesNotRetry ()
		{
		int calls = 0;
		Assert.ThrowsAsync<ProcessorBusyException> (async () => await ProcessorLease.WaitForLeaseAsync<int> (_ => { calls++; throw new ProcessorBusyException (); }, TimeSpan.Zero, default));
		Assert.That (calls, Is.EqualTo (1));
		}
	[Test]
	public void AuthenticationOrNetworkFailure_DoesNotRetry ()
		{
		int calls = 0;
		Assert.ThrowsAsync<IOException> (async () => await ProcessorLease.WaitForLeaseAsync<int> (_ => { calls++; throw new IOException (); }, TimeSpan.FromSeconds (2), default));
		Assert.That (calls, Is.EqualTo (1));
		}
	[Test]
	public async Task BusyThenReleased_AcquiresWithinWait ()
		{
		int calls = 0;
		var result = await ProcessorLease.WaitForLeaseAsync (_ => ++calls == 1 ? throw new ProcessorBusyException () : Task.FromResult (42), TimeSpan.FromSeconds (2), default);
		Assert.That (result, Is.EqualTo (42));
		Assert.That (calls, Is.EqualTo (2));
		}
	[Test]
	public void BusyWait_CanBeCancelled ()
		{
		using var stop = new CancellationTokenSource (TimeSpan.FromMilliseconds (30));
		Assert.That (async () => await ProcessorLease.WaitForLeaseAsync<int> (_ => throw new ProcessorBusyException (), TimeSpan.FromSeconds (30), stop.Token), Throws.InstanceOf<OperationCanceledException> ());
		}
	[TestCase (-1)]
	[TestCase (86401)]
	public void InvalidWait_IsRejectedBeforeConnecting (int seconds)
		{
		Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await ProcessorLease.WaitForLeaseAsync<int> (_ => throw new AssertionException ("Must not connect"), TimeSpan.FromSeconds (seconds), default));
		}
	}