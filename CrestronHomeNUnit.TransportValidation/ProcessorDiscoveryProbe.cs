// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml;

using Makaretu.Dns;

internal static class ProcessorDiscoveryProbe
	{
	public static async Task RunAsync (string settingsPath, params string[] suffixes)
		{
		var settings = new XmlDocument { XmlResolver = null };
		settings.Load (settingsPath);
		IPAddress target = IPAddress.Parse (settings.SelectSingleNode ("/Project/PropertyGroup/CrestronHomeIP")!.InnerText);
		string prefix = string.Join (".", target.ToString ().Split ('.').Take (3)) + ".";
		IPAddress[] targets = suffixes.Length == 0 ? [target] : suffixes.Select (s => IPAddress.Parse (s.Contains (".") ? s : prefix + s)).ToArray ();
		var services = new ConcurrentDictionary<string, byte> ();
		var responses = new ConcurrentDictionary<string, byte> ();
		using var mdns = new MulticastService { UseIpv6 = false, IgnoreDuplicateMessages = false };
		mdns.AnswerReceived += (_, args) =>
			{
				foreach (ResourceRecord record in args.Message.Answers.Concat (args.Message.AdditionalRecords))
					{
					if (record is PTRRecord service && service.Name == new DomainName ("_services._dns-sd._udp.local") && services.Count < 100)
						services.TryAdd (service.DomainName.ToString (), 0);
					if (targets.Contains (args.RemoteEndPoint.Address))
						{
						string value = record is PTRRecord pointer ? pointer.DomainName.ToString () : record is SRVRecord endpoint ? endpoint.Target + ":" + endpoint.Port : record is TXTRecord properties ? string.Join (",", properties.Strings.Select (s => s.Split ('=')[0])) : "";
						responses.TryAdd (args.RemoteEndPoint.Address + " " + record.Type + " " + record.Name + " " + value, 0);
						}
					}
			};
		mdns.Start ();
		for (int attempt = 0; attempt < 6; attempt++)
			{
			mdns.SendQuery ("_services._dns-sd._udp.local", type: DnsType.PTR);
			foreach (string service in services.Keys)
				mdns.SendQuery (service, type: DnsType.PTR);
			await Task.Delay (1000).ConfigureAwait (false);
			}
		Console.WriteLine ("Known processor mDNS records (TXT field names only):");
		foreach (string response in responses.Keys.OrderBy (s => s))
			Console.WriteLine (response);
		Console.WriteLine ("Record count: " + responses.Count);
		}
	}