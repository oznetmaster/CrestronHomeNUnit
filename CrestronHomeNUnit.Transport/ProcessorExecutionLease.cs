// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using System.IO;
using System.Text;

namespace CrestronHomeNUnit.Transport;

/// <summary>Coordinates a processor test host with SFTP leases and other host processes.</summary>
public sealed class ProcessorExecutionLease : IDisposable
	{
	public const string SharedPath = "/user/CrestronHomeNUnit-WorkflowLease";
	private readonly string _path;
	private readonly string _request;
	private readonly bool _ownsParent;
	private bool _disposed;
	private ProcessorExecutionLease (string path, string request, bool ownsParent)
		{
		_path = path;
		_request = request;
		_ownsParent = ownsParent;
		}

	public static ProcessorExecutionLease Acquire (string path, string? owner, string request)
		{
		if (!Guid.TryParseExact (request, "N", out _)) throw new ArgumentException ("Invalid request identity.");
		bool ownsParent = string.IsNullOrEmpty (owner);
		if (ownsParent)
			{
			// Creating a file and an SFTP mkdir at the SAME path are mutually exclusive.
			// Directory.CreateDirectory alone is not an exclusive lock in .NET.
			ClaimFile (path, request);
			}
		else
			{
			if (!Guid.TryParseExact (owner, "N", out _) || !Directory.Exists (path) ||
				!File.Exists (Path.Combine (path, owner)) || File.ReadAllText (Path.Combine (path, owner)) != owner)
				throw new IOException ("Processor lease ownership could not be verified. No test was started.");
			}
		try
			{
			// Also serialize two requests delegated by the same workflow owner.
			ClaimFile (path + ".ActiveTest", request);
			return new (path, request, ownsParent);
			}
		catch
			{
			if (ownsParent) RemoveOwnedFile (path, request);
			throw;
			}
		}

	private static void ClaimFile (string path, string owner)
		{
		try
			{
			using var stream = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			var bytes = Encoding.UTF8.GetBytes (owner);
			stream.Write (bytes, 0, bytes.Length);
			stream.Flush ();
			}
		catch (IOException) when (File.Exists (path) || Directory.Exists (path))
			{
			throw new IOException ("Processor busy: another workflow, reservation or test owns the execution gate.");
			}
		catch (UnauthorizedAccessException) when (Directory.Exists (path))
			{
			throw new IOException ("Processor busy: another workflow or manual reservation owns the lease.");
			}
		}

	private static void RemoveOwnedFile (string path, string owner)
		{
		if (!File.Exists (path) || File.ReadAllText (path) != owner) throw new IOException ("Processor execution lease ownership changed; the lease was retained.");
		File.Delete (path);
		}

	public void Dispose ()
		{
		if (_disposed) return;
		// Remove execution first. A failure retains the parent lease for inspection.
		RemoveOwnedFile (_path + ".ActiveTest", _request);
		if (_ownsParent) RemoveOwnedFile (_path, _request);
		_disposed = true;
		}
	}