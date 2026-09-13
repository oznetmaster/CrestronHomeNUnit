// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeNUnit.Client.Tests;

[TestFixture]
public sealed class WorkflowTests
	{
	private static ProcessorWorkflowOptions Driver (bool cleanup = true) => new (true, true, true, true, cleanup);

	[Test]
	public async Task DriverRunsBothLiveStagesAndCleansUpLast ()
		{
		var operations = new FakeOperations ();
		var result = await ProcessorWorkflow.RunAsync (Driver (), operations);
		Assert.That (result.Passed, Is.True);
		Assert.That (result.DriverUpdateVerified, Is.True);
		Assert.That (operations.Calls, Is.EqualTo (new[] { "local", "prepare", "processor", "processorLive", "deploy", "driverLive", "remove" }));
		}

	[TestCase ("local")]
	[TestCase ("processor")]
	[TestCase ("processorLive")]
	public async Task PredeploymentFailureKeepsActualDriverUntouched (string failedStage)
		{
		var operations = new FakeOperations { FailedStage = failedStage };
		var result = await ProcessorWorkflow.RunAsync (Driver (), operations);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.DriverUpdateAttempted, Is.False);
		Assert.That (operations.Calls, Does.Not.Contain ("deploy"));
		if (failedStage != "local")
			Assert.That (result.Stages.Last ().Stage, Is.EqualTo ("Remove test instance"));
		}

	[TestCase (0, 0, 0, true)]
	[TestCase (1, 0, 1, true)]
	[TestCase (1, 0, 0, false)]
	public async Task MissingSkippedOrIncompleteRequiredTestsBlockGate (int passed, int failed, int skipped, bool complete)
		{
		var operations = new FakeOperations { LiveOutcome = new (passed, failed, skipped, complete) };
		var result = await ProcessorWorkflow.RunAsync (Driver (), operations);
		Assert.That (result.DriverUpdateAttempted, Is.False);
		Assert.That (result.Passed, Is.False);
		}

	[Test]
	public async Task PostdeploymentFailureKeepsInstalledVersionOutcomeAndOriginalFailure ()
		{
		var operations = new FakeOperations { FailedStage = "driverLive", FailCleanup = true };
		var result = await ProcessorWorkflow.RunAsync (Driver (), operations);
		Assert.That (result.DriverUpdateVerified, Is.True);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.Stages.Single (s => s.Stage == "Deployed driver live").Outcome, Is.EqualTo ("Failed"));
		Assert.That (result.Stages.Last ().Outcome, Is.EqualTo ("Error"));
		}

	[Test]
	public async Task UnknownRemoteExecutionRetainsInstance ()
		{
		var operations = new FakeOperations { Disconnect = true };
		var result = await ProcessorWorkflow.RunAsync (Driver (), operations);
		Assert.That (result.Stages.Last ().Outcome, Is.EqualTo ("Deferred"));
		Assert.That (operations.Calls, Does.Not.Contain ("remove"));
		Assert.That (result.DriverUpdateAttempted, Is.False);
		}

	[Test]
	public async Task LibraryWorkflowStopsAfterProcessorTesting ()
		{
		var operations = new FakeOperations ();
		var result = await ProcessorWorkflow.RunAsync (new (false, false, false, false, false), operations);
		Assert.That (result.Passed, Is.True);
		Assert.That (operations.Calls, Is.EqualTo (new[] { "local", "prepare", "processor" }));
		}

	[Test]
	public void DriverDeploymentRequiresPostdeploymentChecks ()
		{
		var operations = new FakeOperations ();
		Assert.ThrowsAsync<ArgumentException> (async () => await ProcessorWorkflow.RunAsync (new (true, true, true, false, true), operations));
		Assert.That (operations.Calls, Is.Empty);
		}

	[Test]
	public async Task ReportingFailureClosesGateWithoutLosingCleanupOrOriginalTestResult ()
		{
		var operations = new FakeOperations ();
		var result = await ProcessorWorkflow.RunAsync (Driver (), operations, stage =>
		{
			if (stage.Stage == "Processor")
				throw new IOException ("Disk full");
		});
		Assert.That (result.Passed, Is.False);
		Assert.That (result.DriverUpdateAttempted, Is.False);
		Assert.That (result.Stages.Any (s => s.Stage == "Processor" && s.Tests?.Passed == 1), Is.True);
		Assert.That (result.Stages.Any (s => s.Stage == "Save workflow evidence"), Is.True);
		Assert.That (operations.Calls.Last (), Is.EqualTo ("remove"));
		}

	private sealed class FakeOperations : IProcessorWorkflowOperations
		{
		public List<string> Calls { get; } = [];
		public bool HasTestInstance
			{
			get; private set;
			}
		public bool RemoteExecutionConfirmedStopped { get; private set; } = true;
		public string? FailedStage
			{
			get; init;
			}
		public WorkflowTestOutcome? LiveOutcome
			{
			get; init;
			}
		public bool FailCleanup
			{
			get; init;
			}
		public bool Disconnect
			{
			get; init;
			}
		private Task<WorkflowTestOutcome> Run (string stage)
			{
			Calls.Add (stage);
			if (Disconnect && stage == "processor")
				{
				RemoteExecutionConfirmedStopped = false;
				throw new IOException ("Disconnected");
				}
			return Task.FromResult (stage == "processorLive" && LiveOutcome != null ? LiveOutcome : stage == FailedStage ? new (0, 1, 0, true) : new WorkflowTestOutcome (1, 0, 0, true));
			}
		public Task<WorkflowTestOutcome> RunLocalTestsAsync (CancellationToken token) => Run ("local");
		public Task<WorkflowTestOutcome> RunProcessorTestsAsync (CancellationToken token) => Run ("processor");
		public Task<WorkflowTestOutcome> RunProcessorLiveTestsAsync (CancellationToken token) => Run ("processorLive");
		public Task<WorkflowTestOutcome> RunDeployedDriverLiveTestsAsync (CancellationToken token) => Run ("driverLive");
		public Task PrepareTestInstanceAsync (CancellationToken token)
			{
			Calls.Add ("prepare");
			HasTestInstance = true;
			return Task.CompletedTask;
			}
		public Task DeployAndVerifyActualDriverAsync (CancellationToken token)
			{
			Calls.Add ("deploy");
			return Task.CompletedTask;
			}
		public Task RemoveTestInstanceAsync (CancellationToken token)
			{
			Calls.Add ("remove");
			if (FailCleanup)
				throw new IOException ("Cleanup failed");
			HasTestInstance = false;
			return Task.CompletedTask;
			}
		}
	}