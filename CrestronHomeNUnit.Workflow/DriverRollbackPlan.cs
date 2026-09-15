// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeNUnit.Workflow;

// Code rollback preserves CURRENT configuration. It never replays saved credentials or tokens.
public sealed record DriverRollbackPlan (string PreviousPackage, string Sha256, ControlProbePlan ConfigurationProbe,
	PropertyCheck[] VerificationChecks)
	{
	public void Validate ()
		{
		if (!Path.IsPathFullyQualified (PreviousPackage) || !File.Exists (PreviousPackage) || !PreviousPackage.EndsWith (".pkg", StringComparison.OrdinalIgnoreCase)
			|| Sha256.Length != 64 || !Sha256.All (Uri.IsHexDigit))
			throw new ArgumentException ("Rollback requires an existing absolute previous-package path and its SHA-256.");
		if (!Path.IsPathFullyQualified (ConfigurationProbe.Executable) || !File.Exists (ConfigurationProbe.Executable)
			|| !Path.IsPathFullyQualified (ConfigurationProbe.WorkingDirectory) || !Directory.Exists (ConfigurationProbe.WorkingDirectory)
			|| ConfigurationProbe.Arguments == null || VerificationChecks.Length == 0)
			throw new ArgumentException ("Rollback requires a private configuration-compatibility probe and post-rollback health checks.");
		}
	}

public sealed record RollbackConfigurationObservation (string RequestId, int DeviceId, string Model, string InstalledVersion,
	string PreviousPackageSha256, bool CompatibleWithPreviousVersion, string ConfigurationIdentity);