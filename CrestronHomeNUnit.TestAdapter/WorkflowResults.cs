// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Xml;
using System.Xml.Linq;

using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace CrestronHomeNUnit.TestAdapter;

internal static class WorkflowResults
	{
	private static readonly TestProperty ExecutionId = TestProperty.Register ("ExecutionId", "ExecutionId", typeof (Guid), TestPropertyAttributes.Hidden, typeof (TestResult));
	private static readonly TestProperty ParentExecutionId = TestProperty.Register ("ParentExecId", "ParentExecId", typeof (Guid), TestPropertyAttributes.Hidden, typeof (TestResult));
	private static readonly TestProperty InnerResultsCount = TestProperty.Register ("InnerResultsCount", "InnerResultsCount", typeof (int), TestPropertyAttributes.Hidden, typeof (TestResult));

	internal static void Report (TestResult parent, string directory, IFrameworkHandle handle)
		{
		var children = Read (parent.TestCase, directory);
		var executionId = Guid.NewGuid ();
		parent.SetPropertyValue (ExecutionId, executionId);
		parent.SetPropertyValue (InnerResultsCount, children.Count);
		foreach (var child in children)
			{
			child.SetPropertyValue (ExecutionId, Guid.NewGuid ());
			child.SetPropertyValue (ParentExecutionId, executionId);
			child.StartTime = parent.StartTime;
			child.EndTime = parent.EndTime;
			}
		// Import every file before publishing a passing aggregate. A malformed result
		// must not leave Test Explorer displaying a successful deployment gate.
		if (children.Any (child => child.Outcome != TestOutcome.Passed)) parent.Outcome = TestOutcome.Failed;
		handle.RecordResult (parent);
		foreach (var child in children) handle.RecordResult (child);
		}

	internal static IReadOnlyList<TestResult> Read (TestCase parent, string directory)
		{
		var children = new List<TestResult> ();
		if (!Directory.Exists (directory)) return children;
		foreach (var suiteDirectory in Directory.EnumerateDirectories (directory).Order (StringComparer.Ordinal))
			{
			var name = Path.GetFileName (suiteDirectory);
			if (name.StartsWith ("local-", StringComparison.Ordinal))
				ReadTrx (Path.Combine (suiteDirectory, "TestResult.trx"), name);
			else if (name.StartsWith ("processor-", StringComparison.Ordinal) || name.StartsWith ("live-", StringComparison.Ordinal))
				ReadNUnit (Path.Combine (suiteDirectory, "TestResult.xml"), name);
			}
		ReadNUnit (Path.Combine (directory, "InstalledDriver.xml"), "Installed driver");
		return children;

		void ReadNUnit (string path, string stage)
			{
			if (!File.Exists (path)) return;
			foreach (var element in Load (path).Descendants ("test-case"))
				{
				var child = Case (stage, (string?)element.Attribute ("fullname") ?? (string?)element.Attribute ("name") ?? "Unnamed test", (string?)element.Attribute ("result"));
				if (double.TryParse ((string?)element.Attribute ("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite (seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
					child.Duration = TimeSpan.FromSeconds (seconds);
				child.ErrorMessage = (string?)element.Element ("failure")?.Element ("message") ?? (string?)element.Element ("reason")?.Element ("message");
				child.ErrorStackTrace = (string?)element.Element ("failure")?.Element ("stack-trace");
				}
			}

		void ReadTrx (string path, string stage)
			{
			if (!File.Exists (path)) return;
			foreach (var element in Load (path).Descendants ().Where (element => element.Name.LocalName == "UnitTestResult"))
				{
				var child = Case (stage, (string?)element.Attribute ("testName") ?? "Unnamed test", (string?)element.Attribute ("outcome"));
				if (TimeSpan.TryParse ((string?)element.Attribute ("duration"), CultureInfo.InvariantCulture, out var duration) && duration >= TimeSpan.Zero) child.Duration = duration;
				child.ErrorMessage = element.Descendants ().FirstOrDefault (element => element.Name.LocalName == "Message")?.Value;
				child.ErrorStackTrace = element.Descendants ().FirstOrDefault (element => element.Name.LocalName == "StackTrace")?.Value;
				}
			}

		TestResult Case (string stage, string name, string? outcome)
			{
			var child = new TestResult (parent)
				{
				DisplayName = stage + " / " + name,
				Outcome = outcome switch { "Passed" => TestOutcome.Passed, "Skipped" or "Inconclusive" or "NotExecuted" => TestOutcome.Skipped, _ => TestOutcome.Failed }
				};
			children.Add (child);
			return child;
			}
		}

	private static XDocument Load (string path)
		{
		using var reader = XmlReader.Create (path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 128 * 1024 * 1024 });
		return XDocument.Load (reader);
		}
	}