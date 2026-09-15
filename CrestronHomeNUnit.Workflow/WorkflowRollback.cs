// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

internal static class WorkflowRollback
	{
	internal sealed record Node (int Id, int? Parent, string? Model, string? Name, int? Room);
	internal interface ISession
		{
		Task VerifyCurrentAsync (CancellationToken token);
		Task RecordAsync (string phase);
		Task ImportPreviousAsync (CancellationToken token);
		Task SwapOnceAsync (CancellationToken token);
		Task VerifyRestoredAsync (CancellationToken token);
		}

	// Only a completed failed post-deployment test stage can trigger automatic rollback.
	// Network errors, cancellation and unknown test/control completion are deliberately excluded.
	internal static bool ShouldAttempt (ProcessorWorkflowResult result, bool stopped, bool prepared)
		=> prepared && stopped && result.DriverUpdateAttempted && result.DriverUpdateVerified && !result.Passed
			&& result.Stages.Count (s => s.Stage == "Deployed driver live" && s.Outcome == "Failed") == 1
			&& result.Stages.Where (s => s.Stage != "Deployed driver live").All (s => s.Outcome == "Passed");

	internal static async Task ExecuteAsync (ISession session, CancellationToken token)
		{
		await session.VerifyCurrentAsync (token).ConfigureAwait (false);
		await session.RecordAsync ("ImportIntent").ConfigureAwait (false);
		token.ThrowIfCancellationRequested ();
		await session.ImportPreviousAsync (token).ConfigureAwait (false);
		await session.VerifyCurrentAsync (token).ConfigureAwait (false);
		await session.RecordAsync ("SwapIntent").ConfigureAwait (false);
		token.ThrowIfCancellationRequested ();
		await session.SwapOnceAsync (token).ConfigureAwait (false);
		await session.VerifyRestoredAsync (token).ConfigureAwait (false);
		await session.RecordAsync ("Restored").ConfigureAwait (false);
		}

	internal static async Task ExecuteGuardedAsync (ISession session, Func<CancellationToken, Task> begin,
		Func<CancellationToken, Task> end, CancellationToken token)
		{
		await begin (token).ConfigureAwait (false);
		await ExecuteAsync (session, token).ConfigureAwait (false);
		// Deliberately no finally: an uncertain mutation or restoration retains the execution marker.
		await end (token).ConfigureAwait (false);
		}

	internal static Node[] Scope (IReadOnlyList<DeviceInfo> devices, int id, string model, string version)
		{
		if (devices.Select (d => d.Id).Distinct ().Count () != devices.Count)
			throw new InvalidDataException ("Device inventory contains duplicate identities.");
		var root = devices.SingleOrDefault (d => d.Id == id) ?? throw new InvalidOperationException ("Rollback target is missing.");
		string? Property (string key) => root.PropertyValues.TryGetValue (key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString () : null;
		if (root.Model != model || root.ParentDeviceId != -6 || Property ("cp.driverConfiguration:driverLoadingStatus") != "Loaded"
			|| !Version.TryParse (Property ("cp.driverInformation:version"), out var current) || current != WorkflowDebugVersion.ParseVersion (version)
			|| !root.PropertyValues.TryGetValue ("cp.driverConfiguration:swapDriverRequiresReboot", out var reboot) || reboot.ValueKind != JsonValueKind.False
			|| !root.PropertyValues.TryGetValue ("cp.driverConfiguration:isConfigured", out var configured) || configured.ValueKind != JsonValueKind.True)
			throw new InvalidOperationException ("Rollback requires the exact configured, Loaded, reboot-free root driver.");
		var selected = new HashSet<int> { id };
		while (true)
			{
			var added = devices.Where (d => d.ParentDeviceId.HasValue && selected.Contains (d.ParentDeviceId.Value)).Select (d => d.Id).ToArray ();
			int count = selected.Count;
			selected.UnionWith (added);
			if (selected.Count == count)
				break;
			}
		return devices.Where (d => selected.Contains (d.Id)).OrderBy (d => d.Id).Select (d => new Node (d.Id, d.ParentDeviceId, d.Model, d.Name, d.LocationId)).ToArray ();
		}

	internal static void VerifyObservation (RollbackConfigurationObservation value, string nonce, int deviceId, string model,
		string version, string hash, string? expectedIdentity)
		{
		if (value.RequestId != nonce || value.DeviceId != deviceId || value.Model != model
			|| !Version.TryParse (value.InstalledVersion, out var observed) || observed != WorkflowDebugVersion.ParseVersion (version)
			|| !string.Equals (value.PreviousPackageSha256, hash, StringComparison.OrdinalIgnoreCase) || !value.CompatibleWithPreviousVersion
			|| value.ConfigurationIdentity == null || value.ConfigurationIdentity.Length != 64 || !value.ConfigurationIdentity.All (Uri.IsHexDigit)
			|| expectedIdentity != null && !string.Equals (expectedIdentity, value.ConfigurationIdentity, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("The configuration probe did not verify current configuration compatibility and identity for this rollback.");
		}

	internal sealed class Prepared : ISession, IAsyncDisposable
		{
		private readonly ConfigurationClient _client;
		private readonly DriverRollbackPlan _plan;
		private readonly PackageBuildPlan _target;
		private readonly DriverPackageInfo _old, _next;
		private readonly string _directory, _path, _host, _fingerprint;
		private readonly NetworkCredential _credential;
		private readonly TimeSpan _timeout;
		private readonly FileStream _locked;
		private Node[] _scope = [];
		private string? _configuration;
		private DriverDeploymentResult? _imported;
		private int _attempted;
		internal int DeviceId => _target.ExpectedDeviceId!.Value;
		internal DriverInstanceReady RestoredDriver => new (DeviceId, _old.Model, _old.Version, "RolledBack");
		private Prepared (ConfigurationClient client, DriverRollbackPlan plan, PackageBuildPlan target, DriverPackageInfo old, DriverPackageInfo next,
			string directory, string path, string host, NetworkCredential credential, string fingerprint, TimeSpan timeout, FileStream locked)
			{
			_client = client;
			_plan = plan;
			_target = target;
			_old = old;
			_next = next;
			_directory = directory;
			_path = path;
			_host = host;
			_credential = credential;
			_fingerprint = fingerprint;
			_timeout = timeout;
			_locked = locked;
			}

		internal static async Task<Prepared> CaptureAsync (ConfigurationClient client, DriverRollbackPlan plan, PackageBuildPlan target,
			string nextPackage, string directory, string host, NetworkCredential credential, string fingerprint, TimeSpan timeout, CancellationToken token)
			{
			plan.Validate ();
			WorkflowEvidence.PrepareLocalResults (directory);
			var next = DriverDeployment.Inspect (nextPackage);
			var path = Path.Combine (directory, Path.GetFileName (plan.PreviousPackage));
			await using (var original = new FileStream (plan.PreviousPackage, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
				if (original.Length is <= 0 or > 64 * 1024 * 1024
					|| !string.Equals (Convert.ToHexString (await SHA256.HashDataAsync (original, token).ConfigureAwait (false)), plan.Sha256, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException ("Previous driver package did not match the planned SHA-256.");
				original.Position = 0;
				await using var destination = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				await original.CopyToAsync (destination, token).ConfigureAwait (false);
				}
			var locked = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
			try
				{
				var old = DriverDeployment.Inspect (path);
				if (Guid.Parse (old.DriverId) != Guid.Parse (next.DriverId) || old.Model != next.Model
					|| WorkflowDebugVersion.ParseVersion (old.Version) >= WorkflowDebugVersion.ParseVersion (next.Version))
					throw new InvalidDataException ("Rollback must name an earlier package of this exact driver.");
				var prepared = new Prepared (client, plan, target, old, next, directory, path, host, credential, fingerprint, timeout, locked);
				prepared._scope = Scope (await client.GetDevicesAsync (token).ConfigureAwait (false), prepared.DeviceId, old.Model, old.Version);
				var root = prepared._scope.Single (n => n.Id == prepared.DeviceId);
				if (root.Name != target.InstanceName || root.Room != target.LocationId)
					throw new InvalidOperationException ("Previous driver name or room differs from the plan.");
				prepared._configuration = await prepared.ProbeAsync ("BeforeUpdate", old.Version, token).ConfigureAwait (false);
				await prepared.RecordAsync ("Prepared").ConfigureAwait (false);
				return prepared;
				}
			catch { locked.Dispose (); throw; }
			}

		private async Task<string> ProbeAsync (string phase, string version, CancellationToken token)
			{
			var nonce = Guid.NewGuid ().ToString ("N");
			var request = Path.Combine (_directory, phase + "-" + nonce + "-request.json");
			var response = Path.Combine (_directory, phase + "-" + nonce + "-response.json");
			await File.WriteAllTextAsync (request, JsonSerializer.Serialize (new
				{
				RequestId = nonce,
				Phase = phase,
				DeviceId,
				Model = _old.Model,
				InstalledVersion = version,
				PreviousVersion = _old.Version,
				PreviousPackageSha256 = _plan.Sha256,
				PreserveCurrentConfiguration = true
				}), token).ConfigureAwait (false);
			var probe = _plan.ConfigurationProbe;
			var exit = await WorkflowEvidence.ProcessAsync (probe.Executable, probe.Arguments.Concat (["--request", request, "--response", response]),
				probe.WorkingDirectory, Path.Combine (_directory, phase + "-" + nonce + ".log"), token).ConfigureAwait (false);
			if (exit != 0)
				throw new InvalidOperationException ("Configuration compatibility probe failed.");
			await using var file = new FileStream (response, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (file.Length is <= 0 or > 65536)
				throw new InvalidDataException ("Configuration probe response exceeded its bound.");
			var value = await JsonSerializer.DeserializeAsync<RollbackConfigurationObservation> (file, cancellationToken: token).ConfigureAwait (false)
				?? throw new InvalidDataException ("Missing configuration compatibility observation.");
			VerifyObservation (value, nonce, DeviceId, _old.Model, version, _plan.Sha256, _configuration);
			return value.ConfigurationIdentity;
			}
		private async Task VerifyAsync (string version, string phase, CancellationToken token)
			{
			var scope = Scope (await _client.GetDevicesAsync (token).ConfigureAwait (false), DeviceId, _old.Model, version);
			if (!_scope.SequenceEqual (scope))
				throw new InvalidOperationException ("The driver or its child identities/rooms changed; automatic rollback is unsafe.");
			await ProbeAsync (phase, version, token).ConfigureAwait (false);
			}
		public Task VerifyCurrentAsync (CancellationToken token) => VerifyAsync (_next.Version, "BeforeRollback", token);
		public Task RecordAsync (string phase) => File.WriteAllTextAsync (Path.Combine (_directory, phase + ".json"),
			JsonSerializer.Serialize (new
				{
				Phase = phase,
				DeviceId,
				PreviousPackage = _old,
				CurrentPackage = _next,
				Sha256 = _plan.Sha256,
				ConfigurationIdentity = _configuration,
				PreserveCurrentConfiguration = true,
				Scope = _scope,
				Utc = DateTimeOffset.UtcNow
				}));
		public async Task ImportPreviousAsync (CancellationToken token)
			{
			if (Interlocked.Exchange (ref _attempted, 1) != 0)
				throw new InvalidOperationException ("Rollback cannot be automatically retried.");
			_locked.Position = 0;
			if (!string.Equals (Convert.ToHexString (await SHA256.HashDataAsync (_locked, token).ConfigureAwait (false)), _plan.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Previous package bytes changed.");
			_locked.Position = 0;
			_imported = await DriverDeployment.DeployAsync (_client, _host, _credential, _fingerprint, _path, _timeout, token).ConfigureAwait (false);
			await File.WriteAllTextAsync (Path.Combine (_directory, "Import.json"), JsonSerializer.Serialize (_imported), token).ConfigureAwait (false);
			}
		public async Task SwapOnceAsync (CancellationToken token)
			{
			var eligible = await _client.GetDriverUpdateEligibilityAsync (_imported!.CatalogueId, token).ConfigureAwait (false);
			if (eligible?.IsSupportsSwapDriver != true || eligible.IsSwapDriverRequiresReboot != false
				|| eligible.EligibleDeviceIds?.SequenceEqual ([DeviceId]) != true
				|| !Version.TryParse (eligible.InstalledDriverVersion, out var installed) || installed != WorkflowDebugVersion.ParseVersion (_next.Version)
				|| !Version.TryParse (eligible.AvailableDriverVersion, out var available) || available != WorkflowDebugVersion.ParseVersion (_old.Version))
				throw new InvalidOperationException ("The processor did not authorize this exact single-instance reboot-free rollback.");
			var operation = await _client.BeginDriverUpdateAsync (new (_imported.CatalogueId, eligible), token).ConfigureAwait (false);
			await File.WriteAllTextAsync (Path.Combine (_directory, "Submitted.json"), JsonSerializer.Serialize (new
				{
				OperationId = operation
				}), token).ConfigureAwait (false);
			var status = await _client.WaitForOperationAsync (operation, _timeout, token).ConfigureAwait (false);
			if (status.Status == "Failed")
				throw new InvalidOperationException ("Rollback operation failed.");
			var completed = await _client.WaitForDriverSwapAsync (operation, _imported.CatalogueId, _timeout, token).ConfigureAwait (false);
			if (completed.IsRebootRequired || completed.DeviceIdsRequiringReconfiguration.Length != 0)
				throw new InvalidOperationException ("Rollback completion requires reboot or reconfiguration; automatic recovery stopped.");
			await File.WriteAllTextAsync (Path.Combine (_directory, "SwapCompleted.json"), JsonSerializer.Serialize (completed), token).ConfigureAwait (false);
			}
		public async Task VerifyRestoredAsync (CancellationToken token)
			{
			await _client.WaitForDriverVersionAsync ([DeviceId], _old.Version, _timeout, token).ConfigureAwait (false);
			await VerifyAsync (_old.Version, "AfterRollback", token).ConfigureAwait (false);
			foreach (var check in _plan.VerificationChecks)
				{
				while (true)
					{
					var id = check.UseActualDriver ? DeviceId : check.DeviceId;
					if (!_scope.Any (n => n.Id == id && n.Model == check.Model))
						throw new InvalidOperationException ("Rollback health check is outside the captured driver scope.");
					var device = await _client.GetDeviceAsync (id, token).ConfigureAwait (false);
					if (device?.Model == check.Model && device.PropertyValues.TryGetValue (check.Property, out var value) && WorkflowRunner.CheckProperty (check, value))
						break;
					await Task.Delay (500, token).ConfigureAwait (false);
					}
				}
			await VerifyAsync (_old.Version, "RestorationConfirmed", token).ConfigureAwait (false);
			}
		public ValueTask DisposeAsync () => _locked.DisposeAsync ();
		}
	}