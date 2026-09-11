// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using CrestronHomeNUnit.Runtime;
using CrestronHomeNUnit.Transport;

using Makaretu.Dns;

internal static class DiscoveryValidation
	{
	public static async Task RunAsync ()
		{
		string root = Path.Combine (Path.GetTempPath (), "NUnitDiscovery-" + Guid.NewGuid ().ToString ("N"));
		try
			{
			ProcessorIdentity[] identities = await Task.WhenAll (Enumerable.Range (0, 8).Select (_ => Task.Run (() => ProcessorIdentity.LoadOrCreate (root))));
			Require (identities.All (i => i.Id == identities[0].Id && i.PairingKey == identities[0].PairingKey), "Concurrent packages generated different processor keys.");
			CheckCache (identities[0].Id);
			using var host = new TestExecutionService (Path.Combine (root, "results"), _ => typeof (DiscoveryValidation).Assembly);
			using var first = new RemoteTestServer (host, identities[0].PairingKey, 0);
			using var second = new RemoteTestServer (host, identities[0].PairingKey, 0);
			Require (first.Port > 0 && second.Port > 0 && first.Port != second.Port, "Automatic ports were not distinct.");
			using var advertisement1 = new PackageAdvertisement ("Discovery validation A", identities[0].Id, "Validation processor", first.Port, Console.Error.WriteLine);
			using var advertisement2 = new PackageAdvertisement ("Discovery validation B", identities[0].Id, "Validation processor", second.Port, Console.Error.WriteLine);
			var packages = await PackageDiscovery.FindAsync (CancellationToken.None);
			var ours = packages.Where (p => p.ProcessorId == identities[0].Id).ToArray ();
			Require (ours.Length == 2 && ours.Any (p => p.Port == first.Port) && ours.Any (p => p.Port == second.Port), "mDNS did not discover both packages on their assigned ports.");
			foreach (DiscoveredPackage package in ours)
				{
				using RemoteTestClient client = await RemoteTestClient.ConnectAsync (package.Host, package.Port, identities[0].PairingKey);
				Require (client.SupportsTestInputs, "Discovered endpoint failed processor-key authentication.");
				}
			advertisement2.Dispose ();
			packages = await PackageDiscovery.FindAsync (CancellationToken.None);
			Require (packages.Count (p => p.ProcessorId == identities[0].Id) == 1, "Stopped package remained discoverable or another package stopped answering.");
			Console.WriteLine ("mDNS discovered two automatically assigned ports, shared processor pairing authenticated both, and shutdown preserved the other package.");
			}
		finally { if (Directory.Exists (root)) Directory.Delete (root, true); }
		}

	private static void CheckCache (string processorId)
		{
		var profile = new ServiceProfile ("validation", PackageAdvertisement.SERVICE_TYPE, 12345, [IPAddress.Loopback]);
		profile.AddProperty ("name", "Example");
		profile.AddProperty ("processor", processorId);
		profile.AddProperty ("host", "Processor");
		profile.AddProperty ("protocol", "2");
		var pointer = new PTRRecord { Name = profile.QualifiedServiceName, DomainName = profile.FullyQualifiedName, TTL = TimeSpan.FromSeconds (120) };
		var message = new Message ();
		message.Answers.Add (pointer);
		var cache = new PackageDiscoveryCache ();
		cache.Add (message);
		Require (cache.Snapshot ().Count == 0, "Incomplete metadata was accepted.");
		foreach (ResourceRecord record in profile.Resources)
			{
			var part = new Message ();
			part.Answers.Add (record);
			cache.Add (part);
			cache.Add (part);
			}
		Require (cache.Snapshot ().Count == 1 && cache.Snapshot ()[0].Port == 12345, "Split or duplicate DNS records were not resolved.");
		pointer.TTL = TimeSpan.Zero;
		cache.Add (message);
		Require (cache.Snapshot ().Count == 0, "Goodbye did not remove the package.");
		}
	private static void Require (bool condition, string message)
		{
		if (!condition)
			throw new InvalidOperationException (message);
		}
	}