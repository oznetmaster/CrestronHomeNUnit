// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;
[TestFixture]
public sealed class ControlledScreenRecordingTests
{
 private sealed class Transport : IAndroidStreamingCommandTransport {
  internal string Outcome="stopped:0"; internal int Stops, Starts; internal string? Script, StopCommand;
  internal TaskCompletionSource<byte[]> Video = new(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task<byte[]> ExecuteStreamingAsync(IReadOnlyList<string> args, Action<ReadOnlyMemory<byte>> output, CancellationToken token) {
   Starts++; Assert.That(args.Take(3), Is.EqualTo(new[]{"exec-out","sh","-c"})); Script=args[3];
   output(new byte[]{0,0,0,1,101,55}); return Video.Task.WaitAsync(token);
  }
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token) {
   token.ThrowIfCancellationRequested(); Assert.That(args[0],Is.EqualTo("shell"));
   if(args[1].StartsWith("cat "))return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(Outcome));
   if(args[1].StartsWith("rm -f "))return Task.FromResult(System.Text.Encoding.UTF8.GetBytes("removed"));
   Stops++; StopCommand=args[1];
   Video.TrySetResult([0,0,0,1,101,55]); return Task.FromResult(Array.Empty<byte>());
  }
 }
 [Test] public async Task RecordingSurvivesPreparationAndStopsOnlyWhenCallerFinishesFeedback() {
  var t=new Transport(); await using var r=await new AndroidDevice(t,"example.app").StartScreenRecordingUntilStoppedAsync();
  Assert.That(r.Completion.IsCompleted,Is.False); Assert.That(t.Stops,Is.Zero);
  var first=r.StopAsync();var second=r.StopAsync(); Assert.That(await first,Is.EqualTo(await second));
  Assert.That(t.Stops,Is.EqualTo(1));Assert.That(t.Starts,Is.EqualTo(1));
  Assert.That(t.Script,Does.StartWith("mkdir "));
  Assert.That(t.Script,Does.Contain("--time-limit 90").And.Contain("echo expired:$result").And.Contain("kill -INT $child"));
  var match=System.Text.RegularExpressions.Regex.Match(t.Script!,"/data/local/tmp/chn-recording-[a-f0-9]{32}");
  Assert.That(match.Success,Is.True); Assert.That(t.StopCommand,Is.EqualTo($"test -d {match.Value} && touch {match.Value}/stop"));
 }
 [Test] public async Task EndedStreamCannotBeMistakenForFeedbackCoverage() {
  var t=new Transport();await using var r=await new AndroidDevice(t,"example.app").StartScreenRecordingUntilStoppedAsync();
  t.Video.SetResult([0,0,0,1,101,55]);await r.Completion;
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await r.StopAsync());Assert.That(t.Starts,Is.EqualTo(1));
 }
 [Test] public async Task RecorderWatchdogFailureIsRetainedWithoutRestartOrInput() {
  var t=new Transport();await using var r=await new AndroidDevice(t,"example.app").StartScreenRecordingUntilStoppedAsync();
  t.Video.SetException(new IOException("Recorder watchdog expired"));
  await Assert.ThrowsAsync<IOException>(async()=>await r.Completion);
  await Assert.ThrowsAsync<IOException>(async()=>await r.StopAsync());Assert.That(t.Starts,Is.EqualTo(1));
 }
 [Test] public async Task ExpiredRecorderIsRejectedEvenWhenAdbReturnsSuccessAndValidVideo() {
  var t=new Transport { Outcome="expired:0" };await using var r=await new AndroidDevice(t,"example.app").StartScreenRecordingUntilStoppedAsync();
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await r.StopAsync());Assert.That(t.Starts,Is.EqualTo(1));
 }
 [Test] public async Task DisposalStopsAndJoinsAnUnfinishedRecording() {
  var t=new Transport();var r=await new AndroidDevice(t,"example.app").StartScreenRecordingUntilStoppedAsync();
  await r.DisposeAsync();Assert.That(r.Completion.IsCompletedSuccessfully,Is.True);Assert.That(t.Stops,Is.EqualTo(1));
 }
 [Test] public async Task CancellationStillCleansUpTheOwnedRecorder() {
  var t=new Transport();using var owner=new CancellationTokenSource();var r=await new AndroidDevice(t,"example.app").StartScreenRecordingUntilStoppedAsync(owner.Token);
  await owner.CancelAsync();await Assert.CatchAsync<OperationCanceledException>(async()=>await r.Completion);
  await r.DisposeAsync();Assert.That(t.Stops,Is.EqualTo(1));Assert.That(t.Starts,Is.EqualTo(1));
 }
 private sealed class ColdStartTransport:IAndroidStreamingCommandTransport {
  internal int Starts,Cleanups;internal TaskCompletionSource<byte[]> Video=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task<byte[]> ExecuteStreamingAsync(IReadOnlyList<string> args,Action<ReadOnlyMemory<byte>> output,CancellationToken token) {
   if(++Starts==1)return Task.FromResult(Array.Empty<byte>());output(new byte[]{0,0,0,1,101,55});return Video.Task.WaitAsync(token);
  }
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> args,CancellationToken token) {
   if(args[1].StartsWith("cat "))return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(Starts==1?"expired:0":"stopped:0"));
   if(args[1].StartsWith("rm -f ")){Cleanups++;return Task.FromResult(System.Text.Encoding.UTF8.GetBytes("removed"));}
   if(Starts>1)Video.TrySetResult([0,0,0,1,101,55]);return Task.FromResult(Array.Empty<byte>());
  }
 }
 [Test] public async Task ColdStartupCanRetryOnlyAfterConfirmedCleanupBeforeReturningReady() {
  var t=new ColdStartTransport();await using var r=await new AndroidDevice(t,"example.app").StartScreenRecordingUntilStoppedAsync();
  Assert.That(t.Starts,Is.EqualTo(2));Assert.That(t.Cleanups,Is.EqualTo(1));Assert.That(r.StartupFailures,Is.EqualTo(new[]{"InvalidDataException"}));
  Assert.That(await r.StopAsync(),Has.Length.EqualTo(6));
 }
}
