// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Net.Sockets;

using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Client;

public sealed record ReadyPackage<T> (DiscoveredPackage Package, T Connection) where T : IDisposable;

/// <summary>Waits for discovery and a connection handshake before any test request is submitted.</summary>
public sealed class PackageReadiness
	{
	private readonly Func<CancellationToken, Task<IReadOnlyList<DiscoveredPackage>>> _discover;
	private readonly TimeSpan _retryDelay;

	public PackageReadiness (Func<CancellationToken, Task<IReadOnlyList<DiscoveredPackage>>>? discover = null,
		 TimeSpan? retryDelay = null)
		{
		_discover = discover ?? PackageDiscovery.FindAsync;
		_retryDelay = retryDelay ?? TimeSpan.FromSeconds (1);
		if (_retryDelay < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (retryDelay));
		}

	public async Task<ReadyPackage<T>> WaitAsync<T> (string processor, string packageName,
		 Func<DiscoveredPackage, CancellationToken, Task<T>> connect, CancellationToken cancellationToken) where T : IDisposable
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (processor);
		ArgumentException.ThrowIfNullOrWhiteSpace (packageName);
		ArgumentNullException.ThrowIfNull (connect);
		var byAddress = IPAddress.TryParse (processor, out var address);
		string? processorId = null;
		while (true)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			var packages = await _discover (cancellationToken).ConfigureAwait (false);
			var matches = packages.Where (p => p.Name.Equals (packageName, StringComparison.OrdinalIgnoreCase)
				 && (byAddress ? IPAddress.TryParse (p.Host, out var found) && address!.Equals (found)
					  : p.ProcessorName.Equals (processor, StringComparison.OrdinalIgnoreCase))).ToArray ();
			if (matches.Length > 1)
				throw new ArgumentException ("Package selection is ambiguous; use an IP and unique package name.");
			if (matches.Length == 1)
				{
				var selected = matches[0];
				if (string.IsNullOrWhiteSpace (selected.ProcessorId))
					throw new InvalidDataException ("The discovered package did not identify its processor.");
				processorId ??= selected.ProcessorId;
				if (processorId != selected.ProcessorId)
					throw new InvalidOperationException ("The processor identity changed while waiting for its test service.");
				try
					{
					// A cancelled wait cannot necessarily cancel the transport's handshake.
					// Observe its eventual completion and dispose any late connection.
					var connecting = connect (selected, cancellationToken);
					T connection;
					try
						{
						connection = await connecting.WaitAsync (cancellationToken).ConfigureAwait (false);
						}
					catch
						{
						_ = connecting.ContinueWith (task =>
						{
							if (task.Status == TaskStatus.RanToCompletion)
								task.Result.Dispose ();
							else
								_ = task.Exception;
						}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
						throw;
						}
					return new (selected, connection);
					}
				catch (Exception exception) when (exception is SocketException or TimeoutException
					 || exception is IOException)
					{
					// Only connection establishment can be retried. Never replay a test request.
					}
				}
			await Task.Delay (_retryDelay, cancellationToken).ConfigureAwait (false);
			}
		}
	}