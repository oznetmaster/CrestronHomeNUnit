// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

internal static class Program
	{
	private static int Main (string[] args)
		{
		try
			{
			if (args.Length < 2 || args.Length > 4)
				throw new ArgumentException ("Usage: ProcessorTestPackage.Validation.exe <merged.dll> <results-directory> [expected-count] [--run-twice|--report-invalid-tests]");
			string assemblyPath = Path.GetFullPath (args[0]);
			string directory = Path.Combine (Path.GetFullPath (args[1]), Guid.NewGuid ().ToString ("N"));
			int expected = args.Length > 2 ? int.Parse (args[2]) : 0;
			bool execute = args.Length > 3 && args[3] == "--run-twice";
			bool reportInvalidTests = args.Length > 3 && args[3] == "--report-invalid-tests";
			if (args.Length > 3 && !execute && !reportInvalidTests)
				throw new ArgumentException ("Unknown validation option: " + args[3]);
			Assembly assembly = Assembly.LoadFrom (assemblyPath);
			MethodInfo run = assembly.GetType ("CrestronHomeNUnit.Driver.PackageTestHost", true)!.GetMethod ("Run")!;
			int repetitions = execute ? 2 : 1;
			for (int iteration = 1; iteration <= repetitions; iteration++)
				{
				string results = Path.Combine (directory, "run-" + iteration);
				int exitCode = (int)run.Invoke (null, new object[] { results, !execute })!;
				if (exitCode != 0)
					return exitCode;
				int count = 0;
				int invalidCount = 0;
				foreach (string file in Directory.GetFiles (results, execute ? "TestResult.xml" : "TestTree.xml", SearchOption.AllDirectories))
					{
					var xml = new XmlDocument { XmlResolver = null };
					xml.Load (file);
					count += int.Parse (xml.DocumentElement!.GetAttribute ("testcasecount"));
					if (!execute)
						{
						XmlNodeList invalid = xml.SelectNodes ("//*[@runstate='NotRunnable']")!;
						invalidCount += invalid.Count;
						if (invalid.Count != 0)
							{
							// Diagnostic snapshots retain upstream discovery failures for comparison with other runtimes.
							File.WriteAllLines (Path.Combine (Path.GetDirectoryName (file)!, "InvalidTests.txt"),
								invalid.Cast<XmlElement> ().Select (node => node.GetAttribute ("fullname") + ": " +
									node.SelectSingleNode ("properties/property[@name='_SKIPREASON']/@value")?.Value));
							if (!reportInvalidTests)
								throw new InvalidOperationException ("The merged package contains invalid tests: " + file);
							}
						}
					}
				if (count == 0 || (expected != 0 && count != expected))
					throw new InvalidOperationException ($"Expected {expected} cases; found {count} in the merged package.");
				Console.WriteLine ($"Package validation {iteration}: {count} cases; " +
					(invalidCount != 0 ? $"diagnostic discovery retained {invalidCount} invalid nodes (not a passing test result)" :
					execute ? "execution passed" : "discovery passed") + ".");
				}
			return 0;
			}
		catch (Exception exception)
			{
			Console.Error.WriteLine (exception);
			return 2;
			}
		}
	}