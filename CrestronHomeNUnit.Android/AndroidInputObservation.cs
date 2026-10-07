// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeNUnit.Android;

/// <summary>One attributed observation measured from the guarded input start. Transport and observation finish independently.
/// Elapsed time uses a monotonic clock; UTC values only locate the events. No physical or first-visible latency is implied.</summary>
public sealed record AndroidInputObservation<T>(T Value, DateTimeOffset InputUtc, DateTimeOffset ObservedUtc,
 DateTimeOffset InputReturnedUtc, double ElapsedMilliseconds, double InputTransportMilliseconds);
