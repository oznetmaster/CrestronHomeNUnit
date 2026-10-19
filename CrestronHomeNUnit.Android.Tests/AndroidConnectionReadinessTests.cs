using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class AndroidConnectionReadinessTests
{
 const string App="com.crestron.phoenix.app";
 const string Home="<hierarchy><node package='com.crestron.phoenix.app' resource-id='com.crestron.phoenix.app:id/home_wholeHouse_name' text='Test Home' enabled='true' bounds='[0,0][100,100]'/></hierarchy>";
 // Same banner spelling/resource and cached tile shape as retained failed hierarchy;
 // unrelated CONNECTED text must never override the actual connection banner.
 static string Connecting(string text="Connecting to home…")=>Home.Replace("</hierarchy>","<node package='"+App+"' resource-id='"+App+":id/connectivityBar_title' text='"+text+"' enabled='true' bounds='[0,0][100,20]'/><node package='"+App+"' text='CONNECTED' enabled='true'/></hierarchy>");
 string directory=null!,owner=null!,lockPath=null!;
 [SetUp] public void Setup(){directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);owner=Guid.NewGuid().ToString("N");lockPath=Path.Combine(directory,"lease");}
 [TearDown] public void Cleanup()=>Directory.Delete(directory,true);
 AndroidRunContext Context(){using var p=Process.GetCurrentProcess();return new(1,owner,Environment.MachineName,p.Id,p.StartTime.ToUniversalTime().Ticks,"192.0.2.1",7,Guid.NewGuid().ToString(),"1.0.0.1",new('a',64),new('b',64),new(Environment.ProcessPath!,"fake",App,"Test Home",lockPath),directory);}
 sealed class Transport(params string[] pages):IAndroidCommandTransport {
  public int Reads,Inputs,Screenshots;public Action<int>? OnRead;
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> args,CancellationToken token){token.ThrowIfCancellationRequested();
   if(args.Contains("input")){Inputs++;throw new AssertionException("Startup cannot send any input.");}
   if(args.Contains("screencap")){Screenshots++;return Task.FromResult(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1cAAAAASUVORK5CYII="));}
   if(args.Contains("uiautomator")){int index=Reads++;OnRead?.Invoke(Reads);return Task.FromResult(Encoding.UTF8.GetBytes(pages[Math.Min(index,pages.Length-1)]+"UI hierchary dumped to: /proc/self/fd/1"));}
   return Task.FromResult(Array.Empty<byte>());
  }
 }
 [TestCase("Connecting to home...")][TestCase("Connecting to home…")]
 public async Task SharedOpenWaitsForActualConnectedHomeBeforeExposingSession(string banner){
  using var lease=AndroidSessionLease.Acquire(lockPath,owner);var io=new Transport(Connecting(banner),Home);
  var session=await AndroidWorkflowSession.OpenAsync(Context(),new(io,App),default);
  Assert.That(io.Reads,Is.EqualTo(2));Assert.That(io.Inputs,Is.Zero);Assert.That(session.ConnectionReadyObserved,Is.True);
  var gap=session.ConnectionUnavailableObservations.Single();Assert.That(File.ReadAllText(gap.HierarchyPath),Does.Contain(banner));
  using var record=JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(gap.HierarchyPath,".json")));
  Assert.That(record.RootElement.GetProperty("GatewayStateAssessed").GetBoolean(),Is.False);session.Complete(true);
 }
 [Test] public async Task StartupWaitAppliesBeforeAllowedHomeNavigationToo(){
  using var lease=AndroidSessionLease.Acquire(lockPath,owner);using var cancel=new CancellationTokenSource();var io=new Transport(Connecting());
  io.OnRead=_=>cancel.Cancel();var context=Context();context=context with{Profile=context.Profile with{AllowedStartingHomes=["Another Home"]}};
  await Assert.CatchAsync<OperationCanceledException>(()=>AndroidWorkflowSession.OpenAsync(context,new(io,App),cancel.Token));
  Assert.That(io.Inputs,Is.Zero);Assert.That(io.Reads,Is.EqualTo(1));
 }
 [Test] public async Task WrongHomeAfterStartupDoesNotReachBodyOrInput(){
  using var lease=AndroidSessionLease.Acquire(lockPath,owner);var io=new Transport(Connecting(),Home.Replace("Test Home","Wrong Home"));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>AndroidWorkflowSession.OpenAsync(Context(),new(io,App),default));
  Assert.That(io.Reads,Is.EqualTo(2));Assert.That(io.Inputs,Is.Zero);
  Assert.That(AndroidWorkflowSession.Read<AndroidRunCompletion>(Path.Combine(directory,"completion.json")).RestorationConfirmed,Is.False);
 }
 [Test] public async Task CancelWhileConnectingRetainsUnavailableEvidenceAndNoPassingCapture(){
  using var lease=AndroidSessionLease.Acquire(lockPath,owner);using var cancel=new CancellationTokenSource();
  var io=new Transport(Connecting());var session=new AndroidWorkflowSession(Context(),new(io,App));
  io.OnRead=_=>Task.Run(async()=>{await Task.Delay(30);cancel.Cancel();});
  await Assert.CatchAsync<OperationCanceledException>(()=>session.CaptureAsync("waiting",h=>CrestronHomePages.RequireHome(h,"Test Home"),cancel.Token));
  Assert.That(io.Screenshots,Is.Zero);Assert.That(session.ConnectionUnavailableObservations,Has.Count.EqualTo(1));
  Assert.That(File.Exists(Path.Combine(directory,"waiting","observation.json")),Is.False);Assert.Throws<InvalidOperationException>(session.RequireConnectedObservation);
 }
 [Test] public async Task LaterConnectingWaitRetainsGapRatherThanUsingCachedTileOrFailingTheTest(){
  using var lease=AndroidSessionLease.Acquire(lockPath,owner);var io=new Transport(Home,Connecting(),Home,Home);
  var session=await AndroidWorkflowSession.OpenAsync(Context(),new(io,App),default);bool observedBlocked=false;
  io.OnRead=n=>{if(n==3){observedBlocked=!session.ConnectionReadyObserved;Assert.Throws<InvalidOperationException>(session.RequireConnectedObservation);}};
  await session.CaptureAsync("first-scenario",h=>CrestronHomePages.RequireHome(h,"Test Home"));
  await session.CaptureAsync("second-scenario",h=>CrestronHomePages.RequireHome(h,"Test Home"));
  Assert.That(observedBlocked,Is.True);Assert.That(io.Inputs,Is.Zero);Assert.That(io.Reads,Is.EqualTo(4));Assert.That(io.Screenshots,Is.EqualTo(2));
  using var record=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory,"first-scenario","observation.json")));
  Assert.That(record.RootElement.GetProperty("ConnectionUnavailable").GetArrayLength(),Is.EqualTo(1));
  Assert.That(record.RootElement.GetProperty("StartedUtc").GetDateTimeOffset(),Is.GreaterThanOrEqualTo(session.ConnectionUnavailableObservations[0].ObservedUtc));
  session.Complete(true);Assert.That(Directory.GetFiles(directory,"session-started"),Has.Length.EqualTo(1));
  Assert.Throws<InvalidOperationException>(()=>session.Complete(true));
 }
 [Test] public async Task OwnershipReleaseDuringStartupCannotBecomeAReadRetryOrPass(){
  using var lease=AndroidSessionLease.Acquire(lockPath,owner);var io=new Transport(Connecting(),Home);
  io.OnRead=_=>lease.Release();
  await Assert.CatchAsync<IOException>(()=>AndroidWorkflowSession.OpenAsync(Context(),new(io,App),default));
  Assert.That(io.Reads,Is.EqualTo(1));Assert.That(io.Inputs,Is.Zero);
 }
 [Test] public async Task UnrecognizedOverlayRemainsFatalRatherThanAConnectionWait(){
  using var lease=AndroidSessionLease.Acquire(lockPath,owner);var panel=Home.Replace("</hierarchy>","<node package='"+App+"' resource-id='"+App+":id/customdevices_toolbarClose' enabled='true'/></hierarchy>");
  var io=new Transport(panel,Home);await Assert.ThrowsAsync<InvalidOperationException>(()=>AndroidWorkflowSession.OpenAsync(Context(),new(io,App),default));Assert.That(io.Reads,Is.EqualTo(1));
 }
}
