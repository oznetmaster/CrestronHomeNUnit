// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

using CrestronHomeNUnit.Runtime;
using CrestronHomeNUnit.Transport;

using NUnit.Framework;

internal static class LiveOptInValidation
	{
	public static async Task RunAsync ()
		{
		string work = Path.Combine (Path.GetTempPath (), "NUnitLiveOptIn-" + Guid.NewGuid ().ToString ("N"));
		var suites = new[]
			{
			new TestSuiteDefinition { Id = "live", Name = "Synthetic live suite", ManualOnly = true, FilterXml = "<filter><class>LiveOptInValidationTests.Probe</class></filter>" },
			new TestSuiteDefinition { Id = "ordinary", Name = "Parameter isolation", FilterXml = "<filter><class>LiveOptInValidationTests.Probe</class></filter>" }
			};
		using var host = new TestExecutionService (work, _ => Assembly.GetExecutingAssembly (), suites);
		using var server = new RemoteTestServer (host, "synthetic-live-opt-in", 0, IPAddress.Loopback);
		using var client = await RemoteTestClient.ConnectAsync ("127.0.0.1", server.Port, "synthetic-live-opt-in");
		Require (client.Suites[0].ManualOnly && !client.Suites[1].ManualOnly, "Manual suite metadata was lost.");
		LiveOptInValidationTests.Probe.Expected = true;
		WireMessage allowed = await client.SendAsync (new WireMessage { Kind = "run", Suite = "live", EnableLiveTests = true, TestInputs = [] });
		Require (allowed.Kind == "complete" && allowed.ExitCode == 0, "Explicit runner opt-in did not reach NUnit: " + allowed.Text);
		WireMessage denied = await host.ExecuteAsync (new WireMessage { Kind = "run", Suite = "live", RequestId = Guid.NewGuid ().ToString ("N") }, _ => { });
		Require (denied.Kind == "error", "A later Home operation inherited the live opt-in.");
		LiveOptInValidationTests.Probe.Expected = false;
		WireMessage ordinary = await client.SendAsync (new WireMessage { Kind = "run", Suite = "ordinary", EnableLiveTests = true });
		Require (ordinary.Kind == "complete" && ordinary.ExitCode == 0, "Live opt-in leaked into an ordinary suite.");
		Console.WriteLine ("Explicit live selection reaches NUnit; Home runs remain blocked and ordinary suites receive no live override.");
		}
	private static void Require (bool condition, string message)
		{
		if (!condition)
			throw new InvalidOperationException (message);
		}
	}

namespace LiveOptInValidationTests
	{
	[TestFixture]
	public sealed class Probe
		{
		public static bool Expected;
		[Test]
		public void ReceivesOperationOverride () => Assert.That (TestContext.Parameters.Get ("EnableLiveTests", false), Is.EqualTo (Expected));
		}
	}