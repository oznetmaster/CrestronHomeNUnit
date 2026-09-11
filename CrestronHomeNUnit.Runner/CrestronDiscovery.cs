// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrestronHomeNUnit.Runner;

public static class CrestronDiscovery
	{
	private const int DISCOVERY_PORT = 41794;
	public static async Task<IReadOnlyDictionary<string, string>> FindNamesAsync (CancellationToken cancellationToken)
		{
		var names = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
		using var socket = new UdpClient (AddressFamily.InterNetwork);
		socket.ExclusiveAddressUse = false;
		socket.Client.SetSocketOption (SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		socket.EnableBroadcast = true;
		socket.Client.Bind (new IPEndPoint (IPAddress.Any, DISCOVERY_PORT));
		// Native Crestron discovery: query, fixed payload length, workstation identity.
		var query = new byte[266];
		query[0] = 0x14;
		query[4] = 1;
		query[5] = 4;
		query[7] = 3;
		byte[] workstation = Encoding.ASCII.GetBytes (Dns.GetHostName ());
		Array.Copy (workstation, 0, query, 10, Math.Min (workstation.Length, 255));
		IPAddress[] broadcasts = NetworkInterface.GetAllNetworkInterfaces ()
			.Where (n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany (n => n.GetIPProperties ().UnicastAddresses)
			.Where (a => a.Address.AddressFamily == AddressFamily.InterNetwork)
			.Select (a => new IPAddress (a.Address.GetAddressBytes ().Zip (a.IPv4Mask.GetAddressBytes (), (address, mask) => (byte)(address | ~mask)).ToArray ()))
			.Distinct ().ToArray ();
		DateTime deadline = DateTime.UtcNow.AddSeconds (5);
		DateTime nextQuery = DateTime.MinValue;
		while (DateTime.UtcNow < deadline)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (DateTime.UtcNow >= nextQuery)
				{
				foreach (IPAddress broadcast in broadcasts)
					{
					try
						{
						await socket.SendAsync (query, query.Length, new IPEndPoint (broadcast, DISCOVERY_PORT)).ConfigureAwait (false);
						}
					catch (SocketException) { }
					}
				nextQuery = DateTime.UtcNow.AddSeconds (2);
				}
			while (socket.Available > 0 && DateTime.UtcNow < deadline)
				{
				UdpReceiveResult response = await socket.ReceiveAsync ().ConfigureAwait (false);
				string? name = ParseName (response.Buffer);
				if (name != null && response.RemoteEndPoint.Port == DISCOVERY_PORT && names.Count < 1024)
					names[response.RemoteEndPoint.Address.ToString ()] = name;
				}
			await Task.Delay (40, cancellationToken).ConfigureAwait (false);
			}
		return names;
		}

	public static string? ParseName (byte[] packet)
		{
		if (packet.Length < 266 || packet.Length > 4096 || packet[0] != 0x15 || packet[1] != 0 || packet[2] != 0 || packet[3] != 0)
			return null;
		int end = Array.IndexOf (packet, (byte)0, 10, 256);
		if (end < 0)
			end = 266;
		string name = Encoding.UTF8.GetString (packet, 10, end - 10).Trim ();
		return name.Length == 0 || name.Any (char.IsControl) ? null : name;
		}
	}