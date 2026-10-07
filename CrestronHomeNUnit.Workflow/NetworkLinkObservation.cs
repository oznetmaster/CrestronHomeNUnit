// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CrestronHomeDevTools;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Workflow;

public sealed record NetworkLinkObservationPackage(string Path, string Sha256, string Model, int LocationId);
public sealed record NetworkLinkRestoration(DateTimeOffset EarliestUtc, DateTimeOffset LatestUtc,
    NetworkLinkTrace Trace, NetworkLinkClockAnchor Before, NetworkLinkClockAnchor After);

/// <summary>Temporary, explicitly bound read-only instrumentation under an existing workflow
/// reservation. Prepare before readiness; arm only after Ready. It neither interrupts equipment
/// nor owns/releases the caller's reservation. Cleanup must succeed before the caller releases it.</summary>
public sealed class NetworkLinkObservation
{
    private readonly string _host, _fingerprint, _owner, _evidence;
    private readonly NetworkCredential _credential;
    private readonly NetworkLinkObservationPackage _package;
    private WorkflowPackageCleanup? _cleanup;
    private DriverInstanceReady? _instance;
    private DiscoveredPackage? _endpoint;
    private string? _key;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private NetworkLinkClockAnchor? _before;
    private bool _prepared, _armed, _cleaned;
    private int _receipt;
    private string? _retainedPackage;

