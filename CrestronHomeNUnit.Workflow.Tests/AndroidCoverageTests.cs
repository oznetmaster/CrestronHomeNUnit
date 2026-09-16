// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Xml.Linq;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class AndroidCoverageTests
	{
	private string _directory = null!;

	[SetUp]
	public void Prepare ()
		{
		_directory = Path.Combine (Path.GetTempPath (), "android-coverage-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		}

	[TearDown]
	public void Remove () => Directory.Delete (_directory, recursive: true);

	private string Discovery (params string[] names)
		{
		var path = Path.Combine (_directory, "discovery.dump");
		new XElement ("NUnitXml", new XElement ("test-run", new XAttribute ("testcasecount", names.Length), new XAttribute ("runstate", "Runnable"),
			names.Select ((name, index) => new XElement ("test-case", new XAttribute ("id", index), new XAttribute ("fullname", "Example." + name), new XAttribute ("runstate", "Runnable"))))).Save (path);
		return path;
		}

	private string Results (params (string Name, string Outcome)[] cases)
		{
		var path = Path.Combine (_directory, "TestResult.trx");
		XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
		new XElement (ns + "TestRun",
			new XElement (ns + "ResultSummary", new XAttribute ("outcome", "Completed"), new XElement (ns + "Counters",
				new XAttribute ("total", cases.Length), new XAttribute ("passed", cases.Count (c => c.Outcome == "Passed")), new XAttribute ("failed", cases.Count (c => c.Outcome == "Failed")))),
			new XElement (ns + "Results", cases.Select ((test, index) => new XElement (ns + "UnitTestResult", new XAttribute ("testId", index), new XAttribute ("executionId", "execution-" + index), new XAttribute ("outcome", test.Outcome)))),
			new XElement (ns + "TestDefinitions", cases.Select ((test, index) => new XElement (ns + "UnitTest", new XAttribute ("id", index), new XAttribute ("name", test.Name),
				new XElement (ns + "TestMethod", new XAttribute ("className", "Example"), new XAttribute ("adapterTypeName", "executor://nunit3testexecutor/")))))).Save (path);
		return path;
		}

	[Test]
	public void ParameterizedNamesAndDuplicateDisplaysRetainMultiplicity ()
		{
		string[] names = ["Case(\"a,b\")", "SameDisplay", "SameDisplay"];
		var expected = AndroidTestCoverage.ReadDiscovery (Discovery (names));
		var path = Results (names.Reverse ().Select (name => (name, "Passed")).ToArray ());
		Assert.That (AndroidTestCoverage.Evaluate (expected, [path], 0).MeetsGate, Is.True);
		Results (("Case(\"a,b\")", "Passed"), ("SameDisplay", "Passed"));
		Assert.That (AndroidTestCoverage.Evaluate (expected, [path], 0).MeetsGate, Is.False);
		}

	[TestCase ("B", "Passed")]
	[TestCase ("A", "NotExecuted")]
	[TestCase ("A", "Failed")]
	public void UnknownSkippedOrFailedCaseCannotSatisfyDiscovery (string name, string outcome)
		{
		var expected = AndroidTestCoverage.ReadDiscovery (Discovery ("A"));
		Assert.That (AndroidTestCoverage.Evaluate (expected, [Results ((name, outcome))], 0).MeetsGate, Is.False);
		}

	[Test]
	public void EmptyExtraOrNonzeroExitResultsFail ()
		{
		var expected = AndroidTestCoverage.ReadDiscovery (Discovery ("A"));
		var path = Results (("A", "Passed"));
		Assert.That (AndroidTestCoverage.Evaluate (expected, [], 0).MeetsGate, Is.False);
		Assert.That (AndroidTestCoverage.Evaluate (expected, [path], 1).MeetsGate, Is.False);
		Results (("A", "Passed"), ("Extra", "Passed"));
		Assert.That (AndroidTestCoverage.Evaluate (expected, [path], 0).MeetsGate, Is.False);
		}

	[TestCase ("Explicit")]
	[TestCase ("Ignored")]
	[TestCase ("NotRunnable")]
	public void NonRunnableDiscoveryIsRejectedBeforeExecution (string state)
		{
		var path = Discovery ("A");
		var document = XDocument.Load (path);
		document.Descendants ("test-run").Single ().SetAttributeValue ("runstate", state);
		document.Save (path);
		Assert.Throws<InvalidDataException> (() => AndroidTestCoverage.ReadDiscovery (path));
		}

	[Test]
	public void EmptyOrInconsistentDiscoveryIsRejected ()
		{
		Assert.Throws<InvalidDataException> (() => AndroidTestCoverage.ReadDiscovery (Discovery ()));
		var path = Discovery ("A", "B");
		var document = XDocument.Load (path);
		document.Descendants ("test-case").Last ().SetAttributeValue ("id", "0");
		document.Save (path);
		Assert.Throws<InvalidDataException> (() => AndroidTestCoverage.ReadDiscovery (path));
		path = Discovery ("A");
		document = XDocument.Load (path);
		document.Descendants ("test-run").Single ().SetAttributeValue ("testcasecount", 2);
		document.Save (path);
		Assert.Throws<InvalidDataException> (() => AndroidTestCoverage.ReadDiscovery (path));
		}

	[TestCase ("duplicate-execution")]
	[TestCase ("unknown-definition")]
	[TestCase ("wrong-adapter")]
	public void InconsistentTrxIdentityIsRejected (string change)
		{
		var path = Results (("A", "Passed"), ("B", "Passed"));
		var document = XDocument.Load (path);
		var ns = document.Root!.Name.Namespace;
		if (change == "duplicate-execution") document.Descendants (ns + "UnitTestResult").Last ().SetAttributeValue ("executionId", "execution-0");
		if (change == "unknown-definition") document.Descendants (ns + "UnitTestResult").Last ().SetAttributeValue ("testId", "missing");
		if (change == "wrong-adapter") document.Descendants (ns + "TestMethod").First ().SetAttributeValue ("adapterTypeName", "different");
		document.Save (path);
		Assert.Throws<InvalidDataException> (() => AndroidTestCoverage.Evaluate (["Example.A", "Example.B"], [path], 0));
		}

	[TestCase ("counter")]
	[TestCase ("aborted")]
	public void IncompleteSummaryCannotPass (string change)
		{
		var path = Results (("A", "Passed"));
		var document = XDocument.Load (path);
		var ns = document.Root!.Name.Namespace;
		if (change == "counter") document.Descendants (ns + "Counters").Single ().SetAttributeValue ("total", 2);
		else document.Descendants (ns + "ResultSummary").Single ().SetAttributeValue ("outcome", "Aborted");
		document.Save (path);
		Assert.That (AndroidTestCoverage.Evaluate (["Example.A"], [path], 0).MeetsGate, Is.False);
		}
	}