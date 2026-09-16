// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Xml;
using System.Xml.Linq;

using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

internal static class AndroidTestCoverage
	{
	private static XDocument Read (string path)
		{
		using var reader = XmlReader.Create (path, new XmlReaderSettings
			{
			DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20 * 1024 * 1024
			});
		return XDocument.Load (reader);
		}

	private static string Required (XElement element, string name) =>
		(string?)element.Attribute (name) is string value && !string.IsNullOrWhiteSpace (value) ? value : throw new InvalidDataException ("Test evidence is missing " + name + ".");

	public static string[] ReadDiscovery (string path)
		{
		var document = Read (path);
		var runs = document.Descendants ("test-run").ToArray ();
		if (runs.Length != 1 || runs[0].DescendantsAndSelf ().Any (e => (string?)e.Attribute ("runstate") is string state && state != "Runnable"))
			throw new InvalidDataException ("UI discovery must contain one runnable NUnit tree with no explicit, ignored or invalid tests.");
		var cases = runs[0].Descendants ("test-case").ToArray ();
		if (cases.Length == 0 || cases.Any (e => (string?)e.Attribute ("runstate") != "Runnable") || cases.Select (e => Required (e, "id")).Distinct (StringComparer.Ordinal).Count () != cases.Length ||
			!int.TryParse ((string?)runs[0].Attribute ("testcasecount"), out var count) || count != cases.Length)
			throw new InvalidDataException ("UI discovery contains empty, duplicate or incomplete case records.");
		return cases.Select (e => Required (e, "fullname")).Order (StringComparer.Ordinal).ToArray ();
		}

	public static WorkflowTestOutcome Evaluate (string[] expected, string[] resultPaths, int exitCode)
		{
		var observed = new List<string> ();
		var executions = new HashSet<string> (StringComparer.Ordinal);
		int passed = 0, failed = 0, skipped = 0;
		bool complete = expected.Length > 0 && resultPaths.Length == 1 && exitCode == 0;
		foreach (var path in resultPaths)
			{
			var document = Read (path);
			var root = document.Root ?? throw new InvalidDataException ("Missing TRX root.");
			if (root.Name.LocalName != "TestRun")
				throw new InvalidDataException ("Unexpected TRX root.");
			var ns = root.Name.Namespace;
			var definitions = root.Element (ns + "TestDefinitions")?.Elements (ns + "UnitTest").ToDictionary (e => Required (e, "id"), StringComparer.Ordinal)
				?? throw new InvalidDataException ("TRX has no test definitions.");
			var results = root.Element (ns + "Results")?.Elements (ns + "UnitTestResult").ToArray () ?? [];
			var counters = root.Element (ns + "ResultSummary")?.Element (ns + "Counters") ?? throw new InvalidDataException ("TRX has no counters.");
			complete &= (string?)counters.Parent!.Attribute ("outcome") == "Completed";
			int Count (string name) => int.TryParse (Required (counters, name), out var value) && value >= 0 ? value : throw new InvalidDataException ("Invalid TRX counter.");
			int filePassed = 0, fileFailed = 0, fileSkipped = 0;
			foreach (var result in results)
				{
				if (!executions.Add (Required (result, "executionId")) || !definitions.TryGetValue (Required (result, "testId"), out var definition))
					throw new InvalidDataException ("TRX execution is duplicated or has no definition.");
				var method = definition.Element (ns + "TestMethod") ?? throw new InvalidDataException ("TRX test method is missing.");
				if (Required (method, "adapterTypeName") != "executor://nunit3testexecutor/")
					throw new InvalidDataException ("UI coverage requires NUnit results.");
				observed.Add (Required (method, "className") + "." + Required (definition, "name"));
				switch (Required (result, "outcome"))
					{
					case "Passed": filePassed++; break;
					case "Failed": fileFailed++; break;
					default: fileSkipped++; break;
					}
				}
			complete &= Count ("total") == results.Length && Count ("passed") == filePassed && Count ("failed") == fileFailed;
			passed += filePassed;
			failed += fileFailed;
			skipped += fileSkipped;
			}
		complete &= expected.Order (StringComparer.Ordinal).SequenceEqual (observed.Order (StringComparer.Ordinal), StringComparer.Ordinal);
		return new (passed, failed, skipped, complete);
		}
	}