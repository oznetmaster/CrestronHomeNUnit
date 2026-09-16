// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

using CrestronHomeDevTools;

namespace CrestronHomeNUnit.Workflow;

/// <summary>Trusted release-workflow pins for ActualDriver.PackagePath. This declaration is not a build attestation.</summary>
public sealed record ReleaseCandidatePlan (string Sha256, string DriverGuid, string DriverVersion, string SourceRepository, string SourceCommit)
	{
	public void Validate (PackageBuildPlan actual)
		{
		if (Sha256?.Length != 64 || !Sha256.All (char.IsAsciiHexDigit) || !Guid.TryParse (DriverGuid, out _) ||
			!Version.TryParse (DriverVersion, out var version) || version.Revision != 0 ||
			SourceCommit?.Length is not (40 or 64) || !SourceCommit.All (char.IsAsciiHexDigit) ||
			!Path.IsPathFullyQualified (SourceRepository) || !Directory.Exists (SourceRepository) ||
			!Path.IsPathFullyQualified (actual.Project) || !Path.IsPathFullyQualified (actual.PackagePath) || !File.Exists (actual.PackagePath))
			throw new ArgumentException ("Release candidates require an existing package/repository, pinned SHA-256, driver GUID, four-part release version ending in zero and full source commit.");
		var project = Path.GetRelativePath (SourceRepository, actual.Project);
		if (Path.IsPathRooted (project) || project == ".." || project.StartsWith (".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			throw new ArgumentException ("The actual driver project must belong to the declared release source repository.");
		}
	}

/// <summary>Retains the exact supplied package under a read lock; never builds or renumbers it.</summary>
internal sealed class WorkflowReleasePackage : IDisposable
	{
	private readonly ReleaseCandidatePlan _plan;
	private readonly FileStream _retained;
	public string Path { get; }
	public DriverPackageInfo Identity { get; }
	public string Sha256 { get; }
	public string SourceCommit => _plan.SourceCommit.ToLowerInvariant ();
	private WorkflowReleasePackage (ReleaseCandidatePlan plan, string path, DriverPackageInfo identity, string hash, FileStream retained)
		{
		_plan = plan;
		Path = path;
		Identity = identity;
		Sha256 = hash;
		_retained = retained;
		}

	internal static async Task<WorkflowReleasePackage> PrepareAsync (ReleaseCandidatePlan plan, PackageBuildPlan actual, string results, CancellationToken token)
		{
		plan.Validate (actual);
		if ((await GitAsync (plan.SourceRepository, ["rev-parse", "--show-prefix"], token).ConfigureAwait (false)).Length != 0)
			throw new InvalidDataException ("Use the release repository root, not one of its subdirectories.");
		await VerifyCommitAsync (plan, token).ConfigureAwait (false);
		if ((await GitAsync (plan.SourceRepository, ["status", "--porcelain=v1", "--untracked-files=normal", "--ignore-submodules=none"], token).ConfigureAwait (false)).Length != 0)
			throw new InvalidDataException ("Release verification requires a clean source checkout before tests/builds start.");
		_ = await GitAsync (plan.SourceRepository, ["ls-files", "--error-unmatch", "--", System.IO.Path.GetRelativePath (plan.SourceRepository, actual.Project)], token).ConfigureAwait (false);
		var directory = System.IO.Path.Combine (results, "packages", "actual");
		Directory.CreateDirectory (directory);
		var path = System.IO.Path.Combine (directory, System.IO.Path.GetFileName (actual.PackagePath));
		await using (var source = new FileStream (actual.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
			if (source.Length is <= 0 or > 64 * 1024 * 1024)
				throw new InvalidDataException ("The release package must be nonempty and at most 64 MiB.");
			await using var copy = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			await source.CopyToAsync (copy, token).ConfigureAwait (false);
			await copy.FlushAsync (token).ConfigureAwait (false);
			}
		var retained = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		try
			{
			var hash = Convert.ToHexString (await SHA256.HashDataAsync (retained, token).ConfigureAwait (false));
			retained.Position = 0;
			if (!hash.Equals (plan.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("The supplied release package differs from its trusted SHA-256 pin.");
			var identity = DriverDeployment.Inspect (path);
			if (!Guid.TryParse (identity.DriverId, out var id) || id != Guid.Parse (plan.DriverGuid) ||
				!Version.TryParse (identity.Version, out var version) || version != Version.Parse (plan.DriverVersion))
				throw new InvalidDataException ("The supplied package differs from the pinned release driver GUID/version.");
			await File.WriteAllTextAsync (System.IO.Path.Combine (results, "ReleaseCandidate.json"), JsonSerializer.Serialize (new
				{
				SchemaVersion = 1, Package = identity, Sha256 = hash, plan.SourceCommit,
				FileName = System.IO.Path.GetFileName (path), SourceInitiallyClean = true,
				Mode = "PrebuiltRelease", VerifiedUtc = DateTimeOffset.UtcNow
				}), token).ConfigureAwait (false);
			return new (plan, path, identity, hash, retained);
			}
		catch
			{
			retained.Dispose ();
			throw;
			}
		}

	internal Task VerifySourceCommitAsync (CancellationToken token) => VerifyCommitAsync (_plan, token);
	internal async Task VerifyPristineSourceAsync (CancellationToken token)
		{
		await VerifySourceCommitAsync (token).ConfigureAwait (false);
		if ((await GitAsync (_plan.SourceRepository, ["status", "--porcelain=v1", "--untracked-files=normal", "--ignore-submodules=none"], token).ConfigureAwait (false)).Length != 0)
			throw new InvalidDataException ("Release source changed before local test execution started.");
		}
	private static async Task VerifyCommitAsync (ReleaseCandidatePlan plan, CancellationToken token)
		{
		var current = await GitAsync (plan.SourceRepository, ["rev-parse", "--verify", "HEAD"], token).ConfigureAwait (false);
		if (!current.Equals (plan.SourceCommit, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("The source checkout is not at the pinned release commit.");
		}

	private static async Task<string> GitAsync (string repository, IEnumerable<string> arguments, CancellationToken token)
		{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource (token);
		timeout.CancelAfter (TimeSpan.FromSeconds (30));
		var start = new ProcessStartInfo ("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add ("--literal-pathspecs");
		start.ArgumentList.Add ("-C");
		start.ArgumentList.Add (repository);
		foreach (var argument in arguments) start.ArgumentList.Add (argument);
		using var process = Process.Start (start) ?? throw new IOException ("Git could not be started for release-source verification.");
		var output = process.StandardOutput.ReadToEndAsync (timeout.Token);
		var errors = process.StandardError.BaseStream.CopyToAsync (Stream.Null, timeout.Token);
		try
			{
			await Task.WhenAll (output, errors, process.WaitForExitAsync (timeout.Token)).ConfigureAwait (false);
			if (process.ExitCode != 0) throw new IOException ("Git could not verify the release checkout/project. No processor operation was requested by this check.");
			return (await output.ConfigureAwait (false)).Trim ();
			}
		finally
			{
			if (!process.HasExited) process.Kill ();
			}
		}

	public void Dispose () => _retained.Dispose ();
	}