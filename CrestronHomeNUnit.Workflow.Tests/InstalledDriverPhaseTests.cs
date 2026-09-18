// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class InstalledDriverPhaseTests
	{
	private static readonly InstalledDriverTestTarget Target = new (42, -6, "Example", "Example Platform", 3,
		"1.2.3.0", "example.platform.ip.developer", "Developer", "tcpClient");
	private static readonly DriverPackageInfo Package = new ("18f59652-571f-4349-8ca5-7b5033229e8d", "Example Platform", "Example", "1.002.0003.0000");
	private static DeviceInfo Device () => new ()
		{
		Id = 42, ParentDeviceId = -6, Name = "Example", Model = "Example Platform", LocationId = 3,
		PropertyValues = new ()
			{
			["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.002.0003.0000"),
			["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement ("Loaded"),
			["cp.driverInformation:developer"] = JsonSerializer.SerializeToElement ("Developer"),
			["cp.driverInformation:controlType"] = JsonSerializer.SerializeToElement ("tcpClient")
			}
		};
	private static DriverInfo Catalogue () => new () { Id = Target.CatalogueId, Model = Package.Model, Manufacturer = Package.Manufacturer, Developer = Target.Developer, Version = "2.0.0.0" };

	[Test]
	public void ExistingPinnedVersionDoesNotRequireLatestCatalogueVersion () =>
		Assert.DoesNotThrow (() => InstalledDriverTests.RequireInstance (Target, Package, Device (), Catalogue ()));

	[TestCase ("id")]
	[TestCase ("parent")]
	[TestCase ("name")]
	[TestCase ("model")]
	[TestCase ("room")]
	[TestCase ("version")]
	[TestCase ("loading")]
	[TestCase ("developer")]
	[TestCase ("control")]
	[TestCase ("catalogue")]
	[TestCase ("manufacturer")]
	[TestCase ("missing")]
	public void ChangedOrUnknownIdentityRefusesTests (string change)
		{
		DeviceInfo? device = Device ();
		var catalogue = Catalogue ();
		switch (change)
			{
			case "id": device = device with { Id = 43 }; break;
			case "parent": device = device with { ParentDeviceId = 99 }; break;
			case "name": device = device with { Name = "Other" }; break;
			case "model": device = device with { Model = "Other" }; break;
			case "room": device = device with { LocationId = 4 }; break;
			case "version": device.PropertyValues["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.2.3.1"); break;
			case "loading": device.PropertyValues["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement ("Unloaded"); break;
			case "developer": device.PropertyValues.Remove ("cp.driverInformation:developer"); break;
			case "control": device.PropertyValues["cp.driverInformation:controlType"] = JsonSerializer.SerializeToElement ("serial"); break;
			case "catalogue": catalogue = catalogue with { Id = "other" }; break;
			case "manufacturer": catalogue = catalogue with { Manufacturer = "Other" }; break;
			case "missing": device = null; break;
			}
		Assert.Throws<InvalidDataException> (() => InstalledDriverTests.RequireInstance (Target, Package, device, catalogue));
		}

	[Test]
	public async Task PassingPhaseVerifiesBothEndsAndReleasesInOrder ()
		{
		var fake = new Operations ();
		var result = await InstalledDriverTests.RunCoreAsync (fake, default);
		Assert.That (result.Passed, Is.True);
		Assert.That (fake.Calls, Is.EqualTo (new[] { "processor", "android", "Before", "begin", "tests", "end", "After", "release-android", "release-processor" }));
		}

	[TestCase ("Before")]
	[TestCase ("android")]
	public async Task PreTestFailureReleasesOwnedReservationsWithoutRunningTests (string fault)
		{
		var fake = new Operations { Fault = fault };
		var result = await InstalledDriverTests.RunCoreAsync (fake, default);
		Assert.That (result.Passed, Is.False);
		Assert.That (fake.Calls, Does.Not.Contain ("tests"));
		Assert.That (fake.Calls.Last (), Is.EqualTo ("release-processor"));
		Assert.That (result.ReservationsReleased, Is.True);
		}

	[TestCase ("begin")]
	[TestCase ("tests")]
	[TestCase ("end")]
	public async Task UncertainControlKeepsBothReservations (string fault)
		{
		var fake = new Operations { Fault = fault };
		var result = await InstalledDriverTests.RunCoreAsync (fake, default);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ReservationsReleased, Is.False);
		Assert.That (fake.Calls.Any (call => call.StartsWith ("release-", StringComparison.Ordinal)), Is.False);
		}

	[TestCase (false, true)]
	[TestCase (true, false)]
	public async Task MissingRestorationOrCleanupCannotPassOrRelease (bool restored, bool cleanup)
		{
		var fake = new Operations { Outcome = new (new (1, 0, 0, true), restored) { CleanupConfirmed = cleanup } };
		var result = await InstalledDriverTests.RunCoreAsync (fake, default);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ReservationsReleased, Is.False);
		Assert.That (fake.Calls, Does.Not.Contain ("end"));
		}

	[Test]
	public async Task FailedDisplayAssertionCanStillReleaseAfterConfirmedRestoration ()
		{
		var fake = new Operations { Outcome = new (new (0, 1, 0, true), true) };
		var result = await InstalledDriverTests.RunCoreAsync (fake, default);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.Tests!.Failed, Is.EqualTo (1));
		Assert.That (result.RestorationConfirmed && result.CleanupConfirmed && result.ReservationsReleased, Is.True);
		}

	[TestCase ("After")]
	[TestCase ("release-android")]
	[TestCase ("release-processor")]
	public async Task FinalVerificationOrReleaseFailureNeverPasses (string fault)
		{
		var fake = new Operations { Fault = fault };
		var result = await InstalledDriverTests.RunCoreAsync (fake, default);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.CandidateVerified, Is.EqualTo (fault != "After"));
		Assert.That (result.ReservationsReleased, Is.EqualTo (fault == "After"));
		}

	[Test]
	public async Task CancellationDoesNotPreventSafeGuardAndReservationRelease ()
		{
		using var cancel = new CancellationTokenSource ();
		var fake = new Operations { AfterTests = cancel.Cancel };
		var result = await InstalledDriverTests.RunCoreAsync (fake, cancel.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ReservationsReleased, Is.True);
		Assert.That (fake.Calls, Does.Contain ("end"));
		Assert.That (result.Detail, Does.StartWith ("Cancelled"));
		}

	private sealed class Operations : IInstalledDriverTestOperations
		{
		public bool ProcessorAcquired { get; private set; }
		public bool AndroidAcquired { get; private set; }
		internal List<string> Calls = [];
		internal string? Fault;
		internal Action? AfterTests;
		internal AndroidTestOutcome Outcome = new (new (1, 0, 0, true), true);
		private Task Call (string name, CancellationToken token)
			{
			Calls.Add (name);
			token.ThrowIfCancellationRequested ();
			if (Fault == name) throw new IOException ("Synthetic failure; no hardware");
			return Task.CompletedTask;
			}
		public async Task AcquireProcessorAsync (CancellationToken token) { await Call ("processor", token); ProcessorAcquired = true; }
		public async Task AcquireAndroidAsync (CancellationToken token) { await Call ("android", token); AndroidAcquired = true; }
		public Task VerifyCandidateAsync (string phase, CancellationToken token) => Call (phase, token);
		public Task BeginControlAsync (CancellationToken token) => Call ("begin", token);
		public async Task<AndroidTestOutcome> RunTestsAsync (CancellationToken token) { await Call ("tests", token); AfterTests?.Invoke (); return Outcome; }
		public Task EndControlAsync (CancellationToken token) => Call ("end", token);
		public Task ReleaseAndroidAsync (CancellationToken token) => Call ("release-android", token);
		public Task ReleaseProcessorAsync (CancellationToken token) => Call ("release-processor", token);
		}
	}