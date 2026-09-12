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
	public static void Choose (IWin32Window owner, string scope)
		{
		using var dialog = new OpenFileDialog { Title = "Choose configuration files for this processor package", Multiselect = true, Filter = "All files (*.*)|*.*", CheckFileExists = true };
		if (dialog.ShowDialog (owner) != DialogResult.OK)
			return;
		// Validate names and sizes before persisting the selection. Only paths are saved.
		LoadFiles (dialog.FileNames);
		Dictionary<string, string[]> profiles = ReadProfiles ();
		profiles[scope] = dialog.FileNames;
		SaveProfiles (profiles);
		}
	public static void Clear (string scope)
		{
		Dictionary<string, string[]> profiles = ReadProfiles ();
		profiles[scope] = [];
		SaveProfiles (profiles);
		}
	public static List<TestInputFile>? Files (string scope)
		{
		Dictionary<string, string[]> profiles = ReadProfiles ();
		return profiles.TryGetValue (scope, out string[]? paths) ? LoadFiles (paths) : null;
		}
	public static string Describe (string scope)
		{
		Dictionary<string, string[]> profiles = ReadProfiles ();
		return profiles.TryGetValue (scope, out string[]? paths) && paths.Length > 0 ? string.Join (", ", paths.Select (Path.GetFileName)) : "No test inputs";
		}
	public static void MigratePackage (string scope, string[] legacyScopes)
		{
		Dictionary<string, string[]> profiles = ReadProfiles ();
		if (TryMigratePackage (profiles, scope, legacyScopes))
			SaveProfiles (profiles);
		}
	internal static bool TryMigratePackage (Dictionary<string, string[]> profiles, string scope, string[] legacyScopes)
		{
		// A saved empty selection is intentional; never restore inputs after Clear inputs.
		if (profiles.ContainsKey (scope))
			return false;
		string[]? candidate = null;
		foreach (string legacy in legacyScopes)
			{
			if (!profiles.TryGetValue (legacy, out string[]? paths) || paths.Length == 0)
				continue;
			if (candidate != null && !candidate.OrderBy (path => path, StringComparer.OrdinalIgnoreCase).SequenceEqual (paths.OrderBy (path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
				return false; // Conflicting old selections require an explicit new choice.
			candidate = paths;
			}
		if (candidate == null)
			return false;
		profiles[scope] = candidate.ToArray ();
		return true;
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