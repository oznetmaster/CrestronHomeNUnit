// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Makaretu.Dns;

namespace CrestronHomeNUnit.Transport;

public sealed class DiscoveredPackage
	{
	public string Instance { get; set; } = "";
	public string ProcessorId { get; set; } = "";
	public string ProcessorName { get; set; } = "";
	public string Name { get; set; } = "";
	public string Host { get; set; } = "";
	public int Port
		{
		get; set;
		}
	public override string ToString () => ProcessorName + " (" + Host + ") — " + Name + " :" + Port;
	}

public sealed class PackageAdvertisement : IDisposable
	{
	public const string SERVICE_TYPE = "_crestron-nunit._tcp";
	private readonly object _gate = new ();
	private readonly MulticastService _mdns = new () { UseIpv6 = false, IgnoreDuplicateMessages = false };
	private readonly ServiceDiscovery _discovery;
	private readonly ServiceProfile _profile;
	private readonly Timer _timer;
	private bool _disposed;
	public PackageAdvertisement (string name, string processorId, string processorName, int port, Action<string> reportError)
		{
		if (port < 1 || port > 65535)
			throw new ArgumentOutOfRangeException (nameof (port));
		_profile = new ServiceProfile (Guid.NewGuid ().ToString ("N"), SERVICE_TYPE, (ushort)port,
			MulticastService.GetIPAddresses ().Where (a => a.AddressFamily == AddressFamily.InterNetwork));
		_profile.AddProperty ("name", Clean (name));
		_profile.AddProperty ("processor", processorId);
		_profile.AddProperty ("host", Clean (processorName));
		_profile.AddProperty ("protocol", "2");
		foreach (ResourceRecord record in _profile.Resources)
			record.TTL = TimeSpan.FromSeconds (120);
		_discovery = new ServiceDiscovery (_mdns);
		try
			{
			_mdns.Start ();
			_discovery.Advertise (_profile);
			_timer = new Timer (_ =>
				{
					lock (_gate)
						{
						if (_disposed)
							return;
						try
							{
							var message = new Message ();
							message.Answers.Add (new PTRRecord { Name = _profile.QualifiedServiceName, DomainName = _profile.FullyQualifiedName, TTL = TimeSpan.FromSeconds (120) });
							message.AdditionalRecords.AddRange (_profile.Resources);
							_mdns.SendAnswer (message, false);
							}
						catch (Exception exception) { reportError (exception.Message); }
						}
				}, null, TimeSpan.Zero, TimeSpan.FromSeconds (30));
			}
		catch
			{
			_discovery.Dispose ();
			_mdns.Dispose ();
			throw;
			}
		}

	private static string Clean (string value) => new (value.Where (c => !char.IsControl (c)).Take (60).ToArray ());

	public void Dispose ()
		{
		lock (_gate)
			{
			if (_disposed)
				return;
			_disposed = true;
			_timer.Dispose ();
			try
				{
				_discovery.Unadvertise (_profile);
				}
			finally
				{
				_discovery.Dispose ();
				_mdns.Dispose ();
				}
			}
		}
	}

