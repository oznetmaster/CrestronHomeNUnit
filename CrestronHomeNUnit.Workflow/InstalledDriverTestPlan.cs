// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeNUnit.Android;

namespace CrestronHomeNUnit.Workflow;

// This private plan selects an existing instance; it grants no deployment or reload authority.
public sealed record InstalledDriverTestPlan
	{
	public required string Host { get; init; }
	public required string CertificateSha256 { get; init; }
	public required string SshFingerprint { get; init; }
	public required string PackagePath { get; init; }
	public required string PackageSha256 { get; init; }
	public string? PackageSourceCommit { get; init; }
	public required string[] SourceRoots { get; init; }
	public required InstalledDriverTestTarget Target { get; init; }
	public required AndroidTestPlan AndroidTests { get; init; }
	public int TimeoutSeconds { get; init; } = 900;
	public int LeaseWaitSeconds { get; init; }

	public void Validate ()
		{
		if (string.IsNullOrWhiteSpace (Host) || string.IsNullOrWhiteSpace (SshFingerprint) ||
			CertificateSha256?.Length != 64 || !CertificateSha256.All (char.IsAsciiHexDigit))
			throw new ArgumentException ("Provide a processor address and verified HTTPS/SSH fingerprints.");
		if (PackageSha256?.Length != 64 || !PackageSha256.All (char.IsAsciiHexDigit) ||
			!Path.IsPathFullyQualified (PackagePath) || !PackagePath.EndsWith (".pkg", StringComparison.OrdinalIgnoreCase) || !File.Exists (PackagePath))
			throw new ArgumentException ("Provide an existing candidate package and its independently trusted SHA-256.");
		if (PackageSourceCommit != null && (PackageSourceCommit.Length != 40 || !PackageSourceCommit.All (char.IsAsciiHexDigit)))
			throw new ArgumentException ("An optional package source commit must be a complete Git SHA-1 from the candidate receipt.");
		if (TimeoutSeconds is < 30 or > 86400 || LeaseWaitSeconds is < 0 or > 86400 ||
			SourceRoots == null || SourceRoots.Length == 0 || SourceRoots.Any (root => !Path.IsPathFullyQualified (root) || !Directory.Exists (root)))
			throw new ArgumentException ("Provide existing absolute fixture source roots and bounded timeouts.");
		if (Target == null || Target.DeviceId <= 0 || Target.LocationId <= 0 || Target.ParentDeviceId == 0 ||
			string.IsNullOrWhiteSpace (Target.Name) || Target.Name.Length > 32 || string.IsNullOrWhiteSpace (Target.Model) ||
			string.IsNullOrWhiteSpace (Target.Developer) || string.IsNullOrWhiteSpace (Target.ControlType) ||
			string.IsNullOrWhiteSpace (Target.CatalogueId) || Target.CatalogueId.Length > 256 || Target.CatalogueId is "." or ".." ||
			Target.CatalogueId.Any (c => char.IsControl (c) || c is '/' or '\\' or ':') ||
			!Version.TryParse (Target.Version, out var version) || version.Revision < 0)
			throw new ArgumentException ("Select the exact existing device, parent, name, model, room, four-part version, developer, control type and catalogue ID.");
		if (AndroidTests == null || !Path.IsPathFullyQualified (AndroidTests.Project) || !File.Exists (AndroidTests.Project) ||
			!Path.IsPathFullyQualified (AndroidTests.ProfilePath) || !File.Exists (AndroidTests.ProfilePath) ||
			!SourceRoots.Any (root => Path.GetFullPath (AndroidTests.Project).StartsWith
				(Path.TrimEndingDirectorySeparator (Path.GetFullPath (root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
			throw new ArgumentException ("Provide an Android fixture project inside a source root and its existing private session profile.");
		AndroidTests.ValidateManagedChildren ();
		AndroidTestSelection.Validate (AndroidTests.RequiredTests);
		AndroidWorkflowSession.Read<AndroidSessionProfile> (AndroidTests.ProfilePath).Validate ();
		}
	}

public sealed record InstalledDriverTestTarget (int DeviceId, int ParentDeviceId, string Name, string Model,
	int LocationId, string Version, string CatalogueId, string Developer, string ControlType);