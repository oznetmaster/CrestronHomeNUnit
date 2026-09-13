// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class RebootPlanTests
	{
	[Test]
	public void ExistingPlansDoNotAuthorizeReboot ()
		{
		var plan = JsonSerializer.Deserialize<WorkflowPlan> ("""
            {"Host":"example.invalid","CertificateSha256":"pin","SshFingerprint":"pin","SourceRoots":["source"],"LocalTests":[],"TestPackage":{"Project":"p","PackagePath":"p","InstanceName":"Tests","LocationId":1},"ProcessorSuites":[]}
            """)!;
		Assert.That (plan.AllowProcessorReboot, Is.False);
		Assert.That (plan.TestPackage.RebootAfterInstall || plan.TestPackage.RebootAfterRemoval, Is.False);
		}

	[TestCase (true, false)]
	[TestCase (false, true)]
	public void InstallAndRemovalOverridesRequireGlobalAuthorization (bool install, bool remove)
		{
		var plan = Plan () with
			{
			TestPackage = Package () with
				{
				RebootAfterInstall = install,
				RebootAfterRemoval = remove
				}
			};
		var exception = Assert.Throws<ArgumentException> (plan.Validate);
		Assert.That (exception!.Message, Does.Contain ("allowProcessorReboot"));
		}

	[Test]
	public void UnknownActivationAfterRebootRetainsLease () =>
		 Assert.That (WorkflowRunner.CanReleaseLease (true, true, false, true), Is.False);

	private static PackageBuildPlan Package () => new ("missing-project", "missing-output", "Tests", 1);
	private static WorkflowPlan Plan () => new ()
		{
		Host = "example.invalid",
		CertificateSha256 = "pin",
		SshFingerprint = "pin",
		SourceRoots = ["source"],
		LocalTests = [new ("project", 1)],
		TestPackage = Package (),
		ProcessorSuites = [new ("unit", 1, [])]
		};
	}