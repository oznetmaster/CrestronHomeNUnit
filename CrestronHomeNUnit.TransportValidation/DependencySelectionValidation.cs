// Copyright (c) 2026 Neil Colvin. MIT License; see LICENSE.
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CrestronHomeNUnit.Runtime;
using CrestronHomeNUnit.Transport;
using NUnit.Framework;

internal static class DependencySelectionValidation
	{
	public static async Task RunAsync ()
		{
		string work = Path.Combine (Path.GetTempPath (), "NUnitDependencySelection-" + Guid.NewGuid ().ToString ("N"));
		const string fixture = "DependencySelectionTests.Probe";
		string all = "<filter><class>" + fixture + "</class></filter>";
		var suites = new[]
			{
			new TestSuiteDefinition { Id = "all", Name = "Synthetic dependency tests", FilterXml = all },
			new TestSuiteDefinition { Id = "ordinary", Name = "Exclude Live", FilterXml = "<filter><and>" + all + "<not><cat>Live</cat></not></and></filter>" }
			};
		try
			{
			using var host = new TestExecutionService (work, _ => Assembly.GetExecutingAssembly (), suites);
			DependencySelectionTests.Probe.Calls = 0;
			WireMessage selected = await host.ExecuteAsync (new WireMessage { Kind = "run", Suite = "all", RequestId = Guid.NewGuid ().ToString ("N"), TestNames = [fixture + ".Dependent"] }, _ => { });
			Require (selected.Kind == "error" && selected.Text.Contains ("dependency is outside"), "Unselected prerequisite was not rejected.");
			Require (DependencySelectionTests.Probe.Calls == 0, "Setup or a test ran before rejecting expanded selection.");
			WireMessage ordinary = await host.ExecuteAsync (new WireMessage { Kind = "run", Suite = "ordinary", RequestId = Guid.NewGuid ().ToString ("N") }, _ => { });
			Require (ordinary.Kind == "error" && ordinary.Text.Contains ("dependency is outside"), "Live prerequisite escaped the ordinary suite filter.");
			Require (DependencySelectionTests.Probe.Calls == 0, "Live prerequisite ran without selection.");
			WireMessage authorized = await host.ExecuteAsync (new WireMessage { Kind = "run", Suite = "all", RequestId = Guid.NewGuid ().ToString ("N"), TestNames = [fixture + ".Dependent", fixture + ".Prerequisite"] }, _ => { });
			Require (authorized.Kind == "complete" && authorized.ExitCode == 0 && DependencySelectionTests.Probe.Calls == 3, "Explicitly selected prerequisite and dependent did not execute.");
			Console.WriteLine ("NUnit dependency expansion: selected-test and Live-category boundaries rejected before setup; explicit prerequisites passed.");
			}
		finally { Directory.Delete (work, true); }
		}
	private static void Require (bool condition, string message)
		{
		if (!condition) throw new InvalidOperationException (message);
		}
	}

namespace DependencySelectionTests
	{
	[TestFixture]
	public sealed class Probe
		{
		public static int Calls;
		[OneTimeSetUp] public void SetUp () => Calls++;
		[Test, Category ("Live")] public void Prerequisite () => Calls++;
		[Test, DependsOnTest (nameof (Prerequisite))] public void Dependent () => Calls++;
		}
	}
