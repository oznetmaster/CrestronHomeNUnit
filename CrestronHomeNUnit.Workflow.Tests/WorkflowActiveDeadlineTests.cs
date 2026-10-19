// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools;
using NUnit.Framework;
namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class WorkflowActiveDeadlineTests
{
 private sealed class Clock:TimeProvider {
  internal long Ticks; internal int Reads;
  public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
  public override long GetTimestamp(){Interlocked.Increment(ref Reads);return Interlocked.Read(ref Ticks);}
  internal void Advance(TimeSpan value)=>Interlocked.Add(ref Ticks,value.Ticks);
 }
 private static async Task Tick(Clock clock) {
  int reads=Volatile.Read(ref clock.Reads);
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
  while(Volatile.Read(ref clock.Reads)<reads+2)await Task.Delay(10,timeout.Token);
 }
 [Test] public async Task OvernightWaitDoesNotConsumeActiveBudgetAndActionStillTimesOut() {
  var clock=new Clock();bool waiting=true;
  await using var budget=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,()=>Volatile.Read(ref waiting),clock);
  await Tick(clock);clock.Advance(TimeSpan.FromHours(36));await Tick(clock);
  Assert.That(budget.Token.IsCancellationRequested,Is.False);
  Volatile.Write(ref waiting,false);await Tick(clock);
  clock.Advance(TimeSpan.FromSeconds(31));
  await Task.Delay(250);
  Assert.That(budget.Token.IsCancellationRequested,Is.True);
 }
 [Test] public async Task ReplyAfterLongSchedulerGapDoesNotChargeHumanWait() {
  var clock=new Clock();bool waiting=true;
  await using var budget=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,()=>Volatile.Read(ref waiting),clock);
  await Tick(clock);clock.Advance(TimeSpan.FromHours(36));Volatile.Write(ref waiting,false);
  await Tick(clock);Assert.That(budget.Token.IsCancellationRequested,Is.False);
 }
 [Test] public async Task ExternalCancellationRemainsEffectiveDuringReadiness() {
  using var cancel=new CancellationTokenSource();
  await using var budget=new WorkflowActiveDeadline(TimeSpan.FromHours(1),cancel.Token,()=>true);
  await cancel.CancelAsync();Assert.That(budget.Token.IsCancellationRequested,Is.True);
 }
 [Test] public async Task BrokenReadinessFailsClosedRatherThanDisablingTimeout() {
  await using var budget=new WorkflowActiveDeadline(TimeSpan.FromHours(1),default,()=>throw new IOException("Synthetic retained-request failure"));
  Assert.That(budget.Token.IsCancellationRequested,Is.True);
  Assert.Throws<InvalidDataException>(budget.ThrowIfFaulted);
 }
 [Test] public void OnlyTheExactPendingReadinessPausesTheBudget() {
  string root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"readiness-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try {
   var planned=new InstalledOperatorReadiness(root,new('a',64),"prepared.step","Prepare the test button.");
   SubmissionOperatorStep.GetOrCreateReadiness(root,new('b',64),planned.Step,"Synthetic",planned.Instructions);
   var other=SubmissionOperatorStep.GetOrCreateReadiness(root,planned.RunKey,"other.step","Synthetic",planned.Instructions);
   Assert.That(planned.IsPending(),Is.False);
   var handle=SubmissionOperatorStep.GetOrCreateReadiness(root,planned.RunKey,planned.Step,"Synthetic",planned.Instructions);
   var otherJson=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(other.Directory,"request.json")))!;
   otherJson["schemaVersion"]=3;otherJson["workerClock"]=new System.Text.Json.Nodes.JsonObject { ["syntheticFutureProtocol"]=true };
   File.WriteAllText(Path.Combine(other.Directory,"request.json"),otherJson.ToJsonString());
   Assert.That(planned.IsPending(),Is.True);
   Assert.Throws<InvalidDataException>(()=>(planned with {Instructions="Different"}).IsPending());
   SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Unable,"Synthetic device unavailable.");
   Assert.That(planned.IsPending(),Is.False);
   Assert.That(planned.ReadStatus()!.Response!.Reason,Is.EqualTo("Synthetic device unavailable."));
   string responsePath=Path.Combine(handle.Directory,"response.json");
   var responseJson=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(responsePath))!;
   responseJson["recordedUtc"]=DateTimeOffset.UtcNow.AddDays(1);
   File.WriteAllText(responsePath,responseJson.ToJsonString());
   Assert.That(planned.ReadStatus()!.Response!.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Unable));
  } finally {Directory.Delete(root,true);}
 }
 [Test] public async Task ReserveSamplesPreparationImmediatelyAndDoesNotResetBudget() {
  var clock=new Clock();
  await using var budget=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,null,clock);
  clock.Advance(TimeSpan.FromSeconds(20));
  Assert.That(budget.RequireRemaining(TimeSpan.FromSeconds(10)),Is.EqualTo(TimeSpan.FromSeconds(10)));
  clock.Advance(TimeSpan.FromTicks(1));
  Assert.Throws<InvalidDataException>(()=>budget.RequireRemaining(TimeSpan.FromSeconds(10)));
  Assert.That(budget.Remaining,Is.LessThan(TimeSpan.FromSeconds(10)));
 }
 [Test] public async Task CanceledAndExpiredBudgetCannotAdmitExecution() {
  var clock=new Clock();using var cancel=new CancellationTokenSource();
  await using var canceled=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),cancel.Token,null,clock);
  await cancel.CancelAsync();
  Assert.That(canceled.Remaining,Is.EqualTo(TimeSpan.Zero));
  Assert.Catch<OperationCanceledException>(()=>canceled.RequireRemaining(TimeSpan.FromSeconds(1)));
  await using var expired=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,null,clock);
  clock.Advance(TimeSpan.FromSeconds(31));
  Assert.That(expired.Remaining,Is.EqualTo(TimeSpan.Zero));
  Assert.Catch<OperationCanceledException>(()=>expired.RequireRemaining(TimeSpan.FromSeconds(1)));
 }
 [TestCase(0)] [TestCase(-1)] [TestCase(86401)]
 public async Task InvalidReserveIsRejectedWithoutExtendingDeadline(int seconds) {
  var clock=new Clock();await using var budget=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,null,clock);
  Assert.Throws<ArgumentOutOfRangeException>(()=>budget.RequireRemaining(TimeSpan.FromSeconds(seconds)));
  Assert.That(budget.Remaining,Is.EqualTo(TimeSpan.FromSeconds(30)));
 }
 [Test] public async Task ReserveKeepsValidatedHumanWaitAndFailureSemantics() {
  var clock=new Clock();bool pending=true;
  await using var budget=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,()=>pending,clock);
  clock.Advance(TimeSpan.FromHours(48));
  Assert.That(budget.RequireRemaining(TimeSpan.FromSeconds(30)),Is.EqualTo(TimeSpan.FromSeconds(30)));
  pending=false;Assert.That(budget.Remaining,Is.EqualTo(TimeSpan.FromSeconds(30)));
  clock.Advance(TimeSpan.FromSeconds(5));
  Assert.That(budget.Remaining,Is.EqualTo(TimeSpan.FromSeconds(25)));
  await using var broken=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,()=>throw new IOException("Synthetic validation failure"),clock);
  Assert.Throws<InvalidDataException>(()=>broken.RequireRemaining(TimeSpan.FromSeconds(1)));
 }
 [Test] public async Task InvalidClockCannotIncreaseAllowance() {
  var clock=new Clock();await using var budget=new WorkflowActiveDeadline(TimeSpan.FromSeconds(30),default,null,clock);
  clock.Advance(TimeSpan.FromSeconds(-1));
  Assert.Throws<InvalidDataException>(()=>budget.RequireRemaining(TimeSpan.FromSeconds(1)));
  Assert.That(budget.Token.IsCancellationRequested,Is.True);
 }

 [Test] public async Task AttendedPhysicalHasNoMonitorEvenAfterArbitraryMonotonicAdvance(){
  var clock=new Clock();using var cancel=new CancellationTokenSource();
  await using var lifetime=WorkflowActiveDeadline.WithoutElapsedTimeout(cancel.Token,clock);
  clock.Advance(TimeSpan.FromDays(10000));Assert.That(lifetime.HasElapsedTimeout,Is.False);
  Assert.That(lifetime.Token.IsCancellationRequested,Is.False);
  Assert.Throws<InvalidOperationException>(()=>{_ = lifetime.Remaining;});
  Assert.Throws<InvalidOperationException>(()=>lifetime.RequireRemaining(TimeSpan.FromSeconds(1)));
  await cancel.CancelAsync();Assert.That(lifetime.Token.IsCancellationRequested,Is.True);
 }
}
