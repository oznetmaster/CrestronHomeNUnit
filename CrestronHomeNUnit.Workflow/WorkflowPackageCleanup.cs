// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Client;

using Renci.SshNet;

namespace CrestronHomeNUnit.Workflow;

// Only created before this workflow uploads its test package, under the shared lease.
// This is not a general catalogue purge and never removes pre-existing paths.
internal sealed class WorkflowPackageCleanup (HashSet<string> protectedPaths, string owner, string evidence)
	{
	internal const string Storage = "/user/ThirdPartyDrivers/Storage/Rad/";
	private const string Manifest = "/user/ThirdPartyDrivers/LocalRad.manifest";
	private const string Lease = "/user/CrestronHomeNUnit-WorkflowLease";
	private const string Guard = Lease + ".ActiveTest";
	private int _attempted;
	internal sealed record Candidate (string Path, string[] Models, bool Protected);
	internal sealed record Result (bool StorageRemoved, bool CatalogueCached, long Bytes, string Detail);
	internal static async Task<ProcessorWorkflowResult> AfterSuccessfulRunAsync (ProcessorWorkflowResult result, bool enabled,
		 Func<Task<Result>> cleanup, Action<WorkflowStageResult> report)
		{
		if (!enabled || !result.Passed) return result;
		WorkflowStageResult stage;
		try
			{
			var removed = await cleanup ().ConfigureAwait (false);
			stage = new ("Remove temporary test package", "Passed", Detail: removed.Detail);
			}
		catch
			{
			stage = new ("Remove temporary test package", "Error", Detail: "Package cleanup could not be confirmed; inspect its retained evidence and processor reservation before another workflow.");
			}
		result = result with { Stages = result.Stages.Append (stage).ToArray () };
		try { report (stage); }
		catch
			{
			result = result with { Stages = result.Stages.Append (new WorkflowStageResult ("Save cleanup evidence", "Error", Detail: "Cleanup stage reporting failed.")).ToArray () };
			}
		return result;
		}

	internal static async Task<WorkflowPackageCleanup> CaptureAsync (string host, NetworkCredential credential, string fingerprint,
		 string owner, string evidence, CancellationToken token)
		{
		using var sftp = await Connect (host, credential, fingerprint, token).ConfigureAwait (false);
		await VerifyOwner (sftp, owner, token).ConfigureAwait (false);
		await Idle (sftp, token).ConfigureAwait (false);
		var paths = new HashSet<string> (StringComparer.Ordinal);
		await foreach (var entry in sftp.ListDirectoryAsync (Storage, token).ConfigureAwait (false))
			if (entry.Name is not "." and not "..") paths.Add (entry.FullName);
		Directory.CreateDirectory (evidence);
		await SaveNew (Path.Combine (evidence, "baseline.json"), JsonSerializer.SerializeToUtf8Bytes (new { Owner = owner, ProtectedPaths = paths }), token).ConfigureAwait (false);
		return new (paths, owner, evidence);
		}

	internal async Task<Result> RemoveAsync (ConfigurationClient client, string host, NetworkCredential credential, string fingerprint,
		 string localPackage, CancellationToken token)
		{
		if (Interlocked.Exchange (ref _attempted, 1) != 0)
			throw new InvalidOperationException ("Package cleanup cannot be retried automatically.");
		var package = DriverDeployment.Inspect (localPackage);
		byte[] expected = await File.ReadAllBytesAsync (localPackage, token).ConfigureAwait (false);
		using var sftp = await Connect (host, credential, fingerprint, token).ConfigureAwait (false);
		await VerifyOwner (sftp, owner, token).ConfigureAwait (false);
		await Idle (sftp, token).ConfigureAwait (false);
		if (await client.IsDriverRefreshInProgressAsync (token).ConfigureAwait (false) != false)
			throw new InvalidOperationException ("Catalogue refresh must be confirmed idle before cleanup.");
		byte[] manifest = await Read (sftp, Manifest, 4 * 1024 * 1024, token).ConfigureAwait (false);
		var devices = await client.GetDevicesAsync (token).ConfigureAwait (false);
		var candidate = SelectCandidate (manifest, package, protectedPaths, devices.Select (d => d.Model));
		if (candidate.Protected)
			return await Finish (new (false, false, 0, "A pre-existing package path was preserved."), token).ConfigureAwait (false);
		byte[] stored = await Read (sftp, candidate.Path, 64 * 1024 * 1024, token).ConfigureAwait (false);
		VerifyHash (expected, stored);
		await SaveNew (Path.Combine (evidence, "package.pkg"), stored, token).ConfigureAwait (false);
		await SaveNew (Path.Combine (evidence, "LocalRad.before.manifest"), manifest, token).ConfigureAwait (false);
		await SaveNew (Path.Combine (evidence, "plan.json"), JsonSerializer.SerializeToUtf8Bytes (new
			{
			candidate.Path, Package = package, Sha256 = Convert.ToHexString (SHA256.HashData (stored)), Bytes = stored.Length
			}), token).ConfigureAwait (false);
		// Test hosts and lease release use this same exclusive marker. Hold it through refresh.
		string marker = "cleanup:" + owner;
		await using (var guard = await sftp.OpenAsync (Guard, FileMode.CreateNew, FileAccess.Write, token).ConfigureAwait (false))
			{
			await guard.WriteAsync (Encoding.UTF8.GetBytes (marker), token).ConfigureAwait (false);
			await guard.FlushAsync (token).ConfigureAwait (false);
			}
		// From this point an interrupted operation retains both marker and lease for inspection.
		await VerifyOwner (sftp, owner, token).ConfigureAwait (false);
		await Idle (sftp, token, allowGuard: true).ConfigureAwait (false);
		var current = await Read (sftp, Manifest, 4 * 1024 * 1024, token).ConfigureAwait (false);
		if (!manifest.AsSpan ().SequenceEqual (current))
			throw new InvalidOperationException ("The catalogue changed after cleanup planning.");
		var before = await client.GetDevicesAsync (token).ConfigureAwait (false);
		SelectCandidate (current, package, protectedPaths, before.Select (d => d.Model));
		VerifyHash (expected, await Read (sftp, candidate.Path, 64 * 1024 * 1024, token).ConfigureAwait (false));
		await SaveNew (Path.Combine (evidence, "deletion-submitted.json"), JsonSerializer.SerializeToUtf8Bytes (new { candidate.Path, Utc = DateTime.UtcNow }), token).ConfigureAwait (false);
		await sftp.DeleteFileAsync (candidate.Path, token).ConfigureAwait (false);
		string operation = await client.BeginLocalDriverRefreshAsync (token).ConfigureAwait (false);
		await SaveNew (Path.Combine (evidence, "refresh-operation.json"), JsonSerializer.SerializeToUtf8Bytes (new { OperationId = operation }), token).ConfigureAwait (false);
		var refreshed = await client.WaitForOperationAsync (operation, TimeSpan.FromMinutes (2), token).ConfigureAwait (false);
		if (refreshed.Status == "Failed") throw new IOException ("Package cleanup refresh failed.");
		while (await client.IsDriverRefreshInProgressAsync (token).ConfigureAwait (false) != false)
			await Task.Delay (500, token).ConfigureAwait (false);
		using var afterManifest = JsonDocument.Parse (await Read (sftp, Manifest, 4 * 1024 * 1024, token).ConfigureAwait (false));
		if (await sftp.ExistsAsync (candidate.Path, token).ConfigureAwait (false)
			 || afterManifest.RootElement.EnumerateArray ().Any (r => r.GetProperty ("LocalPath").GetString () == candidate.Path))
			throw new IOException ("Stored package removal could not be confirmed.");
		var after = await client.GetDevicesAsync (token).ConfigureAwait (false);
		if (before.Any (d => !after.Any (a => a.Id == d.Id && a.Model == d.Model && a.LocationId == d.LocationId)))
			throw new IOException ("Installed device identities changed during cleanup.");
		bool cached = (await client.GetDriversAsync (package.Model, token).ConfigureAwait (false)).Any (d =>
			 string.Equals (d.Model, package.Model, StringComparison.OrdinalIgnoreCase) && VersionEqual (d.Version, package.Version));
		var result = new Result (true, cached, stored.Length, cached
			 ? "Test package removed from storage; Home retains a cached catalogue entry until its next planned reboot."
			 : "Test package removed from storage and the catalogue.");
		await Finish (result, token).ConfigureAwait (false);
		await VerifyOwner (sftp, owner, token).ConfigureAwait (false);
		if (Encoding.UTF8.GetString (await Read (sftp, Guard, 128, token).ConfigureAwait (false)) != marker)
			throw new IOException ("Cleanup marker ownership changed.");
		await sftp.DeleteFileAsync (Guard, token).ConfigureAwait (false);
		return result;
		}

	private async Task<Result> Finish (Result result, CancellationToken token)
		{
		await SaveNew (Path.Combine (evidence, "verified.json"), JsonSerializer.SerializeToUtf8Bytes (result), token).ConfigureAwait (false);
		return result;
		}
	internal static Candidate SelectCandidate (byte[] manifest, DriverPackageInfo package, ISet<string> protectedPaths, IEnumerable<string?> installedModels)
		{
		using var document = JsonDocument.Parse (manifest);
		var rows = document.RootElement.EnumerateArray ().Where (row =>
			 Guid.TryParse (row.GetProperty ("DriverPackageId").GetString (), out var id) && id == Guid.Parse (package.DriverId)
			 && VersionEqual (row.GetProperty ("DriverVersion").GetString (), package.Version)).ToArray ();
		if (rows.Length != 1) throw new InvalidDataException ("Cleanup requires one exact package identity and version.");
		string path = rows[0].GetProperty ("LocalPath").GetString () ?? "";
		ValidatePath (path);
		if (protectedPaths.Contains (path)) return new (path, [], true);
		var models = rows[0].GetProperty ("SupportedModels").EnumerateArray ().Select (m => m.GetString ())
			 .Append (package.Model).ToArray ();
		if (models.Any (string.IsNullOrWhiteSpace)) throw new InvalidDataException ("Unknown supported model; package retained.");
		if (installedModels.Any (installed => models.Any (model => string.Equals (model!.Trim (), installed?.Trim (), StringComparison.OrdinalIgnoreCase))))
			throw new InvalidOperationException ("A package model or alias is still installed.");
		return new (path, models.Select (m => m!).ToArray (), false);
		}
	internal static void ValidatePath (string path)
		{
		if (!path.StartsWith (Storage, StringComparison.Ordinal) || !path.EndsWith (".pkg", StringComparison.OrdinalIgnoreCase)
			 || path[Storage.Length..].IndexOfAny (['/', '\\', '\0']) >= 0 || path[Storage.Length..].Length <= 4)
			throw new InvalidDataException ("Cleanup path is outside package storage or is not a plain package filename.");
		}
	internal static void VerifyHash (byte[] expected, byte[] actual)
		{
		if (!CryptographicOperations.FixedTimeEquals (SHA256.HashData (expected), SHA256.HashData (actual)))
			throw new InvalidDataException ("Stored bytes differ from this workflow's retained test package.");
		}
	private static bool VersionEqual (string? left, string right) => Version.TryParse (left, out var a) && Version.TryParse (right, out var b)
		 && a.Major == b.Major && a.Minor == b.Minor && a.Build == b.Build && Math.Max (0, a.Revision) == Math.Max (0, b.Revision);
	private static async Task<SftpClient> Connect (string host, NetworkCredential credential, string fingerprint, CancellationToken token)
		{
		var sftp = new SftpClient (host, credential.UserName, credential.Password);
		sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
		sftp.OperationTimeout = TimeSpan.FromSeconds (15);
		sftp.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == fingerprint;
		try { await sftp.ConnectAsync (token).ConfigureAwait (false); return sftp; }
		catch { sftp.Dispose (); throw; }
		}
	private static async Task VerifyOwner (SftpClient sftp, string owner, CancellationToken token)
		{
		if (!Guid.TryParseExact (owner, "N", out _)) throw new InvalidDataException ("Invalid workflow owner.");
		if (Encoding.UTF8.GetString (await Read (sftp, Lease + "/" + owner, 32, token).ConfigureAwait (false)) != owner)
			throw new IOException ("Workflow lease ownership could not be confirmed.");
		}
	private static async Task Idle (SftpClient sftp, CancellationToken token, bool allowGuard = false)
		{
		if (!allowGuard && await sftp.ExistsAsync (Guard, token).ConfigureAwait (false)) throw new IOException ("Processor test execution is active.");
		await foreach (var entry in sftp.ListDirectoryAsync ("/user/ThirdPartyDrivers/Import", token).ConfigureAwait (false))
			if (entry.Name is not "." and not "..") throw new IOException ("A processor import is pending.");
		}
	private static async Task<byte[]> Read (SftpClient sftp, string path, long maximum, CancellationToken token)
		{
		var attributes = await sftp.GetAttributesAsync (path, token).ConfigureAwait (false);
		if (!attributes.IsRegularFile || attributes.IsSymbolicLink || attributes.Size > maximum)
			throw new IOException ("Cleanup input is not a bounded regular file.");
		using var buffer = new MemoryStream ();
		await sftp.DownloadFileAsync (path, buffer, token).ConfigureAwait (false);
		if (buffer.Length > maximum) throw new IOException ("Cleanup input exceeded its size limit.");
		return buffer.ToArray ();
		}
	private static async Task SaveNew (string path, byte[] bytes, CancellationToken token)
		{
		await using var file = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		await file.WriteAsync (bytes, token).ConfigureAwait (false);
		await file.FlushAsync (token).ConfigureAwait (false);
		}
	}