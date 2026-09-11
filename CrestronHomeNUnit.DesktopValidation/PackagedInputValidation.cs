// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

using CrestronHomeNUnit.Transport;

internal static class PackagedInputValidation
	{
	public static async Task RunAsync (string assemblyPath, string workDirectory)
		{
		Assembly assembly = Assembly.LoadFrom (Path.GetFullPath (assemblyPath));
		Type configurationType = assembly.GetType ("CrestronHomeNUnit.Driver.PackageConfiguration", true)!;
		object configuration = configurationType.GetMethod ("Load")!.Invoke (null, null)!;
		object service = configurationType.GetMethod ("CreateService")!.Invoke (configuration, new object[] { workDirectory, workDirectory })!;
		Type serverType = assembly.GetType ("CrestronHomeNUnit.Transport.RemoteTestServer", true)!;
		object server = Activator.CreateInstance (serverType, service, "packaged-input-validation", 0, IPAddress.Any)!;
		using var ownedService = (IDisposable)service;
		using var ownedServer = (IDisposable)server;
		int port = (int)serverType.GetProperty ("Port")!.GetValue (server)!;
		string processorId = Guid.NewGuid ().ToString ("N");
		Type advertisementType = assembly.GetType ("CrestronHomeNUnit.Transport.PackageAdvertisement", true)!;
		using var advertisement = (IDisposable)Activator.CreateInstance (advertisementType, "Exact merged package", processorId, "Validation processor", port, (Action<string>)Console.Error.WriteLine)!;
		var discovered = await PackageDiscovery.FindAsync (System.Threading.CancellationToken.None);
		DiscoveredPackage endpoint = discovered.Single (p => p.ProcessorId == processorId);
		Require (endpoint.Port == port, "Merged mDNS advertised the wrong assigned port.");
		Console.WriteLine ("The exact merged and patched package was discovered through mDNS.");
		using var client = await RemoteTestClient.ConnectAsync (endpoint.Host, port, "packaged-input-validation");
		Require (client.SupportsTestInputs && client.Suites.Count == 2, "The merged package did not advertise both suites over the protected connection.");
		foreach (int plugCount in new[] { 2, 3 })
			{
			string hosts = string.Join (",", Enumerable.Range (1, plugCount).Select (i => "{\"DeviceId\":\"synthetic-device-" + i + "\"}"));
			string data = "{\"Enabled\":false,\"Devices\":{\"plug\":{\"Hosts\":[" + hosts + "]}}}";
			WireMessage reply = await client.SendAsync (new WireMessage
				{
				Kind = "discover",
				Suite = "kasatapoclient-live",
				EnableLiveTests = true,
				TestInputs = new List<TestInputFile> { new TestInputFile { Name = "LiveTestSettings.json", Content = Encoding.UTF8.GetBytes (data) } }
				});
			Require (reply.ExitCode == 0 && Count (reply) == plugCount + 6, "Live discovery did not reload runner-provided inputs.");
			Require (reply.Xml.Contains ("device-id:synthetic-device-1"), "The merged fixture did not retain a stable device selector in its test name.");
			}
		string saved = File.ReadAllText (Path.Combine (workDirectory, "kasatapoclient-live", "Inputs", "LiveTestSettings.json"));
		Require (saved.Contains ("\"Enabled\":false"), "The live override rewrote the settings file.");
		WireMessage disabled = await client.SendAsync (new WireMessage { Kind = "discover", Suite = "kasatapoclient-live" });
		Require (Count (disabled) == 7, "A later operation inherited the live override.");
		WireMessage blocked = await client.SendAsync (new WireMessage { Kind = "run", Suite = "kasatapoclient-live" });
		Require (blocked.Kind == "error", "Live execution without runner selection was accepted.");
		WireMessage cleared = await client.SendAsync (new WireMessage { Kind = "discover", Suite = "kasatapoclient-live", TestInputs = [] });
		Require (cleared.ExitCode == 0 && Count (cleared) == 7, "Live discovery did not clear previous inputs.");
		WireMessage unit = await client.SendAsync (new WireMessage { Kind = "run", Suite = "kasatapoclient", TestInputs = [] });
		Require (unit.ExitCode == 0 && Count (unit) == 97, "Packaged unit tests failed over TCP: " + unit.Text);
		Console.WriteLine ("Exact package: live selection overrode Enabled=false without changing JSON; discovery expanded from 8 to 9 cases, clearing restored 7; all 97 unit tests passed over TCP. Live tests were not executed.");
		}
	private static int Count (WireMessage reply)
		{
		var document = new XmlDocument { XmlResolver = null };
		document.LoadXml (reply.Xml);
		return int.Parse (document.DocumentElement!.GetAttribute ("testcasecount"));
		}
	private static void Require (bool condition, string message)
		{
		if (!condition)
			throw new InvalidOperationException (message);
		}
	}