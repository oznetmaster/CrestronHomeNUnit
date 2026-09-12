// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Runner;

public sealed partial class RunnerForm : Form
	{
	private readonly Button _findPackages = new () { Text = "Find packages", AutoSize = true };
	private readonly ComboBox _packages = new () { Width = 570, DropDownStyle = ComboBoxStyle.DropDownList };
	private readonly CancellationTokenSource _closing = new ();
	private bool _finding;
	private readonly Func<Task<IReadOnlyList<DiscoveredPackage>>>? _discoverPackages;
	private string _processorId = "";
	private string _credentialHost = "";
	private readonly TextBox _host = new ()
		{
		Width = 155
		};
	private readonly Button _testInputs = new () { Text = "Test inputs…", AutoSize = true };
	private readonly Button _clearTestInputs = new () { Text = "Clear inputs", AutoSize = true };
	private readonly Label _inputStatus = new () { Text = "No test inputs", AutoSize = true, Padding = new Padding (0, 5, 0, 0) };
	private readonly NumericUpDown _port = new ()
		{
		Minimum = 1,
		Maximum = 65535,
		Value = RemoteTestServer.DefaultPort,
		Width = 75
		};
	private readonly Func<string, string, string, string, Task<ProcessorConnection>> _authenticate;
	private readonly bool _rememberCredentials;
	private readonly TextBox _user = new () { Width = 110 };
	private readonly TextBox _key = new ()
		{
		Width = 240,
		UseSystemPasswordChar = true
		};
	private readonly ComboBox _suite = new ()
		{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 225
		};
	private readonly Button _connect = new ()
		{
		Text = "Connect",
		AutoSize = true
		};
	private readonly Button _discover = new ()
		{
		Text = "Discover",
		AutoSize = true
		};
	private readonly Button _runAll = new ()
		{
		Text = "Run all",
		AutoSize = true
		};
	private readonly Button _runSelected = new ()
		{
		Text = "Run selection",
		AutoSize = true
		};
	private readonly Button _cancel = new ()
		{
		Text = "Cancel",
		AutoSize = true
		};
	private readonly TreeView _tree = new ()
		{
		Dock = DockStyle.Fill,
		HideSelection = false
		};
	private readonly ListView _results = new ()
		{
		Dock = DockStyle.Fill,
		View = View.Details,
		FullRowSelect = true,
		HideSelection = false
		};
	private readonly TextBox _output = new ()
		{
		Dock = DockStyle.Fill,
		Multiline = true,
		ScrollBars = ScrollBars.Both,
		ReadOnly = true,
		WordWrap = false
		};
	private readonly TextBox _details = new ()
		{
		Dock = DockStyle.Fill,
		Multiline = true,
		ScrollBars = ScrollBars.Both,
		ReadOnly = true,
		WordWrap = false
		};
	private readonly ToolStripStatusLabel _status = new ()
		{
		Text = "Connect to the test host on your processor.",
		Spring = true,
		TextAlign = ContentAlignment.MiddleLeft
		};
	private readonly Dictionary<string, ListViewItem> _rows = new (StringComparer.Ordinal);
	private RemoteTestClient? _client;
	private string? _activeRequest;
	private string? _resultDirectory;
	private bool _connecting;
	private bool _recovering;
	private bool _incomplete;
	private readonly ConcurrentQueue<WireMessage> _progress = new ();
	public RunnerForm (Func<string, string, string, string, Task<ProcessorConnection>>? authenticate = null, string? preferencesPath = null, Func<Task<IReadOnlyList<DiscoveredPackage>>>? discoverPackages = null, string? windowPlacementPath = null)
		{
		_authenticate = authenticate ?? ProcessorAuthentication.AuthenticateAsync;
		_discoverPackages = discoverPackages;
		_rememberCredentials = authenticate == null;
		_windowPlacementPath = windowPlacementPath ?? (_rememberCredentials ? Path.Combine (RunnerSettingsStorage.DirectoryPath, "Runner.window.local.json") : null);
		LocationChanged += (_, _) => SaveWindowPlacement ();
		SizeChanged += (_, _) => SaveWindowPlacement ();
		Text = "Crestron Home NUnit Runner";
		using (var iconStream = typeof (RunnerForm).Assembly.GetManifestResourceStream ("CrestronHomeNUnit.Runner.Runner.ico")!)
			Icon = new Icon (iconStream);
		Size = new Size (1250, 850);
		MinimumSize = new Size (1000, 650);
		Font = new Font ("Segoe UI", 10);
		AutoScaleMode = AutoScaleMode.Dpi;
		var connection = new FlowLayoutPanel
			{
			Dock = DockStyle.Top,
			AutoSize = true,
			Padding = new Padding (8),
			WrapContents = true
			};
		connection.Controls.AddRange ([Label ("Processor"), _host, Label ("Port"), _port, Label ("SFTP user"), _user, Label ("Password"), _key, _connect]);
		var actions = new FlowLayoutPanel
			{
			Dock = DockStyle.Top,
			AutoSize = true,
			Padding = new Padding (8),
			WrapContents = true
			};
		var openResults = new Button
			{
			Text = "Open results folder",
			AutoSize = true
			};
		actions.Controls.AddRange ([_suite, _discover, _runAll, _runSelected, _cancel, openResults, _testInputs, _clearTestInputs, _inputStatus]);
		var split = new SplitContainer
			{
			Dock = DockStyle.Fill,
			Size = new Size (1200, 600),
			SplitterDistance = 340
			};
		split.Panel1.Controls.Add (_tree);
		var right = new SplitContainer
			{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Horizontal,
			Size = new Size (800, 600),
			SplitterDistance = 380
			};
		var tabs = new TabControl
			{
			Dock = DockStyle.Fill
			};
		var resultsTab = new TabPage ("Test results");
		var outputTab = new TabPage ("Live output");
		_results.Columns.Add ("Result", 95);
		_results.Columns.Add ("Test", 550);
		_results.Columns.Add ("Seconds", 90);
		resultsTab.Controls.Add (_results);
		outputTab.Controls.Add (_output);
		tabs.TabPages.AddRange ([resultsTab, outputTab]);
		right.Panel1.Controls.Add (tabs);
		right.Panel2.Controls.Add (_details);
		split.Panel2.Controls.Add (right);
		var status = new StatusStrip ();
		status.Items.Add (_status);
		Controls.Add (split);
		Controls.Add (actions);
		Controls.Add (connection);
		var packages = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding (8) };
		packages.Controls.AddRange ([_findPackages, _packages, _useAtNextRestart]);
		Controls.Add (packages);
		_findPackages.Click += async (_, _) => await FindPackagesAsync ();
		_packages.SelectedIndexChanged += async (_, _) => await SelectPackageAsync ();
		Controls.Add (status);
		_connect.Click += async (_, _) => await ConnectAsync ();
		_discover.Click += async (_, _) => await ExecuteAsync ("discover", false);
		_runAll.Click += async (_, _) => await ExecuteAsync ("run", false);
		_runSelected.Click += async (_, _) => await ExecuteAsync ("run", true);
		_tree.AfterSelect += (_, _) => { UpdateControls (); SaveSelections (); };
		_cancel.Click += async (_, _) => await CancelAsync ();
		_testInputs.Click += (_, _) => ConfigureTestInputs (false);
		_clearTestInputs.Click += (_, _) => ConfigureTestInputs (true);
		_suite.SelectedIndexChanged += (_, _) =>
		{
			_tree.Nodes.Clear ();
			RefreshInputStatus ();
			if (!_recovering)
				{
				_results.Items.Clear ();
				_rows.Clear ();
				}
			UpdateControls ();
			SaveSelections ();
		};
		_results.SelectedIndexChanged += (_, _) =>
		{
			if (_results.SelectedItems.Count != 0)
				{
				_details.Text = (string?)_results.SelectedItems[0].Tag ?? "";
				}
		};
		openResults.Click += (_, _) =>
		{
			string path = _resultDirectory ?? Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.MyDocuments), "CrestronHomeNUnit", "Results");
			Directory.CreateDirectory (path);
			Process.Start (new ProcessStartInfo (path) { UseShellExecute = true });
		};
		FormClosing += (_, _) => SaveSelections ();
		FormClosed += (_, _) => { _closing.Cancel (); _client?.Dispose (); };
		_useAtNextRestart.CheckedChanged += (_, _) => SaveSelections ();
		Shown += async (_, _) => await RestoreSelectionsAsync ();
		_host.TextChanged += (_, _) =>
			{
				string host = _host.Text.Trim ().ToLowerInvariant ();
				if (host == _credentialHost)
					return;
				ClearSuiteCatalog ();
				_credentialHost = host;
				_user.Clear ();
				_key.Clear ();
				RestoreCredentials ("host:" + host);
			};
		_port.ValueChanged += (_, _) => ClearSuiteCatalog ();
		LoadSettings ();
		LoadSelections (preferencesPath);
		RefreshInputStatus ();
		UpdateControls ();
		}

	private static Label Label (string text) => new ()
		{
		Text = text,
		AutoSize = true,
		Padding = new Padding (0, 5, 0, 0)
		};
	private string InputScope (string suite) => _processorId.Length == 0 ? suite : _processorId + "/" + suite;

	private void ConfigureTestInputs (bool clear)
		{
		if (_activeRequest != null || _suite.SelectedItem is not TestSuiteInfo suite)
			return;
		try
			{
			if (clear)
				RunnerTestInputs.Clear (InputScope (suite.Id));
			else
				RunnerTestInputs.Choose (this, InputScope (suite.Id));
			RefreshInputStatus ();
			_tree.Nodes.Clear ();
			UpdateControls ();
			_status.Text = "Inputs will be sent on the next discovery or run. Clear inputs also clears the processor copy on that next operation.";
			}
		catch (Exception exception) { MessageBox.Show (this, exception.Message, "Test inputs", MessageBoxButtons.OK, MessageBoxIcon.Error); }
		}
	private void RefreshInputStatus ()
		{
		try
			{
			_inputStatus.Text = _suite.SelectedItem is TestSuiteInfo suite ? RunnerTestInputs.Describe (InputScope (suite.Id)) : "Connect to load test suites";
			}
		catch (Exception) { _inputStatus.Text = "Cannot read input settings"; }
		}

	private void LoadSettings ()
		{
		try
			{
			string path = RunnerSettingsStorage.GetPath ("Runner.local.json");
			if (!File.Exists (path))
				return;
			using var stream = File.OpenRead (path);
			var settings = (RunnerSettings)new DataContractJsonSerializer (typeof (RunnerSettings)).ReadObject (stream)!;
			_host.Text = settings.Host;
			_port.Value = Math.Max (1, settings.Port);
			_user.Text = settings.User;
			_key.Text = settings.Password;
			RestoreCredentials ("host:" + settings.Host.ToLowerInvariant ());
			}
		catch (Exception exception)
			{
			_status.Text = "Cannot load runner settings: " + exception.Message;
			}
		}

	private async Task<IReadOnlyDictionary<string, string>> FindProcessorNamesAsync ()
		{
		try
			{
			return await Task.Run (() => CrestronDiscovery.FindNamesAsync (_closing.Token));
			}
		catch (Exception) { return new Dictionary<string, string> (); }
		}

	private async Task FindPackagesAsync ()
		{
		if (_activeRequest != null || _connecting || _finding)
			return;
		DiscoveredPackage? connectedPackage = _client != null && _packages.SelectedItem is DiscoveredPackage activePackage &&
			activePackage.Host == _host.Text.Trim () && activePackage.Port == (int)_port.Value ? activePackage : null;
		bool endpointChanged = false;
		_finding = true;
		UpdateControls ();
		_status.Text = "Finding test packages on the local network…";
		try
			{
			Task<IReadOnlyDictionary<string, string>> nativeNames = _discoverPackages == null ? FindProcessorNamesAsync () : Task.FromResult<IReadOnlyDictionary<string, string>> (new Dictionary<string, string> ());
			IReadOnlyList<DiscoveredPackage> packages = _discoverPackages == null ? await Task.Run (() => PackageDiscovery.FindAsync (_closing.Token)) : await _discoverPackages ();
			IReadOnlyDictionary<string, string> names = await nativeNames;
			foreach (DiscoveredPackage package in packages)
				if (names.TryGetValue (package.Host, out string? name))
					package.ProcessorName = name;
			packages = packages.OrderBy (p => p.ProcessorName).ThenBy (p => p.ProcessorId).ThenBy (p => p.Name).ToArray ();
			if (IsDisposed)
				return;
			_packages.Items.Clear ();
			_packages.Items.AddRange (packages.Cast<object> ().ToArray ());
			_packages.SelectedIndex = -1;
			if (connectedPackage != null)
				{
				DiscoveredPackage[] matches = packages.Where (package => SamePackage (package, connectedPackage)).ToArray ();
				DiscoveredPackage? current = matches.Length == 1 ? matches[0] : null;
				endpointChanged = current != null && (current.Host != connectedPackage.Host || current.Port != connectedPackage.Port);
				// A discovery timeout must not remove the package with the active connection.
				if (current == null)
					_packages.Items.Add (connectedPackage);
				_packages.SelectedItem = current ?? connectedPackage;
				}
			_status.Text = packages.Count == 0 ? "No packages found. Check the Home tile, or enter its IP and current TCP port manually." : packages.Count + " packages found. Select a package to connect.";
			}
		catch (OperationCanceledException) { }
		catch (Exception exception) { if (!IsDisposed) _status.Text = "Discovery failed: " + exception.Message; }
		finally
			{
			_finding = false;
			if (!IsDisposed)
				UpdateControls ();
			}
		if (endpointChanged && !IsDisposed)
			{
			RemoteTestClient? previous = _client;
			_client = null;
			previous?.Dispose ();
			await RecoverPackageAsync ();
			}
		}

	private async Task SelectPackageAsync ()
		{
		if (_restoringSelections || _finding || _connecting || _activeRequest != null || _packages.SelectedItem is not DiscoveredPackage package)
			return;
		if (_client != null)
			{
			if (package.ProcessorId == _processorId && package.Host == _host.Text.Trim () && package.Port == (int)_port.Value)
				return;
			RemoteTestClient previous = _client;
			_client = null;
			previous.Dispose ();
			}
		SelectPackage ();
		_details.Clear ();
		_output.Clear ();
		_resultDirectory = null;
		if (string.IsNullOrWhiteSpace (_user.Text) || _key.Text.Length == 0)
			{
			_status.Text = "Enter this processor's credentials, then connect.";
			return;
			}
		await ConnectAsync ();
		}

	private void SelectPackage ()
		{
		if (_packages.SelectedItem is not DiscoveredPackage package)
			return;
		ClearSuiteCatalog ();
		string oldHost = _host.Text.Trim ();
		_host.Text = package.Host;
		_port.Value = package.Port;
		if (oldHost != package.Host)
			{
			_user.Clear ();
			_key.Clear ();
			}
		RestoreCredentials ("host:" + package.Host.ToLowerInvariant ());
		RestoreCredentials (package.ProcessorId);
		}

	private void ClearSuiteCatalog ()
		{
		if (_client != null || _connecting)
			return;
		_suite.Items.Clear ();
		_processorId = "";
		RefreshInputStatus ();
		UpdateControls ();
		}

	private void RestoreCredentials (string identity)
		{
		try
			{
			string user = ProcessorKeys.Get ("user:" + identity);
			if (user.Length != 0)
				{
				_user.Text = user;
				_key.Text = ProcessorKeys.Get ("password:" + identity);
				}
			}
		catch (Exception exception) { _status.Text = "Enter processor credentials. Saved credentials could not be read: " + exception.Message; }
		}
	private async Task ConnectAsync ()
		{
		if (_client != null)
			{
			RemoteTestClient previous = _client;
			_client = null;
			previous.Dispose ();
			UpdateControls ();
			return;
			}

		if (_rememberCredentials && (string.IsNullOrWhiteSpace (_user.Text) || _key.Text.Length == 0))
			{
			using var credentials = new ProcessorCredentialsDialog (_host.Text.Trim (), _user.Text);
			if (credentials.ShowDialog (this) != DialogResult.OK)
				return;
			_user.Text = credentials.User;
			_key.Text = credentials.Password;
			}
		string? previousSuite = (_suite.SelectedItem as TestSuiteInfo)?.Id;
		ClearSuiteCatalog ();
		_connecting = true;
		UpdateControls ();
		try
			{
			await RefreshSelectedEndpointAsync ();
			if (IsDisposed)
				return;
			_status.Text = "Signing in to the processor…";
			ProcessorConnection processor = await _authenticate (_host.Text.Trim (), _user.Text.Trim (), _key.Text,
				_packages.SelectedItem is DiscoveredPackage selectedPackage && selectedPackage.Host == _host.Text.Trim () ? selectedPackage.ProcessorId : "");
			if (IsDisposed)
				return;
			if (_packages.SelectedItem is DiscoveredPackage advertised && advertised.Host == _host.Text.Trim () && advertised.ProcessorId != processor.Id)
				throw new InvalidOperationException ("The discovered identity differs from the processor. Find packages again.");
			_status.Text = "Connecting to test package…";
			RemoteTestClient client = await RemoteTestClient.ConnectAsync (_host.Text.Trim (), (int)_port.Value, processor.Token);
			if (IsDisposed)
				{
				client.Dispose ();
				return;
				}

			if (client.Suites.Count == 0)
				{
				client.Dispose ();
				throw new InvalidOperationException ("The package did not advertise any test suites. Update the processor package and reconnect.");
				}

			_client = client;
			_processorId = processor.Id;
			if (_rememberCredentials)
				{
				try
					{
					ProcessorKeys.SaveCredentials (processor.Id, _host.Text, _user.Text.Trim (), _key.Text);
					}
				catch (Exception exception) { _output.AppendText ("Could not remember processor credentials: " + exception.Message + Environment.NewLine); }
				}
			_suite.Items.Clear ();
			_suite.Items.AddRange (client.Suites.Cast<object> ().ToArray ());
			_suite.SelectedItem = client.Suites.FirstOrDefault (suite => suite.Id == previousSuite) ?? client.Suites[0];
			client.Progress += message =>
			{
				_progress.Enqueue (message);
				OnUi (DrainProgress);
			};
			DiscoveredPackage? connectionPackage = _packages.SelectedItem is DiscoveredPackage endpoint &&
				endpoint.Host == _host.Text.Trim () && endpoint.Port == (int)_port.Value ? endpoint : null;
			client.Disconnected += reason => OnUi (() =>
			{
				if (_client == client)
					{
					_client = null;
					if (_activeRequest == null && !_incomplete)
						{
						_status.Text = reason;
						}

					UpdateControls ();
					if (connectionPackage != null && _packages.SelectedItem is DiscoveredPackage selection && SamePackage (connectionPackage, selection))
						_ = RecoverPackageAsync ();
					}
			});
			_status.Text = "Connected. Select a suite and discover its tests.";
			}
		catch (Exception exception)
			{
			if (exception is Renci.SshNet.Common.SshAuthenticationException)
				_key.Clear ();
			_status.Text = "Connection failed: " + exception.Message;
			}
		finally
			{
			_connecting = false;
			UpdateControls ();
			SaveSelections ();
			}
		}

	private async Task ExecuteAsync (string kind, bool selected)
		{
		if (_client == null || _activeRequest != null || _connecting || _finding || _suite.SelectedItem is not TestSuiteInfo suite)
			{
			return;
			}

		var request = new WireMessage
			{
			Kind = kind,
			Suite = suite.Id,
			EnableLiveTests = suite.ManualOnly,
			RequestId = Guid.NewGuid ().ToString ("N")
			};
		if (selected)
			{
			if (_tree.SelectedNode == null)
				{
				_status.Text = "Select a test or fixture in the tree first.";
				return;
				}

			CollectTests (_tree.SelectedNode, request.TestNames);
			if (request.TestNames.Count == 0)
				{
				_status.Text = "The selection contains no tests.";
				return;
				}
			}

		_activeRequest = request.RequestId;
		_incomplete = false;
		_resultDirectory = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.MyDocuments), "CrestronHomeNUnit", "Results", DateTime.Now.ToString ("yyyyMMdd-HHmmss") + "-" + request.Suite + "-" + request.RequestId.Substring (0, 8));
		string diagnostic = "";
		_results.Items.Clear ();
		_rows.Clear ();
		_output.Clear ();
		_details.Clear ();
		UpdateControls ();
		try
			{
			request.TestInputs = RunnerTestInputs.Files (InputScope (request.Suite));
			WireMessage reply = await _client.SendAsync (request);
			DrainProgress ();
			if (reply.Kind == "error" || string.IsNullOrEmpty (reply.Xml))
				{
				diagnostic = reply.Text;
				MarkIncomplete (diagnostic);
				}
			else
				{
				XmlDocument document = ParseXml (reply.Xml);
				if (kind == "discover")
					{
					PopulateTree (document);
					}
				else
					{
					foreach (XmlElement test in document.SelectNodes ("//test-case")!)
						{
						SetResult (test, false);
						}
					}

				_status.Text = reply.Text;
				diagnostic = reply.Text;
				try
					{
					Directory.CreateDirectory (_resultDirectory);
					File.WriteAllText (Path.Combine (_resultDirectory, kind == "discover" ? "TestTree.xml" : "TestResult.xml"), reply.Xml);
					}
				catch (Exception exception)
					{
					_status.Text += " Results could not be saved: " + exception.Message;
					diagnostic += Environment.NewLine + exception;
					}
				}
			}
		catch (Exception exception)
			{
			diagnostic = exception.ToString ();
			MarkIncomplete (exception.Message);
			}
		finally
			{
			try
				{
				Directory.CreateDirectory (_resultDirectory);
				File.WriteAllText (Path.Combine (_resultDirectory, "LiveOutput.txt"), _output.Text);
				File.WriteAllText (Path.Combine (_resultDirectory, "RunStatus.txt"), "Request: " + request.RequestId + Environment.NewLine + "Operation: " + kind + Environment.NewLine + "Suite: " + request.Suite + Environment.NewLine + "Recorded UTC: " + DateTime.UtcNow.ToString ("O") + Environment.NewLine + _status.Text + Environment.NewLine + diagnostic + Environment.NewLine + string.Join (Environment.NewLine, _rows.Values.Select (row => row.SubItems[0].Text + "\t" + row.SubItems[1].Text)));
				}
			catch (Exception exception)
				{
				_status.Text += " Diagnostic files could not be saved: " + exception.Message;
				}

			_activeRequest = null;
			UpdateControls ();
			}
		}

	private void MarkIncomplete (string reason)
		{
		DrainProgress ();
		_incomplete = true;
		string[] activeTests = _rows.Values.Where (row => row.SubItems[0].Text == "Running").Select (row => row.SubItems[1].Text).ToArray ();
		string detail = "Incomplete: " + reason + Environment.NewLine + "No final result was received. The test outcome is unknown.";
		foreach (ListViewItem row in _rows.Values.Where (row => row.SubItems[0].Text == "Running"))
			{
			row.SubItems[0].Text = "Incomplete";
			row.ForeColor = Color.DarkOrange;
			row.Tag = row.SubItems[1].Text + Environment.NewLine + detail;
			}

		_status.Text = "Incomplete: " + reason;
		_details.Text = detail + (activeTests.Length == 0 ? "" : Environment.NewLine + "Last active tests:" + Environment.NewLine + string.Join (Environment.NewLine, activeTests));
		}

	private async Task CancelAsync ()
		{
		if (_client == null || _activeRequest == null)
			{
			return;
			}

		try
			{
			WireMessage reply = await _client.SendAsync (new WireMessage { Kind = "cancel", TargetId = _activeRequest });
			_status.Text = reply.Text;
			}
		catch (Exception exception)
			{
			_status.Text = exception.Message;
			}
		}

	private void DrainProgress ()
		{
		while (_progress.TryDequeue (out WireMessage? message))
			{
			OnProgress (message);
			}
		}

	private void OnProgress (WireMessage message)
		{
		if (message.RequestId != _activeRequest)
			{
			return;
			}

		if (message.Kind == "started")
			{
			_status.Text = message.Text;
			}
		else if (message.Kind == "test-output")
			{
			AppendOutput (message.Text);
			}
		else if (message.Kind is "test-start" or "test-finish")
			{
			XmlElement test = ParseXml (message.Xml).DocumentElement!;
			SetResult (test, message.Kind == "test-start");
			if (message.Kind == "test-finish")
				{
				AppendOutput (test.GetAttribute ("result") + " " + test.GetAttribute ("fullname") + Environment.NewLine);
				}
			}
		}

	private void SetResult (XmlElement test, bool running)
		{
		string name = test.GetAttribute ("fullname");
		string key = test.GetAttribute ("id");
		if (key.Length == 0)
			{
			key = name;
			}

		if (!_rows.TryGetValue (key, out ListViewItem? row))
			{
			row = new ListViewItem (["", name, ""]);
			_rows.Add (key, row);
			_results.Items.Add (row);
			}

		string result = running ? "Running" : test.GetAttribute ("result");
		row.SubItems[0].Text = result;
		row.SubItems[2].Text = test.GetAttribute ("duration");
		row.ForeColor = result switch
			{
				"Failed" => Color.Firebrick,
				"Passed" => Color.DarkGreen,
				"Warning" => Color.DarkOrange,
				_ => SystemColors.WindowText
				};
		row.Tag = name + Environment.NewLine + (test.SelectSingleNode ("failure")?.InnerText ?? test.SelectSingleNode ("reason")?.InnerText ?? "") + Environment.NewLine + (test.SelectSingleNode ("output")?.InnerText ?? "");
		}

	private void PopulateTree (XmlDocument document)
		{
		_tree.BeginUpdate ();
		try
			{
			_tree.Nodes.Clear ();
			RefreshInputStatus ();
			foreach (XmlElement suite in document.SelectNodes ("/test-run/test-suite")!)
				{
				AddNode (_tree.Nodes, suite);
				}

			if (_tree.Nodes.Count > 0)
				{
				_tree.Nodes[0].Expand ();
				}
			}
		finally
			{
			_tree.EndUpdate ();
			}
		}

	private static void AddNode (TreeNodeCollection nodes, XmlElement element)
		{
		var node = new TreeNode (element.GetAttribute ("name"))
			{
			Tag = element.Name == "test-case" ? element.GetAttribute ("fullname") : null
			};
		nodes.Add (node);
		foreach (XmlElement child in element.ChildNodes.OfType<XmlElement> ().Where (child => child.Name is "test-suite" or "test-case"))
			{
			AddNode (node.Nodes, child);
			}
		}

	private static void CollectTests (TreeNode node, List<string> names)
		{
		if (node.Tag is string name)
			{
			names.Add (name);
			}

		foreach (TreeNode child in node.Nodes)
			{
			CollectTests (child, names);
			}
		}

	private static XmlDocument ParseXml (string xml)
		{
		var document = new XmlDocument
			{
			XmlResolver = null
			};
		using var reader = XmlReader.Create (new StringReader (xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MessageStream.MaximumFrameBytes });
		document.Load (reader);
		return document;
		}

	private void AppendOutput (string text)
		{
		if (_output.TextLength > 1000000)
			{
			_output.Text = _output.Text.Substring (_output.TextLength - 500000);
			}
		_output.AppendText (text);
		}

	private void OnUi (Action action)
		{
		if (IsDisposed || Disposing || !IsHandleCreated)
			{
			return;
			}

		try
			{
			BeginInvoke (new Action (() =>
			{
				if (!IsDisposed)
					{
					action ();
					}
			}));
			}
		catch (InvalidOperationException)
			{
			}
		}

	private void UpdateControls ()
		{
		bool connected = _client != null;
		bool idle = _activeRequest == null;
		_connect.Text = connected ? "Disconnect" : "Connect";
		_connect.Enabled = !_connecting && !_finding && !_restoringSelections && !_recovering;
		_findPackages.Enabled = _packages.Enabled = idle && !_connecting && !_finding && !_restoringSelections && !_recovering;
		_host.Enabled = _port.Enabled = _user.Enabled = _key.Enabled = !connected && !_connecting && !_finding && !_restoringSelections && !_recovering;
		bool suiteReady = connected && idle && !_connecting && !_finding && !_restoringSelections && !_recovering && _suite.SelectedItem is TestSuiteInfo;
		_suite.Enabled = suiteReady;
		_testInputs.Enabled = _clearTestInputs.Enabled = suiteReady;
		_discover.Enabled = _runAll.Enabled = suiteReady;
		_runSelected.Enabled = suiteReady && _tree.SelectedNode != null;
		_cancel.Enabled = connected && !idle;
		}

	[DataContract]
	private sealed class RunnerSettings
		{
		[DataMember]
		public string Host { get; set; } = "";

		[DataMember]
		public int Port { get; set; } = RemoteTestServer.DefaultPort;

		[DataMember]
		public string User { get; set; } = "";

		[DataMember]
		public string Password { get; set; } = "";
		}
	}