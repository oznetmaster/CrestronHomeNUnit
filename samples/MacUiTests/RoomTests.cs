// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeNUnit.Mac;
using NUnit.Framework;

namespace MacUiTests;

// No configuration, network access or session creation during discovery.
[NonParallelizable]
public sealed class RoomTests
{
    [Test, Explicit("Select a private Mac profile and reserve the Mac and processor before running."), Category("MacUI")]
    public async Task RoomCanBeInspectedAndHomeRestored()
    {
        var path = Environment.GetEnvironmentVariable("CRESTRON_MAC_PROFILE");
        if (string.IsNullOrWhiteSpace(path)) Assert.Ignore("Set CRESTRON_MAC_PROFILE to a private profile.");
        var profile = JsonSerializer.Deserialize<Profile>(await File.ReadAllTextAsync(path!))!;
        // The owner writes a token in an exclusively reserved file. An absent/changed token prevents input.
        async Task VerifyOwnership(CancellationToken token)
        {
            if (!Guid.TryParseExact(profile.Owner, "N", out _) ||
                (await File.ReadAllTextAsync(profile.OwnershipFile, token)).Trim() != profile.Owner)
                throw new IOException("Mac/processor ownership has not been established.");
        }
        using var transport = new MacHttpTransport(new Uri(profile.Endpoint));
        await using var session = await MacTestSession.OpenAsync(transport,
            Path.Combine(profile.EvidenceRoot, Guid.NewGuid().ToString("N")), profile.Identity,
            VerifyOwnership, TestContext.CurrentContext.CancellationToken);
        var navigation = new MacHomeNavigation(session);
        Exception? failure = null;
        try
        {
            await navigation.OpenRoomAsync(profile.Room, TestContext.CurrentContext.CancellationToken);
            await session.CaptureAsync("inspection", TestContext.CurrentContext.CancellationToken);
        }
        catch (Exception error) { failure = error; }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await navigation.HomeAsync(cleanup.Token); }
        catch (Exception error) { if (failure != null) throw new AggregateException(failure, error); throw; }
        finally
        {
            foreach (var file in Directory.GetFiles(session.EvidenceDirectory)) TestContext.AddTestAttachment(file);
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public sealed record Profile(string Endpoint, string EvidenceRoot, string OwnershipFile, string Owner,
        string Room, MacTestIdentity Identity);
}
