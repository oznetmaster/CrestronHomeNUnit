// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text;

namespace CrestronHomeNUnit.Android;

/// <summary>Persistent reservation for cooperating workers using the same local Android session.</summary>
public sealed class AndroidSessionLease : IDisposable
	{
	private readonly FileStream _stream;
	private readonly string _path;
	public string Owner { get; }
	private AndroidSessionLease (string path, string owner, FileStream stream)
		{
		_path = path;
		Owner = owner;
		_stream = stream;
		}

	public static AndroidSessionLease Acquire (string path, string owner)
		{
		if (!Path.IsPathFullyQualified (path) || !Directory.Exists (Path.GetDirectoryName (path)) || !Guid.TryParseExact (owner, "N", out _))
			throw new ArgumentException ("Provide an absolute session lock path in an existing private directory and a workflow run ID.");
		FileStream stream;
		try
			{
			stream = new (path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
			}
		catch (IOException) when (File.Exists (path))
			{
			throw new IOException ("Android session is reserved. An interrupted reservation must be reconciled before reuse.");
			}
		try
			{
			stream.Write (Encoding.ASCII.GetBytes (owner));
			stream.Flush (flushToDisk: true);
			return new (path, owner, stream);
			}
		catch
			{
			stream.Dispose ();
			// Even a partial ownership marker blocks reuse until its creator is reconciled.
			throw;
			}
		}

	public static void VerifyOwner (string path, string owner)
		{
		if (!Path.IsPathFullyQualified (path) || !Guid.TryParseExact (owner, "N", out _))
			throw new ArgumentException ("Invalid Android reservation identity.");
		using var stream = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (stream.Length != 32 || (File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
			throw new IOException ("Android reservation identity changed.");
		var bytes = new byte[32];
		stream.ReadExactly (bytes);
		if (Encoding.ASCII.GetString (bytes) != owner)
			throw new IOException ("Android reservation belongs to a different workflow.");
		}

	public void Release ()
		{
		ObjectDisposedException.ThrowIf (!_stream.CanRead, this);
		VerifyOwner (_path, Owner);
		File.Delete (_path);
		_stream.Dispose ();
		}

	public void Dispose ()
		{
		_stream.Dispose ();
		// Disposal after failure/crash does not delete the persistent reservation.
		}
	}