// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;

namespace CrestronHomeNUnit.Workflow;

internal static class WorkflowConnection
	{
	// External test processes can outlive an idle configuration session. Refresh only
	// at a completed stage boundary; never replay a command with an uncertain outcome.
	internal static async Task<ConfigurationClient> RefreshAsync (ConfigurationClient previous,
		Func<CancellationToken, Task<ConfigurationClient>> connect, Func<CancellationToken, Task> verifyLease,
		CancellationToken token)
		{
		token.ThrowIfCancellationRequested ();
		await verifyLease (token).ConfigureAwait (false);
		var fresh = await connect (token).ConfigureAwait (false);
		try
			{
			await verifyLease (token).ConfigureAwait (false);
			token.ThrowIfCancellationRequested ();
			await previous.DisposeAsync ().ConfigureAwait (false);
			return fresh;
			}
		catch
			{
			await fresh.DisposeAsync ().ConfigureAwait (false);
			throw;
			}
		}
	}