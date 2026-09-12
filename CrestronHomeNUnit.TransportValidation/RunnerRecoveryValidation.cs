// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using CrestronHomeNUnit.Runner;
using CrestronHomeNUnit.Transport;

internal static class RunnerRecoveryValidation
	{
	public static void Run ()
		{
		using (var isolation = new RunnerForm ((_, _, _, _) => Task.FromResult (new ProcessorConnection ("validation", "validation"))))
			{
			Field<TextBox> (isolation, "_host").Text = "192.0.2.10";
			Field<TextBox> (isolation, "_user").Text = "processor-a-user";
			Field<TextBox> (isolation, "_key").Text = "processor-a-password";
			Field<TextBox> (isolation, "_host").Text = "192.0.2.11";
			Require (Field<TextBox> (isolation, "_user").Text.Length == 0 && Field<TextBox> (isolation, "_key").Text.Length == 0, "Manual processor change reused another processor's credentials.");
			ComboBox packages = Field<ComboBox> (isolation, "_packages");
			packages.Items.Add (new DiscoveredPackage { Host = "192.0.2.11", Port = 12345, ProcessorId = Guid.NewGuid ().ToString ("N"), Name = "Unknown processor package" });
			packages.SelectedIndex = 0;
			Require (!Field<bool> (isolation, "_connecting") && Field<Button> (isolation, "_connect").Enabled, "A package without credentials attempted automatic sign-in.");
			Require ((Field<ToolStripStatusLabel> (isolation, "_status").Text ?? "").Contains ("credentials"), "An unknown processor did not request credentials.");
			}
		CheckSuiteCatalog ();
		CheckPackageSwitching ();
		foreach (bool disconnect in new[]
		{
				true,
				false
		  }

		)
			{
			var listener = new TcpListener (IPAddress.Loopback, 0);
			listener.Start ();
			try
				{
				Task peer = Task.Run (async () =>
				{
					using TcpClient socket = await listener.AcceptTcpClientAsync ();
					var messages = new MessageStream (socket.GetStream ());
					WireMessage hello = messages.Read ()!;
					messages.Write (new WireMessage { Kind = "hello-ok", RequestId = hello.RequestId, Suites = [new TestSuiteInfo { Id = "recovery", Name = "Recovery tests" }] });
					WireMessage request = messages.Read ()!;
					messages.Write (new WireMessage { Kind = "test-finish", RequestId = request.RequestId, Xml = "<test-case id='passed' fullname='Recovery.Completed' result='Passed'/>" });
					messages.Write (new WireMessage { Kind = "test-start", RequestId = request.RequestId, Xml = "<start-test id='active' fullname='Recovery.Interrupted'/>" });
					messages.Write (new WireMessage { Kind = "test-output", RequestId = request.RequestId, Text = "Output before interruption" });
					if (!disconnect)
						{
						messages.Write (new WireMessage { Kind = "error", RequestId = request.RequestId, Text = "Synthetic host failure" });
						// Keep the connection open until the form is disposed.
						messages.Read ();
						}
				});
				using (var form = new RunnerForm ((_, _, _, _) => Task.FromResult (new ProcessorConnection (Guid.NewGuid ().ToString ("N"), "synthetic-validation-key"))))
					{
					_ = form.Handle;
					Field<TextBox> (form, "_host").Text = "127.0.0.1";
					Field<NumericUpDown> (form, "_port").Value = ((IPEndPoint)listener.LocalEndpoint).Port;
					Field<TextBox> (form, "_key").Text = "synthetic-validation-key";
					Pump (Invoke (form, "ConnectAsync"));
					Require ((Field<ToolStripStatusLabel> (form, "_status").Text ?? "").StartsWith ("Connected."), (Field<ToolStripStatusLabel> (form, "_status").Text ?? ""));
					Task operation = Invoke (form, "ExecuteAsync", "run", false);
					string directory = Path.Combine (Path.GetTempPath (), "CrestronHomeNUnit-recovery-" + Guid.NewGuid ().ToString ("N"));
					typeof (RunnerForm).GetField ("_resultDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, directory);
					Pump (operation);
					Application.DoEvents ();
					ListView rows = Field<ListView> (form, "_results");
					Require (rows.Items.Count == 2, "Progress rows were lost.");
					Require (rows.Items[0].Text == "Passed", "Completed test result was changed.");
					Require (rows.Items[1].Text == "Incomplete", "Interrupted test was left running or marked failed.");
					Require ((Field<ToolStripStatusLabel> (form, "_status").Text ?? "").StartsWith ("Incomplete:"), "Disconnect notification overwrote the incomplete status.");
					Require (Field<TextBox> (form, "_details").Text.Contains ("Recovery.Interrupted"), "Last active test was lost.");
					Require (File.ReadAllText (Path.Combine (directory, "RunStatus.txt")).Contains ("Incomplete\tRecovery.Interrupted"), "Incomplete test was not saved.");
					Require (File.ReadAllText (Path.Combine (directory, "LiveOutput.txt")).Contains ("Output before interruption"), "Live output was not saved.");
					Require (!File.Exists (Path.Combine (directory, "TestResult.xml")), "An interrupted run fabricated a final NUnit result.");
					if (!disconnect)
						{
						Field<RemoteTestClient> (form, "_client").Dispose ();
						}
					}

				Pump (peer);
				}
			finally
				{
				listener.Stop ();
				}
			}

		Console.WriteLine ("Runner disconnect and host-error reporting preserve completed results, mark active tests incomplete and save diagnostics.");
		}

	private static void CheckSuiteCatalog ()
		{
		foreach (bool empty in new[] { false, true })
			{
			var listener = new TcpListener (IPAddress.Loopback, 0);
			listener.Start ();
			try
				{
				Task peer = Task.Run (async () =>
					{
						using TcpClient socket = await listener.AcceptTcpClientAsync ();
						var messages = new MessageStream (socket.GetStream ());
						WireMessage hello = messages.Read ()!;
						messages.Write (new WireMessage
							{
							Kind = "hello-ok",
							RequestId = hello.RequestId,
							Suites = empty ? [] : [new TestSuiteInfo { Id = "library", Name = "Library Unit Tests" }, new TestSuiteInfo { Id = "library-live", Name = "Library Live Tests", ManualOnly = true }]
							});
						messages.Read ();
					});
				using (var form = new RunnerForm ((_, _, _, _) => Task.FromResult (new ProcessorConnection ("catalog-validation", "synthetic-key")),
					discoverPackages: () => Task.FromResult<System.Collections.Generic.IReadOnlyList<DiscoveredPackage>> (
						[new DiscoveredPackage { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, ProcessorId = "catalog-validation", Name = "Library tests" }])))
					{
					_ = form.Handle;
					ComboBox suites = Field<ComboBox> (form, "_suite");
					Require (suites.Items.Count == 0 && !suites.Enabled, "A disconnected runner displayed default suites.");
					Field<TextBox> (form, "_host").Text = "127.0.0.1";
					Field<NumericUpDown> (form, "_port").Value = ((IPEndPoint)listener.LocalEndpoint).Port;
					if (empty)
						Pump (Invoke (form, "ConnectAsync"));
					else
						{
						Field<TextBox> (form, "_user").Text = "synthetic-user";
						Field<TextBox> (form, "_key").Text = "synthetic-key";
						ComboBox packages = Field<ComboBox> (form, "_packages");
						packages.Items.Add (new DiscoveredPackage { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, ProcessorId = "catalog-validation", Name = "Library tests" });
						packages.SelectedIndex = 0;
						Pump (WaitForConnection (form));
						}
					if (empty)
						{
						Require (suites.Items.Count == 0 && !suites.Enabled && !Field<Button> (form, "_runAll").Enabled, "An empty catalog enabled guessed suites.");
						Require ((Field<ToolStripStatusLabel> (form, "_status").Text ?? "").Contains ("did not advertise"), "An empty catalog did not explain the connection failure.");
						}
					else
						{
						Require (suites.Items.Count == 2 && suites.Enabled && (suites.Items[0] as TestSuiteInfo)?.Id == "library" && ((suites.Items[1] as TestSuiteInfo)?.ManualOnly == true), "The advertised library catalog was not used.");
						Require (Field<Button> (form, "_discover").Enabled, "Discovery was disabled after loading the catalog.");
						Button runSelected = Field<Button> (form, "_runSelected");
						Require (!runSelected.Enabled, "Run selection was enabled without a selected node.");
						TreeView tree = Field<TreeView> (form, "_tree");
						_ = tree.Handle;
						tree.SelectedNode = tree.Nodes.Add ("Synthetic fixture");
						Require (runSelected.Enabled, "Selecting a fixture did not enable Run selection.");
						suites.SelectedIndex = 1;
						Require (tree.SelectedNode == null && !runSelected.Enabled, "Changing suites left Run selection enabled without a selection.");
						Pump (Invoke (form, "ConnectAsync"));
						Require (!suites.Enabled && !Field<Button> (form, "_runAll").Enabled, "Disconnect left suite actions enabled.");
						Field<TextBox> (form, "_host").Text = "192.0.2.12";
						Require (suites.Items.Count == 0, "Changing the target retained another package's suite catalog.");
						}
					}
				Pump (peer);
				}
			finally { listener.Stop (); }
			}
		Console.WriteLine ("Runner suite catalog: startup, advertised suites, disconnect, target change and empty-catalog rejection passed.");
		}

	private static void CheckPackageSwitching ()
		{
		var first = new TcpListener (IPAddress.Loopback, 0);
		var second = new TcpListener (IPAddress.Loopback, 0);
		first.Start ();
		second.Start ();
		try
			{
			Task Serve (TcpListener listener, string suite) => Task.Run (async () =>
				{
					using TcpClient socket = await listener.AcceptTcpClientAsync ();
					var messages = new MessageStream (socket.GetStream ());
					WireMessage hello = messages.Read ()!;
					messages.Write (new WireMessage { Kind = "hello-ok", RequestId = hello.RequestId, Suites = [new TestSuiteInfo { Id = suite, Name = suite }] });
					Require (messages.Read () == null, "Switching packages unexpectedly ran a test.");
				});
			Task firstPeer = Serve (first, "client-suite");
			Task secondPeer = Serve (second, "driver-suite");
			var clientPackage = new DiscoveredPackage { Host = "127.0.0.1", Port = ((IPEndPoint)first.LocalEndpoint).Port, ProcessorId = "switch-processor", Name = "Client tests" };
			var driverPackage = new DiscoveredPackage { Host = "127.0.0.1", Port = ((IPEndPoint)second.LocalEndpoint).Port, ProcessorId = "switch-processor", Name = "Driver tests" };
			int authentications = 0;
			bool omitConnected = false;
			using (var form = new RunnerForm ((_, _, _, _) =>
				{
					authentications++;
					return Task.FromResult (new ProcessorConnection ("switch-processor", "synthetic-key"));
				}, discoverPackages: () => Task.FromResult<System.Collections.Generic.IReadOnlyList<DiscoveredPackage>> (omitConnected ? [driverPackage] : [clientPackage, driverPackage])))
				{
				_ = form.Handle;
				Field<TextBox> (form, "_host").Text = "127.0.0.1";
				Field<TextBox> (form, "_user").Text = "synthetic-user";
				Field<TextBox> (form, "_key").Text = "synthetic-key";
				ComboBox packages = Field<ComboBox> (form, "_packages");
				ComboBox suites = Field<ComboBox> (form, "_suite");
				Pump (Invoke (form, "FindPackagesAsync"));
				packages.SelectedItem = clientPackage;
				Pump (WaitForConnection (form));
				Require (packages.Enabled && Field<Button> (form, "_findPackages").Enabled, "Connected runner prevents package switching or discovery.");
				Require ((suites.SelectedItem as TestSuiteInfo)?.Id == "client-suite", "First package did not connect.");
				omitConnected = true;
				Pump (Invoke (form, "FindPackagesAsync"));
				Require (packages.SelectedItem == clientPackage && authentications == 1, "Refreshing discovery changed the active connection.");
				typeof (RunnerForm).GetField ("_activeRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, "busy");
				typeof (RunnerForm).GetMethod ("UpdateControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (form, null);
				Require (!packages.Enabled && !Field<Button> (form, "_findPackages").Enabled, "An active run allows package switching.");
				typeof (RunnerForm).GetField ("_activeRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, null);
				typeof (RunnerForm).GetMethod ("UpdateControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (form, null);
				Field<TextBox> (form, "_details").Text = "Old package details";
				packages.SelectedItem = driverPackage;
				Pump (WaitForConnection (form));
				Pump (firstPeer);
				Require (authentications == 2 && suites.Items.Count == 1 && (suites.SelectedItem as TestSuiteInfo)?.Id == "driver-suite", "Selecting another package did not replace the connection and suite catalog.");
				Require (Field<TextBox> (form, "_details").Text.Length == 0 && !Field<Button> (form, "_runSelected").Enabled, "Switching packages retained old results or selections.");
				Require ((Field<ToolStripStatusLabel> (form, "_status").Text ?? "").StartsWith ("Connected."), "The old connection overwrote the new connection status.");
				Pump (Invoke (form, "ConnectAsync"));
				}
			Pump (secondPeer);
			}
		finally { first.Stop (); second.Stop (); }
		Console.WriteLine ("Runner package switching: idle discovery, missing advertisement, reconnect, suite isolation and active-run guard passed.");
		}


	private static async Task WaitForConnection (RunnerForm form)
		{
		while (Field<bool> (form, "_connecting"))
			await Task.Delay (5);
		}

	private static T Field<T> (RunnerForm form, string name) => (T)typeof (RunnerForm).GetField (name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue (form)!;
	private static Task Invoke (RunnerForm form, string name, params object[] args) => (Task)typeof (RunnerForm).GetMethod (name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (form, args)!;
	private static void Pump (Task task)
		{
		DateTime deadline = DateTime.UtcNow.AddSeconds (15);
		while (!task.IsCompleted)
			{
			if (DateTime.UtcNow >= deadline)
				{
				throw new TimeoutException ("Runner recovery validation timed out.");
				}

			Application.DoEvents ();
			Thread.Sleep (5);
			}

		task.GetAwaiter ().GetResult ();
		}

	private static void Require (bool condition, string message)
		{
		if (!condition)
			{
			throw new InvalidOperationException (message);
			}
		}
	}