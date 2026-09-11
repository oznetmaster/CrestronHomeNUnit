// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using CrestronHomeNUnit.Runner;
using CrestronHomeNUnit.Transport;

internal static class ProcessorSmokeValidation
	{
	public static async Task RunAsync (string projectUserFile, string suiteId, string resultsDirectory)
		{
		var settings = new XmlDocument { XmlResolver = null };
		settings.Load (projectUserFile);
		string Setting (string name) => settings.SelectSingleNode ("/Project/PropertyGroup/" + name)?.InnerText ?? throw new InvalidOperationException ("Missing private deployment setting: " + name);
		string host = Setting ("CrestronHomeIP");
		var packages = await PackageDiscovery.FindAsync (CancellationToken.None);
		var candidates = packages.Where (package => package.Host == host).ToArray ();
		if (candidates.Length == 0)
			throw new InvalidOperationException ("No test packages discovered on the configured processor.");
		ProcessorConnection processor = await ProcessorAuthentication.AuthenticateAsync (host, Setting ("CrestronHomeFtpUser"), Setting ("CrestronHomeSftpPassword"), candidates[0].ProcessorId);
		Console.WriteLine (".NET 10 mDNS discovery and SFTP authentication passed.");
		var matches = new System.Collections.Generic.List<DiscoveredPackage> ();
		foreach (DiscoveredPackage package in candidates)
			{
			using var client = await RemoteTestClient.ConnectAsync (host, package.Port, processor.Token);
			TestSuiteInfo? suite = client.Suites.SingleOrDefault (candidate => candidate.Id == suiteId);
			if (suite == null)
				continue;
			matches.Add (package);
			if (suite.ManualOnly)
				throw new InvalidOperationException ("Processor smoke validation does not run live/manual suites.");
			}
		if (matches.Count != 1)
			throw new InvalidOperationException ("Expected one package advertising the requested suite, found " + matches.Count + ".");
		using (var client = await RemoteTestClient.ConnectAsync (host, matches[0].Port, processor.Token))
			{
			TestSuiteInfo suite = client.Suites.Single (candidate => candidate.Id == suiteId);
			if (suite.ManualOnly)
				throw new InvalidOperationException ("Processor smoke validation does not run live/manual suites.");
			Directory.CreateDirectory (resultsDirectory);
			WireMessage discovery = await client.SendAsync (new WireMessage { Kind = "discover", Suite = suiteId });
			if (discovery.Kind != "complete" || discovery.ExitCode != 0)
				throw new InvalidOperationException (discovery.Text);
			File.WriteAllText (Path.Combine (resultsDirectory, "TestTree.xml"), discovery.Xml);
			int starts = 0, finishes = 0;
			client.Progress += message =>
				{
					if (message.Kind == "test-start")
						Interlocked.Increment (ref starts);
					if (message.Kind == "test-finish")
						Interlocked.Increment (ref finishes);
				};
			WireMessage result = await client.SendAsync (new WireMessage { Kind = "run", Suite = suiteId });
			File.WriteAllText (Path.Combine (resultsDirectory, "TestResult.xml"), result.Xml);
			if (result.Kind != "complete" || result.ExitCode != 0)
				throw new InvalidOperationException (result.Text);
			Console.WriteLine ("Existing net472 processor package: " + suite.Name + "; " + result.Text + "; starts=" + starts + "; finishes=" + finishes);
			}

		}
	}