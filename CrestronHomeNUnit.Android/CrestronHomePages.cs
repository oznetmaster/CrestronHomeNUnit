// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Net;

namespace CrestronHomeNUnit.Android;

/// <summary>Read-only assertions for observed Crestron Home Android page structure.</summary>
public static class CrestronHomePages
	{
	public const string ResourcePrefix = "com.crestron.phoenix.app:id/";
	public static AndroidSelector Resource (string id) => new (AndroidSelectorKind.ResourceId, ResourcePrefix + id);

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
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedName);
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedHost);
		if (expectedPort is < 1 or > 65535) throw new ArgumentOutOfRangeException (nameof (expectedPort));
		_ = hierarchy.RequireUnique (Resource ("mobileclaimhome_title"));
		AndroidElement Field (string container) => hierarchy.RequireUnique (Resource ("commonui_animatedEditText_editText") with { AncestorResourceId = ResourcePrefix + container });
		var name = Field ("mobileclaimhome_friendlyNameOrLocation");
		var address = Field ("mobileclaimhome_localIpAddressOrHostName");
		var port = Field ("mobileclaimhome_localPort");
		bool sameHost = IPAddress.TryParse (expectedHost, out var expectedIp) && IPAddress.TryParse (address.Text, out var actualIp)
			? expectedIp.Equals (actualIp) : string.Equals (expectedHost, address.Text, StringComparison.OrdinalIgnoreCase);
		if (!name.Enabled || !address.Enabled || !port.Enabled || name.Text != expectedName || !sameHost ||
			!int.TryParse (port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number != expectedPort)
			throw new InvalidOperationException ("The selected system's saved local endpoint does not match the private workflow target.");
		}
	}