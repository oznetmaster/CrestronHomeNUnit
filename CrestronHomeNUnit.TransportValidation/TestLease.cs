// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System;
using System.Threading;
using System.Threading.Tasks;

internal sealed class TestLease : CrestronHomeNUnit.Client.IProcessorLease
	{
	public string Owner { get; } = Guid.NewGuid ().ToString ("N");
	public Task ReleaseAsync (CancellationToken token) => Task.CompletedTask;
	public void Dispose () { }
	}