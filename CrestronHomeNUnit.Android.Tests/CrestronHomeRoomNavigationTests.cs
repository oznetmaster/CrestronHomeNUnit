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

	[Test]
	public async Task RoomTitleCoveredByBottomBarIsScrolledBeforeTapping ()
		{
		_transport.ClippedRoom = true;
		await Inspect ();
		Assert.That (_transport.RoomSwipes, Is.EqualTo (1));
		Assert.That (_transport.ExtensionOpens, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task RoomBelowCurrentViewportIsFoundByObservedScrolling ()
		{
		_transport.DesiredRoomViewport = 2;
		await Inspect ();
		Assert.That (_transport.RoomSwipes, Is.EqualTo (2));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task PreviouslyScrolledListCanFindARoomAboveIt ()
		{
		_transport.RoomListPosition = 2;
		await Inspect ();
		Assert.That (_transport.RoomUpSwipes, Is.GreaterThan (0));
		Assert.That (_transport.ExtensionOpens, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void LostRoomScrollResponseIsNeverReplayed ()
		{
		_transport.DesiredRoomViewport = 1;
		_transport.ThrowAfterRoomSwipe = true;
		Assert.ThrowsAsync<IOException> (() => Inspect ());
		Assert.That (_transport.RoomSwipes, Is.EqualTo (1));
		Assert.That (_transport.ExtensionOpens, Is.Zero);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void UnreachableRoomStopsAndRestoresHome ()
		{
		_transport.DesiredRoomViewport = 5;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.RoomSwipes, Is.LessThanOrEqualTo (24));
		Assert.That (_transport.ExtensionOpens, Is.Zero);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void DuplicateRoomAfterScrollingIsNeverTapped ()
		{
		_transport.DesiredRoomViewport = 1;
		_transport.DuplicateRoom = true;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.RoomSwipes, Is.EqualTo (1));
		Assert.That (_transport.ExtensionOpens, Is.Zero);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[TestCase (1)]
	[TestCase (3)]
	public async Task OffscreenTileIsRevealedWithCompactRoomHeading (int scrolls)
		{
		_transport.TileViewport = scrolls;
		await Inspect ();
		Assert.That (_transport.Swipes, Is.EqualTo (scrolls));
		Assert.That (_transport.ExtensionOpens, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[TestCase ("top")]
	[TestCase ("bottom")]
	public async Task TileUnderNavigationChromeIsNotTapped (string edge)
		{
		_transport.ClippedTile = edge;
		await Inspect ();
		Assert.That (_transport.Swipes, Is.EqualTo (1));
		Assert.That (_transport.ExtensionOpens, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void StationaryViewportStopsAfterOneGestureAndRestoresHome ()
		{
		_transport.TileViewport = 2;
		_transport.StationaryScroll = true;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.Swipes, Is.EqualTo (1));
		Assert.That (_transport.ExtensionOpens, Is.Zero);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void LostScrollResponseIsNotReplayed ()
		{
		_transport.TileViewport = 1;
		_transport.ThrowAfterSwipe = true;
		Assert.ThrowsAsync<IOException> (() => Inspect ());
		Assert.That (_transport.Swipes, Is.EqualTo (1));
		Assert.That (_transport.ExtensionOpens, Is.Zero);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[TestCase (true)]
	[TestCase (false)]
	public void AmbiguousOrDisabledTileAfterScrollIsNeverTapped (bool duplicate)
		{
		_transport.TileViewport = 1;
		_transport.TileCount = duplicate ? 2 : 1;
		_transport.DisabledTile = !duplicate;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.Swipes, Is.EqualTo (1));
		Assert.That (_transport.ExtensionOpens, Is.Zero);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void AbsentTileSearchHasFiniteGestureBudget ()
		{
		_transport.TileViewport = 100;
		Assert.ThrowsAsync<InvalidOperationException> (() => Inspect ());
		Assert.That (_transport.Swipes, Is.EqualTo (12));
		Assert.That (_transport.ExtensionOpens, Is.Zero);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void ConflictingCompactTitleIsRejected ()
		{
		var document = XDocument.Parse (_transport.Xml ("room"));
		document.Root!.Add (RoomTransport.Node ("room_toolbarTitle", "Another Room"));
		Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireRoom (
			new (document.ToString (), RoomTransport.Application), "Example Room"));
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
		internal int TileViewport;
		internal int Swipes;
		internal int ExtensionOpens;
		internal bool StationaryScroll;
		internal bool ThrowAfterSwipe;
		internal bool DisabledTile;
		internal string? ClippedTile;
		internal bool ClippedRoom;
		internal int DesiredRoomViewport;
		internal int RoomListPosition;
		internal int RoomSwipes;
		internal int RoomUpSwipes;
		internal bool ThrowAfterRoomSwipe;
		private int _viewport;
		internal List<string> Inputs = [];
		internal static XElement Node (string id, string text = "", string description = "", string bounds = "[0,0][100,100]") => new ("node",
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
					var list = Node ("rooms_roomsList", bounds: "[0,100][200,1000]");
					list.Add (Node ("itemRoomTitle", "Other Room " + RoomListPosition, bounds: "[100,150][200,250]"));
					if (RoomListPosition == DesiredRoomViewport || ClippedRoom)
						{
						string bounds = ClippedRoom && RoomListPosition == 0 ? "[0,850][100,950]" : "[0,150][100,250]";
						list.Add (Node ("itemRoomTitle", "Example Room", bounds: bounds));
						if (DuplicateRoom)
							list.Add (Node ("itemRoomTitle", "Example Room", bounds: bounds));
						}
					nodes.Add (list);
					}
				else
					{
					nodes.Add (Node (_viewport == 0 ? "room_name" : "room_toolbarTitle", "Example Room"));
					nodes.Add (Node ("room_back", bounds: "[200,0][300,100]"));
					var scroll = Node ("room_scrollView", bounds: "[0,0][600,1000]");
					scroll.Add (Node ("service", description: "room_service_Other " + _viewport, bounds: "[0,200][100,300]"));
					for (int i = 0; i < (_viewport >= TileViewport ? TileCount : 0); i++)
						{
						string bounds = _viewport == 0 ? ClippedTile switch
							{
								"top" => "[400,50][500,150]",
								"bottom" => "[400,850][500,950]",
								_ => "[400,200][500,300]"
								} : "[400,200][500,300]";
						var tile = Node ("service", description: "room_service_Example Thermostat", bounds: bounds);
						if (DisabledTile)
							tile.SetAttributeValue ("enabled", "false");
						scroll.Add (tile);
						}
					nodes.Add (scroll);
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
			if (arguments[1] == "input" && arguments[2] == "swipe")
				{
				if (Page == "rooms")
					{
					if (arguments[3] != "100" || int.Parse (arguments[4]) >= 900 || int.Parse (arguments[4]) <= 100 ||
						int.Parse (arguments[6]) >= 900 || int.Parse (arguments[6]) <= 100)
						throw new InvalidOperationException ("Swipe must stay inside the unobstructed room list.");
					bool down = int.Parse (arguments[4]) > int.Parse (arguments[6]);
					RoomSwipes++;
					if (!down) RoomUpSwipes++;
					RoomListPosition = Math.Clamp (RoomListPosition + (down ? 1 : -1), 0, 3);
					if (ThrowAfterRoomSwipe) throw new IOException ("Room scroll completed but response was lost.");
					return Task.FromResult (Array.Empty<byte> ());
					}
				if (Page != "room" || arguments[3] != "300" || int.Parse (arguments[4]) >= 900 || int.Parse (arguments[6]) <= 100)
					throw new InvalidOperationException ("Swipe must stay inside the observed room viewport.");
				Swipes++;
				if (!StationaryScroll && (TileCount > 0 || TileViewport > 0))
					_viewport++;
				if (ThrowAfterSwipe)
					throw new IOException ("Scroll completed but response was lost.");
				return Task.FromResult (Array.Empty<byte> ());
				}
			if (arguments[1] != "input" || arguments[2] != "tap")
				throw new InvalidOperationException ("Unexpected command: only observed navigation taps are permitted.");
			Inputs.Add (Page);
			Page = (Page, arguments[3], arguments[4]) switch
				{
					("home", "150", "950") => "rooms",
					("rooms", "50", "200") => "room",
					("room", "450", "250") => "extension",
					("extension", "50", "50") => "room",
					("room", "250", "50") => "rooms",
					("rooms", "50", "950") => "home",
					_ => throw new InvalidOperationException ("Unexpected navigation target.")
					};
			if (Page == "extension")
				ExtensionOpens++;
			if (Inputs.Count == ThrowAfterInput)
				throw new IOException ("Input completed but response was lost.");
			return Task.FromResult (Array.Empty<byte> ());
			}
		}
	}