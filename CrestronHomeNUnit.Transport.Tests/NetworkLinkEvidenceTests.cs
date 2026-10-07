// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System;
using System.IO;
using CrestronHomeNUnit.Transport;
using NUnit.Framework;

namespace CrestronHomeNUnit.Tests;

public sealed class NetworkLinkEvidenceTests
{
    private const string Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static NetworkLinkSample Sample(long start, bool up, long count) => new()
        { Started = start, Finished = start + 1, Carrier = up, Changes = count };
    private static NetworkLinkCycle Cycle() => new(Id, 1000);
    [Test] public void ConnectedTraceCannotClaimAnOutage()
    {
        var c = Cycle(); c.Add(Sample(0,true,5)); c.Add(Sample(100,true,5));
        Assert.That(c.Snapshot(110).State,Is.EqualTo("Armed"));
    }
    [Test] public void RecordsLastDownAndFirstUpWithoutUsingOperatorReply()
    {
        var c = Cycle(); c.Add(Sample(0,true,5)); c.Add(Sample(100,false,6));
        c.Add(Sample(200,false,6)); c.Add(Sample(300,true,7));
        var x = c.Snapshot(9000);
        Assert.That(x.State,Is.EqualTo("Complete")); Assert.That(x.LastDown!.Started,Is.EqualTo(200));
        Assert.That(x.FirstUp!.Finished,Is.EqualTo(301)); Assert.That(x.Samples,Is.EqualTo(4));
        x.LastDown.Started = 999; Assert.That(c.Snapshot(9001).LastDown!.Started,Is.EqualTo(200));
    }
    [TestCase(100,true,7)] [TestCase(100,false,8)] [TestCase(100,false,4)]
    [TestCase(3000,false,6)] [TestCase(0,false,6)]
    public void MissedTransitionsGapsResetsAndClockReversalFail(long time,bool up,long count)
    {
        var c = Cycle(); c.Add(Sample(0,true,5)); c.Add(Sample(time,up,count));
        Assert.That(c.Snapshot(4000).State,Is.EqualTo("Failed"));
    }
    [Test] public void DisconnectedBaselineFails()
    { var c=Cycle(); c.Add(Sample(0,false,2)); Assert.That(c.Snapshot(1).State,Is.EqualTo("Failed")); }
    private static (NetworkLinkTrace,NetworkLinkClockAnchor,NetworkLinkClockAnchor) Timeline()
    {
        var c=Cycle(); c.Add(Sample(1000,true,5));
        // The operator takes 24 seconds after the reconnect prompt, then recovery
        // takes 42 seconds. Sampling remains continuous throughout the wait.
        for(long t=1100;t<85000;t+=100)c.Add(Sample(t,false,6));
        c.Add(Sample(85000,true,7));
        var origin=new DateTimeOffset(2026,10,1,0,0,0,TimeSpan.Zero);
        var a=new NetworkLinkClockAnchor {Id=Id,Frequency=1000,Clock=1002,RequestUtc=origin.AddSeconds(1),ResponseUtc=origin.AddSeconds(1.004),ElapsedSeconds=.004};
        var b=new NetworkLinkClockAnchor {Id=Id,Frequency=1000,Clock=128000,RequestUtc=origin.AddSeconds(127.998),ResponseUtc=origin.AddSeconds(128.002),ElapsedSeconds=.004};
        return(c.Snapshot(128000),a,b);
    }
    [Test] public void OperatorWalkingDelayDoesNotConsumeRecoveryBudget()
    {
        var(t,a,b)=Timeline(); var window=NetworkLinkTiming.Window(t,a,b);
        var recovered=a.RequestUtc.AddSeconds(126); // UTC origin +127: 42 seconds after link-up
        Assert.That((recovered-window.EarliestUtc).TotalSeconds,Is.InRange(42,43));
        Assert.That((window.LatestUtc-window.EarliestUtc).TotalSeconds,Is.LessThan(.3));
    }
    [TestCase("epoch")] [TestCase("utc")] [TestCase("duration")] [TestCase("incomplete")]
    [TestCase("counter")] [TestCase("interface")] [TestCase("gap")] [TestCase("frequency")]
    public void InvalidTimingCannotProduceAWindow(string fault)
    {
        var(t,a,b)=Timeline();
        switch(fault) {
            case "epoch": b.Id=new string('b',32);break;
            case "utc": b.RequestUtc=b.RequestUtc.AddSeconds(20);b.ResponseUtc=b.ResponseUtc.AddSeconds(20);break;
            case "duration": b.ElapsedSeconds=4;break;
            case "incomplete": t.State="Armed";break;
            case "counter": t.FirstUp!.Changes++;break;
            case "interface": t.Interface="eth1";break;
            case "gap": t.MaximumGap=3000;break;
            case "frequency": b.Frequency=2000;break;
        }
        Assert.Throws<InvalidDataException>(()=>NetworkLinkTiming.Window(t,a,b));
    }
}