// Cache records across packets: DNS-SD may return PTR, SRV, TXT and A separately.
public sealed class PackageDiscoveryCache
	{
	private readonly object _gate = new ();
	private readonly List<(ResourceRecord Record, DateTime Expires)> _records = [];
	private readonly Dictionary<DomainName, IPAddress> _sources = new ();
	public void Add (Message message, IPAddress? source = null)
		{
		lock (_gate)
			{
			DateTime now = DateTime.UtcNow;
			if (source != null)
				foreach (SRVRecord service in message.Answers.Concat (message.AdditionalRecords).OfType<SRVRecord> ())
					if (_sources.Count < 4096)
						_sources[service.Name] = source;
			_records.RemoveAll (r => r.Expires <= now);
			foreach (ResourceRecord record in message.Answers.Concat (message.AdditionalRecords))
				{
				if (record is not PTRRecord && record is not SRVRecord && record is not TXTRecord && record is not ARecord)
					continue;
				_records.RemoveAll (r => r.Record.Name == record.Name && r.Record.Type == record.Type &&
					(record is not PTRRecord pointer || r.Record is PTRRecord previous && previous.DomainName == pointer.DomainName)
					&& (record is not ARecord address || r.Record is ARecord previousAddress && previousAddress.Address.Equals (address.Address)));
				if (record.TTL > TimeSpan.Zero && _records.Count < 4096)
					_records.Add ((record, now.AddSeconds (Math.Min (120, record.TTL.TotalSeconds))));
				}
			}
		}

	public IReadOnlyList<DiscoveredPackage> Snapshot ()
		{
		lock (_gate)
			{
			_records.RemoveAll (r => r.Expires <= DateTime.UtcNow);
			ResourceRecord[] records = _records.Select (r => r.Record).ToArray ();
			var packages = new List<DiscoveredPackage> ();
			foreach (PTRRecord pointer in records.OfType<PTRRecord> ().Where (r => r.Name == new DomainName (PackageAdvertisement.SERVICE_TYPE + ".local")))
				{
				SRVRecord? service = records.OfType<SRVRecord> ().FirstOrDefault (r => r.Name == pointer.DomainName);
				TXTRecord? properties = records.OfType<TXTRecord> ().FirstOrDefault (r => r.Name == pointer.DomainName);
				if (service == null || service.Port == 0 || properties == null)
					continue;
				string Value (string key) => properties.Strings.FirstOrDefault (s => s.StartsWith (key + "=", StringComparison.Ordinal))?.Substring (key.Length + 1) ?? "";
				if (Value ("protocol") != "2" || !Guid.TryParseExact (Value ("processor"), "N", out _))
					continue;
				_sources.TryGetValue (service.Name, out IPAddress? source);
				ARecord? address = records.OfType<ARecord> ().Where (r => r.Name == service.Target)
					.OrderByDescending (r => r.Address.Equals (source)).FirstOrDefault ();
				if (address == null || address.Address.Equals (IPAddress.Any))
					continue;
				packages.Add (new DiscoveredPackage
					{
					Instance = pointer.DomainName.ToString (),
					ProcessorId = Value ("processor"),
					ProcessorName = Display (Value ("host")),
					Name = Display (Value ("name")),
					Host = address.Address.ToString (),
					Port = service.Port
					});
				}
			return packages.OrderBy (p => p.ProcessorName).ThenBy (p => p.ProcessorId).ThenBy (p => p.Name).ToArray ();
			}
		}

	public IReadOnlyList<DomainName> ResolutionQueries ()
		{
		lock (_gate)
			return _records.Select (r => r.Record).OfType<PTRRecord> ()
				.Where (r => r.Name == new DomainName (PackageAdvertisement.SERVICE_TYPE + ".local"))
				.Select (r => r.DomainName).Concat (_records.Select (r => r.Record).OfType<SRVRecord> ().Select (r => r.Target)).Distinct ().ToArray ();
		}

	private static string Display (string text) => new (text.Where (c => !char.IsControl (c)).Take (80).ToArray ());
	}

public static class PackageDiscovery
	{
	public static async Task<IReadOnlyList<DiscoveredPackage>> FindAsync (CancellationToken cancellationToken)
		{
		using var mdns = new MulticastService { UseIpv6 = false, IgnoreDuplicateMessages = false };
		var cache = new PackageDiscoveryCache ();
		mdns.AnswerReceived += (_, args) => cache.Add (args.Message, args.RemoteEndPoint.Address);
		mdns.Start ();
		for (int attempt = 0; attempt < 5; attempt++)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			mdns.SendQuery (PackageAdvertisement.SERVICE_TYPE + ".local", type: DnsType.PTR);
			foreach (DomainName name in cache.ResolutionQueries ())
				mdns.SendQuery (name);
			await Task.Delay (1000, cancellationToken).ConfigureAwait (false);
			}
		return cache.Snapshot ();
		}
	}