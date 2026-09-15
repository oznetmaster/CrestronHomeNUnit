// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;

using Renci.SshNet;

namespace CrestronHomeNUnit.Client;

// Atomic mkdir coordinates cooperating CLI/CI clients on different computers.
// A crashed or uncertain run deliberately leaves the lease for inspection, without automatic expiry.
public sealed class ProcessorLease : IProcessorLease
	{
	private const string Path = "/user/CrestronHomeNUnit-WorkflowLease";
	private SftpClient _sftp;
	private readonly NetworkCredential _credential;
	private readonly string _fingerprint;
	private string _host;
	private readonly string _owner;
	public string Owner => _owner;
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
			if (await sftp.ExistsAsync (Path + ".ActiveTest", token).ConfigureAwait (false))
				throw new ProcessorBusyException ();
			// Never delete an existing lock, including an empty one from an interrupted creator.
			try
				{
				await sftp.CreateDirectoryAsync (Path, token).ConfigureAwait (false);
				}
			catch (Renci.SshNet.Common.SftpException) when (!token.IsCancellationRequested)
				{
				if (await sftp.ExistsAsync (Path, token).ConfigureAwait (false))
					throw new ProcessorBusyException ();
				throw;
				}
			using var data = new MemoryStream (System.Text.Encoding.UTF8.GetBytes (runId));
			await sftp.UploadFileAsync (data, Path + "/" + runId, false, null, token).ConfigureAwait (false);
			if (await sftp.ExistsAsync (Path + ".ActiveTest", token).ConfigureAwait (false))
				throw new IOException ("Execution state changed during reservation; the processor lease was retained for inspection.");
			return new (sftp, runId, host, credential, fingerprint);
			}
		catch { sftp.Dispose (); throw; }
		}
	public static Task<ProcessorLease> AcquireAsync (string host, NetworkCredential credential, string fingerprint, string runId, TimeSpan wait, CancellationToken token)
		=> WaitForLeaseAsync (ct => AcquireAsync (host, credential, fingerprint, runId, ct), wait, token);

	internal static async Task<T> WaitForLeaseAsync<T> (Func<CancellationToken, Task<T>> acquire, TimeSpan wait, CancellationToken token)
		{
		if (wait < TimeSpan.Zero || wait > TimeSpan.FromDays (1))
			throw new ArgumentOutOfRangeException (nameof (wait));
		var elapsed = System.Diagnostics.Stopwatch.StartNew ();
		while (true)
			{
			token.ThrowIfCancellationRequested ();
			try
				{
				return await acquire (token).ConfigureAwait (false);
				}
			catch (ProcessorBusyException) when (elapsed.Elapsed < wait)
				{
				await Task.Delay (TimeSpan.FromMilliseconds (Math.Min (1000, Math.Max (1, (wait - elapsed.Elapsed).TotalMilliseconds))), token).ConfigureAwait (false);
				}
			}
		}

	public static async Task<ProcessorLease> ResumeAsync (string host, NetworkCredential credential, string fingerprint, string owner, CancellationToken token)
		{
		if (!Guid.TryParseExact (owner, "N", out _))
			throw new ArgumentException ("Invalid lease owner.");
		var sftp = new SftpClient (host, credential.UserName, credential.Password);
		var lease = new ProcessorLease (sftp, owner, host, credential, fingerprint);
		try
			{
			await lease.VerifyAfterReconnectAsync (host, token).ConfigureAwait (false);
			return lease;
			}
		catch { lease.Dispose (); throw; }
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
		await VerifyOwnerAsync (token).ConfigureAwait (false);
		}
	private async Task VerifyOwnerAsync (CancellationToken token)
		{
		string marker = Path + "/" + _owner;
		if (!await _sftp.ExistsAsync (marker, token).ConfigureAwait (false))
			throw new IOException ("Processor lease ownership could not be confirmed.");
		var attributes = await _sftp.GetAttributesAsync (marker, token).ConfigureAwait (false);
		if (!attributes.IsRegularFile || attributes.Size != 32)
			throw new IOException ("Processor lease ownership marker changed; the lease was retained.");
		using var data = new MemoryStream ();
		await _sftp.DownloadFileAsync (marker, data, token).ConfigureAwait (false);
		if (System.Text.Encoding.UTF8.GetString (data.ToArray ()) != _owner)
			throw new IOException ("Processor lease ownership marker changed; the lease was retained.");
		}

	// Reserve the same execution guard as processor test hosts for installed-device controls.
	public async Task BeginControlAsync (CancellationToken token)
		{
		if (!_sftp.IsConnected)
			await VerifyAfterReconnectAsync (_host, token).ConfigureAwait (false);
		await VerifyOwnerAsync (token).ConfigureAwait (false);
		await using var guard = await _sftp.OpenAsync (Path + ".ActiveTest", FileMode.CreateNew, FileAccess.Write, token).ConfigureAwait (false);
		await guard.WriteAsync (System.Text.Encoding.UTF8.GetBytes ("control:" + _owner), token).ConfigureAwait (false);
		await guard.FlushAsync (token).ConfigureAwait (false);
		await VerifyOwnerAsync (token).ConfigureAwait (false);
		}

	public async Task EndControlAsync (CancellationToken token)
		{
		if (!_sftp.IsConnected)
			await VerifyAfterReconnectAsync (_host, token).ConfigureAwait (false);
		await VerifyOwnerAsync (token).ConfigureAwait (false);
		var attributes = await _sftp.GetAttributesAsync (Path + ".ActiveTest", token).ConfigureAwait (false);
		if (!attributes.IsRegularFile || attributes.IsSymbolicLink || attributes.Size != 40)
			throw new IOException ("Control guard ownership changed; reservation retained.");
		using var data = new MemoryStream ();
		await _sftp.DownloadFileAsync (Path + ".ActiveTest", data, token).ConfigureAwait (false);
		if (System.Text.Encoding.UTF8.GetString (data.ToArray ()) != "control:" + _owner)
			throw new IOException ("Control guard ownership changed; reservation retained.");
		await _sftp.DeleteFileAsync (Path + ".ActiveTest", token).ConfigureAwait (false);
		}

	public async Task ReleaseAsync (CancellationToken token)
		{
		if (!_sftp.IsConnected)
			await VerifyAfterReconnectAsync (_host, token).ConfigureAwait (false);
		await VerifyOwnerAsync (token).ConfigureAwait (false);
		// Claim the same exclusive file used by test hosts before releasing the parent.
		// A test cannot start between a non-atomic existence check and parent deletion.
		await using (var guard = await _sftp.OpenAsync (Path + ".ActiveTest", FileMode.CreateNew, FileAccess.Write, token).ConfigureAwait (false))
			{
			await guard.WriteAsync (System.Text.Encoding.UTF8.GetBytes ("release:" + _owner), token).ConfigureAwait (false);
			await guard.FlushAsync (token).ConfigureAwait (false);
			}
		await VerifyOwnerAsync (token).ConfigureAwait (false);
		await _sftp.DeleteFileAsync (Path + "/" + _owner, token).ConfigureAwait (false);
		// Fails closed if another file exists; no recursive deletion.
		await _sftp.DeleteDirectoryAsync (Path, token).ConfigureAwait (false);
		await _sftp.DeleteFileAsync (Path + ".ActiveTest", token).ConfigureAwait (false);
		}
	public void Dispose () => _sftp.Dispose ();
	}

public interface IProcessorLease : IDisposable
	{
	string Owner
		{
		get;
		}
	Task ReleaseAsync (CancellationToken token);
	}

public sealed class ProcessorBusyException : IOException
	{
	public ProcessorBusyException () : base ("Processor busy: another workflow or manual reservation owns its lease. No operation was started.") { }
	}