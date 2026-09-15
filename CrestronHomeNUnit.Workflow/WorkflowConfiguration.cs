// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

namespace CrestronHomeNUnit.Workflow;

internal static class WorkflowConfiguration
	{
	private const string COMMAND = "cp.driverConfiguration:applyConfiguration";

	internal sealed class Rejected (string message) : InvalidOperationException (message);

	internal sealed record Step (string Id, IReadOnlyDictionary<string, string> Values);
	internal sealed record Inputs (IReadOnlyDictionary<string, string>? Values, Step[]? Steps);

	internal static Inputs? ReadInputs (string? path)
		{
		if (path == null)
			return null;
		try
			{
			using var document = JsonDocument.Parse (File.ReadAllText (path));
			var root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty ("steps", out var steps))
				{
				if (root.EnumerateObject ().Count () != 1 || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength () is < 1 or > 16)
					throw new InvalidDataException ();
				var result = new List<Step> ();
				foreach (var step in steps.EnumerateArray ())
					{
					if (step.ValueKind != JsonValueKind.Object || step.EnumerateObject ().Count () != 2
						 || !step.TryGetProperty ("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace (id.GetString ())
						 || !step.TryGetProperty ("values", out var values) || result.Any (s => s.Id == id.GetString ()))
						throw new InvalidDataException ();
					result.Add (new (id.GetString ()!, ReadValues (values)));
					}
				return new (null, result.ToArray ());
				}
			var flat = ReadValues (root);
			if (flat.Count == 0)
				throw new InvalidDataException ();
			return new (flat, null);
			}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
			{
			// Parser messages, paths and values can reveal private configuration.
			throw new InvalidDataException ("Initial configuration must contain unique item IDs and string values, or an ordered steps array with unique step IDs and values objects.");
			}
		}

	private static Dictionary<string, string> ReadValues (JsonElement element)
		{
		var values = new Dictionary<string, string> (StringComparer.Ordinal);
		if (element.ValueKind != JsonValueKind.Object)
			throw new InvalidDataException ();
		foreach (var item in element.EnumerateObject ())
			if (string.IsNullOrWhiteSpace (item.Name) || item.Value.ValueKind != JsonValueKind.String || !values.TryAdd (item.Name, item.Value.GetString ()!))
				throw new InvalidDataException ();
		return values;
		}

	internal static async Task<bool> ApplyAsync (ConfigurationClient client, DriverInstanceReady target, Inputs inputs, CancellationToken token)
		{
		if (inputs.Steps == null)
			return await ApplyAsync (client, target, inputs.Values!, token).ConfigureAwait (false);
		var device = await GetUnconfiguredTarget (client, target, token).ConfigureAwait (false);
		if (device == null)
			return false;
		const string first = "cp.driverConfiguration:getFirstConfigurationStep";
		const string apply = "cp.driverConfiguration:applyConfigurationStep";
		if (!device.Commands.Contains (first) || !device.Commands.Contains (apply))
			throw new Rejected ("The driver does not advertise a configuration wizard.");
		var current = await client.ExecuteDeviceCommandAsync (target.DeviceId, first, new
			{
			isReconfiguring = false
			}, token).ConfigureAwait (false);
		foreach (var step in inputs.Steps)
			{
			if (current?.ValueKind != JsonValueKind.Object || !current.Value.TryGetProperty ("Id", out var id)
				 || id.ValueKind != JsonValueKind.String || id.GetString () != step.Id
				 || !current.Value.TryGetProperty ("ConfigurationErrors", out var errors) || errors.ValueKind != JsonValueKind.Null
				 || !current.Value.TryGetProperty ("Items", out var items) || items.ValueKind != JsonValueKind.Array)
				throw new Rejected ("The configuration wizard differs from the planned steps or reported errors. No step was retried.");
			ValidateItems (items, step.Values.Keys);
			current = await client.ExecuteDeviceCommandAsync (target.DeviceId, apply,
				new
					{
					stepId = step.Id,
					configurationItemValues = step.Values,
					isReconfiguring = false
					}, token).ConfigureAwait (false);
			}
		if (current is { ValueKind: not JsonValueKind.Null })
			throw new Rejected ("The configuration wizard has not completed. Inspect it before continuing; no additional steps were submitted.");
		return true;
		}

	internal static async Task<bool> ApplyAsync (ConfigurationClient client, DriverInstanceReady target,
		 IReadOnlyDictionary<string, string> values, CancellationToken token)
		{
		var device = await GetUnconfiguredTarget (client, target, token).ConfigureAwait (false);
		if (device == null)
			return false;
		if (!device.Commands.Contains (COMMAND)
			 || !device.PropertyValues.TryGetValue ("cp.driverConfiguration:configurationItems", out var items)
			 || items.ValueKind != JsonValueKind.Array)
			throw new Rejected ("The driver does not advertise supported configuration items.");
		ValidateItems (items, values.Keys);
		var result = await client.ExecuteDeviceCommandAsync (target.DeviceId, COMMAND,
			new
				{
				configurationItemValues = values,
				isoCulture = "en-GB"
				}, token).ConfigureAwait (false);
		if (result is { ValueKind: not JsonValueKind.Null }
			 && (result.Value.ValueKind != JsonValueKind.Array || result.Value.GetArrayLength () != 0))
			throw new Rejected ("The driver rejected initial configuration. Review its settings; values and driver error text are withheld.");
		return true;
		}

	private static async Task<DeviceInfo?> GetUnconfiguredTarget (ConfigurationClient client, DriverInstanceReady target, CancellationToken token)
		{
		var device = await client.GetDeviceAsync (target.DeviceId, token).ConfigureAwait (false);
		if (device == null || device.Id != target.DeviceId || device.Model != target.Model
			 || !device.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version)
			 || version.ValueKind != JsonValueKind.String || !Version.TryParse (version.GetString (), out var installed)
			 || !Version.TryParse (target.Version, out var expected) || installed != expected
			 || !device.PropertyValues.TryGetValue ("cp.driverConfiguration:isConfigured", out var configured)
			 || configured.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
			throw new Rejected ("Initial configuration target identity or configuration state is unconfirmed.");
		return configured.GetBoolean () ? null : device;
		}

	private static void ValidateItems (JsonElement items, IEnumerable<string> keys)
		{
		foreach (var key in keys)
			{
			var matches = items.EnumerateArray ().Where (item => item.TryGetProperty ("Id", out var id)
				 && id.ValueKind == JsonValueKind.String && id.GetString () == key).ToArray ();
			if (matches.Length != 1 || !matches[0].TryGetProperty ("Value", out var metadata)
				 || !metadata.TryGetProperty ("ReadOnly", out var readOnly) || readOnly.ValueKind != JsonValueKind.False)
				throw new Rejected ("Initial configuration includes an unknown, ambiguous or non-writable item.");
			}
		}
	}
