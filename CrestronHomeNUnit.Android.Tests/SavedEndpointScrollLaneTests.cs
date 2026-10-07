// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;
[TestFixture]
public sealed class SavedEndpointScrollLaneTests
{
    static AndroidHierarchy Page(int fieldLeft=56,int fieldRight=664,bool enabled=true,string bounds="",string contentBounds="[28,126][692,1474]")
    {
        var p=CrestronHomePages.ResourcePrefix;
        string fieldBounds=bounds.Length>0?bounds:$"[{fieldLeft},593][{fieldRight},639]";
        return new($"<hierarchy><node package='com.crestron.phoenix.app' resource-id='{p}mobileclaimhome_scrollView' enabled='{enabled.ToString().ToLowerInvariant()}' bounds='[0,126][720,1474]'><node package='com.crestron.phoenix.app' resource-id='{p}mobileclaimhome_content' clickable='true' enabled='true' bounds='{contentBounds}'><node package='com.crestron.phoenix.app' resource-id='{p}commonui_animatedEditText_editText' class='android.widget.EditText' clickable='true' password='true' text='private-sentinel' bounds='{fieldBounds}'/></node></node></hierarchy>","com.crestron.phoenix.app");
    }
    [Test] public void RetainedPortraitGeometryUsesInnerPaddingInsteadOfScreenEdge()
    {
        var lane=CrestronHomePages.SavedEndpointScrollLane(Page());
        Assert.That(lane.Left,Is.EqualTo(28));Assert.That(lane.Right,Is.EqualTo(56));
        Assert.That((lane.Left+lane.Right)/2,Is.EqualTo(42));
        Assert.That(lane.Top,Is.EqualTo(126));Assert.That(lane.Bottom,Is.EqualTo(1474));
    }
    [Test] public void UsesRightInnerPaddingWhenLeftIsOccupied()
    {
        var lane=CrestronHomePages.SavedEndpointScrollLane(Page(fieldLeft:28));
        Assert.That(lane.Left,Is.EqualTo(664));Assert.That(lane.Right,Is.EqualTo(692));
    }
    [Test] public void DoesNotFallBackToUnresponsiveOuterGutter()
        =>Assert.Throws<InvalidOperationException>(()=>CrestronHomePages.SavedEndpointScrollLane(Page(28,692)));
    [Test] public void DisabledViewportCannotBecomeGesture()
        =>Assert.Throws<InvalidOperationException>(()=>CrestronHomePages.SavedEndpointScrollLane(Page(enabled:false)));
    [TestCase("[0,126][720,1474]",0,720)]
    [TestCase("[8,126][712,1474]",8,712)]
    [TestCase("[28,126][800,1474]",56,664)]
    public void MissingOrInvalidGutterCannotInventAnInputLocation(string content,int fieldLeft,int fieldRight)
        =>Assert.Throws<InvalidOperationException>(()=>CrestronHomePages.SavedEndpointScrollLane(Page(fieldLeft,fieldRight,contentBounds:content)));
    [Test] public void MalformedPasswordBoundsAreRejectedWithoutExposingTheirText()
    {
        var error=Assert.Throws<InvalidDataException>(()=>CrestronHomePages.SavedEndpointScrollLane(Page(bounds:"invalid")));
        Assert.That(error!.Message,Does.Not.Contain("private-sentinel"));
    }
}
