// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeNUnit.Transport;

using NUnit.Framework;

namespace CrestronHomeNUnit.Client.Tests;

public sealed class ProcessorExecutionLeaseTests
	{
	private string _directory = null!;
	private string _path = null!;
	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (Path.GetTempPath (), "processor-lease-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		_path = Path.Combine (_directory, "lease");
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_directory, true);

	[Test]
	public void TileLease_ExcludesOtherTilesAndSftpMkdir ()
		{
		using (var lease = ProcessorExecutionLease.Acquire (_path, null, Guid.NewGuid ().ToString ("N")))
			{
			Assert.Throws<IOException> (() => ProcessorExecutionLease.Acquire (_path, null, Guid.NewGuid ().ToString ("N")));
			Assert.Throws<IOException> (() => Directory.CreateDirectory (_path));
			}
		Assert.That (File.Exists (_path), Is.False);
		Assert.That (File.Exists (_path + ".ActiveTest"), Is.False);
		using var next = ProcessorExecutionLease.Acquire (_path, null, Guid.NewGuid ().ToString ("N"));
		}

	[Test]
	public void WorkflowLease_RequiresExactOwnerAndSerializesDelegatedHosts ()
		{
		var owner = Guid.NewGuid ().ToString ("N");
		Directory.CreateDirectory (_path);
		File.WriteAllText (Path.Combine (_path, owner), owner);
		Assert.Throws<IOException> (() => ProcessorExecutionLease.Acquire (_path, null, Guid.NewGuid ().ToString ("N")));
		Assert.Throws<IOException> (() => ProcessorExecutionLease.Acquire (_path, Guid.NewGuid ().ToString ("N"), Guid.NewGuid ().ToString ("N")));
		using (var lease = ProcessorExecutionLease.Acquire (_path, owner, Guid.NewGuid ().ToString ("N")))
			Assert.Throws<IOException> (() => ProcessorExecutionLease.Acquire (_path, owner, Guid.NewGuid ().ToString ("N")));
		Assert.That (File.ReadAllText (Path.Combine (_path, owner)), Is.EqualTo (owner));
		Assert.That (File.Exists (_path + ".ActiveTest"), Is.False);
		}

	[Test]
	public void EmptyWorkflowDirectory_IsNeverAssumedStale ()
		{
		Directory.CreateDirectory (_path);
		Assert.Throws<IOException> (() => ProcessorExecutionLease.Acquire (_path, null, Guid.NewGuid ().ToString ("N")));
		Assert.That (Directory.Exists (_path), Is.True);
		}

	[Test]
	public void ChangedExecutionOwner_RetainsBothLocks ()
		{
		var request = Guid.NewGuid ().ToString ("N");
		var lease = ProcessorExecutionLease.Acquire (_path, null, request);
		File.WriteAllText (_path + ".ActiveTest", "changed");
		Assert.Throws<IOException> (() => lease.Dispose ());
		Assert.That (File.ReadAllText (_path), Is.EqualTo (request));
		Assert.That (File.ReadAllText (_path + ".ActiveTest"), Is.EqualTo ("changed"));
		File.WriteAllText (_path + ".ActiveTest", request);
		lease.Dispose ();
		}

	[Test]
	public void StaleExecutionFile_BlocksTestAndPreservesItsEvidence ()
		{
		File.WriteAllText (_path + ".ActiveTest", "previous execution");
		Assert.Throws<IOException> (() => ProcessorExecutionLease.Acquire (_path, null, Guid.NewGuid ().ToString ("N")));
		Assert.That (File.Exists (_path), Is.False);
		Assert.That (File.ReadAllText (_path + ".ActiveTest"), Is.EqualTo ("previous execution"));
		}
	}