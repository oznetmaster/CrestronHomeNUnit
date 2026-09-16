// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

public static class WorkflowRunner
	{
	public static async Task<ProcessorWorkflowResult> RunAsync (WorkflowPlan plan, NetworkCredential credential, string results,
		 Action<WorkflowStageResult>? progress = null, CancellationToken token = default)
		{
		plan.Validate ();
		var initialConfiguration = WorkflowConfiguration.ReadInputs (plan.ActualDriver?.InitialConfigurationFile);
		if (!string.IsNullOrWhiteSpace (plan.ProcessorSystemName))
			plan = plan with
				{
				Host = (await ProcessorDiscovery.ResolveAsync (plan.ProcessorSystemName, token).ConfigureAwait (false)).Address
				};
		results = Path.GetFullPath (results);
		Directory.CreateDirectory (results);
		await using var exclusive = new FileStream (Path.Combine (results, "Workflow.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		var runId = Guid.NewGuid ().ToString ("N");
		await using var client = await ConfigurationClient.ConnectAsync (ConnectionOptions (plan, plan.Host), credential, token).ConfigureAwait (false);
		using var lease = await ProcessorLease.AcquireAsync (plan.Host, credential, plan.SshFingerprint, runId, TimeSpan.FromSeconds (plan.LeaseWaitSeconds), token).ConfigureAwait (false);
		await File.WriteAllTextAsync (Path.Combine (results, "Lease.json"), JsonSerializer.Serialize (new
			{
			RunId = runId,
			State = "Held"
			}), token).ConfigureAwait (false);
		await using var operations = new Operations (plan, credential, results, client, lease, initialConfiguration);
		ProcessorWorkflowResult? result = null;
		bool release = false;
		var stages = new List<WorkflowStageResult> ();
		void Report (WorkflowStageResult stage)
			{
			stages.Add (stage);
			File.WriteAllText (Path.Combine (results, "Stages.json"), JsonSerializer.Serialize (stages, new JsonSerializerOptions { WriteIndented = true }));
			progress?.Invoke (stage);
			}
		try
			{
			result = await ProcessorWorkflow.RunAsync (new (plan.ActualDriver != null, plan.ActualDriver != null,
				 plan.LiveSuites.Length != 0, plan.ActualDriver != null, plan.RemoveTestInstanceAfterRun), operations, Report, token).ConfigureAwait (false);
			result = await operations.RollbackAfterFailureAsync (result, Report).ConfigureAwait (false);
			result = await WorkflowPackageCleanup.AfterSuccessfulRunAsync (result, plan.RemoveTestPackageAfterSuccessfulRun, () => operations.RemoveTestPackageAsync (token), Report).ConfigureAwait (false);
			await JsonSerializer.SerializeAsync (exclusive, result, cancellationToken: CancellationToken.None).ConfigureAwait (false);
			await exclusive.FlushAsync (CancellationToken.None).ConfigureAwait (false);
			release = CanReleaseLease (operations.RemoteExecutionConfirmedStopped, operations.ActivationUncertain, plan.RemoveTestInstanceAfterRun, operations.HasTestInstance);
			}
		finally
			{
			if (release)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (20));
				try
					{
					await lease.ReleaseAsync (cleanup.Token).ConfigureAwait (false);
					await File.WriteAllTextAsync (Path.Combine (results, "Lease.json"), JsonSerializer.Serialize (new
						{
						RunId = runId,
						State = "Released"
						})).ConfigureAwait (false);
					}
				catch
					{
					if (result != null)
						result = result with
							{
							Stages = result.Stages.Append (new WorkflowStageResult ("Release processor lease", "Error", Detail: "Lease release could not be confirmed; inspect Lease.json before another workflow.")).ToArray ()
							};
					// Completed test/deployment evidence remains intact; a retained lease blocks the next workflow.
					await File.WriteAllTextAsync (Path.Combine (results, "Lease.json"), JsonSerializer.Serialize (new
						{
						RunId = runId,
						State = "ReleaseUnconfirmed"
						})).ConfigureAwait (false);
					}
				}
			}
		exclusive.Position = 0;
		exclusive.SetLength (0);
		await JsonSerializer.SerializeAsync (exclusive, result, cancellationToken: CancellationToken.None).ConfigureAwait (false);
		await exclusive.FlushAsync (CancellationToken.None).ConfigureAwait (false);
		return result!;
		}

	internal static ProcessorConnectionOptions ConnectionOptions (WorkflowPlan plan, string host) => new ()
		{
		Host = host,
		CertificateSha256 = plan.CertificateSha256,
		RequestTimeout = TimeSpan.FromSeconds (plan.StageTimeoutSeconds)
		};

	internal static bool CanReleaseLease (bool remoteExecutionStopped, bool activationUncertain, bool removalRequested, bool hasTestInstance)
		=> remoteExecutionStopped && !activationUncertain && (!removalRequested || !hasTestInstance);

	private sealed class Operations (WorkflowPlan plan, NetworkCredential credential, string results, ConfigurationClient initialClient, ProcessorLease lease,
		 WorkflowConfiguration.Inputs? initialConfiguration) : IProcessorWorkflowOperations, IAsyncDisposable
		{
		private readonly ConfigurationClient _initialClient = initialClient;
		private ConfigurationClient client = initialClient;
		private string _host = plan.Host;
		private int _rebootNumber;
		private readonly List<FileStream> _artifacts = [];
		private readonly Dictionary<string, string> _artifactInputs = [];
		private readonly RemoteSuiteRun _remote = new ();
		private DriverInstanceReady? _test;
		private DriverInstanceReady? _actual;
		private string? _testPath;
		private string? _actualPath;
		private string? _source;
		private WorkflowPackageCleanup? _packageCleanup;
		private WorkflowRollback.Prepared? _rollback;
		public bool HasTestInstance => _test != null;
		public bool RemoteExecutionConfirmedStopped => _remote.ExecutionConfirmedStopped && !ActivationUncertain;
		public bool ActivationUncertain
			{
			get; private set;
			}
		private TimeSpan Timeout => TimeSpan.FromSeconds (plan.StageTimeoutSeconds);
		private CancellationTokenSource Deadline (CancellationToken token)
			{
			var source = CancellationTokenSource.CreateLinkedTokenSource (token);
			source.CancelAfter (Timeout);
			return source;
			}
		private async Task CheckSource (CancellationToken token)
			{
			if (_source != await WorkflowEvidence.SourceDigestAsync (plan.SourceRoots, token).ConfigureAwait (false))
				throw new InvalidOperationException ("Source changed during the workflow; rebuild and rerun tests.");
			}
		public async Task<WorkflowTestOutcome> RunLocalTestsAsync (CancellationToken token)
			{
			_source = await WorkflowEvidence.SourceDigestAsync (plan.SourceRoots, token).ConfigureAwait (false);
			await File.WriteAllTextAsync (Path.Combine (results, "BuildIdentity.json"), JsonSerializer.Serialize (new
				{
				SourceSha256 = _source,
				Configuration = "Debug",
				StartedUtc = DateTimeOffset.UtcNow
				}), token).ConfigureAwait (false);
			int passed = 0, failed = 0, skipped = 0;
			for (int index = 0; index < plan.LocalTests.Length; index++)
				{
				using var deadline = Deadline (token);
				var test = plan.LocalTests[index];
				var dir = Path.Combine (results, "local-" + index);
				WorkflowEvidence.PrepareLocalResults (dir);
				var args = new List<string> { "test", Path.GetFullPath (test.Project), "--configuration", "Debug", "-p:DeployAfterBuild=false", "-p:SkipMergeDependencies=true", "--logger", "trx;LogFilePrefix=TestResult", "--results-directory", dir };
				if (test.Filter != null)
					{
					args.Add ("--filter");
					args.Add (test.Filter);
					}
				var exit = await WorkflowEvidence.ProcessAsync ("dotnet", args, Path.GetDirectoryName (Path.GetFullPath (test.Project))!, Path.Combine (dir, "Build.log"), deadline.Token).ConfigureAwait (false);
				var outcome = WorkflowEvidence.ReadLocalResults (dir, exit, test.MinimumPassed);
				passed += outcome.Passed;
				failed += outcome.Failed;
				skipped += outcome.Skipped;
				if (!outcome.MeetsGate)
					return new (passed, failed, skipped, false);
				}
			await CheckSource (token).ConfigureAwait (false);
			return new (passed, failed, skipped, true);
			}
		private async Task<string> Build (PackageBuildPlan package, bool test, CancellationToken token)
			{
			using var deadline = Deadline (token);
			await CheckSource (deadline.Token).ConfigureAwait (false);
			var prefix = test ? "processor" : "actual";
			var manifestPath = Path.ChangeExtension (Path.GetFullPath (package.Project), ".json");
			if (File.Exists (manifestPath) && !plan.SourceRoots.Any (root => manifestPath.StartsWith (
				Path.GetFullPath (root).TrimEnd (Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException ("The driver manifest must belong to a declared source root before Debug version reconciliation.");
			var reuse = plan.ArtifactReuse;
			var inputs = reuse == null ? null : await WorkflowArtifacts.InputsDigestAsync (package.Project, reuse.BuildInputFiles, deadline.Token, plan.SourceRoots).ConfigureAwait (false);
			var retained = reuse?.PreviousResults == null ? null : await WorkflowArtifacts.TryReuseAsync (reuse.PreviousResults, prefix,
				Path.GetFileName (package.PackagePath), manifestPath, _source!, inputs, client.GetDriversAsync, deadline.Token).ConfigureAwait (false);
			PreparedDebugVersion? preparedVersion = null;
			if (retained == null)
				{
				preparedVersion = await WorkflowDebugVersion.PrepareAsync (manifestPath, client.GetDriversAsync, deadline.Token).ConfigureAwait (false);
				await CheckSource (deadline.Token).ConfigureAwait (false);
				var args = new List<string> { "build", Path.GetFullPath (package.Project), "--configuration", "Debug", "--no-incremental", "-p:DeployAfterBuild=false", "-m:1" };
				if (test)
					args.Add ("-p:BuildProcessorTestPackages=true");
				int exit = await WorkflowEvidence.ProcessAsync ("dotnet", args, Path.GetDirectoryName (Path.GetFullPath (package.Project))!, Path.Combine (results, prefix + "-build.log"), deadline.Token).ConfigureAwait (false);
				if (exit != 0)
					throw new IOException ("Package build failed; see retained build log.");
				inputs = reuse == null ? null : await WorkflowArtifacts.InputsDigestAsync (package.Project, reuse.BuildInputFiles, deadline.Token, plan.SourceRoots).ConfigureAwait (false);
				}
			await CheckSource (deadline.Token).ConfigureAwait (false);
			var dir = Path.Combine (results, "packages", prefix);
			Directory.CreateDirectory (dir);
			var path = Path.Combine (dir, Path.GetFileName (package.PackagePath));
			if (retained == null)
				File.Copy (package.PackagePath, path, overwrite: false);
			else
				await WorkflowArtifacts.CopyVerifiedAsync (retained, path, deadline.Token).ConfigureAwait (false);
			preparedVersion?.VerifyBuiltPackage (DriverDeployment.Inspect (path));
			var locked = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
			_artifacts.Add (locked);
			var hash = Convert.ToHexString (await SHA256.HashDataAsync (locked, deadline.Token).ConfigureAwait (false));
			locked.Position = 0;
			if (retained != null && hash != retained.Receipt.Sha256)
				throw new InvalidDataException ("Copied artifact failed verification.");
			await File.WriteAllTextAsync (Path.Combine (results, prefix + "-package.json"), JsonSerializer.Serialize (new PackageReceipt (
				DriverDeployment.Inspect (path), preparedVersion?.Baseline.ToString () ?? retained?.Receipt.DebugRevisionBaseline,
				hash, _source!, inputs, retained?.RunId)), deadline.Token).ConfigureAwait (false);
			if (inputs != null)
				_artifactInputs.Add (path, inputs);
			return path;
			}
		public async Task PrepareTestInstanceAsync (CancellationToken token)
			{
			// Build once and retain the actual package before any processor stage. Both use the same source identity.
			if (plan.ActualDriver != null)
				_actualPath = await Build (plan.ActualDriver, false, token).ConfigureAwait (false);
			_testPath = await Build (plan.TestPackage, true, token).ConfigureAwait (false);
			if (_actualPath != null && DriverDeployment.Inspect (_actualPath).DriverId == DriverDeployment.Inspect (_testPath).DriverId)
				throw new InvalidDataException ("Actual and test packages have the same driver identity.");
			if (plan.RemoveTestPackageAfterSuccessfulRun)
				_packageCleanup = await WorkflowPackageCleanup.CaptureAsync (_host, credential, plan.SshFingerprint, lease.Owner, Path.Combine (results, "package-cleanup"), token).ConfigureAwait (false);
			_test = await Activate (plan.TestPackage, _testPath, "processor", token).ConfigureAwait (false);
			}
		private async Task<DriverInstanceReady> Activate (PackageBuildPlan target, string package, string prefix, CancellationToken token)
			{
			using var deadline = Deadline (token);
			await CheckSource (deadline.Token).ConfigureAwait (false);
			// A unique version must be newly imported; catalogue reuse cannot prove the uploaded bytes.
			if (_artifactInputs.TryGetValue (package, out var inputs) && inputs != await WorkflowArtifacts.InputsDigestAsync (
				target.Project, plan.ArtifactReuse!.BuildInputFiles, deadline.Token, plan.SourceRoots).ConfigureAwait (false))
				throw new InvalidOperationException ("Build inputs changed after package preparation; rebuild and rerun tests.");
			var info = DriverDeployment.Inspect (package);
			var catalogue = await client.GetDriversAsync (info.Model, deadline.Token).ConfigureAwait (false);
			if (catalogue.Any (d => string.Equals (d.Model?.Trim (), info.Model.Trim (), StringComparison.OrdinalIgnoreCase)
				 && WorkflowDebugVersion.ParseVersion (d.Version) >= WorkflowDebugVersion.ParseVersion (info.Version)))
				throw new InvalidOperationException ("An equal or newer model version is already in the catalogue. Rerun the workflow to reconcile its Debug revision before deployment.");
			ActivationUncertain = true;
			var imported = await DriverDeployment.DeployAsync (client, _host, credential, plan.SshFingerprint, package, Timeout, deadline.Token).ConfigureAwait (false);
			await File.WriteAllTextAsync (Path.Combine (results, prefix + "-import.json"), JsonSerializer.Serialize (imported), deadline.Token).ConfigureAwait (false);
			var ready = await DriverInstanceLifecycle.EnsureAsync (client, imported.CatalogueId, target.InstanceName, target.LocationId, target.ExpectedDeviceId, Timeout, deadline.Token, RebootHandler (target)).ConfigureAwait (false);
			await File.WriteAllTextAsync (Path.Combine (results, prefix + "-activation.json"), JsonSerializer.Serialize (ready), deadline.Token).ConfigureAwait (false);
			ActivationUncertain = false;
			return ready;
			}
		private async Task<WorkflowTestOutcome> RunSuites (SuitePlan[] suites, bool live, CancellationToken token)
			{
			int passed = 0, failed = 0, skipped = 0;
			for (int index = 0; index < suites.Length; index++)
				{
				using var deadline = Deadline (token);
				await CheckSource (deadline.Token).ConfigureAwait (false);
				var outcome = await _remote.RunAsync (plan with
					{
					Host = _host
					}, credential, _test!.Model, suites[index], live, Path.Combine (results, (live ? "live-" : "processor-") + index), deadline.Token, lease.Owner).ConfigureAwait (false);
				passed += outcome.Passed;
				failed += outcome.Failed;
				skipped += outcome.Skipped;
				if (!outcome.MeetsGate)
					return new (passed, failed, skipped, false);
				}
			return new (passed, failed, skipped, true);
			}
		public Task<WorkflowTestOutcome> RunProcessorTestsAsync (CancellationToken token) => RunSuites (plan.ProcessorSuites, false, token);
		public Task<WorkflowTestOutcome> RunProcessorLiveTestsAsync (CancellationToken token) => RunSuites (plan.LiveSuites, true, token);
		public async Task DeployAndVerifyActualDriverAsync (CancellationToken token)
			{
			if (plan.Rollback != null)
				{
				using var deadline = Deadline (token);
				await CheckSource (deadline.Token).ConfigureAwait (false);
				_rollback = await WorkflowRollback.Prepared.CaptureAsync (client, plan.Rollback, plan.ActualDriver!, _actualPath!,
					Path.Combine (results, "rollback"), _host, credential, plan.SshFingerprint, Timeout, deadline.Token).ConfigureAwait (false);
				}
			_actual = await Activate (plan.ActualDriver!, _actualPath!, "actual", token).ConfigureAwait (false);
			if (initialConfiguration != null)
				{
				using var deadline = Deadline (token);
				ActivationUncertain = true;
				bool applied;
				try
					{
					applied = await WorkflowConfiguration.ApplyAsync (client, _actual, initialConfiguration, deadline.Token).ConfigureAwait (false);
					}
				catch (WorkflowConfiguration.Rejected rejected)
					{
					// Only our fixed diagnostics are safe to retain; never copy processor-returned error text.
					await File.WriteAllTextAsync (Path.Combine (results, "actual-configuration.json"),
						JsonSerializer.Serialize (new
							{
							_actual.DeviceId,
							Applied = false,
							Detail = rejected.Message
							}), CancellationToken.None).ConfigureAwait (false);
					throw;
					}
				await File.WriteAllTextAsync (Path.Combine (results, "actual-configuration.json"),
					JsonSerializer.Serialize (new
						{
						_actual.DeviceId,
						Applied = applied
						}), deadline.Token).ConfigureAwait (false);
				ActivationUncertain = false;
				}
			}
		public async Task<WorkflowTestOutcome> RunDeployedDriverLiveTestsAsync (CancellationToken token)
			{
			using var deadline = Deadline (token);
			var cases = new List<XElement> ();
			foreach (var check in plan.DeployedChecks)
				{
				bool passed = false;
				// Devices can become ready after the root driver reports Loaded.
				while (true)
					{
					var device = await client.GetDeviceAsync (check.UseActualDriver ? _actual!.DeviceId : check.DeviceId, deadline.Token).ConfigureAwait (false);
					if (device?.Model == check.Model && await BelongsToActualDriver (device, deadline.Token).ConfigureAwait (false)
						 && device.PropertyValues.TryGetValue (check.Property, out var value))
						passed = CheckProperty (check, value);
					if (passed || deadline.IsCancellationRequested)
						break;
					try
						{
						await Task.Delay (1000, deadline.Token).ConfigureAwait (false);
						}
					catch (OperationCanceledException) when (!token.IsCancellationRequested) { break; }
					}
				var test = new XElement ("test-case", new XAttribute ("name", check.Name), new XAttribute ("fullname", "InstalledDriver." + check.Name), new XAttribute ("result", passed ? "Passed" : "Failed"));
				if (!passed)
					test.Add (new XElement ("failure", new XElement ("message", "Installed device identity/state did not satisfy the configured read-only check.")));
				cases.Add (test);
				// Persist each observation without dumping configuration properties or credentials.
				SaveChecks (cases);
				if (!passed)
					break;
				}
			if (cases.Count == plan.DeployedChecks.Length && cases.All (c => (string?)c.Attribute ("result") == "Passed"))
				{
				for (int index = 0; index < plan.DeployedControls.Length; index++)
					{
					await CheckSource (token).ConfigureAwait (false);
					ActivationUncertain = true;
					var control = await InstalledControlRunner.RunAsync (client, lease, _actual!, plan.DeployedControls[index],
						Path.Combine (results, "installed-control-" + index), token).ConfigureAwait (false);
					ActivationUncertain = !control.RestorationConfirmed;
					var test = new XElement ("test-case", new XAttribute ("name", control.Name), new XAttribute ("fullname", "InstalledDriver." + control.Name), new XAttribute ("result", control.Passed ? "Passed" : "Failed"));
					if (!control.Passed)
						test.Add (new XElement ("failure", new XElement ("message", control.Detail)));
					cases.Add (test);
					SaveChecks (cases);
					if (!control.Passed)
						break;
					}
				}
			await client.WaitForDriverVersionAsync ([_actual!.DeviceId], _actual.Version, Timeout, token).ConfigureAwait (false);
			await CheckSource (token).ConfigureAwait (false);
			return new (cases.Count (c => (string?)c.Attribute ("result") == "Passed"), cases.Count (c => (string?)c.Attribute ("result") == "Failed"), 0, cases.Count == plan.DeployedChecks.Length + plan.DeployedControls.Length);
			}
		private async Task<bool> BelongsToActualDriver (DeviceInfo device, CancellationToken token)
			{
			var visited = new HashSet<int> ();
			while (visited.Add (device.Id))
				{
				if (device.Id == _actual!.DeviceId)
					return true;
				if (device.ParentDeviceId is not > 0)
					return false;
				var parent = await client.GetDeviceAsync (device.ParentDeviceId.Value, token).ConfigureAwait (false);
				if (parent == null)
					return false;
				device = parent;
				}
			return false;
			}
		private void SaveChecks (List<XElement> cases)
			{
			int failed = cases.Count (c => (string?)c.Attribute ("result") == "Failed");
			new XDocument (new XElement ("test-run", new XAttribute ("name", "InstalledDriver"), new XAttribute ("result", failed == 0 ? "Passed" : "Failed"),
				 new XAttribute ("total", cases.Count), new XAttribute ("passed", cases.Count - failed), new XAttribute ("failed", failed), new XAttribute ("skipped", 0), cases)).Save (Path.Combine (results, "InstalledDriver.xml"));
			}
		public async Task<ProcessorWorkflowResult> RollbackAfterFailureAsync (ProcessorWorkflowResult result, Action<WorkflowStageResult> report)
			{
			if (!WorkflowRollback.ShouldAttempt (result, RemoteExecutionConfirmedStopped, _rollback != null))
				return result;
			ActivationUncertain = true;
			WorkflowStageResult stage;
			using var recovery = new CancellationTokenSource (Timeout);
			try
				{
				await WorkflowRollback.ExecuteGuardedAsync (_rollback!, lease.BeginControlAsync, lease.EndControlAsync, recovery.Token).ConfigureAwait (false);
				_actual = _rollback!.RestoredDriver;
				ActivationUncertain = false;
				stage = new ("Rollback actual driver", "Passed", Detail: "Previous code restored and checked with current configuration preserved. The original workflow failure remains.");
				}
			catch
				{
				stage = new ("Rollback actual driver", "Error", Detail: "Rollback could not be verified. The reservation and recovery evidence are retained; no operation was retried.");
				}
			result = result with
				{
				Stages = result.Stages.Append (stage).ToArray ()
				};
			try
				{
				report (stage);
				}
			catch
				{
				ActivationUncertain = true;
				result = result with
					{
					Stages = result.Stages.Append (new WorkflowStageResult ("Save rollback evidence", "Error", Detail: "Rollback reporting failed; inspect retained recovery evidence.")).ToArray ()
					};
				}
			return result;
			}
		public async Task<WorkflowPackageCleanup.Result> RemoveTestPackageAsync (CancellationToken token)
			{
			if (!RemoteExecutionConfirmedStopped || HasTestInstance || _packageCleanup == null || _testPath == null)
				throw new InvalidOperationException ("Test package cleanup requires confirmed instance removal and an upload baseline.");
			ActivationUncertain = true;
			using var deadline = Deadline (token);
			var result = await _packageCleanup.RemoveAsync (client, _host, credential, plan.SshFingerprint, _testPath, deadline.Token).ConfigureAwait (false);
			ActivationUncertain = false;
			return result;
			}
		public async Task RemoveTestInstanceAsync (CancellationToken token)
			{
			if (_test == null || !RemoteExecutionConfirmedStopped || _test.DeviceId == _actual?.DeviceId || _test.DeviceId == plan.ActualDriver?.ExpectedDeviceId)
				throw new InvalidOperationException ("Test instance removal is not safe.");
			await client.RemoveDriverInstanceAsync (_test.DeviceId, _test.Model, _test.Version, Timeout, token, RebootHandler (plan.TestPackage)).ConfigureAwait (false);
			_test = null;
			ActivationUncertain = false;
			}
		private DriverRebootHandler? RebootHandler (PackageBuildPlan target)
			{
			if (!plan.AllowProcessorReboot)
				return null;
			string? evidence = null;
			return new (async (request, token) =>
			{
				if (!_remote.ExecutionConfirmedStopped)
					throw new InvalidOperationException ("A processor test may still be running; reboot cannot be submitted.");
				ActivationUncertain = true;
				evidence = Path.Combine (results, "reboot-" + ++_rebootNumber + ".json");
				await File.WriteAllTextAsync (evidence, JsonSerializer.Serialize (new
					{
					Request = request,
					State = "AuthorizedBeforeSubmission",
					AllowProcessorReboot = true
					}), token).ConfigureAwait (false);
			}, async (request, previous, token) =>
			{
				if (evidence == null)
					throw new InvalidOperationException ("Missing durable reboot authorization evidence.");
				var recovered = await WorkflowRestart.CompleteAsync (request, previous,
						 (state, ct) => File.WriteAllTextAsync (evidence, JsonSerializer.Serialize (new { Request = request, State = state, AllowProcessorReboot = true }), ct),
						 async ct =>
						 {
							 var operationId = await previous.RequestProcessorRebootAsync ((_, _) => Task.FromResult (true),
									  "Authorized development workflow driver update", ct).ConfigureAwait (false);
							 return new ProcessorRebootResult (operationId != null ? ProcessorRebootStatus.Accepted : ProcessorRebootStatus.Cancelled);
						 },
						 ct => ProcessorRestartRecovery.WaitAsync (previous, async connectToken =>
						 {
							 if (!string.IsNullOrWhiteSpace (plan.ProcessorSystemName))
								 _host = WorkflowRestart.SelectRestartAddress (await ProcessorDiscovery.FindAsync (connectToken).ConfigureAwait (false), plan.ProcessorSystemName);
							 return await ConfigurationClient.ConnectAsync (new ()
								 {
								 Host = _host,
								 CertificateSha256 = plan.CertificateSha256,
								 RequestTimeout = TimeSpan.FromSeconds (plan.StageTimeoutSeconds)
								 }, credential, connectToken).ConfigureAwait (false);
						 }, Timeout, ct),
						 ct => lease.VerifyAfterReconnectAsync (_host, ct), token).ConfigureAwait (false);
				client = recovered;
				return recovered;
			})
				{
				RebootAfterInstall = target.RebootAfterInstall,
				RebootAfterRemoval = target.RebootAfterRemoval,
				AdditionalRemovalRebootDeviceIds = target.AdditionalRemovalRebootDeviceIds
				};
			}

		public async ValueTask DisposeAsync ()
			{
			if (_rollback != null)
				await _rollback.DisposeAsync ().ConfigureAwait (false);
			foreach (var artifact in _artifacts)
				artifact.Dispose ();
			if (!ReferenceEquals (client, _initialClient))
				await client.DisposeAsync ().ConfigureAwait (false);
			}
		}
	public static bool CheckProperty (PropertyCheck check, JsonElement value) => check.Expected is { } expected
		 ? JsonElement.DeepEquals (expected, value)
		 : value.ValueKind == JsonValueKind.Number && value.TryGetDouble (out var number) && double.IsFinite (number) && number >= check.Minimum && number <= check.Maximum;
	}