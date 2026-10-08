// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestronHomeNUnit.Mac;

/// <summary>Identity supplied by the fixture after independently checking the processor and driver.
/// Metadata is attribution, not proof of an active app connection.</summary>
public sealed record MacTestIdentity(string ProcessorAddress, string DriverIdentity, string CandidateSha256,
    string AppVersion, string MacOsVersion);

/// <summary>One explicitly owned Appium session. Use serially; retain evidence in a new private directory.
/// The job owns the Mac/processor reservations, tunnel and Appium/XCTest service lifetime.</summary>
public sealed class MacTestSession : IAsyncDisposable
{
    private readonly IMacTransport transport;
    private readonly string root;
    private readonly Func<CancellationToken, Task> verifyOwnership;
    private string? sessionId;
    private int sequence;
    private bool disposed;
    public string EvidenceDirectory => root;
    public const string BundleId = "com.crestron.home";
    private const string ElementKey = "element-6066-11e4-a52e-4f735466cecf";

    private MacTestSession(IMacTransport transport, string root, Func<CancellationToken, Task> verifyOwnership)
    { this.transport = transport; this.root = root; this.verifyOwnership = verifyOwnership; }

    public static async Task<MacTestSession> OpenAsync(IMacTransport transport, string evidenceDirectory,
        MacTestIdentity identity, Func<CancellationToken, Task> verifyOwnership, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(verifyOwnership);
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.ProcessorAddress) || string.IsNullOrWhiteSpace(identity.DriverIdentity) ||
            string.IsNullOrWhiteSpace(identity.AppVersion) || string.IsNullOrWhiteSpace(identity.MacOsVersion) ||
            identity.CandidateSha256.Length != 64 || !identity.CandidateSha256.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Provide verified processor, driver, candidate, app and OS identities.", nameof(identity));
        if (!Path.IsPathFullyQualified(evidenceDirectory) || Directory.Exists(evidenceDirectory) || File.Exists(evidenceDirectory))
            throw new ArgumentException("Provide a new absolute private evidence directory.", nameof(evidenceDirectory));
        await verifyOwnership(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(evidenceDirectory);
        var session = new MacTestSession(transport, evidenceDirectory, verifyOwnership);
        await session.RecordAsync("identity", new { Identity = identity, BundleId, StartedUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        try
        {
            var created = await transport.SendAsync(HttpMethod.Post, "session", new
            {
                capabilities = new { alwaysMatch = new Dictionary<string, object>
                {
                    ["platformName"] = "mac", ["appium:automationName"] = "mac2", ["appium:bundleId"] = BundleId,
                    ["appium:noReset"] = true, ["appium:skipAppKill"] = true,
                    ["appium:systemHost"] = "127.0.0.1", ["appium:serverStartupTimeout"] = 60000
                } }
            }, cancellationToken).ConfigureAwait(false);
            session.sessionId = created.GetProperty("sessionId").GetString();
            if (string.IsNullOrWhiteSpace(session.sessionId)) throw new InvalidDataException("Mac2 returned no session identity.");
            await session.RecordAsync("session", new { session.sessionId }, cancellationToken).ConfigureAwait(false);
            await session.CommandAsync(HttpMethod.Post, "execute/sync", new { script = "macos: activateApp", args = new[] { new { bundleId = BundleId } } }, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception original)
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Mac session opening and cleanup failed.", original, cleanup); }
            throw;
        }
    }

    private async Task<JsonElement> CommandAsync(HttpMethod method, string suffix, object? body, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await verifyOwnership(token).ConfigureAwait(false);
        return await transport.SendAsync(method, "session/" + Uri.EscapeDataString(sessionId!) + "/" + suffix, body, token).ConfigureAwait(false);
    }

    public async Task<MacHierarchy> ReadAsync(CancellationToken token = default)
    {
        var value = await CommandAsync(HttpMethod.Get, "source", null, token).ConfigureAwait(false);
        return new MacHierarchy(value.GetString() ?? throw new InvalidDataException("No Mac hierarchy returned."));
    }

    /// <summary>Retains the exact hierarchy, screenshot and SHA-256 inventory. Evidence may contain private Home data.</summary>
    public async Task<MacHierarchy> CaptureAsync(string label, CancellationToken token = default)
    {
        var hierarchy = await ReadAsync(token).ConfigureAwait(false);
        var observed = DateTimeOffset.UtcNow;
        var image = await CommandAsync(HttpMethod.Get, "screenshot", null, token).ConfigureAwait(false);
        var png = Convert.FromBase64String(image.GetString() ?? "");
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
            throw new InvalidDataException("Mac2 screenshot is not PNG.");
        var prefix = Next(label);
        var xml = Encoding.UTF8.GetBytes(hierarchy.Xml);
        await WriteNewAsync(prefix + ".xml", xml, token).ConfigureAwait(false);
        await WriteNewAsync(prefix + ".png", png, token).ConfigureAwait(false);
        await WriteNewAsync(prefix + ".json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            HierarchyObservedUtc = observed, CapturedUtc = DateTimeOffset.UtcNow,
            XmlSha256 = Convert.ToHexStringLower(SHA256.HashData(xml)), PngSha256 = Convert.ToHexStringLower(SHA256.HashData(png))
        }), token).ConfigureAwait(false);
        return hierarchy;
    }

