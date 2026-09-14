// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeNUnit.TestAdapter;

internal interface IWorkflowExecution
	{
	Task<ProcessorWorkflowResult> RunAsync (WorkflowEntry entry, string results, Action<WorkflowStageResult> progress, CancellationToken token);
	}

internal sealed class WorkflowExecution : IWorkflowExecution
	{
	private sealed record PrivateSettings (string PlanPath, string? UserName, string? Password);

	public async Task<ProcessorWorkflowResult> RunAsync (WorkflowEntry entry, string results, Action<WorkflowStageResult> progress, CancellationToken token)
		{
		if (Environment.GetEnvironmentVariable ("CRESTRON_HOME_WORKFLOW_ACTIVE") == "1")
			throw new InvalidOperationException ("A workflow container cannot be included in its own local test stage.");
		var path = Environment.GetEnvironmentVariable (entry.SettingsEnvironment);
		if (string.IsNullOrWhiteSpace (path)) throw new InvalidOperationException ("Private workflow settings are required.");
		var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var settings = JsonSerializer.Deserialize<PrivateSettings> (await File.ReadAllTextAsync (path, token).ConfigureAwait (false), json)
			?? throw new InvalidDataException ("Invalid private settings.");
		if (!Path.IsPathFullyQualified (settings.PlanPath)) throw new InvalidDataException ("PlanPath must be absolute.");
		var plan = JsonSerializer.Deserialize<WorkflowPlan> (await File.ReadAllTextAsync (settings.PlanPath, token).ConfigureAwait (false), json)
			?? throw new InvalidDataException ("Invalid workflow plan.");
		var user = Environment.GetEnvironmentVariable ("CRESTRON_HOME_USER") ?? settings.UserName;
		var password = Environment.GetEnvironmentVariable ("CRESTRON_HOME_PASSWORD") ?? settings.Password;
		if (string.IsNullOrWhiteSpace (user) || string.IsNullOrEmpty (password)) throw new InvalidDataException ("Processor credentials are required.");
		return await WorkflowRunner.RunAsync (plan, new NetworkCredential (user, password), results, progress, token).ConfigureAwait (false);
		}
	}