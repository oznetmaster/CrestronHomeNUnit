// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeNUnit.Mac;

/// <summary>A fixture-defined binary control. Exact labels/values must come from a retained real app snapshot.
/// This models on/off only; brightness, colour and other properties require their own restoration.</summary>
public sealed record MacBinaryControl(MacSelector State, string On, string Off, bool StateFromValue,
    MacSelector TurnOn, MacSelector TurnOff)
{
    public bool Read(MacHierarchy hierarchy)
    {
        if (string.IsNullOrEmpty(On) || string.IsNullOrEmpty(Off) || On == Off)
            throw new ArgumentException("Provide distinct observed on/off values.");
        var control = hierarchy.Require(State);
        var actual = StateFromValue ? control.Value : control.Label;
        return actual == On ? true : actual == Off ? false :
            throw new InvalidOperationException("UI state is unknown; do not guess or toggle.");
    }

    /// <summary>Reads state first, sends at most one command if needed, then waits for matching UI feedback.</summary>
    public async Task SetAsync(MacTestSession session, bool target, CancellationToken token = default)
    {
        if (Read(await session.ReadAsync(token).ConfigureAwait(false)) != target)
            await session.ClickAsync(target ? TurnOn : TurnOff, token).ConfigureAwait(false);
        await session.WaitAsync(h => Read(h) == target, TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
    }
}
