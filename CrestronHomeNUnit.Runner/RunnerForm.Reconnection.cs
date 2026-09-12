// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;

using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Runner;

public sealed partial class RunnerForm
	{
	private static bool SamePackage (DiscoveredPackage first, DiscoveredPackage second) =>
		first.ProcessorId == second.ProcessorId && first.Name == second.Name;

	private async Task RefreshSelectedEndpointAsync ()
		{
		if (_packages.SelectedItem is not DiscoveredPackage selected || (!_recovering && (selected.Host != _host.Text.Trim () || selected.Port != (int)_port.Value)))
			return;
		_status.Text = "Finding the package's current address and port…";
		var packages = _discoverPackages == null ? await Task.Run (() => PackageDiscovery.FindAsync (_closing.Token)) : await _discoverPackages ();
		if (IsDisposed)
			return;
		DiscoveredPackage[] matches = packages.Where (package => SamePackage (package, selected)).ToArray ();
		if (matches.Length != 1)
			throw new InvalidOperationException (matches.Length == 0
				? "The selected package is not advertising yet. It may still be restarting; connect again when its Home tile is ready."
				: "More than one package matches this processor and package name. Find packages and select the intended instance.");
		DiscoveredPackage current = matches[0];
		if (current.ProcessorName.Length == 0)
			current.ProcessorName = selected.ProcessorName;
		// This is the same processor identity, even if DHCP has changed its address.
		string user = _user.Text;
		string password = _key.Text;
		int index = _packages.SelectedIndex;
		_packages.Items[index] = current;
		_packages.SelectedIndex = index;
		_host.Text = current.Host;
		_port.Value = current.Port;
		_user.Text = user;
		_key.Text = password;
		}

	private async Task RecoverPackageAsync ()
		{
		if (_recovering || IsDisposed || _packages.SelectedItem is not DiscoveredPackage selected ||
			string.IsNullOrWhiteSpace (_user.Text) || _key.Text.Length == 0)
			return;
		_recovering = true;
		UpdateControls ();
		try
			{
			// Finish recording any interrupted run before refreshing the connection.
			while (_activeRequest != null || _connecting || _finding || _restoringSelections)
				{
				await Task.Delay (100, _closing.Token);
				if (IsDisposed)
					return;
				}
			for (int attempt = 0; attempt < 3; attempt++)
				{
				if (attempt != 0)
					await Task.Delay (5000, _closing.Token);
				if (IsDisposed || _client != null || _packages.SelectedItem is not DiscoveredPackage current || !SamePackage (current, selected))
					return;
				await ConnectAsync ();
				if (IsDisposed)
					return;
				if (_client != null)
					{
					_status.Text = _incomplete
						? "Incomplete: the previous run was interrupted. Reconnected on port " + _port.Value + "; no tests were rerun."
						: "Reconnected on port " + _port.Value + ". No tests were rerun.";
					return;
					}
				if (_key.Text.Length == 0)
					return;
				}
			}
		catch (OperationCanceledException) { }
		catch (Exception exception) { if (!IsDisposed) _status.Text = "Reconnect failed: " + exception.Message; }
		finally
			{
			_recovering = false;
			if (!IsDisposed)
				UpdateControls ();
			}
		}
	}