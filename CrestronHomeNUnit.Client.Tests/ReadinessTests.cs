// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net.Sockets;
using System.Security.Authentication;

using CrestronHomeNUnit.Transport;

using NUnit.Framework;

namespace CrestronHomeNUnit.Client.Tests;

[TestFixture]
public sealed class ReadinessTests
	{
	private static DiscoveredPackage Package (int port, string id = "processor-one") => new ()
		{
		Name = "Example Tests",
		ProcessorName = "Development",
		ProcessorId = id,
		Host = "192.0.2.1",
		Port = port
		};

	[Test]
	public async Task WaitsForAdvertisementThenRediscoversChangedPort ()
		{
		var queries = 0;
		var attemptedPorts = new List<int> ();
		var readiness = new PackageReadiness (_ => Task.FromResult<IReadOnlyList<DiscoveredPackage>> (
			 ++queries == 1 ? [] : [Package (queries == 2 ? 10001 : 10002)]), TimeSpan.Zero);
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (3));
		var ready = await readiness.WaitAsync ("Development", "Example Tests", (package, _) =>
		{
			attemptedPorts.Add (package.Port);
			return package.Port == 10001
					? Task.FromException<Connection> (new SocketException ((int)SocketError.ConnectionRefused))
					: Task.FromResult (new Connection ());
		}, deadline.Token);
		using var connection = ready.Connection;
		Assert.That (attemptedPorts, Is.EqualTo (new[] { 10001, 10002 }));
		Assert.That (ready.Package.Port, Is.EqualTo (10002));
		}

	[Test]
	public void AmbiguousPackagesDoNotConnect ()
		{
		var calls = 0;
		var readiness = new PackageReadiness (_ => Task.FromResult<IReadOnlyList<DiscoveredPackage>> ([Package (1), Package (2)]));
		Assert.ThrowsAsync<ArgumentException> (async () => await readiness.WaitAsync ("192.0.2.1", "Example Tests", (_, _) =>
		{
			calls++;
			return Task.FromResult (new Connection ());
		}, CancellationToken.None));
		Assert.That (calls, Is.Zero);
		}

	[Test]
	public void ChangedProcessorIdentityStopsRetry ()
		{
		var queries = 0;
		var connections = 0;
		var readiness = new PackageReadiness (_ => Task.FromResult<IReadOnlyList<DiscoveredPackage>> (
			 [Package (123, ++queries == 1 ? "processor-one" : "processor-two")]), TimeSpan.Zero);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await readiness.WaitAsync ("Development", "Example Tests", (_, _) =>
		{
			connections++;
			return Task.FromException<Connection> (new IOException ("Closed"));
		}, CancellationToken.None));
		Assert.That (connections, Is.EqualTo (1));
		}

	[TestCase (true)]
	[TestCase (false)]
	public void AuthenticationOrMalformedIdentityIsNotRetried (bool authentication)
		{
		var calls = 0;
		var readiness = new PackageReadiness (_ => Task.FromResult<IReadOnlyList<DiscoveredPackage>> ([Package (123)]), TimeSpan.Zero);
		Exception failure = authentication ? new AuthenticationException ("Rejected") : new InvalidDataException ("Invalid identity");
		var actual = Assert.CatchAsync (async () => await readiness.WaitAsync ("Development", "Example Tests", (_, _) =>
		{
			calls++;
			return Task.FromException<Connection> (failure);
		}, CancellationToken.None));
		Assert.That (actual, Is.SameAs (failure));
		Assert.That (calls, Is.EqualTo (1));
		}

	[Test]
	public async Task CancelledHandshakeDisposesLateConnection ()
		{
		var pending = new TaskCompletionSource<Connection> ();
		var entered = new TaskCompletionSource ();
		var readiness = new PackageReadiness (_ => Task.FromResult<IReadOnlyList<DiscoveredPackage>> ([Package (123)]));
		using var cancellation = new CancellationTokenSource ();
		var run = readiness.WaitAsync ("Development", "Example Tests", (_, _) => { entered.SetResult (); return pending.Task; }, cancellation.Token);
		await entered.Task;
		cancellation.Cancel ();
		Assert.CatchAsync<OperationCanceledException> (async () => await run);
		var connection = new Connection ();
		pending.SetResult (connection);
		await connection.Disposed.Task.WaitAsync (TimeSpan.FromSeconds (3));
		}

	[Test]
	public void MissingPackageStopsAtCancellation ()
		{
		using var cancellation = new CancellationTokenSource ();
		var readiness = new PackageReadiness (_ =>
		{
			cancellation.Cancel ();
			return Task.FromResult<IReadOnlyList<DiscoveredPackage>> ([]);
		});
		Assert.CatchAsync<OperationCanceledException> (async () => await readiness.WaitAsync<Connection> ("Development", "Example Tests",
			 (_, _) => throw new AssertionException ("Unexpected connection"), cancellation.Token));
		}

	private sealed class Connection : IDisposable
		{
		public TaskCompletionSource Disposed { get; } = new ();
		public void Dispose () => Disposed.TrySetResult ();
		}
	}