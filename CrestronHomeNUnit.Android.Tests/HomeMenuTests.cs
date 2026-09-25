// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Xml.Linq;
using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class HomeMenuTests
{
    private static XElement Node(string id, string description = "", string bounds = "[0,0][100,100]") => new("node",
        new XAttribute("package", "com.crestron.phoenix.app"),
        new XAttribute("resource-id", CrestronHomePages.ResourcePrefix + id),
        new XAttribute("content-desc", description), new XAttribute("text", ""),
        new XAttribute("enabled", "true"), new XAttribute("bounds", bounds));
    private static XElement Card(string name, string menuId, int left)
    {
        var card = Node("card");
        var titleGroup = Node("titleGroup");
        titleGroup.Add(Node("titleSubtitle_title", name));
        card.Add(titleGroup, Node(menuId, bounds: $"[{left},0][{left + 50},50]"));
        return card;
    }
    private static AndroidHierarchy Screen(params XElement[] cards) => new(
        new XElement("hierarchy", Node("homeswitcher_title"), cards).ToString(), "com.crestron.phoenix.app");

    [TestCase("homeview_more")]
    [TestCase("home_listview_more")]
    public void MenuBelongsToNamedHomeEvenWhenAnotherHomeAppearsFirst(string menuId)
    {
        var selected = CrestronHomePages.HomeMenu(Screen(Card("Other Home", menuId, 10), Card("Test Home", menuId, 200)), "Test Home");
        Assert.That(selected.Left, Is.EqualTo(200));
    }
    [Test]
    public void MissingSelectedMenuDoesNotUseAnotherHomesMenu()
    {
        var selected = Card("Test Home", "unrecognizedMenu", 200);
        // The other Home is deliberately the only one with a recognized menu.
        Assert.Throws<InvalidOperationException>(() => CrestronHomePages.HomeMenu(Screen(Card("Other Home", "homeview_more", 10), selected), "Test Home"));
    }
    [Test]
    public void DuplicateHomeNamesAreRejected() => Assert.Throws<InvalidOperationException>(() =>
        CrestronHomePages.HomeMenu(Screen(Card("Test Home", "homeview_more", 10), Card("Test Home", "homeview_more", 200)), "Test Home"));
    [Test]
    public void DuplicateMenusWithinSelectedHomeAreRejected()
    {
        var card = Card("Test Home", "homeview_more", 10);
        card.Add(Node("home_listview_more"));
        Assert.Throws<InvalidOperationException>(() => CrestronHomePages.HomeMenu(Screen(card), "Test Home"));
    }
    [Test]
    public void DisabledSelectedMenuIsRejected()
    {
        var card = Card("Test Home", "homeview_more", 10);
        card.Elements().Last().SetAttributeValue("enabled", "false");
        Assert.Throws<InvalidOperationException>(() => CrestronHomePages.HomeMenu(Screen(card), "Test Home"));
    }
}
