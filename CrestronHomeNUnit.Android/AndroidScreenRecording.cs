// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeNUnit.Android;

/// <summary>Optional transport support for observing output as it arrives. Output memory is valid only during the callback.</summary>
public interface IAndroidStreamingCommandTransport : IAndroidCommandTransport
{
 Task<byte[]> ExecuteStreamingAsync(IReadOnlyList<string> arguments, Action<ReadOnlyMemory<byte>> output, CancellationToken cancellationToken);
}

/// <summary>A recording that has produced its first encoded display frame. The caller must retain and review Completion.</summary>
public sealed class AndroidScreenRecording : IAsyncDisposable
{
 private readonly CancellationTokenSource lifetime;
 private readonly Func<CancellationToken, Task>? stop;
 private readonly Func<CancellationToken, Task>? cleanup;
 private readonly object stopLock = new();
 private Task<byte[]>? stopped;
 internal AndroidScreenRecording(DateTimeOffset firstFrameReceivedUtc, Task<byte[]> completion, CancellationTokenSource lifetime, Func<CancellationToken, Task>? stop = null, Func<CancellationToken, Task>? cleanup = null)
 { FirstFrameReceivedUtc=firstFrameReceivedUtc;Completion=completion;this.lifetime=lifetime;this.stop=stop;this.cleanup=cleanup; }
 public DateTimeOffset FirstFrameReceivedUtc { get; }
 public Task<byte[]> Completion { get; }
 /// <summary>Retained failure categories from an earlier read-only startup attempt; no input was sent by the recorder.</summary>
 public IReadOnlyList<string> StartupFailures { get; internal set; } = Array.Empty<string>();
 /// <summary>Finish a caller-controlled recording after the required feedback has been observed.
 /// An already-ended stream is incomplete evidence. Multiple calls share one stop request.</summary>
 public Task<byte[]> StopAsync(CancellationToken cancellationToken = default)
 {
  lock(stopLock) return (stopped ??= StopCoreAsync()).WaitAsync(cancellationToken);
 }
 private async Task<byte[]> StopCoreAsync()
 {
  if(stop == null) return await Completion.ConfigureAwait(false);
  if(Completion.IsCompleted) { await Completion.ConfigureAwait(false); throw new InvalidDataException("Recording ended before feedback capture was completed."); }
  using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
  await stop(deadline.Token).ConfigureAwait(false);
  return await Completion.WaitAsync(deadline.Token).ConfigureAwait(false);
 }
 public async ValueTask DisposeAsync()
 {
  if(stop != null) { try { if(!Completion.IsCompleted) await StopAsync().ConfigureAwait(false); else { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await stop(cleanup.Token).ConfigureAwait(false); } } catch { } }
  await lifetime.CancelAsync().ConfigureAwait(false);
  try { await Completion.ConfigureAwait(false); } catch { }
  if(cleanup != null) { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await cleanup(deadline.Token).ConfigureAwait(false); }
  lifetime.Dispose();
 }
}
