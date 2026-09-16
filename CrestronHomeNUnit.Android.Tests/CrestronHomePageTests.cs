// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Xml.Linq;

using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class CrestronHomePageTests
	{
	private const string Application = "com.crestron.phoenix.app";
	private static XElement Node (string id, string text = "") => new ("node", new XAttribute ("package", Application),
		new XAttribute ("resource-id", CrestronHomePages.ResourcePrefix + id), new XAttribute ("text", text), new XAttribute ("enabled", "true"), new XAttribute ("bounds", "[0,0][100,100]"));
	private static AndroidHierarchy Tree (params XElement[] nodes) => new (new XElement ("hierarchy", nodes).ToString (), Application);
	private static XElement Field (string container, string text)
		{
		var root = Node (container);
		root.Add (Node ("commonui_animatedEditText_editText", text));
		return root;
		}
	private static AndroidHierarchy Details (string local = "192.0.2.9", string port = "50001", string name = "Example Home", string remote = "192.0.2.8") => Tree (
		Node ("mobileclaimhome_title", "Edit Example Home"), Field ("mobileclaimhome_friendlyNameOrLocation", name),
		Field ("mobileclaimhome_localIpAddressOrHostName", local), Field ("mobileclaimhome_localPort", port),
		Field ("mobileclaimhome_remoteIpAddressOrHostName", remote));

	[Test]
	public void SavedEndpointUsesScopedLocalFieldsDespiteRepeatedEditControlIds ()
		{
		CrestronHomePages.RequireSavedLocalEndpoint (Details (), "Example Home", "192.0.2.9", 50001);
		Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireSavedLocalEndpoint (Details (local: "192.0.2.8", remote: "192.0.2.9"), "Example Home", "192.0.2.9", 50001));
		}

	[TestCase ("192.0.2.8", "50001", "Example Home")]
	[TestCase ("192.0.2.9", "443", "Example Home")]
	[TestCase ("192.0.2.9", "Port", "Example Home")]
	[TestCase ("192.0.2.9", "50001", "Different Home")]
	public void WrongConnectionCannotBeAccepted (string host, string port, string name)
		=> Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireSavedLocalEndpoint (Details (host, port, name), "Example Home", "192.0.2.9", 50001));

	[Test]
	public void HostnamesCompareWithoutInventingDnsResolution ()
		{
		CrestronHomePages.RequireSavedLocalEndpoint (Details (local: "HOME.EXAMPLE"), "Example Home", "home.example", 50001);
		Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireSavedLocalEndpoint (Details (local: "home.example"), "Example Home", "192.0.2.9", 50001));
		}

	[Test]
	public void ScopedLookupRejectsDuplicateOrMissingLocalFields ()
		{
		var selector = CrestronHomePages.Resource ("commonui_animatedEditText_editText") with { AncestorResourceId = CrestronHomePages.ResourcePrefix + "mobileclaimhome_localPort" };
		Assert.Throws<InvalidOperationException> (() => Tree (Field ("mobileclaimhome_remotePort", "50001")).RequireUnique (selector));
		Assert.Throws<InvalidOperationException> (() => Tree (Field ("mobileclaimhome_localPort", "50001"), Field ("mobileclaimhome_localPort", "50001")).RequireUnique (selector));
		}

	[Test]
	public void UnobstructedExpectedHomeIsRequired ()
		{
		CrestronHomePages.RequireHome (Tree (Node ("home_wholeHouse_name", "Example Home")), "Example Home");
		Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireHome (Tree (Node ("home_wholeHouse_name", "Different")), "Example Home"));
		}

	[TestCase ("customdevices_toolbarClose")]
	[TestCase ("mobileclaimhome_content")]
	[TestCase ("fragmentPulleyContainer")]
	[TestCase ("bottomSheet_infoBar")]
	[TestCase ("homeswitcher_title")]
	[TestCase ("featureMoreActionRoot")]
	public void BackgroundHomeUnderAnOverlayIsNotReady (string overlay)
		=> Assert.Throws<InvalidOperationException> (() => CrestronHomePages.RequireHome (Tree (Node ("home_wholeHouse_name", "Example Home"), Node (overlay)), "Example Home"));
	}