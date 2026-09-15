// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

namespace CrestronHomeNUnit.Workflow;

public static class InstalledControlRunner
	{
	public static async Task<InstalledControlResult> RunAsync (ConfigurationClient client, ProcessorLease lease, DriverInstanceReady root,
		 InstalledControlPlan plan, string evidenceDirectory, CancellationToken token = default)
		{
		plan.Validate ();
		WorkflowEvidence.PrepareLocalResults (evidenceDirectory);
		await lease.BeginControlAsync (token).ConfigureAwait (false);
		var result = await ExecuteAsync (plan, new Session (client, root, plan, evidenceDirectory), token).ConfigureAwait (false);
		if (result.RestorationConfirmed)
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (20));
			await lease.EndControlAsync (cleanup.Token).ConfigureAwait (false);
			}
		return result;
		}

	internal interface ISession
		{
		Task<ControlActivity> ActivityAsync (CancellationToken token);
		Task<ControlObservation> ObserveAsync (CancellationToken token);
		Task SubmitAsync (JsonElement value, CancellationToken token);
		Task<bool> HomeMatchesAsync (JsonElement value, CancellationToken token);
		Task RecordAsync (string phase, ControlObservation? original = null);
		}

	internal static async Task<InstalledControlResult> ExecuteAsync (InstalledControlPlan plan, ISession session, CancellationToken token)
		{
		ControlObservation? original = null;
		ControlActivity? before = null;
		bool submitted = false, completed = false, passed = false, restored = true;
		string detail = "Control did not start.";
		string phase = "target identity and idle completion status";
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (plan.TimeoutSeconds));
		try
			{
			JsonElement target;
			while (true)
				{
				before = await WaitForIdle (session, plan, deadline.Token).ConfigureAwait (false);
				phase = "independent original-state capture";
				original = await session.ObserveAsync (deadline.Token).ConfigureAwait (false);
				if (!InstalledControlPlan.IsScalar (original.Value) || !InstalledControlPlan.IsScalar (original.RestoreValue) || !Equal (original.Value, original.RestoreValue)
					 || plan.BooleanCommands != null && original.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
					throw new InvalidDataException ("Probe must report scalar observation and restoration values in command units.");
				target = plan.InvertBoolean && original.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
					? JsonSerializer.SerializeToElement (!original.Value.GetBoolean ()) : plan.TestValue;
				if (!InstalledControlPlan.IsScalar (target) || Equal (target, original.Value))
					throw new InvalidOperationException ("The test must request a distinct physical state.");
				phase = "stable identity and recovery journal";
				var current = await session.ActivityAsync (deadline.Token).ConfigureAwait (false);
				if (current == before)
					break;
				// Startup or other activity before submission invalidates the captured state, not the target.
				// No command has been sent; capture again under the same bounded preflight deadline.
				await Task.Delay (plan.PollMilliseconds, deadline.Token).ConfigureAwait (false);
				}
			await session.RecordAsync ("OriginalCaptured", original).ConfigureAwait (false);
			await session.RecordAsync ("ControlIntent", original).ConfigureAwait (false);
			deadline.Token.ThrowIfCancellationRequested ();
			submitted = true;
			restored = false;
			await session.SubmitAsync (target, deadline.Token).ConfigureAwait (false);
			await WaitForCompletion (session, before, plan, deadline.Token).ConfigureAwait (false);
			completed = true;
			await WaitForState (session, target, before with
				{
				Completed = before.Completed + 1
				}, plan, deadline.Token).ConfigureAwait (false);
			passed = true;
			detail = "Physical state and Home property verified; original state restored.";
			}
		catch (Exception)
			{
			// Probe/API exception text can contain credentials. Keep diagnostics fixed.
			detail = submitted ? "Control verification failed; inspect private recovery evidence." : $"Preflight failed during {phase}; no command submitted.";
			}
		finally
			{
			if (submitted && original != null && before != null)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (plan.RestoreTimeoutSeconds));
				try
					{
					// Cancellation or a lost submission response must not cause overlapping commands.
					if (!completed)
						await WaitForCompletion (session, before, plan, cleanup.Token).ConfigureAwait (false);
					var restoreBaseline = await session.ActivityAsync (cleanup.Token).ConfigureAwait (false);
					RequireIdle (restoreBaseline);
					if (restoreBaseline != before with
						{
						Completed = before.Completed + 1
						})
						throw new InvalidOperationException ("Driver restarted or concurrent commands occurred.");
					await session.RecordAsync ("RestoreIntent", original).ConfigureAwait (false);
					await session.SubmitAsync (original.RestoreValue, cleanup.Token).ConfigureAwait (false);
					await WaitForCompletion (session, restoreBaseline, plan, cleanup.Token).ConfigureAwait (false);
					await WaitForState (session, original.Value, restoreBaseline with
						{
						Completed = restoreBaseline.Completed + 1
						}, plan, cleanup.Token).ConfigureAwait (false);
					await session.RecordAsync ("Restored", original).ConfigureAwait (false);
					restored = true;
					}
				catch (Exception)
					{
					passed = false;
					detail = "Restoration unconfirmed; retain the processor lease and inspect private evidence before recovery.";
					try
						{
						await session.RecordAsync ("RecoveryRequired", original).ConfigureAwait (false);
						}
					catch { /* The durable intent already requires recovery. */ }
					}
				}
			}
		return new (plan.Name, passed && restored, restored, detail);
		}

	private static async Task<ControlActivity> WaitForIdle (ISession session, InstalledControlPlan plan, CancellationToken token)
		{
		while (true)
			{
			var state = await session.ActivityAsync (token).ConfigureAwait (false);
			if (string.IsNullOrWhiteSpace (state.Epoch) || state.Completed < 0 || state.Pending < 0)
				throw new InvalidDataException ("Invalid driver activity.");
			if (state.Pending == 0)
				return state;
			await Task.Delay (plan.PollMilliseconds, token).ConfigureAwait (false);
			}
		}

	private static void RequireIdle (ControlActivity state)
		{
		if (string.IsNullOrWhiteSpace (state.Epoch) || state.Completed < 0 || state.Pending != 0)
			throw new InvalidOperationException ("Driver must report a valid idle command state.");
		}
	private static async Task WaitForCompletion (ISession session, ControlActivity before, InstalledControlPlan plan, CancellationToken token)
		{
		while (true)
			{
			var state = await session.ActivityAsync (token).ConfigureAwait (false);
			if (state.Epoch != before.Epoch || state.Completed < before.Completed || state.Pending < 0)
				throw new InvalidOperationException ("Command identity or completion history changed.");
			if (state.Pending == 0 && state.Completed == before.Completed + 1)
				return;
			if (state.Completed > before.Completed + 1)
				throw new InvalidOperationException ("Concurrent commands prevent attribution.");
			await Task.Delay (plan.PollMilliseconds, token).ConfigureAwait (false);
			}
		}
	private static async Task WaitForState (ISession session, JsonElement expected, ControlActivity activity, InstalledControlPlan plan, CancellationToken token)
		{
		int matches = 0;
		while (matches < 2)
			{
			if (await session.ActivityAsync (token).ConfigureAwait (false) != activity)
				throw new InvalidOperationException ("Concurrent activity during observation.");
			var observation = await session.ObserveAsync (token).ConfigureAwait (false);
			matches = Equal (observation.Value, expected) && await session.HomeMatchesAsync (expected, token).ConfigureAwait (false) ? matches + 1 : 0;
			if (await session.ActivityAsync (token).ConfigureAwait (false) != activity)
				throw new InvalidOperationException ("Concurrent activity during observation.");
			if (matches < 2)
				await Task.Delay (plan.PollMilliseconds, token).ConfigureAwait (false);
			}
		}
	internal static bool Equal (JsonElement a, JsonElement b) => JsonElement.DeepEquals (a, b);

	internal sealed class Session (ConfigurationClient client, DriverInstanceReady root, InstalledControlPlan plan, string directory) : ISession
		{
		private int _sequence;
		private ControlActivity? _lastActivity;
		private async Task<DeviceInfo> Device (CancellationToken token)
			{
			var target = await client.GetDeviceAsync (plan.DeviceId, token).ConfigureAwait (false);
			if (target == null || target.Id != plan.DeviceId || target.Model != plan.Model || !(plan.BooleanCommands is { } commands ? target.Commands.Contains (commands.True) && target.Commands.Contains (commands.False) : target.Commands.Contains (plan.Command!))
				 || !target.PropertyValues.TryGetValue (plan.IdentityProperty, out var identity) || identity.GetString () != plan.PhysicalIdentity)
				throw new InvalidOperationException ("Installed device identity or command changed.");
			var current = target;
			var visited = new HashSet<int> ();
			while (current.Id != root.DeviceId)
				{
				if (!visited.Add (current.Id) || current.ParentDeviceId is not > 0)
					throw new InvalidOperationException ("Target is not a descendant of the selected driver.");
				current = await client.GetDeviceAsync (current.ParentDeviceId.Value, token).ConfigureAwait (false)
					?? throw new InvalidOperationException ("Parent identity lost.");
				}
			if (current.Model != root.Model || !current.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version) || version.GetString () != root.Version)
				throw new InvalidOperationException ("Actual driver identity or version changed.");
			return target;
			}
		public async Task<ControlActivity> ActivityAsync (CancellationToken token)
			{
			var device = await Device (token).ConfigureAwait (false);
			return _lastActivity = JsonSerializer.Deserialize<ControlActivity> (device.PropertyValues[plan.ActivityProperty].GetString ()!)
				?? throw new InvalidDataException ("Missing command activity.");
			}
		public async Task SubmitAsync (JsonElement value, CancellationToken token)
			{
			await Device (token).ConfigureAwait (false);
			if (plan.BooleanCommands is { } commands)
				{
				if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
					throw new InvalidDataException ("Boolean commands require a boolean state.");
				await client.ExecuteDeviceCommandAsync (plan.DeviceId, value.GetBoolean () ? commands.True : commands.False, null, token).ConfigureAwait (false);
				}
			else
				await client.ExecuteDeviceCommandAsync (plan.DeviceId, plan.Command!, new Dictionary<string, JsonElement> { [plan.Parameter!] = value }, token).ConfigureAwait (false);
			}
		public async Task<bool> HomeMatchesAsync (JsonElement value, CancellationToken token)
			{
			var device = await Device (token).ConfigureAwait (false);
			return device.PropertyValues.TryGetValue (plan.StateProperty, out var actual) && Equal (actual, value);
			}
		public async Task<ControlObservation> ObserveAsync (CancellationToken token)
			{
			await Device (token).ConfigureAwait (false);
			var id = Guid.NewGuid ().ToString ("N");
			var path = Path.Combine (directory, "probe-" + ++_sequence);
			var request = path + "-request.json";
			var response = path + "-response.json";
			await File.WriteAllTextAsync (request, JsonSerializer.Serialize (new
				{
				RequestId = id,
				plan.PhysicalIdentity
				}), token).ConfigureAwait (false);
			var arguments = plan.Probe.Arguments.Concat (["--request", request, "--response", response]);
			var exit = await WorkflowEvidence.ProcessAsync (plan.Probe.Executable, arguments, plan.Probe.WorkingDirectory, path + ".log", token).ConfigureAwait (false);
			if (exit != 0 || !File.Exists (response) || new FileInfo (response).Length > 65536)
				throw new InvalidDataException ("Independent observation failed.");
			var observation = JsonSerializer.Deserialize<ControlObservation> (await File.ReadAllTextAsync (response, token).ConfigureAwait (false));
			if (observation == null || observation.RequestId != id || observation.PhysicalIdentity != plan.PhysicalIdentity
				 || !InstalledControlPlan.IsScalar (observation.Value) || !InstalledControlPlan.IsScalar (observation.RestoreValue))
				throw new InvalidDataException ("Observation identity or values did not match the request.");
			return observation;
			}
		public Task RecordAsync (string phase, ControlObservation? original = null)
			{
			// Each durable transition is a new file; a failed write cannot erase the original state.
			return File.WriteAllTextAsync (Path.Combine (directory, (++_sequence).ToString ("D4") + "-" + phase + ".json"),
				JsonSerializer.Serialize (new
					{
					Phase = phase,
					plan.DeviceId,
					plan.PhysicalIdentity,
					Original = original,
					Activity = _lastActivity,
					plan.Command,
					plan.Parameter,
					plan.BooleanCommands,
					Utc = DateTimeOffset.UtcNow
					}));
			}
		}
	}