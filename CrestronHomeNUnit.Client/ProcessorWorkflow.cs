// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeNUnit.Client;

public sealed record WorkflowTestOutcome (int Passed, int Failed, int Skipped, bool Complete)
	{
	public bool MeetsGate => Complete && Passed > 0 && Failed == 0 && Skipped == 0;
	}

public sealed record ProcessorWorkflowOptions (bool IsDriverProject, bool DeployActualDriver,
	 bool RunProcessorLiveTests, bool RunDeployedDriverLiveTests, bool RemoveTestInstanceAfterRun);

public sealed record WorkflowStageResult (string Stage, string Outcome, WorkflowTestOutcome? Tests = null, string? Detail = null);
public sealed record ProcessorWorkflowResult (IReadOnlyList<WorkflowStageResult> Stages, bool DriverUpdateAttempted, bool DriverUpdateVerified)
	{
	public bool Passed => Stages.Count > 0 && Stages.All (stage => stage.Outcome == "Passed");
	}

// Implementations must preserve individual NUnit results and verify package/instance versions.
// They own the processor lease, immutable build artifacts and the exact test-instance identity.
public interface IProcessorWorkflowOperations
	{
	bool HasTestInstance
		{
		get;
		}
	bool RemoteExecutionConfirmedStopped
		{
		get;
		}
	Task<WorkflowTestOutcome> RunLocalTestsAsync (CancellationToken cancellationToken);
	Task PrepareTestInstanceAsync (CancellationToken cancellationToken);
	Task<WorkflowTestOutcome> RunProcessorTestsAsync (CancellationToken cancellationToken);
	Task<WorkflowTestOutcome> RunProcessorLiveTestsAsync (CancellationToken cancellationToken);
	Task DeployAndVerifyActualDriverAsync (CancellationToken cancellationToken);
	Task<WorkflowTestOutcome> RunDeployedDriverLiveTestsAsync (CancellationToken cancellationToken);
	Task RemoveTestInstanceAsync (CancellationToken cancellationToken);
	}

public static class ProcessorWorkflow
	{
	public static async Task<ProcessorWorkflowResult> RunAsync (ProcessorWorkflowOptions options,
		 IProcessorWorkflowOperations operations, Action<WorkflowStageResult>? report = null, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (options);
		ArgumentNullException.ThrowIfNull (operations);
		if (options.DeployActualDriver && (!options.IsDriverProject || !options.RunDeployedDriverLiveTests))
			throw new ArgumentException ("Actual-driver deployment requires a driver project and configured post-deployment checks.", nameof (options));
		if (options.RunDeployedDriverLiveTests && !options.DeployActualDriver)
			throw new ArgumentException ("This workflow's post-deployment stage requires its actual-driver deployment stage.", nameof (options));
		var results = new List<WorkflowStageResult> ();
		var updateAttempted = false;
		var updateVerified = false;
		var stage = "Local";
		bool reportingFailed = false;
		void Record (WorkflowStageResult result)
			{
			results.Add (result);
			if (reportingFailed || report == null)
				return;
			try
				{
				report (result);
				}
			catch
				{
				reportingFailed = true;
				results.Add (new ("Save workflow evidence", "Error", Detail: "Stage reporting failed; the deployment gate is closed."));
				}
			}
		async Task<bool> Tests (string name, Func<CancellationToken, Task<WorkflowTestOutcome>> run)
			{
			stage = name;
			cancellationToken.ThrowIfCancellationRequested ();
			var outcome = await run (cancellationToken).ConfigureAwait (false);
			Record (new (name, outcome.MeetsGate ? "Passed" : "Failed", outcome,
				 outcome.MeetsGate ? null : "Required tests did not all execute and pass; the deployment gate is closed."));
			return outcome.MeetsGate && !reportingFailed;
			}
		try
			{
			if (!await Tests ("Local", operations.RunLocalTestsAsync).ConfigureAwait (false))
				return Result ();
			stage = "Prepare processor test instance";
			cancellationToken.ThrowIfCancellationRequested ();
			await operations.PrepareTestInstanceAsync (cancellationToken).ConfigureAwait (false);
			Record (new (stage, "Passed"));
			if (reportingFailed)
				return Result ();
			if (!await Tests ("Processor", operations.RunProcessorTestsAsync).ConfigureAwait (false))
				return Result ();
			if (options.RunProcessorLiveTests && !await Tests ("Processor live", operations.RunProcessorLiveTestsAsync).ConfigureAwait (false))
				return Result ();
			if (options.DeployActualDriver)
				{
				stage = "Update actual driver";
				cancellationToken.ThrowIfCancellationRequested ();
				updateAttempted = true;
				await operations.DeployAndVerifyActualDriverAsync (cancellationToken).ConfigureAwait (false);
				updateVerified = true;
				Record (new (stage, "Passed"));
				if (reportingFailed)
					return Result ();
				if (!await Tests ("Deployed driver live", operations.RunDeployedDriverLiveTestsAsync).ConfigureAwait (false))
					return Result ();
				}
			}
		catch (OperationCanceledException)
			{
			Record (new (stage, "Cancelled", Detail: "The stage did not complete. A submitted processor operation may still be running."));
			}
		catch (Exception)
			{
			// Exception text from network/configuration code may contain private inputs.
			Record (new (stage, "Error", Detail: "The stage did not complete; retained test artifacts remain authoritative."));
			}
		finally
			{
			if (options.RemoveTestInstanceAfterRun && operations.HasTestInstance)
				{
				if (!operations.RemoteExecutionConfirmedStopped)
					Record (new ("Remove test instance", "Deferred", Detail: "Remote execution has not been confirmed stopped; the test instance was retained."));
				else
					{
					using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (30));
					try
						{
						await operations.RemoveTestInstanceAsync (cleanup.Token).ConfigureAwait (false);
						Record (new ("Remove test instance", "Passed"));
						}
					catch (Exception)
						{
						Record (new ("Remove test instance", "Error", Detail: "Test results are preserved, but removal could not be confirmed."));
						}
					}
				}
			}
		return Result ();

		// Keep the shared list so finally-stage cleanup is present even after an early return.
		ProcessorWorkflowResult Result () => new (results.AsReadOnly (), updateAttempted, updateVerified);
		}
	}