// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace CrestronHomeNUnit.TestAdapter;

internal sealed record WorkflowEntry (string Id, string Name, string SettingsEnvironment);

internal static class WorkflowCatalog
	{
	internal const string Executor = "executor://CrestronHomeNUnit/Workflow/v1";
	internal static readonly Uri ExecutorUri = new (Executor);
	internal static readonly TestProperty EntryId = TestProperty.Register ("CrestronWorkflow.Id", "Workflow", typeof (string), typeof (TestCase));

	internal static IReadOnlyList<WorkflowEntry> Read (string source)
		{
		// The sidecar contains public names and environment-variable names only. Never read
		// private plans, credentials, assemblies or network state during discovery.
		var path = source + ".workflow-tests.xml";
		if (!File.Exists (path)) return [];
		using var reader = XmlReader.Create (path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
		var root = XDocument.Load (reader).Root;
		if (root?.Name != "Workflows") throw new InvalidDataException ("Expected a Workflows manifest.");
		var entries = root.Elements ().Select (element =>
			{
			if (element.Name != "Workflow" || element.HasElements) throw new InvalidDataException ("Invalid workflow entry.");
			var id = (string?)element.Attribute ("id") ?? "";
			var name = (string?)element.Attribute ("name") ?? "";
			var environment = (string?)element.Attribute ("settingsEnvironment") ?? "";
			if (!Regex.IsMatch (id, "^[A-Za-z][A-Za-z0-9_-]{0,63}$") || string.IsNullOrWhiteSpace (name) || name.Length > 120 ||
				!Regex.IsMatch (environment, "^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
				throw new InvalidDataException ("Invalid workflow ID, name or settings environment-variable name.");
			return new WorkflowEntry (id, name, environment);
			}).ToArray ();
		if (entries.Select (entry => entry.Id).Distinct (StringComparer.Ordinal).Count () != entries.Length)
			throw new InvalidDataException ("Workflow IDs must be unique in a container.");
		return entries;
		}

	internal static TestCase Create (string source, WorkflowEntry entry)
		{
		var test = new TestCase ("CrestronHome.Workflows." + entry.Id, ExecutorUri, source) { DisplayName = entry.Name };
		test.SetPropertyValue (EntryId, entry.Id);
		test.Traits.Add (new Trait ("Category", "ProcessorWorkflow"));
		return test;
		}
	}