// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeNUnit.Workflow;

internal static class WorkflowCommand
	{
	internal static async Task<int> RunAsync (string[] arguments, bool installedTests = false)
		{
		using var stop = new CancellationTokenSource ();
		ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Cancel (); };
		Console.CancelKeyPress += handler;
		try
			{
			var options = new Dictionary<string, string> ();
			for (int index = 0; index < arguments.Length; index++)
				{
				var option = arguments[index];
				if (option is not ("--plan" or "--results" or "--settings") || ++index >= arguments.Length || !options.TryAdd (option, arguments[index]))
					throw new ArgumentException ("Use " + (installedTests ? "installed-tests" : "workflow") + " --plan private.json --results new-directory [--settings private.json].");
				}
			var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			if (!options.TryGetValue ("--plan", out var path) || !options.TryGetValue ("--results", out var results))
				throw new ArgumentException ("Workflow requires --plan and --results.");
			var planText = await File.ReadAllTextAsync (path, stop.Token);
			var settings = options.TryGetValue ("--settings", out var settingsPath)
				 ? JsonSerializer.Deserialize<CliSettings> (await File.ReadAllTextAsync (settingsPath, stop.Token), json) ?? new () : new CliSettings ();
			var user = Environment.GetEnvironmentVariable ("CRESTRON_HOME_USER") ?? settings.UserName;
			var password = Environment.GetEnvironmentVariable ("CRESTRON_HOME_PASSWORD") ?? settings.Password;
			if (string.IsNullOrWhiteSpace (user) || string.IsNullOrEmpty (password))
				throw new ArgumentException ("Supply credentials through environment variables or a private settings file.");
			if (installedTests)
				{
				var installedPlan = JsonSerializer.Deserialize<InstalledDriverTestPlan> (planText, new JsonSerializerOptions (json)
					{ UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
						RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true }) ?? throw new ArgumentException ("Invalid installed-driver test plan.");
				var tested = await InstalledDriverTests.RunAsync (installedPlan, new NetworkCredential (user, password), results, stop.Token);
				Console.WriteLine (tested.Detail);
				Console.WriteLine ($"Candidate verified: {tested.CandidateVerified}; restoration: {tested.RestorationConfirmed}; cleanup: {tested.CleanupConfirmed}; reservations released: {tested.ReservationsReleased}.");
				Console.WriteLine ("Private phase evidence saved to " + Path.GetFullPath (results));
				return tested.Passed ? 0 : stop.IsCancellationRequested ? 130 : !tested.ReservationsReleased ? 3 : 1;
				}
			var plan = JsonSerializer.Deserialize<WorkflowPlan> (planText, json) ?? throw new ArgumentException ("Invalid workflow plan.");
			var result = await WorkflowRunner.RunAsync (plan, new NetworkCredential (user, password), results,
				 stage => Console.WriteLine ($"{stage.Stage}: {stage.Outcome}" + (stage.Tests == null ? "" : $" ({stage.Tests.Passed} passed, {stage.Tests.Failed} failed, {stage.Tests.Skipped} skipped)")), stop.Token);
			Console.WriteLine ($"Actual driver update attempted: {result.DriverUpdateAttempted}; verified: {result.DriverUpdateVerified}.");
			Console.WriteLine ("Workflow evidence saved to " + Path.GetFullPath (results));
			return result.Passed ? 0 : stop.IsCancellationRequested ? 130 : 1;
			}
		catch (Exception exception)
			{
			Console.Error.WriteLine (exception is ArgumentException ? exception.Message : "Workflow could not complete. Inspect retained stage/build/test evidence and the processor lease before retrying.");
			return stop.IsCancellationRequested ? 130 : 2;
			}
		finally { Console.CancelKeyPress -= handler; }
		}
	}