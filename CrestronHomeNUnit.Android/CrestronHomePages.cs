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
		if (labels.Length != 1) throw new InvalidOperationException ("Status-and-button label is missing or ambiguous.");
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
		AndroidElement Field (string container) => hierarchy.RequireUnique (Resource ("commonui_animatedEditText_editText") with { AncestorResourceId = ResourcePrefix + container });
		var name = Field ("mobileclaimhome_friendlyNameOrLocation");
		var address = Field ("mobileclaimhome_localIpAddressOrHostName");
		bool sameHost = IPAddress.TryParse (expectedHost, out var expectedIp) && IPAddress.TryParse (address.Text, out var actualIp)
			? expectedIp.Equals (actualIp) : string.Equals (expectedHost, address.Text, StringComparison.OrdinalIgnoreCase);
		if (!name.Enabled || !address.Enabled || name.Text != expectedName || !sameHost)
			throw new InvalidOperationException ("The selected system's saved local endpoint does not match the private workflow target.");
		}

	internal static void RequireSavedLocalPort (AndroidHierarchy hierarchy, int expectedPort)
		{
		if (expectedPort is < 1 or > 65535) throw new ArgumentOutOfRangeException (nameof (expectedPort));
		var port = hierarchy.RequireUnique (Resource ("commonui_animatedEditText_editText") with { AncestorResourceId = ResourcePrefix + "mobileclaimhome_localPort" });
		if (!port.Enabled || !int.TryParse (port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number != expectedPort)
			throw new InvalidOperationException ("The selected system's saved local port does not match the private workflow target.");
		}
	}