    /// <summary>Retains fresh evidence and intent, requires a unique visible control, then sends exactly one click.
    /// An ambiguous/failed click is not replayed. Use only for explicitly reviewed navigation or device controls.</summary>
    public async Task ClickAsync(MacSelector selector, CancellationToken token = default)
    {
        (await CaptureAsync("before-click", token).ConfigureAwait(false)).Require(selector);
        // Re-read after screenshot latency, before resolving the remote element.
        (await ReadAsync(token).ConfigureAwait(false)).Require(selector);
        var elements = await CommandAsync(HttpMethod.Post, "elements", new { @using = "xpath", value = selector.XPath }, token).ConfigureAwait(false);
        if (elements.GetArrayLength() != 1) throw new InvalidOperationException("Mac2 did not resolve exactly one control.");
        var id = elements[0].GetProperty(ElementKey).GetString() ?? throw new InvalidDataException("Missing element identity.");
        await RecordAsync("input-intent", new { Selector = selector, Utc = DateTimeOffset.UtcNow }, token).ConfigureAwait(false);
        await CommandAsync(HttpMethod.Post, "element/" + Uri.EscapeDataString(id) + "/click", new { }, token).ConfigureAwait(false);
        await RecordAsync("input-returned", new { Utc = DateTimeOffset.UtcNow }, token).ConfigureAwait(false);
    }

    /// <summary>Only reads are repeated. The deadline is a tooling observation budget, not a driver acceptance threshold.</summary>
    public async Task<MacHierarchy> WaitAsync(Func<MacHierarchy, bool> predicate, TimeSpan timeout, CancellationToken token = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        while (true)
        {
            var state = await ReadAsync(deadline.Token).ConfigureAwait(false);
            if (predicate(state)) return state;
            await Task.Delay(250, deadline.Token).ConfigureAwait(false);
        }
    }

    /// <summary>App feedback and independently observed hardware/processor state must both equal the target.
    /// Always attempts restoration with an independent cleanup deadline. Both failures are preserved.</summary>
    public async Task VerifyBinaryControlAsync(Func<CancellationToken, Task<bool>> readIndependentState,
        Func<bool, CancellationToken, Task> setStateAndVerifyUi, Func<CancellationToken, Task> restoreHome,
        CancellationToken token = default)
    {
        bool initial = await readIndependentState(token).ConfigureAwait(false);
        await RecordAsync("initial-state", new { On = initial }, token).ConfigureAwait(false);
        Exception? failure = null;
        var cleanupErrors = new List<Exception>();
        try
        {
            await setStateAndVerifyUi(!initial, token).ConfigureAwait(false);
            if (await readIndependentState(token).ConfigureAwait(false) != !initial)
                throw new InvalidOperationException("Independent device state disagrees with Mac UI feedback.");
            await CaptureAsync("changed", token).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            // The setter must inspect fresh state and issue an explicit target-state command, never blindly toggle.
            await setStateAndVerifyUi(initial, cleanup.Token).ConfigureAwait(false);
            if (await readIndependentState(cleanup.Token).ConfigureAwait(false) != initial)
                throw new InvalidOperationException("Independent device restoration was not confirmed.");
            await CaptureAsync("restored", cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception error) { cleanupErrors.Add(error); }
        using var homeCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await restoreHome(homeCleanup.Token).ConfigureAwait(false); }
        catch (Exception error) { cleanupErrors.Add(error); }
        await RecordAsync("control-result", new { Passed = failure == null && cleanupErrors.Count == 0,
            Restored = cleanupErrors.Count == 0, Failure = failure?.GetType().Name,
            CleanupFailures = cleanupErrors.Select(e => e.GetType().Name).ToArray() }, CancellationToken.None).ConfigureAwait(false);
        if (failure != null) cleanupErrors.Insert(0, failure);
        if (cleanupErrors.Count > 0) throw new AggregateException("Mac UI control test or restoration failed; inspect retained evidence.", cleanupErrors);
    }

    private string Next(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 60 || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Use a short alphanumeric evidence label.", nameof(label));
        return Path.Combine(root, $"{++sequence:D4}-{label}");
    }
    private Task RecordAsync(string label, object value, CancellationToken token) =>
        WriteNewAsync(Next(label) + ".json", JsonSerializer.SerializeToUtf8Bytes(value), token);
    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await file.WriteAsync(bytes, token).ConfigureAwait(false);
        await file.FlushAsync(token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (sessionId != null)
                await transport.SendAsync(HttpMethod.Delete, "session/" + Uri.EscapeDataString(sessionId), null, cleanup.Token).ConfigureAwait(false);
            await RecordAsync("session-closed", new { Utc = DateTimeOffset.UtcNow, SessionDeleted = sessionId != null,
                HostShutdownStillRequired = true }, cleanup.Token).ConfigureAwait(false);
        }
        catch
        {
            await RecordAsync("session-cleanup-failed", new { Utc = DateTimeOffset.UtcNow, HostShutdownStillRequired = true }, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
