// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net.Http.Json;
using System.Text.Json;

namespace CrestronHomeNUnit.Mac;

/// <summary>One W3C WebDriver request. Implementations must never retry input commands.</summary>
public interface IMacTransport
{
    Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken);
}

/// <summary>Bounded Mac2 transport to a loopback Appium service or an authenticated SSH tunnel.
/// Owns its HTTP client, disables redirects, and never starts or installs software.</summary>
public sealed class MacHttpTransport : IMacTransport, IDisposable
{
    private readonly HttpClient client;
    private readonly TimeSpan timeout;
    public MacHttpTransport(Uri endpoint, TimeSpan? commandTimeout = null)
        : this(endpoint, new HttpClientHandler { AllowAutoRedirect = false }, commandTimeout) { }

    internal MacHttpTransport(Uri endpoint, HttpMessageHandler handler, TimeSpan? commandTimeout = null)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "http" ||
            endpoint.Host is not ("127.0.0.1" or "[::1]" or "localhost") ||
            endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
            throw new ArgumentException("Use a loopback HTTP endpoint; use an authenticated tunnel for a remote Mac.", nameof(endpoint));
        timeout = commandTimeout ?? TimeSpan.FromSeconds(90);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(3)) throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        if (!path.StartsWith("session", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) || path.Contains('?') || path.Contains('#'))
            throw new ArgumentException("Expected a relative session command.", nameof(path));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        // Do not expose arbitrary server error text, which can contain private app data.
        if (!response.IsSuccessStatusCode) throw new IOException($"Mac2 returned HTTP {(int)response.StatusCode}; command not retried.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("Mac2 response exceeds 16 MiB.");
            bytes.Write(buffer, 0, count);
        }
        using var json = JsonDocument.Parse(bytes.ToArray());
        var value = json.RootElement.GetProperty("value");
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("error", out _))
            throw new IOException("Mac2 rejected the command; command not retried.");
        return value.Clone();
    }
    public void Dispose() => client.Dispose();
}
