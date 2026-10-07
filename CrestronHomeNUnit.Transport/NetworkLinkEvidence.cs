// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrestronHomeNUnit.Transport;

[DataContract]
public sealed class NetworkLinkSample
{
    [DataMember] public long Started { get; set; }
    [DataMember] public long Finished { get; set; }
    [DataMember] public bool Carrier { get; set; }
    [DataMember] public long Changes { get; set; }
}

[DataContract]
public sealed class NetworkLinkTrace
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Interface { get; set; } = "eth0";
    [DataMember] public long Frequency { get; set; }
    [DataMember] public long Clock { get; set; }
    [DataMember] public string State { get; set; } = "Armed";
    [DataMember] public string Error { get; set; } = "";
    [DataMember] public NetworkLinkSample? Baseline { get; set; }
    [DataMember] public NetworkLinkSample? LastDown { get; set; }
    [DataMember] public NetworkLinkSample? FirstUp { get; set; }
    [DataMember] public long Samples { get; set; }
    [DataMember] public long MaximumGap { get; set; }

    public string ToJson()
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(NetworkLinkTrace)).WriteObject(stream, this);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static NetworkLinkTrace FromJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (NetworkLinkTrace)new DataContractJsonSerializer(typeof(NetworkLinkTrace)).ReadObject(stream)!;
    }
}

/// <summary>Validates a single physical up/down/up cycle. Counter jumps, sampling gaps and
/// invalid clocks are failures, never inferred transitions. Times use one monotonic clock.</summary>
public sealed class NetworkLinkCycle
{
    private readonly NetworkLinkTrace _trace;
    private NetworkLinkSample? _previous;
    public NetworkLinkCycle(string id, long frequency)
    {
        if (!Guid.TryParseExact(id, "N", out _) || frequency <= 0 || frequency > 10000000000L)
            throw new ArgumentException("A fresh trace identity and clock frequency are required.");
        _trace = new NetworkLinkTrace { Id = id, Frequency = frequency };
    }
    public NetworkLinkTrace Snapshot(long clock)
    {
        // Return a detached copy: callers cannot change recorded samples.
        _trace.Clock = clock;
        return NetworkLinkTrace.FromJson(_trace.ToJson());
    }
    public void Fail(string reason) { _trace.State = "Failed"; _trace.Error = reason; }
    public void Add(NetworkLinkSample sample)
    {
        if (_trace.State is "Complete" or "Failed") return;
        if (sample.Started < 0 || sample.Finished < sample.Started || sample.Changes < 0 ||
            sample.Finished - sample.Started > _trace.Frequency)
        { Fail("Invalid or excessively slow kernel observation."); return; }
        if (_previous == null)
        {
            if (!sample.Carrier) { Fail("Interface must be physically connected when armed."); return; }
            _trace.Baseline = Copy(sample);
        }
        else
        {
            long gap = sample.Finished - _previous.Started;
            _trace.MaximumGap = Math.Max(_trace.MaximumGap, gap);
            if (sample.Started < _previous.Finished || gap > _trace.Frequency * 2)
            { Fail("Sampling gap or monotonic clock discontinuity."); return; }
            long expected = _previous.Changes + (sample.Carrier == _previous.Carrier ? 0 : 1);
            if (sample.Changes != expected)
            { Fail("Physical carrier transitions were missed or the counter changed unexpectedly."); return; }
            if (!sample.Carrier) { _trace.LastDown = Copy(sample); _trace.State = "Disconnected"; }
            else if (_trace.LastDown != null)
            { _trace.FirstUp = Copy(sample); _trace.State = "Complete"; }
        }
        _trace.Samples++;
        _previous = Copy(sample);
    }
    private static NetworkLinkSample Copy(NetworkLinkSample x) => new()
        { Started = x.Started, Finished = x.Finished, Carrier = x.Carrier, Changes = x.Changes };
}

/// <summary>Opt-in, read-only Linux carrier recorder owned by one test-host server.
/// Survives client disconnection, expires after 30 minutes, and stops on server disposal.
/// It never changes a network interface or device state.</summary>
internal sealed class NetworkLinkRecorder : IDisposable
{
    private readonly object _gate = new();
    private NetworkLinkCycle? _cycle;
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private bool _disposed;

    public string Execute(string command, string id)
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NetworkLinkRecorder));
            if (command == "link-arm")
            {
                if (_cycle != null) throw new InvalidOperationException("A trace already exists; export and remove this temporary host before another recording.");
                var cycle = new NetworkLinkCycle(id, Stopwatch.Frequency);
                cycle.Add(Read());
                if (cycle.Snapshot(Stopwatch.GetTimestamp()).State == "Failed")
                    throw new InvalidDataException("Physical carrier baseline is unavailable.");
                _cycle = cycle;
                _lifetime = new CancellationTokenSource();
                _worker = ObserveAsync(cycle, _lifetime.Token);
            }
            if (_cycle == null || _cycle.Snapshot(Stopwatch.GetTimestamp()).Id != id)
                throw new InvalidOperationException("Trace identity does not match this host instance.");
            if (command == "link-stop") _lifetime!.Cancel();
            else if (command != "link-arm" && command != "link-read") throw new ArgumentException("Unknown link observation command.");
            string result = _cycle.Snapshot(Stopwatch.GetTimestamp()).ToJson();
            // A stopped trace remains readable, but cannot be silently replaced. Remove the
            // temporary test host after exporting it. This also prevents delayed requests
            // from being attributed to a new recording.
            return result;
        }
    }
    private async Task ObserveAsync(NetworkLinkCycle cycle, CancellationToken token)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (token.IsCancellationRequested) return;
                    if (Stopwatch.GetTimestamp() - start > Stopwatch.Frequency * 1800L)
                    { cycle.Fail("Observation expired without a complete physical cycle."); return; }
                    cycle.Add(Read());
                    if (cycle.Snapshot(Stopwatch.GetTimestamp()).State is "Complete" or "Failed") return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { lock (_gate) cycle.Fail("Carrier observation failed: " + error.GetType().Name); }
    }
    private static NetworkLinkSample Read()
    {
        const string root = "/sys/class/net/eth0/";
        // A counter change during the read is a torn sample. Retry immediately;
        // the state machine still validates the gap and every intervening transition.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            long start = Stopwatch.GetTimestamp();
            long before = long.Parse(File.ReadAllText(root + "carrier_changes").Trim(), System.Globalization.CultureInfo.InvariantCulture);
            string carrier = File.ReadAllText(root + "carrier").Trim();
            long after = long.Parse(File.ReadAllText(root + "carrier_changes").Trim(), System.Globalization.CultureInfo.InvariantCulture);
            long end = Stopwatch.GetTimestamp();
            if (carrier != "0" && carrier != "1") throw new InvalidDataException("Unknown carrier state.");
            if (before == after) return new NetworkLinkSample { Started = start, Finished = end, Carrier = carrier == "1", Changes = after };
        }
        throw new InvalidDataException("Unstable physical carrier sample.");
    }
    public void Dispose()
    {
        Task? worker;
        lock (_gate) { if (_disposed) return; _disposed = true; _lifetime?.Cancel(); worker = _worker; }
        worker?.GetAwaiter().GetResult();
        _lifetime?.Dispose();
    }
}
