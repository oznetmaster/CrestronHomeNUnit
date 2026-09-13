// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;

using Renci.SshNet;

namespace CrestronHomeNUnit.Workflow;

// Atomic mkdir coordinates cooperating CLI/CI clients on different computers.
// A crashed or uncertain run deliberately leaves the lease for inspection, without automatic expiry.
public sealed class ProcessorLease : IDisposable
	{
	private const string Path = "/user/CrestronHomeNUnit-WorkflowLease";
	private SftpClient _sftp;
	private readonly NetworkCredential _credential;
	private readonly string _fingerprint;
	private string _host;
	private readonly string _owner;
	private ProcessorLease (SftpClient sftp, string owner, string host, NetworkCredential credential, string fingerprint)
		{
		_sftp = sftp;
		_owner = owner;
		_host = host;
		_credential = credential;
		_fingerprint = fingerprint;
		}
	public static async Task<ProcessorLease> AcquireAsync (string host, NetworkCredential credential, string fingerprint, string runId, CancellationToken token)
		{
		if (!Guid.TryParseExact (runId, "N", out _))
			throw new ArgumentException ("Invalid run ID.");
		var sftp = new SftpClient (host, credential.UserName, credential.Password);
		sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
		sftp.OperationTimeout = TimeSpan.FromSeconds (15);
		sftp.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == fingerprint;
		try
			{
			await sftp.ConnectAsync (token).ConfigureAwait (false);
			// Never delete an existing lock, including an empty one from an interrupted creator.
			await sftp.CreateDirectoryAsync (Path, token).ConfigureAwait (false);
			using var data = new MemoryStream (System.Text.Encoding.UTF8.GetBytes (runId));
			await sftp.UploadFileAsync (data, Path + "/" + runId, false, null, token).ConfigureAwait (false);
			return new (sftp, runId, host, credential, fingerprint);
			}
		catch { sftp.Dispose (); throw; }
		}
	public async Task VerifyAfterReconnectAsync (string host, CancellationToken token)
		{
		_sftp.Dispose ();
		_host = host;
		_sftp = new SftpClient (_host, _credential.UserName, _credential.Password);
		_sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
		_sftp.OperationTimeout = TimeSpan.FromSeconds (15);
		_sftp.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == _fingerprint;
		await _sftp.ConnectAsync (token).ConfigureAwait (false);
		if (!await _sftp.ExistsAsync (Path + "/" + _owner, token).ConfigureAwait (false))
			throw new IOException ("Processor lease ownership did not survive restart; no further mutations are permitted.");
		}

	public async Task ReleaseAsync (CancellationToken token)
		{
		if (!_sftp.IsConnected)
			await VerifyAfterReconnectAsync (_host, token).ConfigureAwait (false);
		if (!await _sftp.ExistsAsync (Path + "/" + _owner, token).ConfigureAwait (false))
			throw new IOException ("Processor lease ownership could not be confirmed.");
		await _sftp.DeleteFileAsync (Path + "/" + _owner, token).ConfigureAwait (false);
		// Fails closed if another file exists; no recursive deletion.
		await _sftp.DeleteDirectoryAsync (Path, token).ConfigureAwait (false);
		}
	public void Dispose () => _sftp.Dispose ();
	}