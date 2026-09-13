// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Xml;
using System.Xml.Linq;

using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Client;

public sealed record TestResultSummary (bool Complete, int Total, int Passed, int Failed, int Skipped, string Outcome, int HostExitCode = 0)
	{
	public int ExitCode => !Complete ? 3 : HostExitCode != 0 || Failed > 0 || Outcome is "Failed" or "Cancelled" ? 1 : Total == 0 ? 4 : 0;

	public static TestResultSummary FromResponse (WireMessage response)
		{
		if (response.Kind != "complete" || string.IsNullOrWhiteSpace (response.Xml))
			return new (false, 0, 0, 0, 0, "Incomplete");
		using var text = new StringReader (response.Xml);
		using var reader = XmlReader.Create (text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
		var root = XDocument.Load (reader).Root;
		if (root?.Name.LocalName != "test-run")
			throw new InvalidDataException ("Processor did not return an NUnit test-run result.");
		int Count (string name) => int.TryParse ((string?)root.Attribute (name), out var value) && value >= 0 ? value : throw new InvalidDataException ("NUnit result contains a missing or invalid count.");
		var total = Count ("total");
		var outcome = (string?)root.Attribute ("result") ?? "Unknown";
		if (outcome is not ("Passed" or "Failed" or "Warning" or "Skipped" or "Inconclusive" or "Cancelled"))
			return new (false, total, Count ("passed"), Count ("failed"), Count ("skipped"), outcome);
		return new (true, total, Count ("passed"), Count ("failed"), Count ("skipped"), outcome, response.ExitCode);
		}
	}