    public NetworkLinkObservation(string host, NetworkCredential credential, string fingerprint,
        string owner, NetworkLinkObservationPackage package, string evidenceDirectory)
    {
        _host=host; _credential=credential; _fingerprint=fingerprint; _owner=owner;
        _package=package; _evidence=evidenceDirectory;
        if (!Path.IsPathFullyQualified(package.Path) || !Path.IsPathFullyQualified(evidenceDirectory) ||
            package.Sha256.Length!=64 || package.Sha256.Any(c=>!char.IsAsciiHexDigit(c)) ||
            string.IsNullOrWhiteSpace(package.Model) || !Guid.TryParseExact(owner,"N",out _))
            throw new ArgumentException("Bind the exact observer package, processor reservation and evidence directory.");
    }
    public async Task PrepareAsync(ConfigurationClient client,CancellationToken token)
    {
        if (_prepared || _cleanup!=null) throw new InvalidOperationException("Preparation cannot be replayed.");
        Directory.CreateDirectory(_evidence);
        byte[] bytes=await File.ReadAllBytesAsync(_package.Path,token);
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(_package.Sha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Observer package hash changed.");
        _retainedPackage=Path.Combine(_evidence,"observer.pkg");
        using(var retained=new FileStream(_retainedPackage,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
            await retained.WriteAsync(bytes,token);retained.Flush(true);
        }
        var info=DriverDeployment.Inspect(_retainedPackage);
        if(info.Model!=_package.Model || (await client.GetDevicesAsync(token)).Any(d=>d.Model==info.Model))
            throw new InvalidDataException("Observer model is incorrect or already installed.");
        _cleanup=await WorkflowPackageCleanup.CaptureAsync(_host,_credential,_fingerprint,_owner,
            Path.Combine(_evidence,"package-cleanup"),token,allowOwnedControlGuard:true);
        Save("binding",new{Host=_host,Owner=_owner,TraceId=_id,Package=_package});
        var imported=await DriverDeployment.DeployAsync(client,_host,_credential,_fingerprint,_retainedPackage,TimeSpan.FromMinutes(2),token);
        Save("import",imported);
        _instance=await DriverInstanceLifecycle.EnsureAsync(client,imported.CatalogueId,"Network link timing observer",
            _package.LocationId,null,TimeSpan.FromMinutes(2),token);
        Save("instance",_instance);
        var ready=await new PackageReadiness().WaitAsync(_host,_package.Model,async(selected,ct)=>{
            var identity=await ProcessorAuthentication.AuthenticateAsync(selected.Host,_credential.UserName,_credential.Password,
                selected.ProcessorId,_=>_fingerprint,(_,_)=>{}).WaitAsync(ct);
            var connection=await RemoteTestClient.ConnectAsync(selected.Host,selected.Port,identity.Token);
            _key=identity.Token;return connection;
        },token);
        using(ready.Connection) { _endpoint=ready.Package; }
        _prepared=true;
    }
    public async Task ArmAsync(CancellationToken token)
    {
        if(!_prepared || _armed || _cleaned)throw new InvalidOperationException("Observer must be prepared and armed once, after operator readiness.");
        // Record intent before sending: if the reply is lost, cleanup still stops/removes
        // this exact temporary host. Never resend an ambiguous arm request.
        _armed=true;Save("arm-intent",new{TraceId=_id,Utc=DateTimeOffset.UtcNow});
        var result=await Exchange("link-arm",token);
        if(result.Trace.State!="Armed" || result.Trace.Interface!="eth0" || result.Trace.Baseline?.Carrier!=true)
            throw new InvalidDataException("Physical carrier recorder did not arm on a connected interface.");
        _before=result.Anchor;
    }
    public async Task<NetworkLinkRestoration> ReadRestorationAsync(CancellationToken token)
    {
        if(_before==null)throw new InvalidOperationException("No acknowledged physical link baseline.");
        var result=await Exchange("link-read",token);
        var window=NetworkLinkTiming.Window(result.Trace,_before,result.Anchor);
        var proof=new NetworkLinkRestoration(window.EarliestUtc,window.LatestUtc,result.Trace,_before,result.Anchor);
        Save("restoration-window",proof);return proof;
    }
    private async Task<(NetworkLinkTrace Trace,NetworkLinkClockAnchor Anchor)> Exchange(string command,CancellationToken token)
    {
        if(_endpoint==null || _key==null)throw new InvalidOperationException("Observer connection is not prepared.");
        // Each exchange reconnects to the same authenticated endpoint. The recorder
        // deliberately survives the expected loss of the controller TCP connection.
        var connecting=RemoteTestClient.ConnectAsync(_host,_endpoint.Port,_key);
        RemoteTestClient connection;
        try { connection=await connecting.WaitAsync(token); }
        catch { _=connecting.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();else _=t.Exception;},TaskScheduler.Default);throw; }
        using(connection) {
            var timer=Stopwatch.StartNew();var started=DateTimeOffset.UtcNow;
            var reply=await connection.SendAsync(new(){Kind=command,TargetId=_id}).WaitAsync(TimeSpan.FromSeconds(5),token);
            var ended=DateTimeOffset.UtcNow;timer.Stop();
            if(reply.Kind!="complete")throw new InvalidDataException("Physical link observation command failed: "+reply.Text);
            var trace=NetworkLinkTrace.FromJson(reply.Text);
            if(trace.Id!=_id)throw new InvalidDataException("Physical trace identity changed.");
            var anchor=new NetworkLinkClockAnchor {Id=_id,Clock=trace.Clock,Frequency=trace.Frequency,
                RequestUtc=started,ResponseUtc=ended,ElapsedSeconds=timer.Elapsed.TotalSeconds};
            Save(command,new{Trace=trace,Anchor=anchor});return(trace,anchor);
        }
    }
    public async Task CleanupAsync(ConfigurationClient client,CancellationToken token)
    {
        if(_cleaned)return;
        if(_cleanup==null){_cleaned=true;return;}
        if(_instance==null)throw new InvalidOperationException("Observer activation was not confirmed; reconcile retained import evidence before releasing the reservation.");
        if(_armed && _endpoint!=null) {
            try {await Exchange("link-stop",token);}
            catch(Exception error) {Save("stop-unconfirmed",new{ErrorType=error.GetType().Name});}
        }
        // Removing the test host disposes and joins its sampler even when the final
        // protocol reply was lost. Verify removal before touching package storage.
        await client.RemoveDriverInstanceAsync(_instance.DeviceId,_instance.Model,_instance.Version,TimeSpan.FromMinutes(2),token);
        Save("instance-removed",new{_instance.DeviceId,Utc=DateTimeOffset.UtcNow});
        var removed=await _cleanup.RemoveAsync(client,_host,_credential,_fingerprint,_retainedPackage!,token);
        if(!removed.StorageRemoved)throw new InvalidDataException("Temporary observer storage was not removed.");
        _cleaned=true;Save("cleanup",new{InstanceRemoved=true,SamplerStopped=true,StorageRemoved=true});
    }
    private void Save(string name,object value)
    {
        string path=Path.Combine(_evidence,$"{Interlocked.Increment(ref _receipt):D3}-{name}.json");
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        JsonSerializer.Serialize(file,value);file.Flush(true);
    }
}
