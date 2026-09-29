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
}
