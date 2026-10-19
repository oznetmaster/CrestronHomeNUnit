// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class AttendedInstalledDriverTests
{
 private sealed class Clock:TimeProvider {
  private long _ticks;
  public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
  public override long GetTimestamp()=>_ticks;
  internal void Advance()=>_ticks+=TimeSpan.FromDays(10000).Ticks;
 }
 private sealed class Operations: IInstalledDriverTestOperations {
  public bool ProcessorAcquired{get;private set;}
  public bool AndroidAcquired{get;private set;}
  internal List<string> Calls=[];
  internal TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal TaskCompletionSource Finish=new(TaskCreationOptions.RunContinuationsAsynchronously);
  internal bool ConfirmCleanup;
  private Task Call(string phase,CancellationToken token){token.ThrowIfCancellationRequested();Calls.Add(phase);return Task.CompletedTask;}
  public async Task AcquireProcessorAsync(CancellationToken token){await Call("processor",token);ProcessorAcquired=true;}
  public async Task AcquireAndroidAsync(CancellationToken token){await Call("android",token);AndroidAcquired=true;}
  public Task VerifyCandidateAsync(string phase,CancellationToken token)=>Call(phase,token);
  public Task BeginControlAsync(CancellationToken token)=>Call("begin",token);
  public async Task<AndroidTestOutcome> RunTestsAsync(CancellationToken token){
   await Call("tests",token);Entered.TrySetResult();
   try{await Finish.Task.WaitAsync(token);return new(new(1,0,0,true),true);}
   catch(OperationCanceledException) when(ConfirmCleanup){return new(new(0,1,0,true),true){CleanupConfirmed=true};}
  }
  public Task EndControlAsync(CancellationToken token)=>Call("end",token);
  public Task ReleaseAndroidAsync(CancellationToken token)=>Call("release-android",token);
  public Task ReleaseProcessorAsync(CancellationToken token)=>Call("release-processor",token);
 }
 [Test] public async Task PublicUnlimitedLifetimeAllowsLongHumanWaitThroughOwnedPhaseCoordinator(){
  var clock=new Clock();using var explicitCancel=new CancellationTokenSource();
  await using var lifetime=WorkflowActiveDeadline.WithoutElapsedTimeout(explicitCancel.Token,clock);
  var ops=new Operations();var task=InstalledDriverTests.RunCoreAsync(ops,lifetime.Token);
  await ops.Entered.Task;clock.Advance();
  Assert.That(lifetime.Token.IsCancellationRequested,Is.False);Assert.That(task.IsCompleted,Is.False);
  ops.Finish.SetResult();var result=await task;
  Assert.That(result.Passed,Is.True);Assert.That(result.ReservationsReleased,Is.True);
  Assert.That(ops.Calls.TakeLast(4),Is.EqualTo(new[]{"end","After","release-android","release-processor"}));
 }
 [TestCase(false)][TestCase(true)] public async Task ExplicitCancellationReachesFakeFixtureAndCannotInventSafeRelease(bool cleanup){
  using var explicitCancel=new CancellationTokenSource();
  await using var lifetime=WorkflowActiveDeadline.WithoutElapsedTimeout(explicitCancel.Token);
  var ops=new Operations{ConfirmCleanup=cleanup};var task=InstalledDriverTests.RunCoreAsync(ops,lifetime.Token);
  await ops.Entered.Task;await explicitCancel.CancelAsync();var result=await task;
  Assert.That(result.Passed,Is.False);Assert.That(result.ReservationsReleased,Is.EqualTo(cleanup));
  Assert.That(ops.Calls.Contains("end"),Is.EqualTo(cleanup));
  Assert.That(result.Detail,Does.StartWith("Cancelled"));
 }
}
