// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

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
	public bool RemoveTestInstanceAfterRun
		{
		get; init;
		}
	public int StageTimeoutSeconds { get; init; } = 600;
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
		if (StageTimeoutSeconds is < 30 or > 86400 || SourceRoots.Length == 0 || LocalTests.Length == 0 || ProcessorSuites.Length == 0)
			throw new ArgumentException ("Configure source roots, local tests, processor suites and a bounded timeout.");
		if (LocalTests.Any (t => t.MinimumPassed < 1) || ProcessorSuites.Concat (LiveSuites).Any (t => t.MinimumPassed < 1))
			throw new ArgumentException ("Every required test stage needs a positive minimum passed count.");
		if (!AllowProcessorReboot && new[] { TestPackage, ActualDriver }.OfType<PackageBuildPlan> ().Any (p => p.RebootAfterInstall || p.RebootAfterRemoval))
			throw new ArgumentException ("Explicit install/removal reboots require allowProcessorReboot.");
		if (ActualDriver != null && (LiveSuites.Length == 0 || DeployedChecks.Length == 0))
			throw new ArgumentException ("Driver updates require processor live tests and installed-driver checks.");
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
		if (DeployedChecks.Any (c => c.DeviceId <= 0 || string.IsNullOrWhiteSpace (c.Model) || string.IsNullOrWhiteSpace (c.Property)
			 || (c.Expected is not null ? c.Minimum != null || c.Maximum != null
				  : c.Minimum == null || c.Maximum == null || !double.IsFinite (c.Minimum.Value) || !double.IsFinite (c.Maximum.Value) || c.Minimum > c.Maximum)))
			throw new ArgumentException ("Each installed-device check needs an ID, model, property, and either an expected value or numeric range.");
		}
	}

public sealed record LocalTestPlan (string Project, int MinimumPassed, string? Filter = null);
public sealed record PackageBuildPlan (string Project, string PackagePath, string InstanceName, int LocationId, int? ExpectedDeviceId = null)
	{
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
public sealed record PropertyCheck (string Name, int DeviceId, string Model, string Property, JsonElement? Expected = null, double? Minimum = null, double? Maximum = null);