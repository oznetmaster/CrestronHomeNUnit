// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Reflection;

using CrestronHomeNUnit.Runtime;

// This executable validates the local suite or the exact merged DLL extracted from the .pkg.
// It does not connect to a processor; the existing desktop runner will own that transport.
internal static class Program
	{
	public static int Main (string[] args)
		{
		try
			{
			if (args.Length == 3 && args[0] == "--package-inputs")
				{
				PackagedInputValidation.RunAsync (args[1], args[2]).GetAwaiter ().GetResult ();
				return 0;
				}

			if (args.Length < 1 || args.Length > 4)
				{
				Console.Error.WriteLine ("Usage: CrestronHomeNUnit.DesktopValidation.exe <results-directory> [self-tests|compatibility] [packaged-driver.dll] [--explore|--repeat]");
				return 2;
				}

			string workDirectory = Path.GetFullPath (args[0]);
			string suite = args.Length >= 2 ? args[1] : "self-tests";
			Console.WriteLine ("Runtime: " + Environment.Version + "; Mono: " + (Type.GetType ("Mono.Runtime") != null));
			if (args.Length <= 2)
				return EmbeddedTestHost.Run (suite == "self-tests" ? typeof (NUnit.Framework.Tests.Assertions.AssertThrowsTests).Assembly : typeof (LanguageTests).Assembly, workDirectory, Console.Out, false, suite);
			if (args.Length == 4 && args[3] != "--explore" && args[3] != "--repeat")
				throw new ArgumentException ("Unknown option: " + args[3]);
			Assembly packagedAssembly = Assembly.LoadFile (Path.GetFullPath (args[2]));
			Type hostType = packagedAssembly.GetType ("CrestronHomeNUnit.Runtime.EmbeddedTestHost", true)!;
			MethodInfo run = hostType.GetMethod ("Run", BindingFlags.Public | BindingFlags.Static)!;
			int repeats = args.Length == 4 && args[3] == "--repeat" ? 2 : 1;
			for (int iteration = 1; iteration <= repeats; iteration++)
				{
				string directory = repeats == 1 ? workDirectory : Path.Combine (workDirectory, "run-" + iteration);
				Console.WriteLine ("Same-process execution " + iteration + " of " + repeats);
				int exitCode = (int)run.Invoke (null, [packagedAssembly, directory, Console.Out, args.Length == 4 && args[3] == "--explore", suite])!;
				if (exitCode != 0)
					{
					return exitCode;
					}
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