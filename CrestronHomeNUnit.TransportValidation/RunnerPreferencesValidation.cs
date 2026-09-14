// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using CrestronHomeNUnit.Runner;
using CrestronHomeNUnit.Transport;

internal static class RunnerPreferencesValidation
	{
	public static void Run ()
		{
		string directory = Path.Combine (Path.GetTempPath (), "RunnerPreferences-" + Guid.NewGuid ().ToString ("N"));
		string path = Path.Combine (directory, "selections.json");
		int previousPort = 0;
		string originalSelections = "";
		for (int phase = 0; phase < 2; phase++)
			{
			var listener = new TcpListener (IPAddress.Loopback, 0);
			listener.Start ();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			int discoveries = 0;
			try
				{
				Task peer = Task.Run (async () =>
				{
					using TcpClient socket = await listener.AcceptTcpClientAsync ();
					var messages = new MessageStream (socket.GetStream ());
					WireMessage hello = messages.Read ()!;
					messages.Write (new WireMessage { Kind = "hello-ok", RequestId = hello.RequestId, Suites = [new TestSuiteInfo { Id = "restore-suite", Name = "Restore suite" }] });
					WireMessage? request;
					while ((request = messages.Read ()) != null)
						{
						if (request.Kind != "discover")
							throw new InvalidOperationException ("Restoring preferences attempted to run tests.");
						discoveries++;
						messages.Write (new WireMessage { Kind = "complete", RequestId = request.RequestId, Xml = "<test-run><test-suite name='Fixture'><test-case name='Stable case' fullname='Fixture.StableCase' id='case'/></test-suite></test-run>" });
						}
				});
				var package = new DiscoveredPackage { Host = "127.0.0.1", Port = port, Name = "Restore package", ProcessorId = "restore-processor" };
				using (var form = new RunnerForm ((_, _, _, _) => Task.FromResult (new ProcessorConnection ("restore-processor", "synthetic-token")), path,
					 () => Task.FromResult<IReadOnlyList<DiscoveredPackage>> ([package]), acquireLease: () => Task.FromResult<CrestronHomeNUnit.Client.IProcessorLease> (new TestLease ())))
					{
					_ = form.Handle;
					Field<TextBox> (form, "_host").Text = "127.0.0.1";
					Field<TextBox> (form, "_user").Text = "private-test-user";
					Field<TextBox> (form, "_key").Text = "private-test-password";
					if (phase == 0)
						{
						Pump (Invoke (form, "FindPackagesAsync"));
						Field<ComboBox> (form, "_packages").SelectedItem = package;
						Pump (WaitConnecting (form));
						TreeView tree = Field<TreeView> (form, "_tree");
						_ = tree.Handle;
						tree.SelectedNode = tree.Nodes.Add ("Fixture").Nodes.Add ("Stable case");
						tree.SelectedNode.Tag = "Fixture.StableCase";
						Field<CheckBox> (form, "_useAtNextRestart").Checked = true;
						string saved = File.ReadAllText (path);
						originalSelections = saved;
						Require (saved.Contains ("restore-suite") && saved.Contains ("Fixture.StableCase"), "Selections were not saved.");
						Require (!saved.Contains ("private-test-user") && !saved.Contains ("private-test-password") && !saved.Contains ("synthetic-token"), "Credentials were persisted with selections.");
						previousPort = port;
						}
					else
						{
						// Replace the saved endpoint with an invalid old port. Restoration must use discovery.
						string saved = File.ReadAllText (path).Replace ("\"Port\":" + previousPort, "\"Port\":1");
						File.WriteAllText (path, saved);
						typeof (RunnerForm).GetMethod ("LoadSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (form, [path]);
						Pump (Invoke (form, "RestoreSelectionsAsync"));
						Require ((int)Field<NumericUpDown> (form, "_port").Value == port, "Restore reused the saved port instead of the discovered endpoint.");
						Require ((Field<TreeView> (form, "_tree").SelectedNode?.Tag as string) == "Fixture.StableCase", "Saved test selection was not restored.");
						Require (Field<Button> (form, "_runSelected").Enabled, "Restored selection did not enable its run button.");
						Require (discoveries == 1, "Restore did not perform exactly one discovery.");
						Field<CheckBox> (form, "_useAtNextRestart").Checked = false;
						Require (!File.Exists (path), "Unchecking restart preferences did not remove the saved selection.");
						}
					Pump (Invoke (form, "ConnectAsync"));
					}
				Pump (peer);
				}
			finally { listener.Stop (); }
			}
		File.WriteAllText (path, originalSelections);
		using (var missing = new RunnerForm ((_, _, _, _) => throw new InvalidOperationException ("An absent package must not connect."), path,
			 () => Task.FromResult<IReadOnlyList<DiscoveredPackage>> ([])))
			{
			_ = missing.Handle;
			Pump (Invoke (missing, "RestoreSelectionsAsync"));
			typeof (RunnerForm).GetMethod ("SaveSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (missing, null);
			Require (File.ReadAllText (path) == originalSelections, "An unavailable package overwrote the saved selections.");
			Field<CheckBox> (missing, "_useAtNextRestart").Checked = false;
			}
		Console.WriteLine ("Runner preferences: save, restart, changed port, suite/test restoration, missing package, no test execution, no credentials, and opt-out passed.");
		}

	private static T Field<T> (RunnerForm form, string name) => (T)typeof (RunnerForm).GetField (name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue (form)!;
	private static Task Invoke (RunnerForm form, string name) => (Task)typeof (RunnerForm).GetMethod (name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (form, null)!;
	private static async Task WaitConnecting (RunnerForm form)
		{
		while (Field<bool> (form, "_connecting"))
			await Task.Delay (5);
		}
	private static void Pump (Task task)
		{
		DateTime deadline = DateTime.UtcNow.AddSeconds (15);
		while (!task.IsCompleted)
			{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException ("Runner preferences validation timed out.");
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