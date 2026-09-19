// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using System.IO;
using System.Reflection;

using CrestronHomeNUnit.Runtime;

internal static class Program
	{
	private static int Main (string[] args)
		{
		try
			{
			if (args.Length < 2 || args.Length > 5 || args[1] is not ("self-tests" or "compatibility"))
				throw new ArgumentException ("Usage: CrestronHomeNUnit.NUnit5.DesktopValidation.exe <results-directory> <self-tests|compatibility> [--explore|--repeat] [--merged <package.dll>]");
			bool explore = false;
			bool repeat = false;
			Assembly? merged = null;
			for (int argument = 2; argument < args.Length; argument++)
				{
				switch (args[argument])
					{
					case "--explore" when !explore && !repeat:
						explore = true;
						break;
					case "--repeat" when !explore && !repeat:
						repeat = true;
						break;
					case "--merged" when merged is null && argument + 1 < args.Length:
						merged = Assembly.LoadFrom (Path.GetFullPath (args[++argument]));
						break;
					default:
						throw new ArgumentException ("Invalid or duplicate option: " + args[argument]);
					}
				}

			string directory = Path.GetFullPath (args[0]);
			string version = typeof (NUnit.Framework.Assert).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute> ()!.InformationalVersion;
			Console.WriteLine ($"NUnit snapshot: {version}; CLR: {Environment.Version}; Mono: {Type.GetType ("Mono.Runtime") != null}");
			Console.WriteLine (merged is null ? "Unmerged upstream assemblies" : "Merged processor assembly: " + merged.FullName);
			int repeats = repeat ? 2 : 1;
			int outcome = 0;
			for (int iteration = 1; iteration <= repeats; iteration++)
				{
				string results = repeats == 1 ? directory : Path.Combine (directory, "run-" + iteration);
				Console.WriteLine ($"Same-process run {iteration} of {repeats}");
				Assembly tests = args[1] == "self-tests" ? typeof (NUnit.Framework.Tests.Assertions.AssertThrowsTests).Assembly : typeof (LanguageTests).Assembly;
				int result = merged is null ? EmbeddedTestHost.Run (tests, results, Console.Out, explore, args[1]) :
					(int)merged.GetType ("CrestronHomeNUnit.Runtime.EmbeddedTestHost", true)!.GetMethod ("Run")!
						.Invoke (null, new object[] { merged, results, Console.Out, explore, args[1] })!;
				if (result != 0)
					outcome = result;
				}
			return outcome;
			}
		catch (Exception exception)
			{
			Console.Error.WriteLine (exception);
			return 2;
			}
		}
	}