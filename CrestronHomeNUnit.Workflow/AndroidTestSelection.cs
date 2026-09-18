// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

using CrestronHomeNUnit.Android;

namespace CrestronHomeNUnit.Workflow;

internal sealed record AndroidTestSelection (string Path, string Sha256, string SettingsPath, string SettingsSha256)
	{
	internal static void Validate (IReadOnlyList<string>? required)
		{
		if (required == null)
			return;
		if (required.Count is < 1 or > 1024 || required.Any (name => string.IsNullOrWhiteSpace (name) || name.Length > 4096 || name.Any (char.IsControl)) ||
			required.Distinct (StringComparer.Ordinal).Count () != required.Count || required.Sum (name => name.Length) > 256 * 1024)
			throw new ArgumentException ("Android requiredTests must contain bounded, distinct, nonempty exact NUnit full names. Omit it to run the entire project.");
		}

	internal static string[] Select (string[] discovered, IReadOnlyList<string>? required)
		{
		Validate (required);
		if (required == null)
			return discovered;
		var names = required.ToHashSet (StringComparer.Ordinal);
		if (names.Any (name => !discovered.Contains (name, StringComparer.Ordinal)))
			throw new InvalidDataException ("An Android required test is absent from discovery. No UI tests were started; check requiredTests against discovery.dump.");
		// One requested full name includes every case carrying that name, preserving multiplicity.
		return discovered.Where (names.Contains).Order (StringComparer.Ordinal).ToArray ();
		}

	internal static AndroidTestSelection Save (string directory, AndroidRunContext context, string[] discovered, string[] expected)
		{
		var settingsPath = System.IO.Path.Combine (directory, "selection.runsettings");
		// Encode each UTF-16 character, so quotes, backslashes and apparent filter
		// operators in a test name never become selection syntax. Anchors retain exact matching.
		string Literal (string name) => "'\\\\A" + string.Concat (name.Select (c => "\\\\u" + ((int)c).ToString ("X4"))) + "\\\\z'";
		string expression = string.Join (" or ", expected.Distinct (StringComparer.Ordinal).Select (name => "test =~ " + Literal (name)));
		using (var stream = new FileStream (settingsPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
			{
			new XDocument (new XElement ("RunSettings", new XElement ("NUnit", new XElement ("Where", expression)))).Save (stream);
			stream.Flush (flushToDisk: true);
			}
		var settingsSha = Hash (settingsPath);
		var path = System.IO.Path.Combine (directory, "selection.json");
		using (var stream = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
			{
			JsonSerializer.Serialize (stream, new
				{
				SchemaVersion = 1, context.RunId, context.PackageSha256,
				DiscoveredTests = discovered, ExpectedTests = expected,
				ExcludedTests = discovered.Where (name => !expected.Contains (name, StringComparer.Ordinal)).ToArray (),
				SettingsSha256 = settingsSha
				});
			stream.Flush (flushToDisk: true);
			}
		return new (path, Hash (path), settingsPath, settingsSha);
		}

	internal void RequireUnchanged ()
		{
		if (Hash (Path) != Sha256 || Hash (SettingsPath) != SettingsSha256)
			throw new InvalidDataException ("Android case selection changed during execution.");
		}

	private static string Hash (string path)
		{
		AndroidProducerInventory.RequireOrdinaryPath (path);
		using var stream = File.OpenRead (path);
		if (stream.Length > 20 * 1024 * 1024)
			throw new InvalidDataException ("Android case selection exceeds its size limit.");
		return Convert.ToHexString (SHA256.HashData (stream));
		}
	}