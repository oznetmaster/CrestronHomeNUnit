// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

internal delegate Task<ManagedDeviceValidationResult> ManagedAndroidLifecycle (
	IReadOnlyList<ManagedDeviceTestTarget> targets,
	Func<IReadOnlyList<ManagedDeviceTestBinding>, CancellationToken, Task<ManagedDeviceTestOutcome>> tests, CancellationToken token);

internal static class WorkflowManagedAndroid
	{
	internal static Task<AndroidTestOutcome> RunAsync (AndroidTestPlan plan, DriverInstanceReady actual, string journal,
		TimeSpan timeout, Func<CancellationToken, Task<ConfigurationClient>> openConnection, Func<CancellationToken, Task> verifyOwnership,
		Func<IReadOnlyList<AndroidManagedDeviceBinding>, CancellationToken, Task<AndroidTestOutcome>> runTests, CancellationToken token) =>
		RunCoreAsync (plan, actual, verifyOwnership, runTests,
			(targets, tests, ct) => ManagedDeviceValidation.RunAsync (openConnection, targets, journal, timeout, verifyOwnership, tests, ct), token);

	internal static async Task<AndroidTestOutcome> RunCoreAsync (AndroidTestPlan plan, DriverInstanceReady actual,
		Func<CancellationToken, Task> verifyOwnership,
		Func<IReadOnlyList<AndroidManagedDeviceBinding>, CancellationToken, Task<AndroidTestOutcome>> runTests,
		ManagedAndroidLifecycle lifecycle, CancellationToken token = default)
		{
		plan.ValidateManagedChildren ();
		var targets = plan.ManagedChildren.Select (child => new ManagedDeviceTestTarget (child.Alias,
			new (actual.DeviceId, actual.Model, actual.Version, child.ManagedDeviceId, child.Name, child.Model, child.LocationId))).ToArray ();
		await verifyOwnership (token).ConfigureAwait (false);
		if (targets.Length == 0)
			{
			var legacy = await runTests ([], token).ConfigureAwait (false);
			await verifyOwnership (token).ConfigureAwait (false);
			return legacy;
			}
		AndroidTestOutcome? tested = null;
		var result = await lifecycle (targets, async (bindings, ct) =>
			{
			if (bindings.Count != targets.Length || targets.Any (target => bindings.Count (binding =>
				binding.Alias == target.Alias && binding.Request == target.Request) != 1))
				throw new InvalidDataException ("The created managed children do not match the requested test targets.");
			var mapped = Array.AsReadOnly (bindings.Select (binding => new AndroidManagedDeviceBinding (binding.Alias,
				binding.DeviceId, binding.Request.ParentId, binding.Request.ChildModel, binding.Request.Name, binding.Request.LocationId)).ToArray ());
			tested = await runTests (mapped, ct).ConfigureAwait (false);
			return new ManagedDeviceTestOutcome (tested.Tests.MeetsGate, tested.RestorationConfirmed);
			}, token).ConfigureAwait (false);
		await verifyOwnership (token).ConfigureAwait (false);
		return new (tested?.Tests ?? new WorkflowTestOutcome (0, 1, 0, false),
			result.RestorationConfirmed && tested?.RestorationConfirmed == true)
			{ CleanupConfirmed = result.CleanupConfirmed };
		}
	}