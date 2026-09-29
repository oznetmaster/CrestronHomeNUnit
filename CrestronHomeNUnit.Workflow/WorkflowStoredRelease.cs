// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestronHomeDevTools;
using Renci.SshNet;

namespace CrestronHomeNUnit.Workflow;

// Reuse is read-only and opt-in. A catalogue version alone never establishes package identity.
internal static class WorkflowStoredRelease
{
 internal const string Manifest = "/user/ThirdPartyDrivers/LocalRad.manifest";
 internal sealed record Verified(string Path, string CatalogueId, string Sha256, string ManifestSha256);

 internal static string SelectPath(byte[] manifest, DriverPackageInfo package)
 {
  using var document=JsonDocument.Parse(manifest);
  var rows=document.RootElement.EnumerateArray().Where(r=>
   Guid.TryParse(r.GetProperty("DriverPackageId").GetString(),out var id) && id==Guid.Parse(package.DriverId) &&
   WorkflowDebugVersion.ParseVersion(r.GetProperty("DriverVersion").GetString())==Version.Parse(package.Version)).ToArray();
  if(rows.Length!=1)throw new InvalidDataException("Stored release requires one exact local manifest identity.");
  var row=rows[0];
  if(!string.Equals(row.GetProperty("ModelName").GetString(),package.Model,StringComparison.OrdinalIgnoreCase) ||
   !string.Equals(row.GetProperty("Manufacturer").GetString(),package.Manufacturer,StringComparison.OrdinalIgnoreCase) ||
   !row.GetProperty("SupportedModels").EnumerateArray().Any(m=>string.Equals(m.GetString(),package.Model,StringComparison.OrdinalIgnoreCase)))
   throw new InvalidDataException("Stored release manifest model or manufacturer differs.");
  string path=row.GetProperty("LocalPath").GetString()??"";
  WorkflowPackageCleanup.ValidatePath(path);
  return path;
 }

 internal static string SelectCatalogue(DriverPackageInfo package,IReadOnlyList<DriverInfo> catalogue)
 {
  var matches=catalogue.Where(d=>string.Equals(d.Model?.Trim(),package.Model.Trim(),StringComparison.OrdinalIgnoreCase) &&
   WorkflowDebugVersion.ParseVersion(d.Version)>=Version.Parse(package.Version)).ToArray();
  if(matches.Length!=1 || string.IsNullOrWhiteSpace(matches[0].Id) || matches[0].AvailabilityState!="LocalByUser" ||
   !string.Equals(matches[0].Manufacturer,package.Manufacturer,StringComparison.OrdinalIgnoreCase) ||
   WorkflowDebugVersion.ParseVersion(matches[0].Version)!=Version.Parse(package.Version))
   throw new InvalidDataException("Stored release requires one exact local catalogue entry and no newer version.");
  return matches[0].Id;
 }

