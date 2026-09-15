// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeNUnit.Workflow;

// An absolute setter with independent observation. Inputs and evidence belong outside source control.
public sealed record InstalledControlPlan
	{
	public required string Name
		{
		get; init;
		}
	public required int DeviceId
		{
		get; init;
		}
	public required string Model
		{
		get; init;
		}
	public required string PhysicalIdentity
		{
		get; init;
		}
	public string? Command
		{
		get; init;
		}
	public string? Parameter
		{
		get; init;
		}
	public required string StateProperty
		{
		get; init;
		}
	public BooleanControlCommands? BooleanCommands
		{
		get; init;
		}
	public string IdentityProperty { get; init; } = "controlDeviceId";
	public string ActivityProperty { get; init; } = "controlStatus";
	[System.Text.Json.Serialization.JsonIgnore (Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
	public JsonElement TestValue
		{
		get; init;
		}
	public bool InvertBoolean
		{
		get; init;
		}
	public required ControlProbePlan Probe
		{
		get; init;
		}
	public int TimeoutSeconds { get; init; } = 120;
	public int RestoreTimeoutSeconds { get; init; } = 120;
	public int PollMilliseconds { get; init; } = 1000;

	public void Validate ()
		{
		if (DeviceId <= 0 || new[] { Name, Model, PhysicalIdentity, StateProperty, IdentityProperty, ActivityProperty }.Any (string.IsNullOrWhiteSpace)
			 || TimeoutSeconds is < 1 or > 900 || RestoreTimeoutSeconds is < 1 or > 900 || PollMilliseconds is < 10 or > 10000)
			throw new ArgumentException ("A control needs explicit identity, an absolute setter, observation properties and bounded timeouts.");
		if (BooleanCommands is { } commands)
			{
			if (Command != null || Parameter != null || string.IsNullOrWhiteSpace (commands.True) || string.IsNullOrWhiteSpace (commands.False)
				 || commands.True == commands.False || !InvertBoolean && TestValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
				throw new ArgumentException ("Boolean control needs two distinct absolute commands, without setter fields.");
			}
		else if (string.IsNullOrWhiteSpace (Command) || string.IsNullOrWhiteSpace (Parameter))
			throw new ArgumentException ("Specify an absolute setter or a pair of absolute boolean commands.");
		if (InvertBoolean ? TestValue.ValueKind != JsonValueKind.Undefined : !IsScalar (TestValue))
			throw new ArgumentException ("Choose either a scalar test value or boolean inversion.");
		if (Probe == null || !Path.IsPathFullyQualified (Probe.Executable) || !File.Exists (Probe.Executable)
			 || !Path.IsPathFullyQualified (Probe.WorkingDirectory) || !Directory.Exists (Probe.WorkingDirectory)
			 || Probe.Arguments == null || Probe.Arguments.Any (a => a == null))
			throw new ArgumentException ("An independent probe needs an existing absolute executable and working directory.");
		}
	internal static bool IsScalar (JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.String
		 || value.ValueKind == JsonValueKind.Number && value.TryGetDouble (out var n) && double.IsFinite (n);
	}

public sealed record ControlProbePlan (string Executable, string WorkingDirectory, string[] Arguments);
public sealed record ControlObservation (string RequestId, string PhysicalIdentity, JsonElement Value, JsonElement RestoreValue);
public sealed record ControlActivity (string Epoch, long Completed, int Pending);
public sealed record InstalledControlResult (string Name, bool Passed, bool RestorationConfirmed, string Detail);

public sealed record BooleanControlCommands (string True, string False);