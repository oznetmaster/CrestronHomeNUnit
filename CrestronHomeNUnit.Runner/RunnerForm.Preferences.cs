// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Runner;

public sealed partial class RunnerForm
	{
	private readonly CheckBox _useAtNextRestart = new () { Text = "Use at next restart", AutoSize = true, Padding = new Padding (0, 5, 0, 0) };
	private string? _preferencesPath;
	private SavedSelections? _savedSelections;
	private bool _restoringSelections;

	private void LoadSelections (string? path)
		{
		_preferencesPath = path ?? (_rememberCredentials ? Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeNUnit", "Runner.selections.local.json") : null);
		if (_preferencesPath == null || !File.Exists (_preferencesPath))
			return;
		try
			{
			if (new FileInfo (_preferencesPath).Length > 65536)
				throw new InvalidDataException ("Saved selections are too large.");
			using var stream = File.OpenRead (_preferencesPath);
			_savedSelections = (SavedSelections?)new DataContractJsonSerializer (typeof (SavedSelections)).ReadObject (stream);
			_restoringSelections = true;
			_useAtNextRestart.Checked = _savedSelections != null;
			}
		catch (Exception exception) { _status.Text = "Cannot restore saved selections: " + exception.Message; }
		finally { _restoringSelections = false; }
		}

	private void SaveSelections ()
		{
		if (_preferencesPath == null || _restoringSelections || _finding || _connecting)
			return;
		try
			{
			if (!_useAtNextRestart.Checked)
				{
				if (File.Exists (_preferencesPath))
					File.Delete (_preferencesPath);
				_savedSelections = null;
				return;
				}
			DiscoveredPackage? package = _packages.SelectedItem as DiscoveredPackage;
			if (package?.Host != _host.Text.Trim () || package.Port != (int)_port.Value)
				package = null;
			// Keep the last complete selection when a saved package is temporarily unavailable.
			if (_client == null && _savedSelections != null && (package == null || (package.ProcessorId == _savedSelections.ProcessorId && package.Name == _savedSelections.PackageName)))
				return;
			var selections = new SavedSelections
				{
				Host = _host.Text.Trim (),
				Port = (int)_port.Value,
				ProcessorId = package?.ProcessorId ?? _processorId,
				PackageName = package?.Name ?? "",
				SuiteId = (_suite.SelectedItem as TestSuiteInfo)?.Id ?? "",
				TestName = _tree.SelectedNode?.Tag as string ?? "",
				NodePath = _tree.SelectedNode?.FullPath ?? ""
				};
			Directory.CreateDirectory (Path.GetDirectoryName (_preferencesPath)!);
			string temporary = _preferencesPath + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
			try
				{
				using (var stream = File.Create (temporary))
					new DataContractJsonSerializer (typeof (SavedSelections)).WriteObject (stream, selections);
				if (File.Exists (_preferencesPath))
					File.Replace (temporary, _preferencesPath, null);
				else
					File.Move (temporary, _preferencesPath);
				_savedSelections = selections;
				}
			finally { if (File.Exists (temporary)) File.Delete (temporary); }
			}
		catch (Exception exception) { _status.Text = "Cannot save selections: " + exception.Message; }
		}

	private async Task RestoreSelectionsAsync ()
		{
		SavedSelections? saved = _savedSelections;
		if (saved == null || !_useAtNextRestart.Checked)
			return;
		_restoringSelections = true;
		try
			{
			if (saved.PackageName.Length != 0)
				{
				await FindPackagesAsync ();
				if (IsDisposed)
					return;
				DiscoveredPackage[] matches = _packages.Items.Cast<DiscoveredPackage> ().Where (package => package.ProcessorId == saved.ProcessorId && package.Name == saved.PackageName).ToArray ();
				if (matches.Length != 1)
					{
					_status.Text = matches.Length == 0 ? "Saved package was not found. Select a package to reconnect." : "More than one package matches the saved selection. Select the intended package.";
					return;
					}
				_packages.SelectedItem = matches[0];
				SelectPackage ();
				}
			else
				{
				_host.Text = saved.Host;
				_port.Value = Math.Max (1, Math.Min (65535, saved.Port));
				RestoreCredentials (saved.ProcessorId);
				}
			if (string.IsNullOrWhiteSpace (_user.Text) || _key.Text.Length == 0)
				{
				_status.Text = "Selections restored. Enter processor credentials, then connect.";
				return;
				}
			await ConnectAsync ();
			if (_client == null || IsDisposed)
				return;
			TestSuiteInfo? suite = _suite.Items.Cast<TestSuiteInfo> ().FirstOrDefault (item => item.Id == saved.SuiteId);
			if (suite == null)
				{
				_status.Text = "Connected. The saved suite is unavailable; select a suite.";
				return;
				}
			_suite.SelectedItem = suite;
			if (saved.NodePath.Length != 0 || saved.TestName.Length != 0)
				{
				await ExecuteAsync ("discover", false);
				if (_client == null || IsDisposed)
					return;
				TreeNode? node = FindSavedNode (_tree.Nodes, saved);
				if (node == null)
					{
					_status.Text = "Connected. The saved test or fixture was not found; select a test.";
					return;
					}
				_tree.SelectedNode = node;
				node.EnsureVisible ();
				}
			_status.Text = "Selections restored. No tests have been run.";
			}
		catch (Exception exception) { if (!IsDisposed) _status.Text = "Cannot restore selections: " + exception.Message; }
		finally
			{
			_restoringSelections = false;
			if (!IsDisposed)
				UpdateControls ();
			}
		}

	private static TreeNode? FindSavedNode (TreeNodeCollection nodes, SavedSelections saved)
		{
		foreach (TreeNode node in nodes)
			{
			if (saved.TestName.Length != 0 ? (node.Tag as string) == saved.TestName : node.FullPath == saved.NodePath)
				return node;
			TreeNode? child = FindSavedNode (node.Nodes, saved);
			if (child != null)
				return child;
			}
		return null;
		}

	[DataContract]
	private sealed class SavedSelections
		{
		[DataMember] public string Host { get; set; } = "";
		[DataMember]
		public int Port
			{
			get; set;
			}
		[DataMember] public string ProcessorId { get; set; } = "";
		[DataMember] public string PackageName { get; set; } = "";
		[DataMember] public string SuiteId { get; set; } = "";
		[DataMember] public string TestName { get; set; } = "";
		[DataMember] public string NodePath { get; set; } = "";
		}
	}