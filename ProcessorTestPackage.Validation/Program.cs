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
				throw new ArgumentException ("Usage: ProcessorTestPackage.Validation.exe <merged.dll> <results-directory> [expected-count] [--run-twice]");
			string assemblyPath = Path.GetFullPath (args[0]);
			string directory = Path.Combine (Path.GetFullPath (args[1]), Guid.NewGuid ().ToString ("N"));
			int expected = args.Length > 2 ? int.Parse (args[2]) : 0;
			bool execute = args.Length > 3 && args[3] == "--run-twice";
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
				foreach (string file in Directory.GetFiles (results, execute ? "TestResult.xml" : "TestTree.xml", SearchOption.AllDirectories))
					{
					var xml = new XmlDocument { XmlResolver = null };
					xml.Load (file);
					count += int.Parse (xml.DocumentElement!.GetAttribute ("testcasecount"));
					if (!execute && xml.SelectNodes ("//*[@runstate='NotRunnable']")!.Count != 0)
						throw new InvalidOperationException ("The merged package contains invalid tests: " + file);
					}
				if (count == 0 || (expected != 0 && count != expected))
					throw new InvalidOperationException ($"Expected {expected} cases; found {count} in the merged package.");
				Console.WriteLine ($"Package validation {iteration}: {count} cases; {(execute ? "execution passed" : "discovery passed")}.");
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