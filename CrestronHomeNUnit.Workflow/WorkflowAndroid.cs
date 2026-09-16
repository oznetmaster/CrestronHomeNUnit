// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

internal sealed record AndroidTestOutcome (WorkflowTestOutcome Tests, bool RestorationConfirmed);
internal delegate Task<int> AndroidTestProcess (string executable, IEnumerable<string> arguments, string directory, string log,
	CancellationToken token, IReadOnlyDictionary<string, string>? environment);

internal static class WorkflowAndroid
	{
	public static async Task<AndroidTestOutcome> RunAsync (AndroidTestPlan plan, AndroidSessionProfile profile, string owner,
		string host, int deviceId, string package, string source, string directory, CancellationToken token, AndroidTestProcess? runProcess = null)
		{
		WorkflowEvidence.PrepareLocalResults (directory);
		AndroidSessionLease.VerifyOwner (profile.LockPath, owner);
		var identity = DriverDeployment.Inspect (package);
		using var coordinator = Process.GetCurrentProcess ();
		var context = new AndroidRunContext (1, owner, Environment.MachineName, coordinator.Id, coordinator.StartTime.ToUniversalTime ().Ticks,
			host, deviceId, identity.DriverId, identity.Version, Convert.ToHexString (SHA256.HashData (await File.ReadAllBytesAsync (package, token).ConfigureAwait (false))),
			source, profile, Path.GetFullPath (directory));
		var contextPath = Path.Combine (directory, "context.json");
		await File.WriteAllTextAsync (contextPath, JsonSerializer.Serialize (context), token).ConfigureAwait (false);
		var assemblyDirectory = Path.Combine (directory, "assembly");
		var common = new[] { "test", plan.Project, "--configuration", "Debug", "--framework", "net10.0", "--output", assemblyDirectory,
			"-p:DeployAfterBuild=false", "-p:BuildForTests=true" };
		runProcess ??= WorkflowEvidence.ProcessAsync;
		int discoveryExit = await runProcess ("dotnet", common.Concat (["--list-tests", "--", "NUnit.DumpXmlTestDiscovery=true"]),
			Path.GetDirectoryName (plan.Project)!, Path.Combine (directory, "Discovery.log"), token,
			new Dictionary<string, string> { [AndroidWorkflowSession.CONTEXT_VARIABLE] = "" }).ConfigureAwait (false);
		if (discoveryExit != 0)
			throw new InvalidDataException ("Android test discovery failed; execution was not started.");
		var dumps = Directory.GetFiles (Path.Combine (assemblyDirectory, "Dump"), "D_*.dll.dump");
		if (dumps.Length != 1)
			throw new InvalidDataException ("Android tests require exactly one NUnit discovery assembly.");
		var discoveryPath = Path.Combine (directory, "discovery.dump");
		File.Copy (dumps[0], discoveryPath, overwrite: false);
		var inventory = AndroidTestCoverage.ReadDiscovery (discoveryPath);
		AndroidSessionLease.VerifyOwner (profile.LockPath, owner);
		var arguments = common.Concat (new[] { "--no-build", "--no-restore",
			"--logger", "trx;LogFilePrefix=TestResult", "--results-directory", directory });
		int exit = await runProcess ("dotnet", arguments, Path.GetDirectoryName (plan.Project)!, Path.Combine (directory, "Tests.log"), token,
			new Dictionary<string, string> { [AndroidWorkflowSession.CONTEXT_VARIABLE] = contextPath }).ConfigureAwait (false);
		AndroidSessionLease.VerifyOwner (profile.LockPath, owner);
		var completionPath = Path.Combine (directory, "completion.json");
		bool restored = false;
		if (File.Exists (completionPath))
			{
			var completion = AndroidWorkflowSession.Read<AndroidRunCompletion> (completionPath);
			restored = CompletionMatches (completion, context);
			}
		var coverage = AndroidTestCoverage.Evaluate (inventory, Directory.GetFiles (directory, "TestResult*.trx"), exit);
		await File.WriteAllTextAsync (Path.Combine (directory, "coverage.json"), JsonSerializer.Serialize (new
			{
			context.RunId, context.PackageSha256, DiscoverySha256 = Convert.ToHexString (SHA256.HashData (await File.ReadAllBytesAsync (discoveryPath, token).ConfigureAwait (false))),
			ExpectedTests = inventory, Results = coverage
			}), token).ConfigureAwait (false);
		return new (coverage, restored);
		}

	internal static bool CompletionMatches (AndroidRunCompletion completion, AndroidRunContext context) =>
		completion.SchemaVersion == 1 && completion.RunId == context.RunId && completion.PackageSha256 == context.PackageSha256 && completion.RestorationConfirmed;
	}