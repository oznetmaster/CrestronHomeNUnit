// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;

namespace CrestronHomeNUnit.Workflow;

// Preserve the original public API while sharing coordination with standalone clients.
public sealed class ProcessorLease : IDisposable
	{
	private readonly Client.ProcessorLease _lease;
	public string Owner => _lease.Owner;
	private ProcessorLease (Client.ProcessorLease lease) => _lease = lease;
	public static async Task<ProcessorLease> AcquireAsync (string host, NetworkCredential credential, string fingerprint, string runId, CancellationToken token)
		=> new (await Client.ProcessorLease.AcquireAsync (host, credential, fingerprint, runId, token).ConfigureAwait (false));
	public static async Task<ProcessorLease> AcquireAsync (string host, NetworkCredential credential, string fingerprint, string runId, TimeSpan wait, CancellationToken token)
		=> new (await Client.ProcessorLease.AcquireAsync (host, credential, fingerprint, runId, wait, token).ConfigureAwait (false));
	public static async Task<ProcessorLease> ResumeAsync (string host, NetworkCredential credential, string fingerprint, string owner, CancellationToken token)
		=> new (await Client.ProcessorLease.ResumeAsync (host, credential, fingerprint, owner, token).ConfigureAwait (false));
	public Task VerifyAfterReconnectAsync (string host, CancellationToken token) => _lease.VerifyAfterReconnectAsync (host, token);
	public Task ReleaseAsync (CancellationToken token) => _lease.ReleaseAsync (token);
	internal Task BeginControlAsync (CancellationToken token) => _lease.BeginControlAsync (token);
	internal Task EndControlAsync (CancellationToken token) => _lease.EndControlAsync (token);
	public void Dispose () => _lease.Dispose ();
	}