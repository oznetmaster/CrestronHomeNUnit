// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using CrestronHomeNUnit.Client;

namespace CrestronHomeNUnit.Workflow;

public static class WorkflowEvidence
	{
	public static WorkflowTestOutcome ReadTrx (string path, int exitCode, int minimumPassed)
		{
		using var reader = XmlReader.Create (path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
		var root = XDocument.Load (reader).Root ?? throw new InvalidDataException ("Missing test results.");
		var counters = root.Descendants ().Single (e => e.Name.LocalName == "Counters");
		int Count (string key) => int.TryParse ((string?)counters.Attribute (key), out var n) && n >= 0 ? n : throw new InvalidDataException ("Invalid TRX counts.");
		int total = Count ("total"), passed = Count ("passed"), failed = Count ("failed");
		var cases = root.Descendants ().Where (e => e.Name.LocalName == "UnitTestResult").ToArray ();
		bool complete = exitCode == 0 && passed >= minimumPassed && total == passed + failed
			 && cases.Length == total && cases.Count (e => (string?)e.Attribute ("outcome") == "Passed") == passed;
		return new (passed, failed, Math.Max (0, total - passed - failed), complete);
		}

	internal static void PrepareLocalResults (string directory)
		{
		if (Directory.Exists (directory) && Directory.EnumerateFileSystemEntries (directory).Any ())
			throw new InvalidOperationException ("Use a new results directory for each workflow run; previous evidence must not be reused.");
		Directory.CreateDirectory (directory);
		}

	internal static WorkflowTestOutcome ReadLocalResults (string directory, int exitCode, int minimumPassed)
		{
		var outcomes = Directory.EnumerateFiles (directory, "TestResult*.trx").Order (StringComparer.Ordinal)
			.Select (path => ReadTrx (path, exitCode, 1)).ToArray ();
		int passed = outcomes.Sum (outcome => outcome.Passed);
		return new (passed, outcomes.Sum (outcome => outcome.Failed), outcomes.Sum (outcome => outcome.Skipped),
			outcomes.Length > 0 && passed >= minimumPassed && outcomes.All (outcome => outcome.MeetsGate));
		}

	public static async Task<int> ProcessAsync (string executable, IEnumerable<string> arguments, string directory, string log, CancellationToken token)
		{
		var start = new ProcessStartInfo (executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		// A local test command must not recursively start another deployment workflow.
		start.Environment["CRESTRON_HOME_WORKFLOW_ACTIVE"] = "1";
		foreach (var argument in arguments)
			start.ArgumentList.Add (argument);
		using var process = Process.Start (start) ?? throw new IOException ("Could not start build/test process.");
		var output = process.StandardOutput.ReadToEndAsync ();
		var error = process.StandardError.ReadToEndAsync ();
		try
			{
			await process.WaitForExitAsync (token).ConfigureAwait (false);
			}
		finally
			{
			if (!process.HasExited)
				{
				process.Kill (entireProcessTree: true);
				await process.WaitForExitAsync ().ConfigureAwait (false);
				}
			await File.WriteAllTextAsync (log, await output.ConfigureAwait (false) + await error.ConfigureAwait (false)).ConfigureAwait (false);
			}
		return process.ExitCode;
		}

	// Hash tracked and non-ignored source files, including dirty edits; never record their contents.
	public static async Task<string> SourceDigestAsync (IEnumerable<string> roots, CancellationToken token)
		{
		using var digest = IncrementalHash.CreateHash (HashAlgorithmName.SHA256);
		foreach (var root in roots.Order (StringComparer.OrdinalIgnoreCase))
			{
			var start = new ProcessStartInfo ("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
			foreach (var arg in new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard" })
				start.ArgumentList.Add (arg);
			using var process = Process.Start (start)!;
			var output = process.StandardOutput.ReadToEndAsync (token);
			var errors = process.StandardError.ReadToEndAsync (token);
			await process.WaitForExitAsync (token).ConfigureAwait (false);
			if (process.ExitCode != 0)
				throw new IOException ("Could not establish source identity.");
			_ = await errors.ConfigureAwait (false);
			foreach (var name in (await output.ConfigureAwait (false)).Split ('\0', StringSplitOptions.RemoveEmptyEntries).Distinct ().Order (StringComparer.Ordinal))
				{
				digest.AppendData (Encoding.UTF8.GetBytes (Path.GetFullPath (root) + "\0" + name + "\0"));
				var path = Path.Combine (root, name);
				if (!File.Exists (path))
					{
					digest.AppendData (Encoding.UTF8.GetBytes ("missing"));
					continue;
					}
				var data = await File.ReadAllBytesAsync (path, token).ConfigureAwait (false);
				// These two generated manifest fields change during normal Debug builds.
				// Keep the prepared major/minor/patch significant; only the Debug counter is variable.
				if (path.EndsWith (".json", StringComparison.OrdinalIgnoreCase))
					{
					var text = Encoding.UTF8.GetString (data);
					text = Regex.Replace (text, "(\"DriverVersion\"\\s*:\\s*\"[0-9]+[.][0-9]+[.][0-9]+[.])[0-9]+(\")", "$1DEBUG$2");
					text = Regex.Replace (text, "(\"VersionDate\"\\s*:\\s*\")[^\"]*(\")", "$1DATE$2");
					data = Encoding.UTF8.GetBytes (text);
					}
				digest.AppendData (SHA256.HashData (data));
				}
			}
		return Convert.ToHexString (digest.GetHashAndReset ());
		}
	}