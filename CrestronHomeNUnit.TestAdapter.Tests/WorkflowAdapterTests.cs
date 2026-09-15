// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;

using CrestronHomeNUnit.Client;

using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

using NUnit.Framework;

namespace CrestronHomeNUnit.TestAdapter.Tests;

[TestFixture]
public sealed class WorkflowAdapterTests
	{
	private string _directory = null!;
	private string _source = null!;
	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (Path.GetTempPath (), "workflow-adapter-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		_source = Path.Combine (_directory, "Example.dll");
		File.WriteAllText (_source + ".workflow-tests.xml", "<Workflows><Workflow id='example' name='Example development workflow' settingsEnvironment='CRESTRON_ADAPTER_TEST_UNSET'/></Workflows>");
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_directory, true);

	[Test]
	public void Discovery_DoesNotRequireAnAssemblyPlanCredentialsOrNetwork ()
		{
		var entry = WorkflowCatalog.Read (_source).Single ();
		var first = WorkflowCatalog.Create (_source, entry);
		Assert.That (File.Exists (_source), Is.False);
		Assert.That (first.Id, Is.EqualTo (WorkflowCatalog.Create (_source, entry).Id));
		Assert.That (first.DisplayName, Is.EqualTo ("Example development workflow"));
		Assert.That (WorkflowCatalog.Read (Path.Combine (_directory, "OrdinaryNUnitTests.dll")), Is.Empty);
		}

	[TestCase ("<Workflows><Workflow id='same' name='a' settingsEnvironment='A'/><Workflow id='same' name='b' settingsEnvironment='B'/></Workflows>")]
	[TestCase ("<Workflows><Workflow id='../path' name='a' settingsEnvironment='A'/></Workflows>")]
	[TestCase ("<!DOCTYPE Workflows [<!ENTITY x SYSTEM 'file:///missing'>]><Workflows>&x;</Workflows>")]
	public void Discovery_RejectsAmbiguousOrUnsafeManifests (string xml)
		{
		File.WriteAllText (_source + ".workflow-tests.xml", xml);
		Assert.That (() => WorkflowCatalog.Read (_source), Throws.Exception);
		}

	[Test]
	public void SelectedWorkflow_RunsOnceAndReportsLocalRemoteAndInstalledResults ()
		{
		var backend = new FakeExecution ((directory, _) =>
			{
			Directory.CreateDirectory (Path.Combine (directory, "local-0"));
			File.WriteAllText (Path.Combine (directory, "local-0", "TestResult.trx"), "<TestRun xmlns='urn:test'><Results><UnitTestResult testName='Local' outcome='Passed' duration='00:00:01'/></Results></TestRun>");
			Directory.CreateDirectory (Path.Combine (directory, "processor-0"));
			File.WriteAllText (Path.Combine (directory, "processor-0", "TestResult.xml"), "<test-run><test-case fullname='Remote' result='Passed' duration='2.5'/></test-run>");
			File.WriteAllText (Path.Combine (directory, "InstalledDriver.xml"), "<test-run><test-case name='Online' result='Passed'/></test-run>");
			return Task.FromResult (new ProcessorWorkflowResult ([new ("Local", "Passed"), new ("Processor", "Passed"), new ("Update actual driver", "Passed")], true, true));
			});
		var (handle, calls) = Handle ();
		var test = WorkflowCatalog.Create (_source, WorkflowCatalog.Read (_source).Single ());
		new WorkflowExecutor (backend).RunTests ([test, test], Context (), handle);
		var results = calls.Results;
		Assert.That (backend.Calls, Is.EqualTo (1));
		Assert.That (results, Has.Count.EqualTo (4));
		Assert.That (results.All (result => result.Outcome == TestOutcome.Passed), Is.True);
		Assert.That (results.Skip (1).Select (result => result.DisplayName), Is.EquivalentTo (new[] { "local-0 / Local", "processor-0 / Remote", "Installed driver / Online" }));
		Assert.That (results[0].GetPropertyValue (TestProperty.Find ("InnerResultsCount")!, 0), Is.EqualTo (3));
		var id = results[0].GetPropertyValue (TestProperty.Find ("ExecutionId")!, Guid.Empty);
		Assert.That (results.Skip (1).All (result => result.GetPropertyValue (TestProperty.Find ("ParentExecId")!, Guid.Empty) == id), Is.True);
		}

	[TestCase ("Passed", TestOutcome.Passed)]
	[TestCase ("Failed", TestOutcome.Failed)]
	public void BothFrameworkResultsRemainVisibleAndAFailureCannotBeHidden (string secondOutcome, TestOutcome expected)
		{
		var backend = new FakeExecution ((directory, _) =>
			{
			var local = Path.Combine (directory, "local-0");
			Directory.CreateDirectory (local);
			File.WriteAllText (Path.Combine (local, "TestResult_net472.trx"), "<TestRun><Results><UnitTestResult testName='SameTest' outcome='Passed'/></Results></TestRun>");
			File.WriteAllText (Path.Combine (local, "TestResult_net10.0.trx"), $"<TestRun><Results><UnitTestResult testName='SameTest' outcome='{secondOutcome}'/></Results></TestRun>");
			return Task.FromResult (new ProcessorWorkflowResult ([new ("Local", "Passed")], false, false));
			});
		var (handle, calls) = Handle ();
		var test = WorkflowCatalog.Create (_source, WorkflowCatalog.Read (_source).Single ());
		new WorkflowExecutor (backend).RunTests ([test], Context (), handle);
		Assert.That (calls.Results, Has.Count.EqualTo (3));
		Assert.That (calls.Results[0].Outcome, Is.EqualTo (expected));
		Assert.That (calls.Results.Skip (1).Select (result => result.DisplayName), Is.EquivalentTo (new[]
			{ "local-0 / TestResult_net472 / SameTest", "local-0 / TestResult_net10.0 / SameTest" }));
		}

	[Test]
	public async Task Cancel_PropagatesToBackendAndWaitsForCleanup ()
		{
		var started = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
		var cleanup = new TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously);
		var backend = new FakeExecution (async (_, token) =>
			{
			started.SetResult ();
			try { await Task.Delay (Timeout.Infinite, token); }
			finally { await cleanup.Task; }
			return new ([], false, false);
			});
		var (handle, calls) = Handle ();
		var executor = new WorkflowExecutor (backend);
		var running = Task.Run (() => executor.RunTests (new[] { _source }, Context (), handle));
		await started.Task.WaitAsync (TimeSpan.FromSeconds (10));
		executor.Cancel ();
		Assert.That (running.IsCompleted, Is.False);
		cleanup.SetResult ();
		await running.WaitAsync (TimeSpan.FromSeconds (10));
		Assert.That (calls.Results.Single ().Outcome, Is.EqualTo (TestOutcome.Failed));
		Assert.That (calls.Results.Single ().ErrorMessage, Does.Contain ("cancelled"));
		}

	[Test]
	public void BackendException_DoesNotExposeSecretsOrPass ()
		{
		var (handle, calls) = Handle ();
		new WorkflowExecutor (new FakeExecution ((_, _) => throw new IOException ("secret-password-value"))).RunTests (new[] { _source }, Context (), handle);
		var result = calls.Results.Single ();
		Assert.That (result.Outcome, Is.EqualTo (TestOutcome.Failed));
		Assert.That (result.ErrorMessage, Does.Not.Contain ("secret-password-value"));
		}

	[Test]
	public void MalformedResults_CloseTheGateBeforePublishingPassingAggregate ()
		{
		var (handle, calls) = Handle ();
		new WorkflowExecutor (new FakeExecution ((directory, _) =>
			{
			File.WriteAllText (Path.Combine (directory, "InstalledDriver.xml"), "not xml");
			return Task.FromResult (new ProcessorWorkflowResult ([new ("Checks", "Passed")], true, true));
			})).RunTests (new[] { _source }, Context (), handle);
		Assert.That (calls.Results.Single ().Outcome, Is.EqualTo (TestOutcome.Failed));
		}

	[Test]
	public void NUnitFailures_AreDecodedAndUnknownOutcomesFail ()
		{
		File.WriteAllText (Path.Combine (_directory, "InstalledDriver.xml"), "<test-run><test-case name='Check' result='Unknown'><failure><message>A &amp; B</message><stack-trace>Here</stack-trace></failure></test-case></test-run>");
		var result = WorkflowResults.Read (WorkflowCatalog.Create (_source, WorkflowCatalog.Read (_source).Single ()), _directory).Single ();
		Assert.That (result.Outcome, Is.EqualTo (TestOutcome.Failed));
		Assert.That (result.ErrorMessage, Is.EqualTo ("A & B"));
		Assert.That (result.ErrorStackTrace, Is.EqualTo ("Here"));
		}

	private IRunContext Context ()
		{
		var context = DispatchProxy.Create<IRunContext, Calls> ();
		((Calls)(object)context).Directory = _directory;
		return context;
		}
	private static (IFrameworkHandle, Calls) Handle ()
		{
		var handle = DispatchProxy.Create<IFrameworkHandle, Calls> ();
		return (handle, (Calls)(object)handle);
		}
	public class Calls : DispatchProxy
		{
		public string? Directory { get; set; }
		public List<TestResult> Results { get; } = [];
		protected override object? Invoke (MethodInfo? targetMethod, object?[]? args)
			{
			if (targetMethod?.Name == "RecordResult") Results.Add ((TestResult)args![0]!);
			if (targetMethod?.Name == "get_TestRunDirectory") return Directory;
			return targetMethod?.ReturnType == typeof (bool) ? false : null;
			}
		}
	private sealed class FakeExecution (Func<string, CancellationToken, Task<ProcessorWorkflowResult>> run) : IWorkflowExecution
		{
		public int Calls { get; private set; }
		public Task<ProcessorWorkflowResult> RunAsync (WorkflowEntry entry, string results, Action<WorkflowStageResult> progress, CancellationToken token)
			{
			Calls++;
			return run (results, token);
			}
		}
	}