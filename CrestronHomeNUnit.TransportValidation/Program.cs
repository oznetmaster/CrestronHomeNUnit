// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using CrestronHomeNUnit.Runner;
using CrestronHomeNUnit.Runtime;
using CrestronHomeNUnit.Transport;

using NUnit.Framework;

internal static class Program
	{
	[STAThread]
	private static int Main (string[] args)
		{
		try
			{
			if (args.Length == 4 && args[0] == "--processor-smoke")
				{
				Task.Run (() => ProcessorSmokeValidation.RunAsync (args[1], args[2], args[3])).GetAwaiter ().GetResult ();
				return 0;
				}
			if (args.Length == 1 && args[0] == "--runner-preferences")
				{
				InitializeUiValidation ();
				RunnerPreferencesValidation.Run ();
				return 0;
				}
			if (args.Length > 1 && args[0] == "--native-names")
				{
				var names = Task.Run (() => CrestronDiscovery.FindNamesAsync (CancellationToken.None)).GetAwaiter ().GetResult ();
				foreach (string address in args.Skip (1))
					Console.WriteLine (address + ": " + (names.TryGetValue (address, out string? name) ? name : "not found"));
				return 0;
				}
			if (args.Length >= 3 && args[0] == "--ssh-names")
				{
				Task.Run (() => SshNames.ReadAsync (args[1], args.Skip (2).ToArray ())).GetAwaiter ().GetResult ();
				return 0;
				}
			if (args.Length >= 2 && args[0] == "--mdns-services")
				{
				Task.Run (() => ProcessorDiscoveryProbe.RunAsync (args[1], args.Skip (2).ToArray ())).GetAwaiter ().GetResult ();
				return 0;
				}
			if (args.Length == 2 && args[0] == "--sftp-login")
				{
				SftpValidation.Run (args[1]);
				return 0;
				}
			if (args.Length == 1 && args[0] == "--mdns")
				{
				Task.Run (DiscoveryValidation.RunAsync).GetAwaiter ().GetResult ();
				return 0;
				}

			if (args.Length != 0 && args[0] == "--remote")
				{
				return CheckRemoteAsync (args).GetAwaiter ().GetResult ();
				}

			InitializeUiValidation ();
			using (var form = new RunnerForm ())
				{
				form.CreateControl ();
				}

			Console.WriteLine ("Windows runner constructs successfully.");
			RunnerStorageValidation.Run ();
			RunnerWindowPlacementValidation.Run ();
			RunnerRecoveryValidation.Run ();
			RunnerPreferencesValidation.Run ();
			CheckFraming ();
			Task.Run (TestInputsValidation.RunAsync).GetAwaiter ().GetResult ();
			Task.Run (LiveOptInValidation.RunAsync).GetAwaiter ().GetResult ();
			Task.Run (CheckTransportAsync).GetAwaiter ().GetResult ();
			Console.WriteLine ("Transport validation passed.");
			return 0;
			}
		catch (Exception exception)
			{
			Console.Error.WriteLine (exception);
			return 1;
			}
		}

	private static void InitializeUiValidation ()
		{
		// The harness pumps messages without Application.Run. Keep its UI context
		// installed while successive forms are constructed and disposed on .NET 10.
		System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
		SynchronizationContext.SetSynchronizationContext (new System.Windows.Forms.WindowsFormsSynchronizationContext ());
		}
	private static void CheckFraming ()
		{
		using var bytes = new MemoryStream ();
		var stream = new MessageStream (bytes);
		stream.Write (new WireMessage { Kind = "test-output", RequestId = "first", Text = "Unicode: λ — 中文\nsecond line" });
		stream.Write (new WireMessage { Kind = "complete", RequestId = "second", Xml = "<test-run/>" });
		bytes.Position = 0;
		var fragmented = new MessageStream (new FragmentedStream (bytes));
		Require (fragmented.Read ()!.Text == "Unicode: λ — 中文\nsecond line", "Fragmented Unicode message changed.");
		Require (fragmented.Read ()!.RequestId == "second", "Adjacent message was lost.");
		Require (fragmented.Read () == null, "Clean EOF was not recognized.");
		foreach (byte[] invalid in new[]
		{
				new byte[]
				{
					 127,
					 255,
					 255,
					 255
				},
				new byte[]
				{
					 0,
					 0
				},
				new byte[]
				{
					 0,
					 0,
					 0,
					 3,
					 123
				}
		  }

		)
			{
			try
				{
				new MessageStream (new MemoryStream (invalid)).Read ();
				throw new Exception ("Invalid frame was accepted.");
				}
			catch (IOException)
				{
				}
			catch (InvalidDataException)
				{
				}
			}

		Console.WriteLine ("Partial reads, adjacent frames, Unicode, truncated frames and length bounds passed.");
		}

	private static async Task CheckTransportAsync ()
		{
		string work = Path.Combine (Path.GetTempPath (), "CrestronHomeNUnit-transport-" + Guid.NewGuid ().ToString ("N"));
		using var host = new TestExecutionService (work, _ => Assembly.GetExecutingAssembly ());
		using var server = new RemoteTestServer (host, "local-validation-pairing-key", 0, IPAddress.Loopback);
		try
			{
			using var wrongKey = await RemoteTestClient.ConnectAsync ("127.0.0.1", server.Port, "wrong-key");
			throw new Exception ("Wrong pairing key was accepted.");
			}
		catch (IOException)
			{
			}

		using var client = await RemoteTestClient.ConnectAsync ("127.0.0.1", server.Port, "local-validation-pairing-key");
		var events = new ConcurrentQueue<WireMessage> ();
		client.Progress += events.Enqueue;
		WireMessage discovery = await WithTimeout (client.SendAsync (Request ("discover")));
		Require (discovery.Kind == "complete" && discovery.Xml.Contains ("AsyncLifecycleTests.Quick"), "Discovery did not return NUnit XML.");
		WireMessage selected = Request ("run", "AsyncLifecycleTests.Quick");
		WireMessage completed = await WithTimeout (client.SendAsync (selected));
		var xml = new XmlDocument
			{
			XmlResolver = null
			};
		xml.LoadXml (completed.Xml);
		Require (completed.ExitCode == 0 && xml.DocumentElement!.GetAttribute ("total") == "1", "Selected test did not run alone.");
		Require (events.Any (message => message.Kind == "test-start" && message.RequestId == selected.RequestId), "Test start was not streamed.");
		Require (events.Any (message => message.Kind == "test-finish" && message.RequestId == selected.RequestId), "Test result was not streamed.");
		Require (events.Any (message => message.Kind == "test-output" && message.Text.Contains ("live progress")), "Live output was not streamed.");
		WireMessage noTests = await WithTimeout (client.SendAsync (Request ("run", "Missing.Test")));
		Require (noTests.ExitCode == 2 && noTests.Xml == "", "An empty selection returned stale results.");
		WireMessage failed = await WithTimeout (client.SendAsync (Request ("run", "AsyncLifecycleTests.ExpectedFailure")));
		Require (failed.ExitCode == 1 && failed.Xml.Contains ("expected failure for transport validation"), "Failed-test details were not transferred.");
		Console.WriteLine ("Authentication, discovery, selected execution, progress, failure XML and stale-result protection passed.");
		AsyncLifecycleTests.Entered.Reset ();
		AsyncLifecycleTests.Release.Reset ();
		WireMessage slow = Request ("run", "AsyncLifecycleTests.Slow");
		Task<WireMessage> running = client.SendAsync (slow);
		Require (await Task.Run (() => AsyncLifecycleTests.Entered.Wait (10000)), "Blocking test did not start.");
		WireMessage busy = await WithTimeout (client.SendAsync (Request ("discover")));
		Require (busy.Kind == "error" && busy.Text.Contains ("already running"), "Overlapping run was not rejected.");
		WireMessage homeBusy = await host.ExecuteAsync (Request ("run"), _ =>
		{
		});
		Require (homeBusy.Kind == "error", "Home command bypassed the shared execution guard.");
		WireMessage cancel = await WithTimeout (client.SendAsync (new WireMessage { Kind = "cancel", TargetId = slow.RequestId }));
		Require (cancel.Kind == "cancelled", "Cancellation command was not acknowledged.");
		AsyncLifecycleTests.Release.Set ();
		Require ((await WithTimeout (running)).ExitCode == 3, "Cancelled run did not return cancellation status.");
		Require ((await WithTimeout (client.SendAsync (Request ("run", "AsyncLifecycleTests.Quick")))).ExitCode == 0, "Host did not recover after cancellation.");
		Console.WriteLine ("Overlapping Home/TCP commands, cooperative cancellation and rerun passed.");
		AsyncLifecycleTests.Entered.Reset ();
		AsyncLifecycleTests.Release.Reset ();
		Task<WireMessage> disconnectedRun = client.SendAsync (Request ("run", "AsyncLifecycleTests.Slow"));
		Require (await Task.Run (() => AsyncLifecycleTests.Entered.Wait (10000)), "Disconnect test did not start.");
		client.Dispose ();
		try
			{
			await WithTimeout (disconnectedRun);
			throw new Exception ("Disconnected request completed unexpectedly.");
			}
		catch (IOException)
			{
			}

		AsyncLifecycleTests.Release.Set ();
		using var reconnected = await RemoteTestClient.ConnectAsync ("127.0.0.1", server.Port, "local-validation-pairing-key");
		WireMessage retry;
		DateTime deadline = DateTime.UtcNow.AddSeconds (10);
		do
			{
			retry = await WithTimeout (reconnected.SendAsync (Request ("run", "AsyncLifecycleTests.Quick")));
			if (retry.Kind == "error")
				{
				await Task.Delay (50);
				}
			}
		while (retry.Kind == "error" && DateTime.UtcNow < deadline);
		Require (retry.Kind == "complete" && retry.ExitCode == 0, "Host did not recover after disconnect.");
		Console.WriteLine ("Disconnect cleanup and reconnect passed.");
		}

	private static WireMessage Request (string kind, string? selected = null)
		{
		var request = new WireMessage
			{
			Kind = kind,
			Suite = "compatibility",
			RequestId = Guid.NewGuid ().ToString ("N")
			};
		if (selected != null)
			{
			request.TestNames.Add (selected);
			}

		return request;
		}

	private static async Task<T> WithTimeout<T> (Task<T> task)
		{
		if (await Task.WhenAny (task, Task.Delay (15000)) != task)
			{
			throw new TimeoutException ("Transport check timed out.");
			}

		return await task;
		}

	private static void Require (bool condition, string failure)
		{
		if (!condition)
			{
			throw new Exception (failure);
			}
		}

	private static async Task<int> CheckRemoteAsync (string[] args)
		{
		if (args.Length != 4 && (args.Length != 5 || args[4] != "--repeat"))
			{
			throw new ArgumentException ("Usage: --remote <runner-settings.json> <suite> <result-directory> [--repeat]");
			}

		using var settingsStream = File.OpenRead (args[1]);
		var settings = (RemoteSettings)new System.Runtime.Serialization.Json.DataContractJsonSerializer (typeof (RemoteSettings)).ReadObject (settingsStream)!;
		using var client = await RemoteTestClient.ConnectAsync (settings.Host, settings.Port, settings.PairingKey);
		int starts = 0, finishes = 0;
		client.Progress += message =>
		{
			if (message.Kind == "test-start")
				{
				Interlocked.Increment (ref starts);
				}

			if (message.Kind == "test-finish")
				{
				Interlocked.Increment (ref finishes);
				}
		};
		WireMessage discovery = await client.SendAsync (new WireMessage { Kind = "discover", Suite = args[2] });
		if (discovery.Kind != "complete" || discovery.ExitCode != 0)
			{
			throw new Exception (discovery.Text);
			}

		Directory.CreateDirectory (args[3]);
		File.WriteAllText (Path.Combine (args[3], "TestTree.xml"), discovery.Xml);
		int repeats = args.Length == 5 ? 2 : 1;
		for (int iteration = 1; iteration <= repeats; iteration++)
			{
			starts = finishes = 0;
			string directory = repeats == 1 ? args[3] : Path.Combine (args[3], "run-" + iteration);
			Directory.CreateDirectory (directory);
			WireMessage result = await client.SendAsync (new WireMessage { Kind = "run", Suite = args[2] });
			File.WriteAllText (Path.Combine (directory, "TestResult.xml"), result.Xml);
			Console.WriteLine ("Execution " + iteration + ": " + result.Text + "; streamed starts: " + starts + "; streamed finishes: " + finishes);
			if (result.Kind != "complete")
				{
				return 2;
				}

			if (result.ExitCode != 0)
				{
				return result.ExitCode;
				}
			}

		return 0;
		}

	[System.Runtime.Serialization.DataContract]
	private sealed class RemoteSettings
		{
		[System.Runtime.Serialization.DataMember]
		public string Host { get; set; } = "";

		[System.Runtime.Serialization.DataMember]
		public int Port
			{
			get; set;
			}

		[System.Runtime.Serialization.DataMember]
		public string PairingKey { get; set; } = "";
		}

	private sealed class FragmentedStream (Stream inner) : Stream
		{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => inner.Length;
		public override long Position
			{
			get => inner.Position; set => throw new NotSupportedException ();
			}

		public override int Read (byte[] buffer, int offset, int count) => inner.Read (buffer, offset, Math.Min (count, 1));
		public override void Flush ()
			{
			}

		public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException ();
		public override void SetLength (long value) => throw new NotSupportedException ();
		public override void Write (byte[] buffer, int offset, int count) => throw new NotSupportedException ();
		}
	}

[TestFixture]
public sealed class AsyncLifecycleTests
	{
	public static readonly ManualResetEventSlim Entered = new (false);
	public static readonly ManualResetEventSlim Release = new (false);
	[Test]
	public void Quick ()
		{
		TestContext.Progress.WriteLine ("live progress");
		Assert.That (2 + 2, Is.EqualTo (4));
		}

	[Test]
	public void ExpectedFailure () => Assert.Fail ("expected failure for transport validation");
	[Test]
	public void Slow ()
		{
		Entered.Set ();
		Assert.That (Release.Wait (TimeSpan.FromSeconds (15)), Is.True, "Validation did not release the blocking test.");
		}
	}