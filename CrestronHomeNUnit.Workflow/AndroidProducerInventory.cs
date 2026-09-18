// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeNUnit.Workflow;

/// <summary>Retains the complete test output before execution and detects subsequent changes.</summary>
internal sealed class AndroidProducerInventory
	{
	private readonly byte[] _manifest;
	private AndroidProducerInventory (byte[] manifest) => _manifest = manifest;
	public string Sha256 => Convert.ToHexString (SHA256.HashData (_manifest));
	internal static void RequireOrdinaryPath (string path)
		{
		for (var current = Path.GetFullPath (path); current != null; current = Path.GetDirectoryName (current))
			if ((File.GetAttributes (current) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Retained Android producer paths cannot traverse links or junctions.");
		}
	internal static AndroidProducerInventory Capture (string directory, CancellationToken token)
		{
		directory = Path.GetFullPath (directory);
		RequireOrdinaryPath (directory);
		var files = new SortedDictionary<string, string> (StringComparer.Ordinal);
		var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		var pending = new Stack<string> ();
		pending.Push (directory);
		long total = 0;
		int entries = 0;
		while (pending.TryPop (out var folder))
			foreach (string path in Directory.EnumerateFileSystemEntries (folder))
				{
				token.ThrowIfCancellationRequested ();
				if (++entries > 8192)
					throw new InvalidDataException ("Android producer inventory is too large.");
				RequireOrdinaryPath (path);
				if (Directory.Exists (path))
					{
					pending.Push (path);
					continue;
					}
				string relative = Path.GetRelativePath (directory, path).Replace ('\\', '/');
				if (relative.Length > 1024 || relative.Contains (':') || relative.Split ('/').Any (part =>
					part is "" or "." or ".." || part.EndsWith (' ') || part.EndsWith ('.')) || !names.Add (relative))
					throw new InvalidDataException ("Ambiguous Android producer path.");
				using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (input.Length > 32 * 1024 * 1024 || (total += input.Length) > 512 * 1024 * 1024 || files.Count >= 4096)
					throw new InvalidDataException ("Android producer exceeds its bounded inventory.");
				files.Add (relative, Convert.ToHexString (SHA256.HashData (input)));
				}
		if (files.Count == 0)
			throw new InvalidDataException ("Android producer inventory is empty.");
		var manifest = JsonSerializer.SerializeToUtf8Bytes (new
			{
			schemaVersion = 1,
			files = files.Select (file => new { relativePath = file.Key, sha256 = file.Value })
			});
		if (manifest.Length > 1024 * 1024)
			throw new InvalidDataException ("Android producer manifest is too large.");
		return new (manifest);
		}
	internal void Save (string path)
		{
		RequireOrdinaryPath (Path.GetDirectoryName (Path.GetFullPath (path))!);
		using var output = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		output.Write (_manifest);
		output.Flush (flushToDisk: true);
		}
	internal void RequireUnchanged (string directory, string manifestPath, CancellationToken token)
		{
		RequireOrdinaryPath (manifestPath);
		using var input = new FileStream (manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length != _manifest.Length || Convert.ToHexString (SHA256.HashData (input)) != Sha256 ||
			Capture (directory, token).Sha256 != Sha256)
			throw new InvalidDataException ("Android test files or their retained manifest changed during execution.");
		}
	}