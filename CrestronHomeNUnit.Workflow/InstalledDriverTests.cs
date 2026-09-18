// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

public sealed record InstalledDriverTestResult (WorkflowTestOutcome? Tests, bool RestorationConfirmed,
	bool CleanupConfirmed, bool CandidateVerified, bool ReservationsReleased, string Detail)
	{
	public bool Passed => Tests?.MeetsGate == true && RestorationConfirmed && CleanupConfirmed && CandidateVerified && ReservationsReleased;
	}

internal interface IInstalledDriverTestOperations
	{
	bool ProcessorAcquired { get; }
	bool AndroidAcquired { get; }
	Task AcquireProcessorAsync (CancellationToken token);
	Task AcquireAndroidAsync (CancellationToken token);
	Task VerifyCandidateAsync (string phase, CancellationToken token);
	Task BeginControlAsync (CancellationToken token);
	Task<AndroidTestOutcome> RunTestsAsync (CancellationToken token);
	Task EndControlAsync (CancellationToken token);
	Task ReleaseAndroidAsync (CancellationToken token);
	Task ReleaseProcessorAsync (CancellationToken token);
	}

/// <summary>Runs selected Android fixtures against a pinned existing driver, without deploying or reloading it.</summary>
public static class InstalledDriverTests
	{
	public static async Task<InstalledDriverTestResult> RunAsync (InstalledDriverTestPlan plan, NetworkCredential credential,
		string results, CancellationToken token = default)
		{
		plan.Validate ();
		ArgumentNullException.ThrowIfNull (credential);
		plan = plan with { SourceRoots = plan.SourceRoots.ToArray (), AndroidTests = plan.AndroidTests with
			{ RequiredTests = plan.AndroidTests.RequiredTests?.ToArray (), ManagedChildren = plan.AndroidTests.ManagedChildren.ToArray () } };
		results = Path.GetFullPath (results);
		WorkflowEvidence.PrepareLocalResults (results);
		await using var report = new FileStream (Path.Combine (results, "InstalledDriverTests.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		await JsonSerializer.SerializeAsync (report, new { State = "Preparing", DriverUpdateAttempted = false }, cancellationToken: token).ConfigureAwait (false);
		report.Flush (flushToDisk: true);
		try
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (plan.TimeoutSeconds));
			// Snapshot once, holding both the source and the retained candidate against replacement.
			var package = Path.Combine (results, Path.GetFileName (plan.PackagePath));
			await using (var output = new FileStream (package, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			await using (var input = new FileStream (plan.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
				if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException ("Candidate must fit within 64 MiB.");
				await input.CopyToAsync (output, deadline.Token).ConfigureAwait (false);
				output.Flush (flushToDisk: true);
				}
			await using var snapshot = new FileStream (package, FileMode.Open, FileAccess.Read, FileShare.Read);
			var digest = Convert.ToHexString (await SHA256.HashDataAsync (snapshot, deadline.Token).ConfigureAwait (false));
			if (!digest.Equals (plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Candidate bytes do not match the trusted package receipt; no reservation or tests started.");
			var identity = DriverDeployment.Inspect (package);
			RequirePackage (plan.Target, identity);
			using var profileInput = new FileStream (plan.AndroidTests.ProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			var profile = AndroidWorkflowSession.Read<AndroidSessionProfile> (plan.AndroidTests.ProfilePath);
			profile.Validate ();
			var profileHash = SHA256.HashData (await File.ReadAllBytesAsync (plan.AndroidTests.ProfilePath, deadline.Token).ConfigureAwait (false));
			var source = await WorkflowEvidence.SourceDigestAsync (plan.SourceRoots, deadline.Token).ConfigureAwait (false);
			using var operations = new Operations (plan, credential, results, package, identity, profile, profileHash, source);
			var result = await RunCoreAsync (operations, deadline.Token).ConfigureAwait (false);
			report.Position = 0;
			report.SetLength (0);
			await JsonSerializer.SerializeAsync (report, result, cancellationToken: CancellationToken.None).ConfigureAwait (false);
			report.Flush (flushToDisk: true);
			return result;
			}
		catch
			{
			try
				{
				report.Position = 0;
				report.SetLength (0);
				await JsonSerializer.SerializeAsync (report, new { State = "Failed", DriverUpdateAttempted = false,
					Detail = "Preparation or evidence recording failed; inspect Phases.jsonl for reservation state." }, cancellationToken: CancellationToken.None).ConfigureAwait (false);
				report.Flush (flushToDisk: true);
				}
			catch { /* Keep the original failure when evidence storage also fails. */ }
			throw;
			}
		}

	internal static async Task<InstalledDriverTestResult> RunCoreAsync (IInstalledDriverTestOperations operations, CancellationToken token)
		{
		bool safe = true, verified = false, released = false;
		AndroidTestOutcome? outcome = null;
		string detail = "Tests did not complete.";
		try
			{
			await operations.AcquireProcessorAsync (token).ConfigureAwait (false);
			await operations.AcquireAndroidAsync (token).ConfigureAwait (false);
			await operations.VerifyCandidateAsync ("Before", token).ConfigureAwait (false);
			// Even an interrupted guard creation must be inspected before another run.
			safe = false;
			await operations.BeginControlAsync (token).ConfigureAwait (false);
			outcome = await operations.RunTestsAsync (token).ConfigureAwait (false);
			if (outcome.SafeToRelease)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (30));
				await operations.EndControlAsync (cleanup.Token).ConfigureAwait (false);
				safe = true;
				await operations.VerifyCandidateAsync ("After", token).ConfigureAwait (false);
				verified = true;
				detail = outcome.Passed ? "Selected installed-driver tests and restoration passed." : "Selected tests failed; restoration and owned-child cleanup were confirmed.";
				}
			else detail = "Restoration or owned-child cleanup is unconfirmed; reservations retained for inspection.";
			}
		catch (Exception)
			{
			detail = token.IsCancellationRequested ? "Cancelled or timed out; inspect phase receipts and reservation state before retrying."
				: "Installed-driver phase could not complete; inspect phase receipts and reservation state before retrying.";
			}
		finally
			{
			if (operations.ProcessorAcquired && safe)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (30));
				try
					{
					if (operations.AndroidAcquired) await operations.ReleaseAndroidAsync (cleanup.Token).ConfigureAwait (false);
					await operations.ReleaseProcessorAsync (cleanup.Token).ConfigureAwait (false);
					released = true;
					}
				catch { detail += " Reservation release could not be confirmed."; }
				}
			}
		return new (outcome?.Tests, outcome?.RestorationConfirmed == true, outcome?.CleanupConfirmed == true, verified, released, detail);
		}

	internal static void RequirePackage (InstalledDriverTestTarget target, DriverPackageInfo package)
		{
		if (target.Model != package.Model || !SameVersion (target.Version, package.Version))
			throw new InvalidDataException ("Candidate model or version differs from the selected installed target.");
		}

	internal static void RequireInstance (InstalledDriverTestTarget target, DriverPackageInfo package, DeviceInfo? device, DriverInfo? catalogue)
		{
		RequirePackage (target, package);
		string? Property (string key) => device?.PropertyValues.TryGetValue (key, out var value) == true && value.ValueKind == JsonValueKind.String ? value.GetString () : null;
		if (device == null || device.Id != target.DeviceId || device.ParentDeviceId != target.ParentDeviceId || device.Name != target.Name ||
			device.Model != target.Model || device.LocationId != target.LocationId || !SameVersion (Property ("cp.driverInformation:version"), target.Version) ||
			Property ("cp.driverConfiguration:driverLoadingStatus") != "Loaded" || Property ("cp.driverInformation:developer") != target.Developer ||
			Property ("cp.driverInformation:controlType") != target.ControlType || catalogue == null || catalogue.Id != target.CatalogueId ||
			catalogue.Model != package.Model || catalogue.Manufacturer != package.Manufacturer || catalogue.Developer != target.Developer)
			throw new InvalidDataException ("Existing device identity, loaded version or selected catalogue association could not be confirmed.");
		}

	private static bool SameVersion (string? first, string? second) => Version.TryParse (first, out var left) && Version.TryParse (second, out var right) && left == right;

	private sealed class Operations (InstalledDriverTestPlan plan, NetworkCredential credential, string results, string package,
		DriverPackageInfo identity, AndroidSessionProfile profile, byte[] profileHash, string source) : IInstalledDriverTestOperations, IDisposable
		{
		private readonly string _owner = Guid.NewGuid ().ToString ("N");
		private ProcessorLease? _processor;
		private AndroidSessionLease? _android;
		public bool ProcessorAcquired => _processor != null;
		public bool AndroidAcquired => _android != null;
		private ProcessorConnectionOptions Connection => new () { Host = plan.Host, CertificateSha256 = plan.CertificateSha256, RequestTimeout = TimeSpan.FromSeconds (30) };
		private void Record (string phase, string state) => File.AppendAllText (Path.Combine (results, "Phases.jsonl"), JsonSerializer.Serialize
			(new { RunId = _owner, Phase = phase, State = state, ObservedUtc = DateTimeOffset.UtcNow }) + Environment.NewLine);
		public async Task AcquireProcessorAsync (CancellationToken token)
			{
			Record ("Processor reservation", "Starting");
			_processor = await ProcessorLease.AcquireAsync (plan.Host, credential, plan.SshFingerprint, _owner, TimeSpan.FromSeconds (plan.LeaseWaitSeconds), token).ConfigureAwait (false);
			Record ("Processor reservation", "Held");
			}
		public Task AcquireAndroidAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Record ("Android reservation", "Starting");
			_android = AndroidSessionLease.Acquire (profile.LockPath, _owner);
			Record ("Android reservation", "Held");
			return Task.CompletedTask;
			}
		private async Task VerifyOwnership (CancellationToken token)
			{
			await _processor!.VerifyAfterReconnectAsync (plan.Host, token).ConfigureAwait (false);
			AndroidSessionLease.VerifyOwner (profile.LockPath, _owner);
			var currentProfileHash = SHA256.HashData (await File.ReadAllBytesAsync (plan.AndroidTests.ProfilePath, token).ConfigureAwait (false));
			if (source != await WorkflowEvidence.SourceDigestAsync (plan.SourceRoots, token).ConfigureAwait (false) ||
				!profileHash.AsSpan ().SequenceEqual (currentProfileHash))
				throw new InvalidDataException ("Fixture source or Android profile changed during the phase.");
			}
		public async Task VerifyCandidateAsync (string phase, CancellationToken token)
			{
			Record (phase + " candidate verification", "Starting");
			await VerifyOwnership (token).ConfigureAwait (false);
			await using (var client = await ConfigurationClient.ConnectAsync (Connection, credential, token).ConfigureAwait (false))
				RequireInstance (plan.Target, identity, await client.GetDeviceAsync (plan.Target.DeviceId, token).ConfigureAwait (false),
					await client.GetDriverAsync (plan.Target.CatalogueId, token).ConfigureAwait (false));
			var payload = await DriverPayloadInspection.CompareAsync (plan.Host, credential, plan.SshFingerprint, package, plan.PackageSha256,
				plan.Target.CatalogueId, TimeSpan.FromMinutes (2), token).ConfigureAwait (false);
			// Use a fresh configuration session after file comparison; never substitute a saved LKG snapshot.
			await using (var client = await ConfigurationClient.ConnectAsync (Connection, credential, token).ConfigureAwait (false))
				RequireInstance (plan.Target, identity, await client.GetDeviceAsync (plan.Target.DeviceId, token).ConfigureAwait (false),
					await client.GetDriverAsync (plan.Target.CatalogueId, token).ConfigureAwait (false));
			await VerifyOwnership (token).ConfigureAwait (false);
			await File.WriteAllTextAsync (Path.Combine (results, phase + "Candidate.json"), JsonSerializer.Serialize
				(new { RunId = _owner, Target = plan.Target, Payload = payload, FixtureSourceSha256 = source,
					AndroidProfileSha256 = Convert.ToHexString (profileHash), plan.PackageSourceCommit }), token).ConfigureAwait (false);
			Record (phase + " candidate verification", "Passed");
			}
		public async Task BeginControlAsync (CancellationToken token)
			{
			Record ("Control guard", "Starting");
			await _processor!.BeginControlAsync (token).ConfigureAwait (false);
			Record ("Control guard", "Held");
			}
		public async Task<AndroidTestOutcome> RunTestsAsync (CancellationToken token)
			{
			Record ("Android tests", "Starting");
			var actual = new DriverInstanceReady (plan.Target.DeviceId, plan.Target.Model, plan.Target.Version, "ExistingVerified");
			var result = await WorkflowManagedAndroid.RunAsync (plan.AndroidTests, actual, Path.Combine (results, "AndroidManagedChildren"),
				TimeSpan.FromSeconds (120), ct => ConfigurationClient.ConnectAsync (Connection, credential, ct), VerifyOwnership,
				(bindings, ct) => WorkflowAndroid.RunAsync (plan.AndroidTests, profile, _owner, plan.Host, actual.DeviceId, package, source,
					Path.Combine (results, "AndroidUI"), ct, releaseSourceCommit: plan.PackageSourceCommit, managedDevices: bindings), token).ConfigureAwait (false);
			Record ("Android tests", result.Passed ? "Passed" : "Failed");
			return result;
			}
		public async Task EndControlAsync (CancellationToken token)
			{
			await _processor!.EndControlAsync (token).ConfigureAwait (false);
			Record ("Control guard", "Released");
			}
		public Task ReleaseAndroidAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			_android!.Release ();
			Record ("Android reservation", "Released");
			return Task.CompletedTask;
			}
		public async Task ReleaseProcessorAsync (CancellationToken token)
			{
			await _processor!.ReleaseAsync (token).ConfigureAwait (false);
			Record ("Processor reservation", "Released");
			}
		public void Dispose () { _android?.Dispose (); _processor?.Dispose (); }
		}
	}