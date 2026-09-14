// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;

using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace CrestronHomeNUnit.TestAdapter;

[FileExtension (".dll"), DefaultExecutorUri (WorkflowCatalog.Executor)]
public sealed class WorkflowDiscoverer : ITestDiscoverer
	{
	public void DiscoverTests (IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
		{
		foreach (var source in sources)
			{
			try
				{
				foreach (var entry in WorkflowCatalog.Read (source)) discoverySink.SendTestCase (WorkflowCatalog.Create (source, entry));
				}
			catch
				{
				logger.SendMessage (TestMessageLevel.Error, "A Crestron workflow discovery manifest is invalid. Check its IDs, names and settingsEnvironment attributes.");
				}
			}
		}
	}

[ExtensionUri (WorkflowCatalog.Executor)]
public sealed class WorkflowExecutor : ITestExecutor
	{
	private readonly IWorkflowExecution _execution;
	private readonly CancellationTokenSource _stop = new ();
	public WorkflowExecutor () : this (new WorkflowExecution ()) { }
	internal WorkflowExecutor (IWorkflowExecution execution) => _execution = execution;
	public void Cancel () => _stop.Cancel ();

	public void RunTests (IEnumerable<string>? sources, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
		{
		if (sources == null || frameworkHandle == null) return;
		var tests = new List<TestCase> ();
		new WorkflowDiscoverer ().DiscoverTests (sources, runContext!, frameworkHandle, new Sink (tests));
		RunTests (tests, runContext, frameworkHandle);
		}

	public void RunTests (IEnumerable<TestCase>? tests, IRunContext? runContext, IFrameworkHandle? frameworkHandle)
		{
		if (tests == null || frameworkHandle == null) return;
		// Serial execution prevents this adapter from racing its own deployment/build gates.
		// The shared remote lease also excludes workflows from other test hosts or machines.
		var filter = runContext?.GetTestCaseFilter (["FullyQualifiedName", "DisplayName", "TestCategory"], _ => null);
		foreach (var test in tests.DistinctBy (test => test.Id))
			{
			if (_stop.IsCancellationRequested) break;
			if (filter != null && !filter.MatchTestCase (test, property => property switch
				{
				"FullyQualifiedName" => test.FullyQualifiedName,
				"DisplayName" => test.DisplayName,
				"TestCategory" => new[] { "ProcessorWorkflow" },
				_ => null
				})) continue;
			RunOneAsync (test, runContext?.TestRunDirectory, frameworkHandle).GetAwaiter ().GetResult ();
			}
		}

	private async Task RunOneAsync (TestCase test, string? runDirectory, IFrameworkHandle handle)
		{
		handle.RecordStart (test);
		var start = DateTimeOffset.UtcNow;
		var elapsed = Stopwatch.StartNew ();
		var parent = new TestResult (test) { DisplayName = test.DisplayName, StartTime = start, Outcome = TestOutcome.Failed };
		var directory = Path.Combine (runDirectory ?? Path.Combine (Path.GetTempPath (), "CrestronHomeNUnit"), "workflow-" + Guid.NewGuid ().ToString ("N"));
		try
			{
			var id = test.GetPropertyValue (WorkflowCatalog.EntryId, "");
			var entry = WorkflowCatalog.Read (test.Source).Single (entry => entry.Id == id);
			Directory.CreateDirectory (directory);
			var result = await _execution.RunAsync (entry, directory, stage =>
				{
				var message = stage.Stage + ": " + stage.Outcome;
				if (stage.Tests != null) message += $" ({stage.Tests.Passed} passed, {stage.Tests.Failed} failed, {stage.Tests.Skipped} skipped)";
				handle.SendMessage (TestMessageLevel.Informational, message);
				}, _stop.Token).ConfigureAwait (false);
			parent.Outcome = result.Passed && !_stop.IsCancellationRequested ? TestOutcome.Passed : TestOutcome.Failed;
			if (parent.Outcome != TestOutcome.Passed) parent.ErrorMessage = "Workflow did not pass all required stages. Inspect the child results and retained evidence.";
			foreach (var stage in result.Stages)
				parent.Messages.Add (new TestResultMessage (TestResultMessage.StandardOutCategory, stage.Stage + ": " + stage.Outcome + (stage.Detail == null ? "" : ". " + stage.Detail)));
			}
		catch
			{
			// Raw exceptions may include credentials or transferred device configuration.
			parent.ErrorMessage = _stop.IsCancellationRequested
				? "Workflow cancelled. Review retained evidence and lease state before retrying."
				: "Workflow could not complete. Verify the private settings environment variable and inspect retained workflow evidence.";
			}
		parent.EndTime = DateTimeOffset.UtcNow;
		parent.Duration = elapsed.Elapsed;
		parent.Messages.Add (new TestResultMessage (TestResultMessage.StandardOutCategory, "Private workflow evidence: " + directory));
		try
			{
			WorkflowResults.Report (parent, directory, handle);
			}
		catch
			{
			parent.Outcome = TestOutcome.Failed;
			parent.ErrorMessage = "Workflow result import failed. Retained files remain authoritative.";
			handle.RecordResult (parent);
			}
		finally { handle.RecordEnd (test, parent.Outcome); }
		}

	private sealed class Sink (List<TestCase> tests) : ITestCaseDiscoverySink
		{
		public void SendTestCase (TestCase discoveredTest) => tests.Add (discoveredTest);
		}
	}