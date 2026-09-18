// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using CrestronHomeNUnit.Android;

namespace CrestronHomeNUnit.Workflow;

// Keep plans and input files outside the checkout. No credentials are serialized in results.
public sealed record WorkflowPlan
	{
	public required string Host
		{
		get; init;
		}
	public required string CertificateSha256
		{
		get; init;
		}
	public required string SshFingerprint
		{
		get; init;
		}
	public required string[] SourceRoots
		{
		get; init;
		}
	public required LocalTestPlan[] LocalTests
		{
		get; init;
		}
	public required PackageBuildPlan TestPackage
		{
		get; init;
		}
	public PackageBuildPlan? ActualDriver
		{
		get; init;
		}
	public required SuitePlan[] ProcessorSuites
		{
		get; init;
		}
	public SuitePlan[] LiveSuites { get; init; } = [];
	public PropertyCheck[] DeployedChecks { get; init; } = [];
	public InstalledControlPlan[] DeployedControls { get; init; } = [];
	public AndroidTestPlan? AndroidTests { get; init; }
	public ReleaseCandidatePlan? ReleaseCandidate { get; init; }
	public ArtifactReusePlan? ArtifactReuse
		{
		get; init;
		}
	public DriverRollbackPlan? Rollback
		{
		get; init;
		}
	public bool RemoveTestInstanceAfterRun
		{
		get; init;
		}
	public bool RemoveTestPackageAfterSuccessfulRun
		{
		get; init;
		}
	public int StageTimeoutSeconds { get; init; } = 600;
	public int LeaseWaitSeconds
		{
		get; init;
		}
	public bool AllowProcessorReboot
		{
		get; init;
		}
	public string? ProcessorSystemName
		{
		get; init;
		}

	public void Validate ()
		{
		if (string.IsNullOrWhiteSpace (Host) || string.IsNullOrWhiteSpace (CertificateSha256) || string.IsNullOrWhiteSpace (SshFingerprint))
			throw new ArgumentException ("Processor address and verified HTTPS/SSH fingerprints are required.");
		if (StageTimeoutSeconds is < 30 or > 86400 || LeaseWaitSeconds is < 0 or > 86400 || SourceRoots.Length == 0 || LocalTests.Length == 0 || ProcessorSuites.Length == 0)
			throw new ArgumentException ("Configure source roots, local tests, processor suites and a bounded timeout.");
		if (LocalTests.Any (t => t.MinimumPassed < 1) || ProcessorSuites.Concat (LiveSuites).Any (t => t.MinimumPassed < 1))
			throw new ArgumentException ("Every required test stage needs a positive minimum passed count.");
		if (RemoveTestPackageAfterSuccessfulRun && !RemoveTestInstanceAfterRun)
			throw new ArgumentException ("Automatic package cleanup requires confirmed test-instance removal.");
		if (!AllowProcessorReboot && new[] { TestPackage, ActualDriver }.OfType<PackageBuildPlan> ().Any (p => p.RebootAfterInstall || p.RebootAfterRemoval))
			throw new ArgumentException ("Explicit install/removal reboots require allowProcessorReboot.");
		if (new[] { TestPackage, ActualDriver }.OfType<PackageBuildPlan> ().Any (p => p.AdditionalRemovalRebootDeviceIds == null
			|| p.AdditionalRemovalRebootDeviceIds.Length > 0 && (!p.RebootAfterRemoval || p.AdditionalRemovalRebootDeviceIds.Any (id => id <= 0 || id == p.ExpectedDeviceId)
			|| p.AdditionalRemovalRebootDeviceIds.Distinct ().Count () != p.AdditionalRemovalRebootDeviceIds.Length)))
			throw new ArgumentException ("Additional removal reboot scope requires explicit reboot policy and distinct existing device IDs.");
		if (ActualDriver != null && (LiveSuites.Length == 0 || DeployedChecks.Length == 0))
			throw new ArgumentException ("Driver updates require processor live tests and installed-driver checks.");
		if (ReleaseCandidate != null)
			{
			if (ActualDriver == null) throw new ArgumentException ("A release candidate requires an actual driver target.");
			ReleaseCandidate.Validate (ActualDriver);
			if (!SourceRoots.Any (root => Path.TrimEndingDirectorySeparator (Path.GetFullPath (ReleaseCandidate.SourceRepository)).Equals
				(Path.TrimEndingDirectorySeparator (Path.GetFullPath (root)), StringComparison.OrdinalIgnoreCase)))
				throw new ArgumentException ("Declare the release source repository itself as a source root so its files, including submodule worktrees, are checked directly.");
			}
		if (DeployedControls.Length > 0 && ActualDriver == null)
			throw new ArgumentException ("Post-deployment controls require an actual driver target.");
		if (AndroidTests != null)
			{
			AndroidTests.ValidateManagedChildren ();
			AndroidTestSelection.Validate (AndroidTests.RequiredTests);
			if (ActualDriver == null || !Path.IsPathFullyQualified (AndroidTests.Project) || !File.Exists (AndroidTests.Project) ||
				!Path.IsPathFullyQualified (AndroidTests.ProfilePath) || !File.Exists (AndroidTests.ProfilePath) ||
				!SourceRoots.Any (root => Path.GetFullPath (AndroidTests.Project).StartsWith (Path.GetFullPath (root).TrimEnd (Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
				throw new ArgumentException ("Android tests require an actual driver, a test project within a source root and an existing private profile.");
			AndroidWorkflowSession.Read<AndroidSessionProfile> (AndroidTests.ProfilePath).Validate ();
			}
		if (DeployedControls.Select (c => c.Name).Concat (DeployedChecks.Select (c => c.Name)).Distinct (StringComparer.Ordinal).Count () != DeployedControls.Length + DeployedChecks.Length)
			throw new ArgumentException ("Installed-driver check names must be unique.");
		foreach (var control in DeployedControls)
			control.Validate ();
		if (Rollback != null)
			{
			Rollback.Validate ();
			if (ActualDriver?.ExpectedDeviceId == null || ActualDriver.InitialConfigurationFile != null || AllowProcessorReboot)
				throw new ArgumentException ("Rollback requires an existing exact driver target, preserved current configuration and a reboot-free workflow.");
			}
		if (ArtifactReuse != null && (ArtifactReuse.BuildInputFiles == null
			|| ArtifactReuse.BuildInputFiles.Any (path => !Path.IsPathFullyQualified (path) || !File.Exists (path))
			|| ArtifactReuse.PreviousResults is string previous && (!Path.IsPathFullyQualified (previous) || !Directory.Exists (previous))))
			throw new ArgumentException ("Artifact reuse needs an existing absolute results directory and existing absolute build-input files.");
		if (TestPackage.InitialConfigurationFile != null)
			throw new ArgumentException ("Initial configuration is supported only for the actual driver.");
		if (ActualDriver?.InitialConfigurationFile is string input && (!Path.IsPathFullyQualified (input) || !File.Exists (input)))
			throw new ArgumentException ("Initial configuration needs an existing private file with an absolute path.");
		if (ActualDriver != null && (TestPackage.ExpectedDeviceId == ActualDriver.ExpectedDeviceId && TestPackage.ExpectedDeviceId != null
			 || TestPackage.InstanceName == ActualDriver.InstanceName || TestPackage.PackagePath == ActualDriver.PackagePath))
			throw new ArgumentException ("Test and actual driver targets must be distinct.");
		foreach (var p in new[] { TestPackage, ActualDriver }.OfType<PackageBuildPlan> ())
			{
			if (p.LocationId <= 0 || p.ExpectedDeviceId is <= 0 || string.IsNullOrWhiteSpace (p.InstanceName) || p.InstanceName.Length > 32)
				throw new ArgumentException ("Package targets need a room and unique instance name of at most 32 characters.");
			if (!File.Exists (p.Project) || !Path.IsPathFullyQualified (p.PackagePath))
				throw new ArgumentException ("Invalid build project or package output path.");
			}
		if (DeployedChecks.Concat (Rollback?.VerificationChecks ?? []).Any (c => (c.UseActualDriver ? c.DeviceId != 0 || ActualDriver == null : c.DeviceId <= 0) || string.IsNullOrWhiteSpace (c.Model) || string.IsNullOrWhiteSpace (c.Property)
			 || (c.Expected is not null ? c.Minimum != null || c.Maximum != null
				  : c.Minimum == null || c.Maximum == null || !double.IsFinite (c.Minimum.Value) || !double.IsFinite (c.Maximum.Value) || c.Minimum > c.Maximum)))
			throw new ArgumentException ("Each installed-device check needs an ID or useActualDriver with deviceId zero, model, property, and either an expected value or numeric range.");
		}
	}

public sealed record LocalTestPlan (string Project, int MinimumPassed, string? Filter = null);
public sealed record AndroidTestPlan (string Project, string ProfilePath)
	{
	public IReadOnlyList<AndroidManagedChildPlan> ManagedChildren { get; init; } = [];
	public IReadOnlyList<string>? RequiredTests { get; init; }

	internal void ValidateManagedChildren ()
		{
		if (ManagedChildren == null || ManagedChildren.Any (child => child == null ||
			string.IsNullOrWhiteSpace (child.Alias) || child.Alias.Length > 64 ||
			child.Alias.Any (c => !char.IsAsciiLetterOrDigit (c) && c is not ('_' or '-')) ||
			string.IsNullOrWhiteSpace (child.ManagedDeviceId) || string.IsNullOrWhiteSpace (child.Model) ||
			string.IsNullOrWhiteSpace (child.Name) || child.Name.Length > 32 || child.LocationId <= 0) ||
			ManagedChildren.Select (child => child.Alias).Distinct (StringComparer.OrdinalIgnoreCase).Count () != ManagedChildren.Count ||
			ManagedChildren.Select (child => child.ManagedDeviceId).Distinct (StringComparer.Ordinal).Count () != ManagedChildren.Count ||
			ManagedChildren.Select (child => (child.LocationId, child.Name)).Distinct ().Count () != ManagedChildren.Count)
			throw new ArgumentException ("Managed-child tests require distinct aliases, advertised child identities and room/name pairs, with an explicit model and room.");
		}
	}
public sealed record AndroidManagedChildPlan (string Alias, string ManagedDeviceId, string Name, string Model, int LocationId);
public sealed record PackageBuildPlan (string Project, string PackagePath, string InstanceName, int LocationId, int? ExpectedDeviceId = null)
	{
	public string? ManifestPath { get; init; }
	public int[] AdditionalRemovalRebootDeviceIds { get; init; } = [];
	public string? InitialConfigurationFile
		{
		get; init;
		}
	public bool RebootAfterInstall
		{
		get; init;
		}
	public bool RebootAfterRemoval
		{
		get; init;
		}
	}
public sealed record SuitePlan (string Id, int MinimumPassed, string[] Inputs);
public sealed record PropertyCheck (string Name, int DeviceId, string Model, string Property, JsonElement? Expected = null, double? Minimum = null, double? Maximum = null)
	{
	public bool UseActualDriver
		{
		get; init;
		}
	}