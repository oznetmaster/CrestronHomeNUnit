// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class CrestronHomeRoomNavigationTests
	{
	private string _directory = null!;
	private AndroidSessionLease _lease = null!;
	private RoomTransport _transport = null!;
	private CrestronHomeNavigation _navigation = null!;

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		var owner = Guid.NewGuid ().ToString ("N");
		var path = Path.Combine (_directory, "worker.lease");
		_lease = AndroidSessionLease.Acquire (path, owner);
		using var process = Process.GetCurrentProcess ();
		var context = new AndroidRunContext (1, owner, Environment.MachineName, process.Id, process.StartTime.ToUniversalTime ().Ticks,
			"192.0.2.1", 7, Guid.NewGuid ().ToString (), "1.0.0.1", new ('A', 64), new ('B', 64),
			new (Environment.ProcessPath!, "fake-serial", "com.crestron.phoenix.app", "Example Home", path), _directory);
		_transport = new ();
		_navigation = new (new (context, new (_transport, context.Profile.Application)), TimeSpan.FromMilliseconds (60));
		}

	[TearDown]
	public void TearDown ()
		{
		_lease.Release ();
		Directory.Delete (_directory, true);
		}

	private Task Inspect (Action<AndroidHierarchy>? verify = null, string checkId = "room") => _navigation.InspectRoomExtensionAsync (
		checkId, "Example Room", "Example Thermostat", "Room Controls", verify ?? (_ => { }));

	[Test]
	public async Task RoomInspectionRestoresHomeAndCanRunAgain ()
		{
		for (int run = 0; run < 2; run++)
			{
			await Inspect (h => Assert.That (h.RequireUnique (CrestronHomePages.Resource ("temperature")).Text, Is.EqualTo ("21")), "room-" + run);
			Assert.That (_navigation.HomeRestored, Is.True);
			}
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "home", "rooms", "room", "extension", "room", "rooms", "home", "rooms", "room", "extension", "room", "rooms" }));
		}

	[Test]
	public void AssertionFailureStillRestoresHome ()
		{
		Assert.ThrowsAsync<InvalidDataException> (() => Inspect (_ => throw new InvalidDataException ("Unexpected temperature")));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (File.Exists (Path.Combine (_directory, "room.controls", "observation.json")), Is.False);
		Assert.That (File.Exists (Path.Combine (_directory, "room.home-restored", "observation.json")), Is.True);
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public void UncertainCompletedNavigationIsNeverReplayed (int input)
		{
		_transport.ThrowAfterInput = input;
		Assert.ThrowsAsync<IOException> (() => Inspect ());
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (_transport.Inputs, Has.Count.EqualTo (input * 2));
		}

	[Test]
	public void DuplicateRoomNamePreventsRoomSelection ()
		{
		_transport.DuplicateRoom = true;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "home", "rooms" }));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[TestCase (false)]
	[TestCase (true)]
	public void MissingOrDuplicateTilePreventsOpeningAnUnidentifiedDevice (bool duplicate)
		{
		_transport.TileCount = duplicate ? 2 : 0;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "home", "rooms", "room", "rooms" }));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[TestCase ("extra")]
	[TestCase ("disabled")]
	[TestCase ("overlap")]
	public void ChangedBottomTabLayoutSendsNoInput (string change)
		{
		_transport.ExtraTab = change == "extra";
		_transport.InvalidTab = change;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.Inputs, Is.Empty);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void BackgroundRoomAndRoomsAreRejectedWhileExtensionIsOpen ()
		{
		var hierarchy = new AndroidHierarchy (_transport.Xml ("extension"), RoomTransport.Application);
		Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireRoom (hierarchy, "Example Room"));
		Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireRooms (hierarchy));
		CrestronHomePages.RequireExtensionPage (hierarchy, "Room Controls");
		}

	private sealed class RoomTransport : IAndroidCommandTransport
		{
		internal const string Application = "com.crestron.phoenix.app";
		internal string Page = "home";
		internal bool DuplicateRoom;
		internal bool ExtraTab;
		internal string? InvalidTab;
		internal int TileCount = 1;
		internal int ThrowAfterInput;
		internal List<string> Inputs = [];
		private static XElement Node (string id, string text = "", string description = "", string bounds = "[0,0][100,100]") => new ("node",
			new XAttribute ("package", Application), new XAttribute ("resource-id", CrestronHomePages.ResourcePrefix + id),
			new XAttribute ("text", text), new XAttribute ("content-desc", description), new XAttribute ("enabled", "true"), new XAttribute ("bounds", bounds));

		internal string Xml (string page)
			{
			var nodes = new List<XElement> ();
			var bar = Node ("bottomNavigationView", bounds: "[0,900][200,1000]");
			for (int i = 0; i < (ExtraTab ? 3 : 2); i++)
				{
				var tab = Node ("", bounds: $"[{i * 100},900][{(i + 1) * 100},1000]");
				tab.SetAttributeValue ("class", "android.view.ViewGroup");
				tab.SetAttributeValue ("clickable", "true");
				if (i == 1 && InvalidTab == "disabled")
					tab.SetAttributeValue ("enabled", "false");
				if (i == 1 && InvalidTab == "overlap")
					tab.SetAttributeValue ("bounds", "[50,900][200,1000]");
				tab.Add (Node ("itemBottomNavigationIcon"));
				bar.Add (tab);
				}
			nodes.Add (bar);
			if (page == "home")
				nodes.Add (Node ("home_wholeHouse_name", "Example Home"));
			else
				{
				nodes.Add (Node ("fragmentRoomsTitle", "Rooms"));
				if (page == "rooms")
					{
					nodes.Add (Node ("itemRoomTitle", "Example Room"));
					if (DuplicateRoom)
						nodes.Add (Node ("itemRoomTitle", "Example Room"));
					}
				else
					{
					nodes.Add (Node ("room_name", "Example Room"));
					nodes.Add (Node ("room_back", bounds: "[200,0][300,100]"));
					for (int i = 0; i < TileCount; i++)
						nodes.Add (Node ("service", description: "room_service_Example Thermostat", bounds: "[400,0][500,100]"));
					if (page == "extension")
						{
						nodes.Add (Node ("customdevices_toolbarTitle", "Room Controls"));
						nodes.Add (Node ("customdevices_toolbarClose"));
						nodes.Add (Node ("temperature", "21"));
						}
					}
				}
			return new XElement ("hierarchy", nodes).ToString (SaveOptions.DisableFormatting);
			}

		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (arguments[0] == "exec-out")
				return Task.FromResult (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
			if (arguments[1] == "uiautomator")
				return Task.FromResult (Encoding.UTF8.GetBytes ("UI hierarchy dumped to: " + arguments[3]));
			if (arguments[1] == "rm")
				return Task.FromResult (Array.Empty<byte> ());
			if (arguments[1] == "cat")
				return Task.FromResult (Encoding.UTF8.GetBytes (Xml (Page)));
			if (arguments[1] != "input" || arguments[2] != "tap")
				throw new InvalidOperationException ("Unexpected command: only observed navigation taps are permitted.");
			Inputs.Add (Page);
			Page = (Page, arguments[3], arguments[4]) switch
				{
				("home", "150", "950") => "rooms",
				("rooms", "50", "50") => "room",
				("room", "450", "50") => "extension",
				("extension", "50", "50") => "room",
				("room", "250", "50") => "rooms",
				("rooms", "50", "950") => "home",
				_ => throw new InvalidOperationException ("Unexpected navigation target.")
				};
			if (Inputs.Count == ThrowAfterInput)
				throw new IOException ("Input completed but response was lost.");
			return Task.FromResult (Array.Empty<byte> ());
			}
		}
	}