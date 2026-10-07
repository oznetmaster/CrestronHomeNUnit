// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class CaptureDiagnosticTests
{
    private string directory = null!;
    private AndroidSessionLease lease = null!;
    private AndroidRunContext context = null!;
    [SetUp] public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var owner = Guid.NewGuid().ToString("N");
        var leasePath = Path.Combine(directory, "worker.lease");
        lease = AndroidSessionLease.Acquire(leasePath, owner);
        using var process = Process.GetCurrentProcess();
        context = new(1, owner, Environment.MachineName, process.Id, process.StartTime.ToUniversalTime().Ticks,
            "192.0.2.1", 7, Guid.NewGuid().ToString(), "1.0.0.1", new('A',64), new('B',64),
            new(Environment.ProcessPath!, "fake-serial", "com.crestron.phoenix.app", "Example Home", leasePath), directory);
    }
    [TearDown] public void TearDown()
    {
        try { lease.Release(); } finally { lease.Dispose(); }
        Directory.Delete(directory, true);
    }
    [Test] public async Task InterruptedFirstCaptureRetainsSameCancellationAndNoInput()
    {
        var transport = new DiagnosticTransport { Block = true };
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var error = await Assert.CatchAsync<OperationCanceledException>(() => new AndroidDevice(transport, context.Profile.Application).CaptureAsync(cancelled.Token));
        Assert.That(error, Is.SameAs(transport.Cancellation));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancelled.Token));
        using var attempts = JsonDocument.Parse((string)error.Data[AndroidDevice.CAPTURE_ATTEMPTS_KEY]!);
        Assert.That(attempts.RootElement.GetArrayLength(), Is.EqualTo(1));
        Assert.That(attempts.RootElement[0].GetProperty("Attempt").GetInt32(), Is.EqualTo(1));
        Assert.That(transport.Commands, Has.Count.EqualTo(1));
        Assert.That(transport.Commands.All(a => a.Contains("uiautomator")), Is.True);
    }
    [Test] public async Task ExhaustedReadsRetainBoundedAttemptsWithoutRawErrorText()
    {
        var transport = new DiagnosticTransport { Fail = true };
        var error = await Assert.ThrowsAsync<IOException>(() => new AndroidDevice(transport, context.Profile.Application).CaptureAsync());
        string json = (string)error!.Data[AndroidDevice.CAPTURE_ATTEMPTS_KEY]!;
        using var attempts = JsonDocument.Parse(json);
        Assert.That(attempts.RootElement.GetArrayLength(), Is.EqualTo(3));
        Assert.That(attempts.RootElement.EnumerateArray().Select(a => a.GetProperty("Attempt").GetInt32()), Is.EqualTo(new[]{1,2,3}));
        Assert.That(json, Does.Not.Contain("private-sentinel"));
        Assert.That(transport.Commands, Has.Count.EqualTo(3));
    }
    [Test] public async Task NavigationTimeoutBeforeFirstCompletedReadStillProducesDiagnostic()
    {
        var transport = new DiagnosticTransport { Block = true };
        var navigation = new CrestronHomeNavigation(new(context, new(transport, context.Profile.Application)), TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<TimeoutException>(() => navigation.RestoreHomeAsync());
        Assert.That(error!.InnerException, Is.SameAs(transport.Cancellation));
        using var diagnostic = JsonDocument.Parse(File.ReadAllText((string)error.Data["NavigationDiagnostic"]!));
        Assert.That(diagnostic.RootElement.GetProperty("CapturesStarted").GetInt32(), Is.EqualTo(1));
        Assert.That(diagnostic.RootElement.GetProperty("CapturesCompleted").GetInt32(), Is.Zero);
        Assert.That(diagnostic.RootElement.GetProperty("LastObservedPage").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(diagnostic.RootElement.GetProperty("CaptureAttemptsJson").GetString(), Is.Not.Null);
        Assert.That(Directory.GetFiles(directory,"*.xml"), Is.Empty);
        Assert.That(navigation.HomeRestored, Is.False);
        Assert.That(transport.Commands, Has.Count.EqualTo(1));
    }
    [Test] public async Task CallerCancellationIsNotConvertedToNavigationTimeout()
    {
        var transport = new DiagnosticTransport { Block = true };
        var navigation = new CrestronHomeNavigation(new(context, new(transport, context.Profile.Application)), TimeSpan.FromSeconds(5));
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var error = await Assert.CatchAsync<OperationCanceledException>(() => navigation.RestoreHomeAsync(cancelled.Token));
        Assert.That(error, Is.SameAs(transport.Cancellation));
        Assert.That(Directory.GetFiles(directory,"navigation-timeout-*"), Is.Empty);
        Assert.That(navigation.HomeRestored, Is.False);
        Assert.That(transport.Commands, Has.Count.EqualTo(1));
    }
    [Test] public async Task RejectedPageDiagnosticDistinguishesCompletedReadFromCaptureStall()
    {
        var transport = new DiagnosticTransport();
        var navigation = new CrestronHomeNavigation(new(context, new(transport, context.Profile.Application)), TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<TimeoutException>(() => navigation.RestoreHomeAsync());
        using var diagnostic = JsonDocument.Parse(File.ReadAllText((string)error!.Data["NavigationDiagnostic"]!));
        Assert.That(diagnostic.RootElement.GetProperty("CapturesCompleted").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(diagnostic.RootElement.GetProperty("LastGuardRejection").GetString(), Is.EqualTo(nameof(InvalidOperationException)));
        Assert.That(File.Exists((string)error.Data["LastObservedPage"]!), Is.True);
        Assert.That(transport.Commands.All(a => a.Contains("uiautomator")), Is.True);
        Assert.That(navigation.HomeRestored, Is.False);
    }
    [TestCase("ERROR: null root node returned by UiTestAutomationBridge.", "null-root")]
    [TestCase("private-sentinel", "missing-status")]
    [TestCase("<broken>UI hierchary dumped to: /proc/self/fd/1", "invalid-xml")]
    [TestCase("<hierarchy/>private-sentinel", "missing-status")]
    public async Task FailedCaptureRetainsSafeCauseWithoutRawOutput(string stream, string expected)
    {
        var transport = new DiagnosticTransport { Stream = stream };
        var error = await Assert.ThrowsAsync<IOException>(() => new AndroidDevice(transport, context.Profile.Application).CaptureAsync());
        string json = (string)error!.Data[AndroidDevice.CAPTURE_ATTEMPTS_KEY]!;
        using var attempts = JsonDocument.Parse(json);
        Assert.That(attempts.RootElement.GetArrayLength(), Is.EqualTo(3));
        Assert.That(attempts.RootElement.EnumerateArray().Select(a => a.GetProperty("FailureCode").GetString()), Is.All.EqualTo(expected));
        Assert.That(json, Does.Not.Contain("private-sentinel"));
        Assert.That(transport.Commands.All(a => a.Contains("uiautomator")), Is.True);
    }
    [Test] public async Task DefaultNavigationBudgetAllowsTransientNullRootReadToFinish()
    {
        // Retained HPNEIL failure: first null-root read took 17s; the old 25s
        // navigation timeout canceled the following read after only 7.6s.
        var transport = new SlowTransientRootTransport();
        var navigation = new CrestronHomeNavigation(new(context, new(transport, context.Profile.Application)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await navigation.RestoreHomeAsync(deadline.Token);
        Assert.That(navigation.HomeRestored, Is.True);
        Assert.That(transport.Reads, Is.EqualTo(2));
        Assert.That(Directory.GetFiles(directory,"navigation-timeout-*"), Is.Empty);
    }
    [Test] public async Task RestoredHomeEvidenceReceivesItsOwnBoundedBudget()
    {
        var transport = new PhasedHomeTransport();
        var navigation = new CrestronHomeNavigation(new(context, new(transport, context.Profile.Application)));
        await navigation.RestoreAndCaptureHomeAsync("phase-budget", TimeSpan.FromMilliseconds(250));
        Assert.That(navigation.HomeRestored, Is.True);
        Assert.That(transport.Reads, Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(directory,"phase-budget.home-restored","observation.json")), Is.True);
    }
    [Test] public async Task FailedRestorationCannotProducePassedHomeEvidence()
    {
        var transport = new PhasedHomeTransport { Block = true };
        var navigation = new CrestronHomeNavigation(new(context, new(transport, context.Profile.Application)));
        await Assert.CatchAsync<OperationCanceledException>(()=>navigation.RestoreAndCaptureHomeAsync("phase-budget", TimeSpan.FromMilliseconds(100)));
        Assert.That(navigation.HomeRestored, Is.False);
        Assert.That(transport.Reads, Is.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(directory,"phase-budget.home-restored")), Is.False);
    }
    private sealed class PhasedHomeTransport : IAndroidCommandTransport
    {
        public bool Block;
        public int Reads;
        public async Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken token)
        {
            if(arguments.SequenceEqual(new[]{"exec-out","screencap","-p"}))
                return new byte[]{137,80,78,71,13,10,26,10};
            Assert.That(arguments, Is.EqualTo(new[]{"exec-out","uiautomator","dump","/proc/self/fd/1"}));
            Reads++;
            await Task.Delay(Block ? Timeout.Infinite : 175, token);
            return Encoding.UTF8.GetBytes("<hierarchy><node package='com.crestron.phoenix.app' resource-id='"+CrestronHomePages.ResourcePrefix+"home_wholeHouse_name' text='Example Home' enabled='true' bounds='[0,0][100,100]'/></hierarchy>UI hierchary dumped to: /proc/self/fd/1");
        }
    }
    private sealed class SlowTransientRootTransport : IAndroidCommandTransport
    {
        public int Reads;
        public async Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken token)
        {
            Assert.That(arguments, Is.EqualTo(new[]{"exec-out","uiautomator","dump","/proc/self/fd/1"}), "Observation must never replay input.");
            Reads++;
            if(Reads == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(17), token);
                return Encoding.UTF8.GetBytes("ERROR: null root node returned by UiTestAutomationBridge.");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            string Node(string id, string text = "") => "<node package='com.crestron.phoenix.app' resource-id='"+CrestronHomePages.ResourcePrefix+id+"' text='"+text+"' content-desc='' enabled='true' bounds='[0,0][100,100]'/>";
            return Encoding.UTF8.GetBytes("<hierarchy>"+Node("fragmentHomeContainer")+Node("home_wholeHouse_name","Example Home")+Node("home_wholeHouse_topbarMenuButton")+"</hierarchy>UI hierchary dumped to: /proc/self/fd/1");
        }
    }
    private sealed class DiagnosticTransport : IAndroidCommandTransport
    {
        public string? Stream;
        public bool Block;
        public bool Fail;
        public OperationCanceledException? Cancellation;
        public List<string[]> Commands { get; } = [];
        public async Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken token)
        {
            Commands.Add(arguments.ToArray());
            if(Fail) throw new IOException("private-sentinel");
            if(Block)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch(OperationCanceledException error) { Cancellation=error; throw; }
            }
            return Encoding.UTF8.GetBytes(Stream ?? "<hierarchy rotation=\"0\"></hierarchy>UI hierchary dumped to: /proc/self/fd/1");
        }
    }
}
