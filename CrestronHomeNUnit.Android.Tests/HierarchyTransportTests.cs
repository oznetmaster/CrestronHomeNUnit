// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text;
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;
[TestFixture] public sealed class HierarchyTransportTests
{
 const string Xml="<hierarchy><node package='example.app' resource-id='switch' enabled='true' bounds='[0,0][100,100]' /></hierarchy>UI hierchary dumped to: /proc/self/fd/1";
 sealed class Transport(Func<IReadOnlyList<string>,CancellationToken,Task<byte[]>> run):IAndroidCommandTransport {
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> a,CancellationToken t)=>run(a,t);
 }
 [Test] public async Task FreshReadsUseIndependentTransportAndOnlyOneInputIsSent() {
  int reads=0,inputs=0,prepared=0;
  var reader=new Transport((a,t)=>{Assert.That(a,Is.EqualTo(new[]{"exec-out","uiautomator","dump","/proc/self/fd/1"}));reads++;return Task.FromResult(Encoding.UTF8.GetBytes(Xml));});
  var input=new Transport((a,t)=>{Assert.That(a.Take(3),Is.EqualTo(new[]{"shell","input","tap"}));Assert.That(reads,Is.EqualTo(2));Assert.That(prepared,Is.EqualTo(1));inputs++;return Task.FromResult(Array.Empty<byte>());});
  var device=new AndroidDevice(input,"example.app",reader);
  await device.TapAsync(h=>h.RequireUnique(new(AndroidSelectorKind.ResourceId,"switch")),_=>{},()=>{},CancellationToken.None,_=>{prepared++;return Task.CompletedTask;});
  Assert.That(inputs,Is.EqualTo(1));
 }
 [Test] public async Task CancelledHierarchyReadNeverStartsInputOrRetries() {
  int reads=0,inputs=0;var started=new TaskCompletionSource();using var cancel=new CancellationTokenSource();
  var reader=new Transport(async(a,t)=>{reads++;started.SetResult();await Task.Delay(Timeout.InfiniteTimeSpan,t);return [];});
  var input=new Transport((a,t)=>{inputs++;return Task.FromResult(Array.Empty<byte>());});
  var task=new AndroidDevice(input,"example.app",reader).TapAsync(new(AndroidSelectorKind.ResourceId,"switch"),_=>{},cancel.Token);
  await started.Task;cancel.Cancel();await Assert.ThrowsAsync<TaskCanceledException>(async()=>await task);
  Assert.That(reads,Is.EqualTo(1));Assert.That(inputs,Is.Zero);
 }
 [Test] public async Task UncertainInputUsesInputTransportOnce() {
  int inputs=0;var reader=new Transport((a,t)=>Task.FromResult(Encoding.UTF8.GetBytes(Xml)));
  var input=new Transport((a,t)=>{inputs++;throw new TimeoutException("uncertain input");});
  await Assert.ThrowsAsync<TimeoutException>(()=>new AndroidDevice(input,"example.app",reader).TapAsync(new(AndroidSelectorKind.ResourceId,"switch"),_=>{}));Assert.That(inputs,Is.EqualTo(1));
 }
}
