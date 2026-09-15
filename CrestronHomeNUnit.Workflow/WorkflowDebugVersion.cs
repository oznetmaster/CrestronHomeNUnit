// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using CrestronHomeDevTools;

namespace CrestronHomeNUnit.Workflow;

internal sealed record PreparedDebugVersion (Guid DriverId, string Model, Version Baseline)
	{
	internal void VerifyBuiltPackage (DriverPackageInfo package)
		{
		var built = WorkflowDebugVersion.ParseVersion (package.Version);
		if (!Guid.TryParse (package.DriverId, out var identity) || identity != DriverId
			 || !string.Equals (package.Model.Trim (), Model.Trim (), StringComparison.OrdinalIgnoreCase)
			 || built.Major != Baseline.Major || built.Minor != Baseline.Minor || built.Build != Baseline.Build || built <= Baseline)
			throw new InvalidDataException ("The build did not produce the expected driver with a new Debug revision; no package was deployed.");
		}
	}

internal static class WorkflowDebugVersion
	{
	private static readonly UTF8Encoding _encoding = new (false, true);
	private static readonly Regex _revisionPattern = new ("(?<prefix>\"DriverVersion\"\\s*:\\s*\"[0-9]+[.][0-9]+[.][0-9]+[.])(?<revision>[0-9]+)(?<suffix>\")");

	internal static Version ParseVersion (string? text)
		{
		if (!Version.TryParse (text, out var version) || version.Revision < 0
			 || version.Major > 65534 || version.Minor > 65534 || version.Build > 65534 || version.Revision > 65534)
			throw new InvalidDataException ("Driver versions must contain four supported numeric components.");
		return version;
		}

	internal static Version SelectBaseline (string model, Version current, IEnumerable<DriverInfo> catalogue)
		{
		var baseline = current;
		foreach (var driver in catalogue.Where (item => string.Equals (item.Model?.Trim (), model.Trim (), StringComparison.OrdinalIgnoreCase)))
			{
			var existing = ParseVersion (driver.Version);
			var existingRelease = new Version (existing.Major, existing.Minor, existing.Build);
			var sourceRelease = new Version (current.Major, current.Minor, current.Build);
			if (existingRelease > sourceRelease)
				throw new InvalidOperationException ("The processor has a newer release of this model. Update the source release version before building; only the Debug revision is reconciled automatically.");
			if (existing > baseline) baseline = existing;
			}
		if (baseline.Revision >= 65534)
			throw new InvalidOperationException ("The Debug revision is exhausted. Advance the source release version before building.");
		return baseline;
		}

	// Standard driver/test projects keep their manifest beside the project with the same basename.
	// A custom layout retains the existing build and deployment checks instead of guessing a manifest.
	internal static async Task<PreparedDebugVersion?> PrepareAsync (string path,
		Func<string, CancellationToken, Task<IReadOnlyList<DriverInfo>>> readCatalogue, CancellationToken token)
		{
		if (!File.Exists (path)) return null;
		token.ThrowIfCancellationRequested ();
		await using var file = new FileStream (path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
		if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException ("Driver manifest exceeds the supported size.");
		var original = new byte[(int)file.Length];
		await file.ReadExactlyAsync (original, token).ConfigureAwait (false);
		bool bom = original.AsSpan ().StartsWith (new byte[] { 0xef, 0xbb, 0xbf });
		var text = _encoding.GetString (original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
		using var document = JsonDocument.Parse (text);
		var general = document.RootElement.GetProperty ("GeneralInformation");
		if (!Guid.TryParse (general.GetProperty ("Guid").GetString (), out var identity)
			 || general.GetProperty ("BaseModel").GetString () is not string model || string.IsNullOrWhiteSpace (model))
			throw new InvalidDataException ("Driver manifest identity is incomplete.");
		var current = ParseVersion (general.GetProperty ("DriverVersion").GetString ());
		var matches = _revisionPattern.Matches (text);
		if (matches.Count != 1) throw new InvalidDataException ("DriverVersion must be unambiguous before reconciliation.");
		var catalogue = await readCatalogue (model, token).ConfigureAwait (false);
		var baseline = SelectBaseline (model, current, catalogue);
		token.ThrowIfCancellationRequested ();
		if (baseline != current)
			{
			var match = matches[0].Groups["revision"];
			var updated = text[..match.Index] + baseline.Revision.ToString ("D4") + text[(match.Index + match.Length)..];
			var bytes = _encoding.GetBytes ((bom ? "\uFEFF" : string.Empty) + updated);
			file.Position = 0;
			// Once the local write begins, complete it even if cancellation is requested.
			// The build script then allocates the next revision above this baseline.
			await file.WriteAsync (bytes, CancellationToken.None).ConfigureAwait (false);
			file.SetLength (bytes.Length);
			await file.FlushAsync (CancellationToken.None).ConfigureAwait (false);
			}
		return new (identity, model, baseline);
		}
	}