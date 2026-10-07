// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System;
using System.IO;

namespace CrestronHomeNUnit.Transport;

public sealed class NetworkLinkClockAnchor
{
    public string Id { get; set; } = "";
    public long Frequency { get; set; }
    public long Clock { get; set; }
    public DateTimeOffset RequestUtc { get; set; }
    public DateTimeOffset ResponseUtc { get; set; }
    public double ElapsedSeconds { get; set; }
}

/// <summary>Maps a processor-local carrier transition into a conservative controller UTC
/// interval. Request/response round trips bracket clock readings; no equal-clock assumption
/// is made. Two anchors detect offset discontinuities. 100 ppm drift plus 50 ms is allowed
/// in each direction. This describes physical carrier, not application/API readiness.</summary>
public static class NetworkLinkTiming
{
    public static (DateTimeOffset EarliestUtc, DateTimeOffset LatestUtc) Window(
        NetworkLinkTrace trace, NetworkLinkClockAnchor before, NetworkLinkClockAnchor after)
    {
        if (trace.State != "Complete" || trace.Error != "" || trace.Interface != "eth0" ||
            trace.Frequency <= 0 || trace.Frequency > 10000000000L || trace.Baseline == null ||
            trace.LastDown == null || trace.FirstUp == null || trace.Samples < 3 ||
            trace.MaximumGap <= 0 || trace.MaximumGap > trace.Frequency * 2 ||
            !trace.Baseline.Carrier || trace.LastDown.Carrier || !trace.FirstUp.Carrier ||
            trace.LastDown.Changes != trace.Baseline.Changes + 1 ||
            trace.FirstUp.Changes != trace.Baseline.Changes + 2 ||
            trace.Baseline.Started < 0 || trace.Baseline.Finished < trace.Baseline.Started ||
            trace.LastDown.Started < trace.Baseline.Finished || trace.LastDown.Finished < trace.LastDown.Started ||
            trace.FirstUp.Started < trace.LastDown.Finished || trace.FirstUp.Finished < trace.FirstUp.Started ||
            trace.FirstUp.Finished - trace.LastDown.Started > trace.Frequency * 2 ||
            trace.Clock < trace.FirstUp.Finished)
            throw new InvalidDataException("No uninterrupted, bounded physical link restoration was recorded.");
        ValidateAnchor(before, trace); ValidateAnchor(after, trace);
        if (before.Clock > trace.LastDown.Started || after.Clock < trace.FirstUp.Finished ||
            after.Clock <= before.Clock || after.RequestUtc < before.ResponseUtc ||
            (after.Clock - before.Clock) / (double)trace.Frequency > 1800)
            throw new InvalidDataException("Clock anchors do not surround this physical cycle.");
        // Check both clocks against both round-trip bounds, allowing bounded oscillator
        // drift. A UTC jump cannot silently shift the physical event nearer recovery.
        var cross = Map(before, after.Clock);
        if (cross.Item2 < after.RequestUtc || cross.Item1 > after.ResponseUtc)
            throw new InvalidDataException("Controller clock changed or processor clock calibration is inconsistent.");
        var a = Map(before, trace.LastDown.Started);
        var b = Map(after, trace.LastDown.Started);
        var c = Map(before, trace.FirstUp.Finished);
        var d = Map(after, trace.FirstUp.Finished);
        var first = a.Item1 > b.Item1 ? a.Item1 : b.Item1;
        var last = c.Item2 < d.Item2 ? c.Item2 : d.Item2;
        if (first > last) throw new InvalidDataException("Clock intervals contradict each other.");
        return (first, last);
    }
    private static void ValidateAnchor(NetworkLinkClockAnchor anchor, NetworkLinkTrace trace)
    {
        double utcElapsed = (anchor.ResponseUtc - anchor.RequestUtc).TotalSeconds;
        if (anchor.Id != trace.Id || anchor.Frequency != trace.Frequency || anchor.Clock < 0 ||
            double.IsNaN(anchor.ElapsedSeconds) || double.IsInfinity(anchor.ElapsedSeconds) ||
            anchor.ElapsedSeconds < 0 || anchor.ElapsedSeconds > 5 || utcElapsed < 0 ||
            Math.Abs(utcElapsed - anchor.ElapsedSeconds) > .05)
            throw new InvalidDataException("Unbound, slow or discontinuous clock calibration.");
    }
    private static (DateTimeOffset, DateTimeOffset) Map(NetworkLinkClockAnchor anchor, long ticks)
    {
        double seconds = (ticks - anchor.Clock) / (double)anchor.Frequency;
        double uncertainty = Math.Abs(seconds) * .0001 + .05;
        return (anchor.RequestUtc.AddSeconds(seconds - uncertainty), anchor.ResponseUtc.AddSeconds(seconds + uncertainty));
    }
}
