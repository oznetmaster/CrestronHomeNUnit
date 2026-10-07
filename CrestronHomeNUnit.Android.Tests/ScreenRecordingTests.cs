// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;
[TestFixture]
public sealed class ScreenRecordingTests
{
 private sealed class Transport : IAndroidCommandTransport {
  internal IReadOnlyList<string>? Arguments;
  internal Func<CancellationToken,Task<byte[]>> Result = _ => Task.FromResult<byte[]>([0,0,0,1,103,1]);
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments,CancellationToken token) {Arguments=arguments;return Result(token);}
 }
 [Test] public async Task RecordingStreamsWithoutInputOrAndroidTemporaryFiles() {
  var transport=new Transport();var data=await new AndroidDevice(transport,"example.app").CaptureScreenRecordingAsync(15);
  Assert.That(transport.Arguments,Is.EqualTo(new[]{"exec-out","screenrecord","--output-format=h264","--show-frame-time","--bit-rate","2000000","--time-limit","15","-"}));
  Assert.That(data,Is.EqualTo(new byte[]{0,0,0,1,103,1}));
 }
 [TestCase(0)][TestCase(31)] public async Task RejectsUnboundedOrLongRecordingBeforeTransport(int seconds) {
  var transport=new Transport();await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await new AndroidDevice(transport,"example.app").CaptureScreenRecordingAsync(seconds));Assert.That(transport.Arguments,Is.Null);
 }
 [Test] public async Task DeviceTextIsNotAcceptedAsVideo() {
  var transport=new Transport{Result=_=>Task.FromResult(System.Text.Encoding.UTF8.GetBytes("recorder unavailable"))};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await new AndroidDevice(transport,"example.app").CaptureScreenRecordingAsync(15));
 }
 [Test] public async Task CancelledRecordingIsNotRetried() {
  int calls=0;var transport=new Transport{Result=async token=>{calls++;await Task.Delay(Timeout.InfiniteTimeSpan,token);return [];}};
  using var cancel=new CancellationTokenSource();var pending=new AndroidDevice(transport,"example.app").CaptureScreenRecordingAsync(15,cancel.Token);await cancel.CancelAsync();
  await Assert.CatchAsync<OperationCanceledException>(async()=>await pending);Assert.That(calls,Is.EqualTo(1));
 }
 private sealed class StreamTransport : IAndroidStreamingCommandTransport {
  internal Action<ReadOnlyMemory<byte>>? Observer;
  internal TaskCompletionSource<byte[]> Finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal bool Ended;
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments,CancellationToken token)=>throw new InvalidOperationException("Streaming required");
  public async Task<byte[]> ExecuteStreamingAsync(IReadOnlyList<string> arguments,Action<ReadOnlyMemory<byte>> output,CancellationToken token) {
   Observer=output;try{return await Finished.Task.WaitAsync(token);}finally{Ended=true;}
  }
 }
 [Test] public async Task ReadyRequiresFrameNotOnlyCodecHeadersAndHandlesSplitStartCode() {
  var transport=new StreamTransport();var pending=new AndroidDevice(transport,"example.app").StartScreenRecordingAsync(15);
  transport.Observer!(new byte[]{0,0,0,1,103,42,0,0,1,104,12});Assert.That(pending.IsCompleted,Is.False);
  transport.Observer!(new byte[]{0,0});Assert.That(pending.IsCompleted,Is.False);
  transport.Observer!(new byte[]{1,101,55});await using var recording=await pending;
  Assert.That(recording.Completion.IsCompleted,Is.False);Assert.That(recording.FirstFrameReceivedUtc,Is.Not.EqualTo(default(DateTimeOffset)));
  transport.Finished.SetResult([0,0,0,1,101,55]);Assert.That(await recording.Completion,Has.Length.EqualTo(6));
 }
 [Test] public async Task EndedRecordingCannotClaimReadiness() {
  var transport=new StreamTransport();var pending=new AndroidDevice(transport,"example.app").StartScreenRecordingAsync(15);
  transport.Finished.SetResult([0,0,0,1,103,42]);await Assert.ThrowsAsync<InvalidDataException>(async()=>await pending);Assert.That(transport.Ended,Is.True);
 }
 [Test] public async Task CancelBeforeFirstFrameJoinsTransport() {
  var transport=new StreamTransport();using var cancel=new CancellationTokenSource();var pending=new AndroidDevice(transport,"example.app").StartScreenRecordingAsync(15,cancel.Token);
  await cancel.CancelAsync();await Assert.CatchAsync<OperationCanceledException>(async()=>await pending);Assert.That(transport.Ended,Is.True);
 }

 private sealed class StartupTransport(bool alwaysFails) : IAndroidStreamingCommandTransport {
  internal int Calls; internal TaskCompletionSource<byte[]> Finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> args,CancellationToken ct)=>throw new InvalidOperationException("No inputs allowed");
  public Task<byte[]> ExecuteStreamingAsync(IReadOnlyList<string> args,Action<ReadOnlyMemory<byte>> output,CancellationToken ct){Calls++;if(alwaysFails || Calls==1)return Task.FromResult(Array.Empty<byte>());output(new byte[]{0,0,0,1,101,55});return Finished.Task.WaitAsync(ct);}
 }
 [Test] public async Task EmptyStartupCanRetryBeforeAnyInputAndRetainsItsFailure() {
  var transport=new StartupTransport(false);await using var recording=await new AndroidDevice(transport,"example.app").StartScreenRecordingAsync(15);
  Assert.That(transport.Calls,Is.EqualTo(2));Assert.That(recording.StartupFailures,Is.EqualTo(new[]{"InvalidDataException"}));transport.Finished.SetResult([0,0,0,1,101,55]);await recording.Completion;
 }
 [Test] public async Task RepeatedEmptyStartupStopsAfterTwoAttemptsWithBothFailures() {
  var transport=new StartupTransport(true);var error=await Assert.ThrowsAsync<InvalidDataException>(async()=>await new AndroidDevice(transport,"example.app").StartScreenRecordingAsync(15));
  Assert.That(transport.Calls,Is.EqualTo(2));Assert.That((string?)error!.Data["AndroidRecordingStartupFailures"],Is.EqualTo("[\"InvalidDataException\",\"InvalidDataException\"]"));
 }
}