 internal static async Task<Verified> VerifySnapshotAsync(DriverPackageInfo package,string expectedSha256,
  Func<string,int,CancellationToken,Task<byte[]>> read,
  Func<CancellationToken,Task<IReadOnlyList<DriverInfo>>> catalogue,
  Func<CancellationToken,Task<IReadOnlyList<DeviceInfo>>> devices,CancellationToken token)
 {
  string id=SelectCatalogue(package,await catalogue(token).ConfigureAwait(false));
  byte[] before=await read(Manifest,4*1024*1024,token).ConfigureAwait(false);
  string path=SelectPath(before,package);
  VerifyUnused(before,package,await devices(token).ConfigureAwait(false));
  byte[] stored=await read(path,64*1024*1024,token).ConfigureAwait(false);
  string hash=Convert.ToHexStringLower(SHA256.HashData(stored));
  if(!hash.Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Stored release bytes differ from the pinned candidate.");
  byte[] after=await read(Manifest,4*1024*1024,token).ConfigureAwait(false);
  if(!before.AsSpan().SequenceEqual(after) || SelectCatalogue(package,await catalogue(token).ConfigureAwait(false))!=id)
   throw new InvalidDataException("Stored release catalogue changed during verification.");
  VerifyUnused(after,package,await devices(token).ConfigureAwait(false));
  return new(path,id,hash,Convert.ToHexStringLower(SHA256.HashData(before)));
 }

 internal static void VerifyUnused(byte[] manifest,DriverPackageInfo package,IReadOnlyList<DeviceInfo> devices)
 {
  string path=SelectPath(manifest,package);
  using var document=JsonDocument.Parse(manifest);
  var models=document.RootElement.EnumerateArray().Where(r=>r.GetProperty("LocalPath").GetString()==path)
   .SelectMany(r=>r.GetProperty("SupportedModels").EnumerateArray().Select(m=>m.GetString())).Append(package.Model).ToArray();
  if(models.Any(string.IsNullOrWhiteSpace) || devices.Any(d=>models.Any(m=>string.Equals(m!.Trim(),d.Model?.Trim(),StringComparison.OrdinalIgnoreCase))))
   throw new InvalidDataException("Stored release reuse requires no installed package model or alias; an already loaded process is not byte verification.");
 }

 internal static async Task<DriverDeploymentResult> VerifyAsync(ConfigurationClient client,string host,
  NetworkCredential credential,string fingerprint,string owner,DriverPackageInfo package,string expectedSha256,
  string results,CancellationToken token)
 {
  using var sftp=new SftpClient(host,credential.UserName,credential.Password);
  sftp.ConnectionInfo.Timeout=TimeSpan.FromSeconds(10);sftp.OperationTimeout=TimeSpan.FromSeconds(30);
  sftp.HostKeyReceived+=(_,e)=>e.CanTrust=e.FingerPrintSHA256==fingerprint;
  await sftp.ConnectAsync(token).ConfigureAwait(false);
  async Task<byte[]> Read(string path,int maximum,CancellationToken ct)
  {
   var attributes=await sftp.GetAttributesAsync(path,ct).ConfigureAwait(false);
   if(!attributes.IsRegularFile || attributes.IsSymbolicLink || attributes.Size>maximum)
    throw new InvalidDataException("Stored release verification requires bounded regular files.");
   await using var input=await sftp.OpenAsync(path,FileMode.Open,FileAccess.Read,ct).ConfigureAwait(false);
   using var buffer=new MemoryStream();byte[] chunk=new byte[81920];int count;
   while((count=await input.ReadAsync(chunk,ct).ConfigureAwait(false))>0) {
    if(buffer.Length+count>maximum)throw new InvalidDataException("Stored release input exceeded its bound.");
    buffer.Write(chunk,0,count);
   }
   return buffer.ToArray();
  }
  async Task VerifyLease()
  {
   if(Encoding.UTF8.GetString(await Read("/user/CrestronHomeNUnit-WorkflowLease/"+owner,32,token).ConfigureAwait(false))!=owner)
    throw new InvalidDataException("Stored release verification requires the current workflow lease.");
   if(await client.IsDriverRefreshInProgressAsync(token).ConfigureAwait(false)!=false)
    throw new InvalidOperationException("Stored release verification requires an idle catalogue.");
  }
  await VerifyLease().ConfigureAwait(false);
  var verified=await VerifySnapshotAsync(package,expectedSha256,Read,
   ct=>client.GetDriversAsync(package.Model,ct),ct=>client.GetDevicesAsync(ct),token).ConfigureAwait(false);
  await VerifyLease().ConfigureAwait(false);
  await using var receipt=new FileStream(Path.Combine(results,"actual-reuse.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
  await JsonSerializer.SerializeAsync(receipt,new {Mode="VerifiedStoredPackage",Package=package,Verified=verified,Utc=DateTimeOffset.UtcNow},cancellationToken:token).ConfigureAwait(false);
  return new(package,verified.Sha256,verified.CatalogueId,"VerifiedStoredPackage",true);
 }
}
