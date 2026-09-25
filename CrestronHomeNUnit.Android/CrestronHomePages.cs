// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Net;
using System.Xml.Linq;

namespace CrestronHomeNUnit.Android;

/// <summary>Read-only assertions for observed Crestron Home Android page structure.</summary>
public static class CrestronHomePages
	{
	public const string ResourcePrefix = "com.crestron.phoenix.app:id/";
	public static AndroidSelector Resource (string id) => new (AndroidSelectorKind.ResourceId, ResourcePrefix + id);

	private static void RequireNoNavigationOverlay (AndroidHierarchy hierarchy)
		{
		foreach (var overlay in new[] { "customdevices_toolbarClose", "customdevice_selectionRecyclerView", "mobileclaimhome_content", "fragmentPulleyContainer", "bottomSheet_infoBar", "homeswitcher_title", "featureMoreActionRoot" })
			hierarchy.RequireAbsent (Resource (overlay));
		}

	public static void RequireRooms (AndroidHierarchy hierarchy)
		{
		RequireNoNavigationOverlay (hierarchy);
		hierarchy.RequireAbsent (Resource ("room_back"));
		var heading = hierarchy.RequireUnique (Resource ("fragmentRoomsTitle"));
		if (!heading.Enabled || heading.Text != "Rooms")
			throw new InvalidOperationException ("The unobstructed Rooms screen is not open.");
		}

	public static void RequireRoom (AndroidHierarchy hierarchy, string roomName)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (roomName);
		RequireNoNavigationOverlay (hierarchy);
		// Scrolling replaces the large heading with a compact, fixed toolbar title.
		var large = hierarchy.Find (Resource ("room_name"));
		var compact = hierarchy.Find (Resource ("room_toolbarTitle"));
		var headings = large.Concat (compact).ToArray ();
		if (large.Length > 1 || compact.Length > 1 || headings.Length == 0 || headings.Any (heading => !heading.Enabled || heading.Text != roomName) ||
			!hierarchy.RequireUnique (Resource ("room_back")).Enabled)
			throw new InvalidOperationException ("The expected room is not open.");
		}

	internal static AndroidElement RoomsViewport (AndroidHierarchy hierarchy)
		{
		RequireRooms (hierarchy);
		var container = hierarchy.RequireUnique (Resource ("rooms_roomsList"));
		var heading = hierarchy.RequireUnique (Resource ("fragmentRoomsTitle"));
		var bar = hierarchy.RequireUnique (Resource ("bottomNavigationView"));
		int top = Math.Max (container.Top, heading.Bottom);
		int bottom = Math.Min (container.Bottom, bar.Top);
		if (!container.Enabled || !bar.Enabled || bottom - top < 80)
			throw new InvalidOperationException ("The visible room list is unavailable.");
		return container with { Top = top, Bottom = bottom };
		}

	internal static bool RoomChoiceVisible (AndroidHierarchy hierarchy, AndroidElement room)
		{
		var viewport = RoomsViewport (hierarchy);
		return room.ResourceId == ResourcePrefix + "itemRoomTitle" && room.Left >= viewport.Left && room.Right <= viewport.Right &&
			room.Top >= viewport.Top && room.Bottom <= viewport.Bottom;
		}

	internal static string RoomsViewportSignature (AndroidHierarchy hierarchy)
		{
		var document = XDocument.Parse (hierarchy.MaskedXml);
		var container = document.Descendants ("node").Single (node =>
			(string?)node.Attribute ("package") == "com.crestron.phoenix.app" &&
			(string?)node.Attribute ("resource-id") == ResourcePrefix + "rooms_roomsList");
		return string.Join ("\n", container.Descendants ("node").Where (node =>
			(string?)node.Attribute ("package") == "com.crestron.phoenix.app" &&
			(string?)node.Attribute ("resource-id") == ResourcePrefix + "itemRoomTitle")
			.Select (node => (string?)node.Attribute ("text") + "|" + (string?)node.Attribute ("bounds")));
		}

	internal static AndroidElement RoomViewport (AndroidHierarchy hierarchy)
		{
		var container = hierarchy.RequireUnique (Resource ("room_scrollView"));
		var back = hierarchy.RequireUnique (Resource ("room_back"));
		var bar = hierarchy.RequireUnique (Resource ("bottomNavigationView"));
		int top = Math.Max (container.Top, back.Bottom);
		int bottom = Math.Min (container.Bottom, bar.Top);
		if (!container.Enabled || !back.Enabled || !bar.Enabled || bottom - top < 80)
			throw new InvalidOperationException ("The room's visible scrolling area is unavailable.");
		return container with
			{
			Top = top,
			Bottom = bottom
			};
		}

	internal static bool RoomTileVisible (AndroidHierarchy hierarchy, AndroidElement tile)
		{
		var viewport = RoomViewport (hierarchy);
		return tile.Left >= viewport.Left && tile.Right <= viewport.Right && tile.Top >= viewport.Top && tile.Bottom <= viewport.Bottom;
		}

	internal static string RoomViewportSignature (AndroidHierarchy hierarchy)
		{
		var document = XDocument.Parse (hierarchy.MaskedXml);
		var container = document.Descendants ("node").Single (node =>
			(string?)node.Attribute ("package") == "com.crestron.phoenix.app" &&
			(string?)node.Attribute ("resource-id") == ResourcePrefix + "room_scrollView");
		// Ignore changing sensor text; progress is movement of the service cards, not a temperature update.
		return string.Join ("\n", container.Descendants ("node").Where (node =>
			(string?)node.Attribute ("package") == "com.crestron.phoenix.app" &&
			((string?)node.Attribute ("content-desc"))?.StartsWith ("room_service_", StringComparison.Ordinal) == true)
			.Select (node => (string?)node.Attribute ("content-desc") + "|" + (string?)node.Attribute ("bounds")));
		}

	// This app's two bottom tabs have no unique accessibility names. Validate the
	// observed structure and geometry before selecting either tab; never use saved coordinates.
	internal static AndroidElement BottomTab (AndroidHierarchy hierarchy, bool rooms)
		{
		var document = XDocument.Parse (hierarchy.MaskedXml);
		bool Is (XElement node, string id) => (string?)node.Attribute ("package") == "com.crestron.phoenix.app" &&
			(string?)node.Attribute ("resource-id") == ResourcePrefix + id;
		var bars = document.Descendants ("node").Where (node => Is (node, "bottomNavigationView")).ToArray ();
		if (bars.Length != 1)
			throw new InvalidOperationException ("The bottom navigation bar is missing or ambiguous.");
		var buttons = bars[0].Elements ("node").ToArray ();
		if (buttons.Length != 2 || buttons.Any (node =>
			(string?)node.Attribute ("package") != "com.crestron.phoenix.app" ||
			(string?)node.Attribute ("class") != "android.view.ViewGroup" ||
			(string?)node.Attribute ("clickable") != "true" ||
			(string?)node.Attribute ("enabled") != "true" ||
			node.Descendants ("node").Count (child => Is (child, "itemBottomNavigationIcon")) != 1))
			throw new InvalidOperationException ("The bottom navigation layout has changed; no input was sent.");
		var bar = AndroidHierarchy.ReadElement (bars[0]);
		var left = AndroidHierarchy.ReadElement (buttons[0]);
		var right = AndroidHierarchy.ReadElement (buttons[1]);
		if (!bar.Enabled || left.Right > right.Left || left.Left < bar.Left || right.Right > bar.Right ||
			left.Top < bar.Top || right.Top < bar.Top || left.Bottom > bar.Bottom || right.Bottom > bar.Bottom ||
			left.Top != right.Top || left.Bottom != right.Bottom)
			throw new InvalidOperationException ("The bottom navigation bounds are inconsistent; no input was sent.");
		return rooms ? right : left;
		}

	// Resolve the menu inside the selected Home's card, in either list or grid view.
	// A global resource-ID selector is ambiguous when more than one Home is saved.
	internal static AndroidElement HomeMenu (AndroidHierarchy hierarchy, string expectedHome)
		{
		_ = hierarchy.RequireUnique (Resource ("homeswitcher_title"));
		var document = XDocument.Parse (hierarchy.MaskedXml);
		bool InApp (XElement node) => (string?)node.Attribute ("package") == "com.crestron.phoenix.app";
		bool IsMenu (XElement node) => InApp (node) &&
			(string?)node.Attribute ("resource-id") is ResourcePrefix + "homeview_more" or ResourcePrefix + "home_listview_more";
		var labels = document.Descendants ("node").Where (node => InApp (node) &&
			(string?)node.Attribute ("content-desc") == expectedHome).ToArray ();
		if (labels.Length != 1)
			throw new InvalidOperationException ("The selected Home card is missing or ambiguous.");
		var container = labels[0].Ancestors ().FirstOrDefault (node => node.Descendants ("node").Any (IsMenu));
		var menus = container?.Descendants ("node").Where (IsMenu).ToArray () ?? [];
		bool containsOtherHome = container?.Descendants ("node").Any (node => InApp (node) &&
			(string?)node.Attribute ("resource-id") == ResourcePrefix + "titleSubtitle_title" &&
			!string.IsNullOrEmpty ((string?)node.Attribute ("content-desc")) &&
			(string?)node.Attribute ("content-desc") != expectedHome) == true;
		if (menus.Length != 1 || containsOtherHome)
			throw new InvalidOperationException ("The selected Home card's menu is missing or ambiguous.");
		var menu = AndroidHierarchy.ReadElement (menus[0]);
		if (!menu.Enabled || menu.Right <= menu.Left || menu.Bottom <= menu.Top)
			throw new InvalidOperationException ("The selected Home menu is disabled or has invalid bounds.");
		return menu;
		}

	public static void RequireExtensionPage (AndroidHierarchy hierarchy, string title)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (title);
		foreach (var overlay in new[] { "mobileclaimhome_content", "fragmentPulleyContainer", "bottomSheet_infoBar", "homeswitcher_title", "featureMoreActionRoot" })
			hierarchy.RequireAbsent (Resource (overlay));
		var heading = hierarchy.RequireUnique (Resource ("customdevices_toolbarTitle"));
		if (!heading.Enabled || heading.Text != title || !hierarchy.RequireUnique (Resource ("customdevices_toolbarClose")).Enabled)
			throw new InvalidOperationException ("The expected extension page is not open.");
		}

	/// <summary>Read a status-and-button row by its own label, never a repeated global button ID.</summary>
	public static (string Status, string Action, bool Enabled) ReadStatusAndButton (AndroidHierarchy hierarchy, string label)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (label);
		// The hierarchy has already been securely parsed and password-masked.
		var document = XDocument.Parse (hierarchy.MaskedXml);
		bool Is (XElement node, string id) => (string?)node.Attribute ("package") == "com.crestron.phoenix.app" &&
			(string?)node.Attribute ("resource-id") == ResourcePrefix + id;
		var labels = document.Descendants ("node").Where (node => Is (node, "titleSubtitle_title") && (string?)node.Attribute ("text") == label &&
			node.Parent != null && Is (node.Parent, "customdevice_statusAndButtonTitleSubtitle")).ToArray ();
		if (labels.Length != 1)
			throw new InvalidOperationException ("Status-and-button label is missing or ambiguous.");
		var titleGroup = labels[0].Parent!;
		var row = titleGroup.Parent ?? throw new InvalidOperationException ("Status-and-button row is missing.");
		var statuses = titleGroup.Elements ("node").Where (node => Is (node, "titleSubtitle_subtitle")).ToArray ();
		var actions = row.Elements ("node").Where (node => Is (node, "customdevice_statusAndButtonAction")).ToArray ();
		if (statuses.Length != 1 || actions.Length != 1 || (string?)actions[0].Attribute ("password") == "true")
			throw new InvalidOperationException ("Status-and-button row is incomplete or ambiguous.");
		return ((string?)statuses[0].Attribute ("text") ?? "", (string?)actions[0].Attribute ("text") ?? "", (string?)actions[0].Attribute ("enabled") == "true");
		}

	public static void RequireHome (AndroidHierarchy hierarchy, string expectedName)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedName);
		foreach (var overlay in new[] { "customdevices_toolbarClose", "mobileclaimhome_content", "fragmentPulleyContainer", "bottomSheet_infoBar", "homeswitcher_title", "featureMoreActionRoot" })
			hierarchy.RequireAbsent (Resource (overlay));
		hierarchy.RequireAbsent (Resource ("room_back"));
		hierarchy.RequireAbsent (Resource ("fragmentRoomsTitle"));
		var home = hierarchy.RequireUnique (Resource ("home_wholeHouse_name"));
		if (!home.Enabled || home.Text != expectedName)
			throw new InvalidOperationException ("The expected unobstructed Home screen is not selected.");
		}

	public static void RequireSavedLocalEndpoint (AndroidHierarchy hierarchy, string expectedName, string expectedHost, int expectedPort)
		{
		RequireSavedLocalAddress (hierarchy, expectedName, expectedHost);
		RequireSavedLocalPort (hierarchy, expectedPort);
		}

	internal static void RequireSavedLocalAddress (AndroidHierarchy hierarchy, string expectedName, string expectedHost)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedName);
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedHost);
		_ = hierarchy.RequireUnique (Resource ("mobileclaimhome_title"));
		AndroidElement Field (string container) => hierarchy.RequireUnique (Resource ("commonui_animatedEditText_editText") with
			{
			AncestorResourceId = ResourcePrefix + container
			});
		var name = Field ("mobileclaimhome_friendlyNameOrLocation");
		var address = Field ("mobileclaimhome_localIpAddressOrHostName");
		bool sameHost = IPAddress.TryParse (expectedHost, out var expectedIp) && IPAddress.TryParse (address.Text, out var actualIp)
			? expectedIp.Equals (actualIp) : string.Equals (expectedHost, address.Text, StringComparison.OrdinalIgnoreCase);
		if (!name.Enabled || !address.Enabled || name.Text != expectedName || !sameHost)
			throw new InvalidOperationException ("The selected system's saved local endpoint does not match the private workflow target.");
		}

	internal static void RequireSavedLocalPort (AndroidHierarchy hierarchy, int expectedPort)
		{
		if (expectedPort is < 1 or > 65535)
			throw new ArgumentOutOfRangeException (nameof (expectedPort));
		var port = hierarchy.RequireUnique (Resource ("commonui_animatedEditText_editText") with
			{
			AncestorResourceId = ResourcePrefix + "mobileclaimhome_localPort"
			});
		if (!port.Enabled || !int.TryParse (port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number != expectedPort)
			throw new InvalidOperationException ("The selected system's saved local port does not match the private workflow target.");
		}
	}
