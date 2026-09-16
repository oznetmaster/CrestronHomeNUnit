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
		Assert.That (plan.TestPackage.AdditionalRemovalRebootDeviceIds, Is.Empty);
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

	[TestCase (new[] { 18 }, false)]
	[TestCase (new[] { 18, 18 }, true)]
	[TestCase (new[] { 0 }, true)]
	[TestCase (new[] { 17 }, true)]
	public void SharedRemovalRequiresReviewedDistinctExistingIds (int[] scope, bool removal)
		{
		var plan = Plan () with
			{
			AllowProcessorReboot = true,
			TestPackage = Package () with
				{
				ExpectedDeviceId = 17,
				RebootAfterRemoval = removal,
				AdditionalRemovalRebootDeviceIds = scope
				}
			};
		Assert.That (Assert.Throws<ArgumentException> (plan.Validate)!.Message, Does.Contain ("Additional removal reboot scope"));
		}

	private static PackageBuildPlan Package () => new ("missing-project", "missing-output", "Tests", 1);
	[Test]
	public void ReviewedSharedScopeSurvivesPlanSerialization ()
		{
		var plan = Plan () with
			{
			AllowProcessorReboot = true,
			TestPackage = new (typeof (RebootPlanTests).Assembly.Location, Path.Combine (Path.GetTempPath (), "example.pkg"), "Tests", 1)
				{
				RebootAfterRemoval = true,
				AdditionalRemovalRebootDeviceIds = [18]
				}
			};
		var restored = JsonSerializer.Deserialize<WorkflowPlan> (JsonSerializer.Serialize (plan))!;
		Assert.DoesNotThrow (restored.Validate);
		Assert.That (restored.TestPackage.AdditionalRemovalRebootDeviceIds, Is.EqualTo (new[] { 18 }));
		}
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