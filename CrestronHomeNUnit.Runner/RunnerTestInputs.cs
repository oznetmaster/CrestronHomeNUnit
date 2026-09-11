// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Runner;

internal static class RunnerTestInputs
	{
	private static string ProfilePath => RunnerSettingsStorage.GetPath ("Runner.inputs.local.json");
	public static void Choose (IWin32Window owner, string suite)
		{
		using var dialog = new OpenFileDialog { Title = "Choose configuration files for this suite", Multiselect = true, Filter = "All files (*.*)|*.*", CheckFileExists = true };
		if (dialog.ShowDialog (owner) != DialogResult.OK)
			return;
		// Validate names and sizes before persisting the selection. Only paths are saved.
		LoadFiles (dialog.FileNames);
		Dictionary<string, string[]> profiles = ReadProfiles ();
		profiles[suite] = dialog.FileNames;
		SaveProfiles (profiles);
		}
	public static void Clear (string suite)
		{
		Dictionary<string, string[]> profiles = ReadProfiles ();
		profiles[suite] = [];
		SaveProfiles (profiles);
		}
	public static List<TestInputFile>? Files (string suite)
		{
		Dictionary<string, string[]> profiles = ReadProfiles ();
		return profiles.TryGetValue (suite, out string[]? paths) ? LoadFiles (paths) : null;
		}
	public static string Describe (string suite)
		{
		Dictionary<string, string[]> profiles = ReadProfiles ();
		return profiles.TryGetValue (suite, out string[]? paths) && paths.Length > 0 ? string.Join (", ", paths.Select (Path.GetFileName)) : "No test inputs";
		}
	private static List<TestInputFile> LoadFiles (string[] paths)
		{
		var files = new List<TestInputFile> ();
		foreach (string path in paths)
			{
			if (new FileInfo (path).Length > SecureTestData.MaximumFileBytes)
				throw new InvalidDataException ("Test-input files must be at most 1 MB each.");
			files.Add (new TestInputFile { Name = Path.GetFileName (path), Content = File.ReadAllBytes (path) });
			}
		SecureTestData.ValidateFiles (files);
		return files;
		}
	private static Dictionary<string, string[]> ReadProfiles ()
		{
		if (!File.Exists (ProfilePath))
			return new Dictionary<string, string[]> (StringComparer.Ordinal);
		using var stream = File.OpenRead (ProfilePath);
		return (Dictionary<string, string[]>)new DataContractJsonSerializer (typeof (Dictionary<string, string[]>)).ReadObject (stream)!;
		}
	private static void SaveProfiles (Dictionary<string, string[]> profiles)
		{
		Directory.CreateDirectory (RunnerSettingsStorage.DirectoryPath);
		using var stream = File.Create (ProfilePath);
		new DataContractJsonSerializer (typeof (Dictionary<string, string[]>)).WriteObject (stream, profiles);
		}
	}