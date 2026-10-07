// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text;
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;
[TestFixture]
public sealed class AndroidInputObservationTests
{
 private static AndroidSelector Selector=>new(AndroidSelectorKind.ResourceId,"example.app:id/power");
 private sealed class Transport : IAndroidCommandTransport {
  internal int Inputs;internal bool Enabled=true;internal string Bounds="[0,0][100,100]";internal IReadOnlyList<string>? LastInput;internal Func<CancellationToken,Task>? Input;
  internal Func<CancellationToken,Task>? Capture;
  public async Task<byte[]> ExecuteAsync(IReadOnlyList<string> args,CancellationToken token) {
   token.ThrowIfCancellationRequested();
   if(args.Contains("uiautomator") && Capture!=null)await Capture(token);
   if(args.Contains("uiautomator"))return Encoding.UTF8.GetBytes("<hierarchy><node package='example.app' resource-id='example.app:id/power' bounds='"+Bounds+"' enabled='"+(Enabled?"true":"false")+"' /></hierarchy>UI hierarchy dumped to: /proc/self/fd/1");
   Inputs++;LastInput=args.ToArray();if(Input!=null)await Input(token);return [];
  }
 }
 [Test] public async Task CompletionCanBeObservedBeforeInputTransportReturns() {
  // Inline continuations make the ordering deterministic, without sleep-based latency assertions.
  var response=new TaskCompletionSource<int>();var held=new TaskCompletionSource();var release=new TaskCompletionSource();
  var transport=new Transport{Input=async token=>{response.SetResult(7);held.SetResult();await release.Task.WaitAsync(token);}};
  var pending=new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>response.Task);
  await held.Task;Assert.That(pending.IsCompleted,Is.False,"Success also requires confirmed input transport completion");release.SetResult();
  var result=await pending;
  Assert.Multiple(()=>{Assert.That(result.Value,Is.EqualTo(7));Assert.That(result.ElapsedMilliseconds,Is.LessThan(result.InputTransportMilliseconds));Assert.That(transport.Inputs,Is.EqualTo(1));});
 }
 [Test] public async Task ObservationAfterTransportStillUsesTheActualObservationEndpoint() {
  var returned=new TaskCompletionSource();var response=new TaskCompletionSource<int>();
  var transport=new Transport{Input=_=>{returned.SetResult();return Task.CompletedTask;}};
  var pending=new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>response.Task);
  await returned.Task;Assert.That(pending.IsCompleted,Is.False);response.SetResult(9);var result=await pending;
  Assert.That(result.Value,Is.EqualTo(9));Assert.That(result.ElapsedMilliseconds,Is.GreaterThanOrEqualTo(result.InputTransportMilliseconds));
 }
 [TestCase(false)][TestCase(true)] public async Task InvalidPageOrDisabledControlStartsNeitherInputNorObserver(bool disabled) {
  int observers=0;var transport=new Transport{Enabled=!disabled};
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,
   _=>{if(!disabled)throw new InvalidOperationException("Wrong page");},_=>{observers++;return Task.FromResult(1);}));
  Assert.That(observers,Is.Zero);Assert.That(transport.Inputs,Is.Zero);
 }
 [Test] public async Task AlreadyCompleteObservationCannotDescribeAnUnsentInput() {
  var transport=new Transport();await Assert.ThrowsAsync<InvalidOperationException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>Task.FromResult(1)));
  Assert.That(transport.Inputs,Is.Zero);
 }
 [Test] public async Task FailedInputCancelsAndJoinsObserverWithoutRetryOrSuccess() {
  bool cancelled=false;var transport=new Transport{Input=_=>throw new IOException("Synthetic transport failure")};
  await Assert.ThrowsAsync<IOException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},async token=>{
   try{await Task.Delay(Timeout.InfiniteTimeSpan,token);return 1;}finally{cancelled=true;}}));
  Assert.That(cancelled,Is.True);Assert.That(transport.Inputs,Is.EqualTo(1));
 }
 [Test] public async Task CompletedObservationCannotHideFailedInput() {
  var response=new TaskCompletionSource<int>();var transport=new Transport{Input=_=>{response.SetResult(1);throw new TimeoutException("Synthetic uncertain input");}};
  await Assert.ThrowsAsync<TimeoutException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>response.Task));Assert.That(transport.Inputs,Is.EqualTo(1));
 }
 [Test] public async Task ObserverFailureIsNotAResponseOrAnInputReplay() {
  var response=new TaskCompletionSource<int>();var transport=new Transport{Input=_=>{response.SetException(new InvalidDataException("Attribution changed"));return Task.CompletedTask;}};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>response.Task));Assert.That(transport.Inputs,Is.EqualTo(1));
 }
 [Test] public async Task CancellationJoinsObserverAndInputWithoutRetry() {
  using var cancel=new CancellationTokenSource();bool inputEnded=false,observerEnded=false;var started=new TaskCompletionSource();
  var transport=new Transport{Input=async token=>{started.SetResult();try{await Task.Delay(Timeout.InfiniteTimeSpan,token);}finally{inputEnded=true;}}};
  var task=new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},async token=>{try{await Task.Delay(Timeout.InfiniteTimeSpan,token);return 1;}finally{observerEnded=true;}},cancel.Token);
  await started.Task;await cancel.CancelAsync();await Assert.CatchAsync<OperationCanceledException>(async()=>await task);
  Assert.That(inputEnded&&observerEnded,Is.True);Assert.That(transport.Inputs,Is.EqualTo(1));
 }

 [Test] public async Task ObserverFailureCancelsInputThatHasNotReturnedAndPreservesTheCause() {
  var response=new TaskCompletionSource<int>();bool inputEnded=false;
  var transport=new Transport{Input=async token=>{response.SetException(new InvalidDataException("Attribution lost"));try{await Task.Delay(Timeout.InfiniteTimeSpan,token);}finally{inputEnded=true;}}};
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
  var error=await Assert.ThrowsAsync<InvalidDataException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>response.Task,deadline.Token));
  Assert.That(error!.Message,Is.EqualTo("Attribution lost"));Assert.That(inputEnded,Is.True);Assert.That(transport.Inputs,Is.EqualTo(1));
 }

 [Test] public async Task SlowGuardFinishesBeforeRecordingPreparationAndInputClock() {
  var captureStarted=new TaskCompletionSource();var releaseCapture=new TaskCompletionSource();var prepared=new TaskCompletionSource();var releasePreparation=new TaskCompletionSource();var response=new TaskCompletionSource<int>();
  var events=new List<string>();var transport=new Transport{Capture=async token=>{captureStarted.TrySetResult();await releaseCapture.Task.WaitAsync(token);events.Add("captured");},Input=_=>{events.Add("input");response.SetResult(4);return Task.CompletedTask;}};
  var pending=new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>events.Add("guarded"),_=>{events.Add("observing");return response.Task;},async token=>{events.Add("preparing");prepared.SetResult();await releasePreparation.Task.WaitAsync(token);events.Add("ready");});
  await captureStarted.Task;Assert.That(events,Is.Empty);releaseCapture.SetResult();await prepared.Task;Assert.That(events,Is.EqualTo(new[]{"captured","guarded","preparing"}));Assert.That(transport.Inputs,Is.Zero);
  var preparationEnd=DateTimeOffset.UtcNow;releasePreparation.SetResult();var result=await pending;
  Assert.That(events,Is.EqualTo(new[]{"captured","guarded","preparing","ready","captured","guarded","observing","input"}));Assert.That(result.InputUtc,Is.GreaterThanOrEqualTo(preparationEnd));Assert.That(transport.Inputs,Is.EqualTo(1));
 }
 [Test] public async Task RecordingPreparationFailureSendsNoInputAndStartsNoObserver() {
  int observed=0;var transport=new Transport();
  await Assert.ThrowsAsync<IOException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>{observed++;return Task.FromResult(1);},_=>throw new IOException("Recorder unavailable")));
  Assert.That(transport.Inputs,Is.Zero);Assert.That(observed,Is.Zero);
 }
 [TestCase(false)][TestCase(true)] public async Task WrongPageOrDisabledControlNeverStartsRecording(bool disabled) {
  int prepared=0;var transport=new Transport{Enabled=!disabled};
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{if(!disabled)throw new InvalidOperationException("Wrong page");},_=>Task.FromResult(1),_=>{prepared++;return Task.CompletedTask;}));
  Assert.That(prepared,Is.Zero);Assert.That(transport.Inputs,Is.Zero);
 }
 [Test] public async Task CancelDuringRecordingPreparationSendsNoTap() {
  using var cancel=new CancellationTokenSource();var started=new TaskCompletionSource();bool joined=false;var transport=new Transport();
  var pending=new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>Task.FromResult(1),async token=>{started.SetResult();try{await Task.Delay(Timeout.InfiniteTimeSpan,token);}finally{joined=true;}},cancel.Token);
  await started.Task;await cancel.CancelAsync();await Assert.CatchAsync<OperationCanceledException>(async()=>await pending);Assert.That(joined,Is.True);Assert.That(transport.Inputs,Is.Zero);
 }

 [TestCase(false)][TestCase(true)] public async Task PageOrControlChangedDuringPreparationSendsNoInput(bool disabled) {
  int guards=0,observers=0;var transport=new Transport();
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,
   _=>{if(++guards==2&&!disabled)throw new InvalidOperationException("Page disconnected");},
   _=>{observers++;return Task.FromResult(1);},_=>{if(disabled)transport.Enabled=false;return Task.CompletedTask;}));
  Assert.That(guards,Is.EqualTo(2));Assert.That(transport.Inputs,Is.Zero);Assert.That(observers,Is.Zero);
 }
 [Test] public async Task PreparationUsesFreshControlCoordinatesAndSendsOnlyOneTap() {
  var response=new TaskCompletionSource<int>();var transport=new Transport{Input=_=>{response.SetResult(7);return Task.CompletedTask;}};
  var result=await new AndroidDevice(transport,"example.app").ObserveTapAsync(Selector,_=>{},_=>response.Task,
   _=>{transport.Bounds="[200,300][300,400]";return Task.CompletedTask;});
  Assert.That(result.Value,Is.EqualTo(7));Assert.That(transport.Inputs,Is.EqualTo(1));
  Assert.That(transport.LastInput!.TakeLast(2),Is.EqualTo(new[]{"250","350"}));
 }
}
