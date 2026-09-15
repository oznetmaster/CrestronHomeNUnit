// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

public sealed record ArtifactReusePlan (string? PreviousResults, string[] BuildInputFiles);

internal sealed record PackageReceipt (DriverPackageInfo Package, string? DebugRevisionBaseline, string Sha256,
	string SourceSha256, string? BuildInputsSha256 = null, string? ReusedFromRunId = null);

internal sealed record ReusedArtifact (string Path, string RunId, PackageReceipt Receipt);

internal static class WorkflowArtifacts
	{
	private sealed record BuildIdentity (string SourceSha256, string Configuration);
	private sealed record LeaseReceipt (string RunId, string State);

	// Reuse is opt-in and refers to a private, locally trusted result directory, never downloaded evidence.
	internal static async Task<ReusedArtifact?> TryReuseAsync (string previous, string prefix, string filename,
		string manifest, string sourceHash, string? inputsHash,
		Func<string, CancellationToken, Task<IReadOnlyList<DriverInfo>>> catalogue, CancellationToken token)
		{
		var result = await ReadAsync<ProcessorWorkflowResult> (Path.Combine (previous, "Workflow.json"), token).ConfigureAwait (false);
		var lease = await ReadAsync<LeaseReceipt> (Path.Combine (previous, "Lease.json"), token).ConfigureAwait (false);
		if (result.Stages == null || !result.Passed || lease.State != "Released" || !Guid.TryParseExact (lease.RunId, "N", out _)
			|| !HasTests (result, "Local") || !HasTests (result, "Processor")
			|| prefix == "actual" && (!result.DriverUpdateAttempted || !result.DriverUpdateVerified || !HasTests (result, "Deployed driver live")))
			throw new InvalidDataException ("Artifact reuse requires a completed, successful workflow with a released processor lease.");
		var identity = await ReadAsync<BuildIdentity> (Path.Combine (previous, "BuildIdentity.json"), token).ConfigureAwait (false);
		if (identity.Configuration != "Debug" || identity.SourceSha256 != sourceHash || inputsHash == null || !File.Exists (manifest))
			return null;
		var receipt = await ReadAsync<PackageReceipt> (Path.Combine (previous, prefix + "-package.json"), token).ConfigureAwait (false);
		if (receipt.SourceSha256 != sourceHash)
			throw new InvalidDataException ("Retained package and workflow source identities disagree.");
		if (receipt.BuildInputsSha256 != inputsHash)
			return null;
		var path = Path.Combine (previous, "packages", prefix, filename);
		await using var file = OpenBounded (path, 64 * 1024 * 1024);
		var hash = Convert.ToHexString (await SHA256.HashDataAsync (file, token).ConfigureAwait (false));
		var package = DriverDeployment.Inspect (path);
		if (hash != receipt.Sha256 || package != receipt.Package)
			throw new InvalidDataException ("Retained package bytes or identity do not match their receipt.");
		using var metadata = await ReadDocumentAsync (manifest, token).ConfigureAwait (false);
		var general = metadata.RootElement.GetProperty ("GeneralInformation");
		var version = WorkflowDebugVersion.ParseVersion (package.Version);
		var current = WorkflowDebugVersion.ParseVersion (general.GetProperty ("DriverVersion").GetString ());
		if (!Guid.TryParse (general.GetProperty ("Guid").GetString (), out var driverId) || driverId != Guid.Parse (package.DriverId)
			|| !string.Equals (package.Model.Trim (), general.GetProperty ("BaseModel").GetString ()?.Trim (), StringComparison.OrdinalIgnoreCase)
			|| new Version (version.Major, version.Minor, version.Build) != new Version (current.Major, current.Minor, current.Build))
			throw new InvalidDataException ("Retained package does not match the requested driver and source release.");
		var drivers = await catalogue (package.Model, token).ConfigureAwait (false);
		if (drivers.Any (d => string.Equals (d.Model?.Trim (), package.Model.Trim (), StringComparison.OrdinalIgnoreCase)
			&& WorkflowDebugVersion.ParseVersion (d.Version) >= version))
			return null;
		return new (path, lease.RunId, receipt);
		}

	private static bool HasTests (ProcessorWorkflowResult result, string stage)
		=> result.Stages.Count (s => s.Stage == stage && s.Tests?.MeetsGate == true) == 1;

	// Resolve the SDK in this project's directory, so global.json is respected. No build is performed.
	internal static async Task<string?> InputsDigestAsync (string project, IEnumerable<string> externalFiles, CancellationToken token, IEnumerable<string>? sourceRoots = null)
		{
		var start = new ProcessStartInfo ("dotnet")
			{
			WorkingDirectory = Path.GetDirectoryName (Path.GetFullPath (project))!,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		start.ArgumentList.Add ("--version");
		using var process = Process.Start (start) ?? throw new IOException ("Could not identify the build SDK.");
		var output = process.StandardOutput.ReadToEndAsync ();
		var errors = process.StandardError.ReadToEndAsync ();
		try
			{
			await process.WaitForExitAsync (token).ConfigureAwait (false);
			}
		finally
			{
			if (!process.HasExited)
				{
				process.Kill (true);
				await process.WaitForExitAsync ().ConfigureAwait (false);
				}
			}
		_ = await errors.ConfigureAwait (false);
		if (process.ExitCode != 0)
			throw new IOException ("Could not identify the build SDK.");
		return await DigestInputsAsync (project, externalFiles, (await output.ConfigureAwait (false)).Trim (), token, sourceRoots).ConfigureAwait (false);
		}

	internal static async Task<string?> DigestInputsAsync (string project, IEnumerable<string> externalFiles, string sdk, CancellationToken token, IEnumerable<string>? sourceRoots = null)
		{
		var files = new SortedSet<string> (StringComparer.OrdinalIgnoreCase);
		var projects = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		var pending = new Queue<string> ();
		pending.Enqueue (Path.GetFullPath (project));
		while (pending.TryDequeue (out var next))
			{
			if (!projects.Add (next))
				continue;
			if (sourceRoots != null && !sourceRoots.Any (root => next.StartsWith (Path.GetFullPath (root).TrimEnd (
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
				return null;
			if (projects.Count > 256)
				throw new InvalidDataException ("The restore graph exceeds the artifact reuse limit.");
			var assets = Path.Combine (Path.GetDirectoryName (next)!, "obj", "project.assets.json");
			if (!File.Exists (assets))
				return null; // Custom intermediate layouts build normally.
			files.Add (assets);
			using var document = await ReadDocumentAsync (assets, token, 16 * 1024 * 1024).ConfigureAwait (false);
			var restore = document.RootElement.GetProperty ("project").GetProperty ("restore");
			if (!string.Equals (Path.GetFullPath (restore.GetProperty ("projectPath").GetString ()!), next, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Restore graph does not belong to the requested project.");
			foreach (var framework in restore.GetProperty ("frameworks").EnumerateObject ())
				if (framework.Value.TryGetProperty ("projectReferences", out var references))
					foreach (var reference in references.EnumerateObject ())
						pending.Enqueue (Path.GetFullPath (reference.Name));
			}
		foreach (var path in externalFiles)
			files.Add (Path.GetFullPath (path));
		// A backend update invalidates earlier receipts, including changes to its build arguments.
		files.Add (typeof (WorkflowArtifacts).Assembly.Location);
		using var hash = IncrementalHash.CreateHash (HashAlgorithmName.SHA256);
		hash.AppendData (Encoding.UTF8.GetBytes ("artifact-v1\0Debug\0" + sdk + "\0" + RuntimeInformation.FrameworkDescription + "\0" + RuntimeInformation.OSDescription + "\0" + RuntimeInformation.ProcessArchitecture));
		foreach (var file in files)
			{
			hash.AppendData (Encoding.UTF8.GetBytes ("\0" + file + "\0"));
			await using var stream = OpenBounded (file, 256 * 1024 * 1024);
			hash.AppendData (await SHA256.HashDataAsync (stream, token).ConfigureAwait (false));
			}
		return Convert.ToHexString (hash.GetHashAndReset ());
		}

	internal static async Task CopyVerifiedAsync (ReusedArtifact artifact, string destination, CancellationToken token)
		{
		await using var source = OpenBounded (artifact.Path, 64 * 1024 * 1024);
		if (Convert.ToHexString (await SHA256.HashDataAsync (source, token).ConfigureAwait (false)) != artifact.Receipt.Sha256)
			throw new InvalidDataException ("Retained package changed before reuse.");
		source.Position = 0;
		Directory.CreateDirectory (Path.GetDirectoryName (destination)!);
		await using var target = new FileStream (destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		await source.CopyToAsync (target, token).ConfigureAwait (false);
		}

	private static FileStream OpenBounded (string path, long maximum)
		{
		var file = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (file.Length is > 0 && file.Length <= maximum)
			return file;
		file.Dispose ();
		throw new InvalidDataException ("An artifact or evidence file is empty or exceeds its supported size.");
		}
	private static async Task<T> ReadAsync<T> (string path, CancellationToken token)
		{
		await using var file = OpenBounded (path, 1024 * 1024);
		return await JsonSerializer.DeserializeAsync<T> (file, cancellationToken: token).ConfigureAwait (false)
			?? throw new InvalidDataException ("Artifact evidence is missing.");
		}
	private static async Task<JsonDocument> ReadDocumentAsync (string path, CancellationToken token, long maximum = 4 * 1024 * 1024)
		{
		await using var file = OpenBounded (path, maximum);
		return await JsonDocument.ParseAsync (file, cancellationToken: token).ConfigureAwait (false);
		}
	}