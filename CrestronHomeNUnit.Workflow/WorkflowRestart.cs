// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;

namespace CrestronHomeNUnit.Workflow;

internal static class WorkflowRestart
	{
	internal static async Task<ConfigurationClient> CompleteAsync (DriverRebootRequest request, ConfigurationClient previous,
		 Func<string, CancellationToken, Task> save,
		 Func<CancellationToken, Task<ProcessorRebootResult>> sendReboot,
		 Func<CancellationToken, Task<ConfigurationClient>> reconnect,
		 Func<CancellationToken, Task> verifyLease, CancellationToken token)
		{
		if (request.Mode == DriverRebootMode.ExplicitAfterOperation)
			{
			// If firmware has already closed the old session, do not send a second reboot.
			if (!previous.WaitForDisconnectAsync ().IsCompletedSuccessfully)
				{
				await save ("RebootSubmissionAttempted", token).ConfigureAwait (false);
				var result = await sendReboot (token).ConfigureAwait (false);
				if (result.Status != ProcessorRebootStatus.Accepted)
					throw new IOException ("Configuration reboot was not acknowledged; inspect the processor. No retry was made.");
				}
			}
		await save ("WaitingForRestart", token).ConfigureAwait (false);
		var recovered = await reconnect (token).ConfigureAwait (false);
		try
			{
			await verifyLease (token).ConfigureAwait (false);
			await save ("ReconnectedAndLeaseVerified", token).ConfigureAwait (false);
			return recovered;
			}
		catch
			{
			await recovered.DisposeAsync ().ConfigureAwait (false);
			throw;
			}
		}

	internal static string SelectRestartAddress (IEnumerable<DiscoveredProcessor> processors, string name)
		{
		var matches = processors.Where (p => p.SystemName.Equals (name, StringComparison.OrdinalIgnoreCase)).ToArray ();
		if (matches.Length == 0)
			throw new IOException ("The processor has not reappeared in discovery yet.");
		if (matches.Length != 1)
			throw new InvalidOperationException ("More than one processor has the configured system name; recovery cannot choose a target.");
		return matches[0].Address;
		}
	}