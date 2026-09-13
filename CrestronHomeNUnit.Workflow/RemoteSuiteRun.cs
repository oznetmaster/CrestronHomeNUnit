// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Workflow;

internal sealed class RemoteSuiteRun
	{
	public bool ExecutionConfirmedStopped { get; private set; } = true;
	public async Task<WorkflowTestOutcome> RunAsync (WorkflowPlan plan, NetworkCredential credential, string packageName,
		 SuitePlan suite, bool live, string directory, CancellationToken token)
		{
		Directory.CreateDirectory (directory);
		var ready = await new PackageReadiness ().WaitAsync (plan.Host, packageName, async (selected, ct) =>
		{
			var identity = await ProcessorAuthentication.AuthenticateAsync (selected.Host, credential.UserName, credential.Password,
					selected.ProcessorId, _ => plan.SshFingerprint, (_, _) => { }).WaitAsync (ct).ConfigureAwait (false);
			return await RemoteTestClient.ConnectAsync (selected.Host, selected.Port, identity.Token).ConfigureAwait (false);
		}, token).ConfigureAwait (false);
		using var client = ready.Connection;
		var selectedSuite = client.Suites.SingleOrDefault (s => s.Id == suite.Id) ?? throw new InvalidDataException ("Required processor suite was not advertised.");
		if (selectedSuite.ManualOnly != live)
			throw new InvalidDataException ("Suite live/manual classification differs from the workflow plan.");
		var inputs = new List<TestInputFile> ();
		foreach (var path in suite.Inputs)
			{
			if (new FileInfo (path).Length > SecureTestData.MaximumFileBytes)
				throw new InvalidDataException ("Input exceeds permitted size.");
			inputs.Add (new ()
				{
				Name = System.IO.Path.GetFileName (path),
				Content = await File.ReadAllBytesAsync (path, token).ConfigureAwait (false)
				});
			}
		if (inputs.Count > 0)
			SecureTestData.ValidateFiles (inputs);
		using var progress = new StreamWriter (System.IO.Path.Combine (directory, "Progress.jsonl")) { AutoFlush = true };
		var sync = new object ();
		bool progressFailed = false;
		void Report (WireMessage message)
			{
			lock (sync)
				{
				try
					{
					progress.WriteLine (JsonSerializer.Serialize (new
						{
						message.Kind,
						message.Text,
						message.Xml
						}));
					}
				catch { progressFailed = true; }
				}
			}
		client.Progress += Report;
		string id = Guid.NewGuid ().ToString ("N");
		Task<WireMessage>? running = null;
		try
			{
			await File.WriteAllTextAsync (System.IO.Path.Combine (directory, "Summary.json"), "{\"Complete\":false,\"Outcome\":\"Running\"}", token).ConfigureAwait (false);
			token.ThrowIfCancellationRequested ();
			ExecutionConfirmedStopped = false;
			running = client.SendAsync (new ()
				{
				Kind = "run",
				Suite = suite.Id,
				RequestId = id,
				EnableLiveTests = live,
				TestInputs = inputs.Count > 0 ? inputs : null
				});
			var response = await running.WaitAsync (token).ConfigureAwait (false);
			ExecutionConfirmedStopped = response.Kind == "complete";
			await File.WriteAllTextAsync (System.IO.Path.Combine (directory, "TestResult.xml"), response.Xml, CancellationToken.None).ConfigureAwait (false);
			var summary = TestResultSummary.FromResponse (response);
			await File.WriteAllTextAsync (System.IO.Path.Combine (directory, "Summary.json"), JsonSerializer.Serialize (summary), CancellationToken.None).ConfigureAwait (false);
			lock (sync)
				return new (summary.Passed, summary.Failed, summary.Skipped, !progressFailed && summary.Complete && summary.ExitCode == 0
					 && summary.Passed >= suite.MinimumPassed && summary.Total == summary.Passed);
			}
		catch
			{
			if (running != null && !ExecutionConfirmedStopped)
				{
				try
					{
					await client.SendAsync (new ()
						{
						Kind = "cancel",
						TargetId = id
						}).WaitAsync (TimeSpan.FromSeconds (3)).ConfigureAwait (false);
					// The cancellation acknowledgement is not proof that the original execution stopped.
					var final = await running.WaitAsync (TimeSpan.FromSeconds (3)).ConfigureAwait (false);
					ExecutionConfirmedStopped = final.Kind == "complete";
					if (ExecutionConfirmedStopped)
						await File.WriteAllTextAsync (System.IO.Path.Combine (directory, "TestResult.xml"), final.Xml).ConfigureAwait (false);
					}
				catch { }
				}
			if (running != null)
				_ = running.ContinueWith (t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
			await File.WriteAllTextAsync (System.IO.Path.Combine (directory, "Summary.json"), JsonSerializer.Serialize (new
				{
				Complete = false,
				Outcome = "Incomplete",
				ExecutionConfirmedStopped
				})).ConfigureAwait (false);
			throw;
			}
		finally { client.Progress -= Report; lock (sync) progress.Flush (); }
		}
	}