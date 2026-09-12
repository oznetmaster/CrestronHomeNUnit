// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using CrestronHomeNUnit.Runner;
using CrestronHomeNUnit.Transport;

internal static class RunnerEndpointValidation
	{
	public static void Run ()
		{
		foreach (string mode in new[] { "connect", "address", "disconnect", "refresh" })
			CheckChangedPort (mode);
		foreach (bool ambiguous in new[] { false, true })
			CheckUnavailable (ambiguous);
		Console.WriteLine ("Runner endpoints: stale port, dropped connection, refreshed discovery, missing/ambiguous identity, preserved results and no test replay passed.");
		}

	private static void CheckChangedPort (string mode)
		{
		bool initialRefresh = mode is "connect" or "address";
		var first = new TcpListener (IPAddress.Loopback, 0);
		var second = new TcpListener (IPAddress.Loopback, 0);
		first.Start ();
		second.Start ();
		var drop = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		try
			{
			DiscoveredPackage Package (TcpListener listener) => new () { ProcessorId = "endpoint-processor", Name = "Endpoint tests", Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port };
			DiscoveredPackage old = Package (first);
			if (mode == "address")
				old.Host = "localhost";
			DiscoveredPackage current = Package (second);
			DiscoveredPackage advertised = initialRefresh ? current : old;
			Task Serve (TcpListener listener, bool waitForDrop) => Task.Run (async () =>
				{
					using TcpClient socket = await listener.AcceptTcpClientAsync ();
					var messages = new MessageStream (socket.GetStream ());
					WireMessage hello = messages.Read ()!;
					messages.Write (new WireMessage { Kind = "hello-ok", RequestId = hello.RequestId, Suites = [new TestSuiteInfo { Id = "endpoint-suite", Name = "Endpoint suite" }] });
					if (waitForDrop)
						await drop.Task;
					else
						Require (messages.Read () == null, "Reconnection unexpectedly sent a test operation.");
				});
			Task firstPeer = initialRefresh ? Task.CompletedTask : Serve (first, mode == "disconnect");
			Task secondPeer = Serve (second, false);
			int discoveries = 0;
			using (var form = new RunnerForm ((_, _, _, _) => Task.FromResult (new ProcessorConnection (old.ProcessorId, "synthetic-key")),
				discoverPackages: () => { discoveries++; return Task.FromResult<IReadOnlyList<DiscoveredPackage>> ([advertised]); }))
				{
				_ = form.Handle;
				Field<TextBox> (form, "_host").Text = old.Host;
				Field<TextBox> (form, "_user").Text = "synthetic-user";
				Field<TextBox> (form, "_key").Text = "synthetic-key";
				ComboBox packages = Field<ComboBox> (form, "_packages");
				packages.Items.Add (old);
				packages.SelectedIndex = 0;
				Pump (WaitFor (() => !Field<bool> (form, "_connecting")));
				Require (Field<RemoteTestClient?> (form, "_client") != null, "Initial connection failed.");
				if (!initialRefresh)
					{
					Field<ListView> (form, "_results").Items.Add ("Passed");
					Field<TextBox> (form, "_details").Text = "Previous result details";
					advertised = current;
					if (mode == "disconnect")
						{
						drop.SetResult (true);
						Pump (WaitFor (() => discoveries >= 2 && !Field<bool> (form, "_recovering") && !Field<bool> (form, "_connecting")));
						}
					else
						Pump (Invoke (form, "FindPackagesAsync"));
					Require (Field<ListView> (form, "_results").Items.Count == 1 && Field<TextBox> (form, "_details").Text == "Previous result details", "Reconnection discarded previous results.");
					}
				Require (Field<RemoteTestClient?> (form, "_client") != null && (int)Field<NumericUpDown> (form, "_port").Value == current.Port, "Runner reused the stale port.");
				Require (Field<TextBox> (form, "_host").Text == current.Host, "Runner reused the stale processor address.");
				Require (packages.Items.Count == 1 && ((DiscoveredPackage)packages.SelectedItem!).Port == current.Port, "Package list retained a stale duplicate endpoint.");
				Pump (Invoke (form, "ConnectAsync"));
				Application.DoEvents ();
				Require (!Field<bool> (form, "_recovering"), "Explicit Disconnect started automatic recovery.");
				}
			Pump (firstPeer);
			Pump (secondPeer);
			}
		finally { drop.TrySetResult (true); first.Stop (); second.Stop (); }
		}

	private static void CheckUnavailable (bool ambiguous)
		{
		var selected = new DiscoveredPackage { ProcessorId = "missing-processor", Name = "Missing tests", Host = "127.0.0.1", Port = 1 };
		int authentications = 0;
		using var form = new RunnerForm ((_, _, _, _) =>
			{
				authentications++;
				return Task.FromResult (new ProcessorConnection (selected.ProcessorId, "synthetic-key"));
			}, discoverPackages: () => Task.FromResult<IReadOnlyList<DiscoveredPackage>> (ambiguous ? [selected, selected] : [new DiscoveredPackage { ProcessorId = "different-processor", Name = selected.Name, Host = selected.Host, Port = selected.Port }]));
		_ = form.Handle;
		Field<TextBox> (form, "_host").Text = selected.Host;
		Field<TextBox> (form, "_user").Text = "synthetic-user";
		Field<TextBox> (form, "_key").Text = "synthetic-key";
		Field<ComboBox> (form, "_packages").Items.Add (selected);
		Field<ComboBox> (form, "_packages").SelectedIndex = 0;
		Pump (WaitFor (() => !Field<bool> (form, "_connecting")));
		Require (authentications == 0 && Field<RemoteTestClient?> (form, "_client") == null, "Unavailable or ambiguous discovery connected to the stale endpoint.");
		Require ((Field<ToolStripStatusLabel> (form, "_status").Text ?? "").Contains (ambiguous ? "More than one" : "not advertising"), "Unavailable package did not explain the failure.");
		}

	private static T Field<T> (RunnerForm form, string name) => (T)typeof (RunnerForm).GetField (name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue (form)!;
	private static Task Invoke (RunnerForm form, string name) => (Task)typeof (RunnerForm).GetMethod (name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (form, null)!;
	private static async Task WaitFor (Func<bool> ready)
		{
		while (!ready ())
			await Task.Delay (5);
		}
	private static void Pump (Task task)
		{
		DateTime deadline = DateTime.UtcNow.AddSeconds (15);
		while (!task.IsCompleted)
			{
			if (DateTime.UtcNow >= deadline)
				throw new TimeoutException ("Endpoint recovery validation timed out.");
			Application.DoEvents ();
			Thread.Sleep (5);
			}
		task.GetAwaiter ().GetResult ();
		}
	private static void Require (bool condition, string message)
			{
			if (!condition)
				throw new InvalidOperationException (message);
			}
	}