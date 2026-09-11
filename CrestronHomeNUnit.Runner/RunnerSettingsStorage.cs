// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace CrestronHomeNUnit.Runner;

internal static class RunnerSettingsStorage
	{
	internal static string DirectoryPath => Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeNUnit");

	internal static string GetPath (string filename)
		{
		MigrateFile (filename, AppContext.BaseDirectory, DirectoryPath);
		// Visual Studio's new target has a different output folder from the Framework build.
		string? configurationDirectory = Path.GetDirectoryName (AppContext.BaseDirectory.TrimEnd (Path.DirectorySeparatorChar));
		if (configurationDirectory != null)
			MigrateFile (filename, Path.Combine (configurationDirectory, "net472"), DirectoryPath);
		return Path.Combine (DirectoryPath, filename);
		}

	internal static void MigrateFile (string filename, string legacyDirectory, string settingsDirectory)
		{
		string destination = Path.Combine (settingsDirectory, filename);
		string legacy = Path.Combine (legacyDirectory, filename);
		if (File.Exists (destination) || !File.Exists (legacy))
			return;
		Directory.CreateDirectory (settingsDirectory);
		string temporary = destination + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
		try
			{
			File.Copy (legacy, temporary, false);
			File.SetAttributes (temporary, File.GetAttributes (temporary) & ~FileAttributes.ReadOnly);
			try
				{
				File.Move (temporary, destination, false);
				}
			catch (IOException) when (File.Exists (destination)) { }
			}
		finally { if (File.Exists (temporary)) File.Delete (temporary); }
		}
	}