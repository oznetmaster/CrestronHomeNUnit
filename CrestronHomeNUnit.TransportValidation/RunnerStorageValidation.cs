// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Reflection;

using CrestronHomeNUnit.Runner;

internal static class RunnerStorageValidation
	{
	public static void Run ()
		{
		string directory = Path.Combine (Path.GetTempPath (), "RunnerStorage-" + Guid.NewGuid ().ToString ("N"));
		string legacy = Path.Combine (directory, "old-runner");
		string settings = Path.Combine (directory, "local-app-data");
		Directory.CreateDirectory (legacy);
		try
			{
			Type storage = typeof (RunnerForm).Assembly.GetType ("CrestronHomeNUnit.Runner.RunnerSettingsStorage", true)!;
			MethodInfo migrate = storage.GetMethod ("MigrateFile", BindingFlags.Static | BindingFlags.NonPublic)!;
			foreach (string filename in new[] { "Runner.local.json", "Runner.inputs.local.json" })
				{
				string oldPath = Path.Combine (legacy, filename);
				string newPath = Path.Combine (settings, filename);
				File.WriteAllText (oldPath, "{\"selection\":\"preserve this value\"}");
				File.SetAttributes (oldPath, FileAttributes.ReadOnly);
				migrate.Invoke (null, [filename, legacy, settings]);
				Require (File.ReadAllText (newPath) == File.ReadAllText (oldPath), "Migration changed existing settings.");
				// An installed app must be able to edit its migrated settings.
				Require ((File.GetAttributes (newPath) & FileAttributes.ReadOnly) == 0, "Migration kept the source file's read-only attribute.");
				File.WriteAllText (newPath, "{\"selection\":\"new selection\"}");
				migrate.Invoke (null, [filename, legacy, settings]);
				Require (File.ReadAllText (newPath).Contains ("new selection"), "Migration overwrote newer user settings.");
				Require (File.ReadAllText (oldPath).Contains ("preserve this value"), "Migration modified the old runner's settings.");
				File.SetAttributes (oldPath, FileAttributes.Normal);
				}
			migrate.Invoke (null, ["missing.json", legacy, settings]);
			Require (!File.Exists (Path.Combine (settings, "missing.json")), "A missing legacy file created settings.");
			Require (Directory.GetFiles (settings, "*.tmp").Length == 0, "Migration left temporary files.");
			}
		finally
			{
			foreach (string file in Directory.GetFiles (directory, "*", SearchOption.AllDirectories))
				File.SetAttributes (file, FileAttributes.Normal);
			Directory.Delete (directory, true);
			}
		Console.WriteLine ("Runner storage: legacy migration, read-only source, newer settings, original preservation and missing files passed.");
		}
	private static void Require (bool condition, string message)
		{
		if (!condition)
			throw new InvalidOperationException (message);
		}
	}