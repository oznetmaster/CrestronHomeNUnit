// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeNUnit.Workflow;

/// <summary>One explicitly selected fixture may prepare its UI before requesting indefinite human readiness.</summary>
public sealed record InstalledOperatorReadiness(string Directory, string RunKey, string Step, string Instructions)
{
 private static readonly JsonSerializerOptions Json=new() {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
  UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true,
  AllowDuplicateProperties=false,Converters={new JsonStringEnumConverter(allowIntegerValues:false)}};
 public const string EnvironmentVariable = "CRESTRON_SUBMISSION_PREPARED_READINESS";
 public void Validate() {
  if (!Path.IsPathFullyQualified(Directory) || RunKey.Length != 64 || !RunKey.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f') ||
   string.IsNullOrWhiteSpace(Step) || Step.Length > 128 || Step.Any(char.IsControl) ||
   string.IsNullOrWhiteSpace(Instructions) || Instructions.Length > 3000)
   throw new ArgumentException("Prepared readiness needs a private inbox, exact run and step, and explicit instructions.");
 }
 public SubmissionOperatorStatus? ReadStatus() {
  Validate();
  if (!System.IO.Directory.Exists(Directory)) return null;
  SubmissionOperatorStatus? match=null; int count=0;
  foreach(var folder in System.IO.Directory.EnumerateDirectories(Directory)) {
   if(++count>1024)throw new InvalidDataException("Operator inbox exceeds its bound.");
   if(!Guid.TryParseExact(Path.GetFileName(folder),"N",out _) || !File.Exists(Path.Combine(folder,"ready.sha256")))continue;
   if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Operator request directory is a link.");
   var digest=new FileInfo(Path.Combine(folder,"ready.sha256"));
   if(digest.Length!=64)throw new InvalidDataException("Invalid operator request digest.");
   byte[] bytes=ReadBounded(Path.Combine(folder,"request.json"));
   using var document=JsonDocument.Parse(bytes);
   // Other requests may use a newer timed-action protocol. Only this schema-2
   // readiness belongs to this budget; do not parse unrelated action DTOs.
   if(document.RootElement.GetProperty("runKey").GetString()!=RunKey || document.RootElement.GetProperty("step").GetString()!=Step)continue;
   string hash=File.ReadAllText(digest.FullName);
   if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=hash)throw new InvalidDataException("Prepared readiness request changed.");
   var r=JsonSerializer.Deserialize<SubmissionOperatorRequest>(bytes,Json)??throw new InvalidDataException("Missing readiness request.");
   if (!r.IsReadiness || r.Instructions != Instructions || r.Id!=Path.GetFileName(folder) || r.CreatedUtc==default || string.IsNullOrWhiteSpace(r.Target))
    throw new InvalidDataException("Prepared readiness binding changed.");
   SubmissionOperatorResponse? response=null;
   string responsePath=Path.Combine(folder,"response.json");
   if(System.IO.Directory.Exists(responsePath))throw new IOException("Readiness response is not a file.");
   if(File.Exists(responsePath)) {
    response=JsonSerializer.Deserialize<SubmissionOperatorResponse>(ReadBounded(responsePath),Json)??throw new InvalidDataException("Empty readiness response.");
    if(response.SchemaVersion is not (1 or 2) || response.RequestId!=r.Id || response.RequestSha256!=hash || response.RecordedUtc==default ||
     response.Outcome is not (SubmissionOperatorOutcome.Done or SubmissionOperatorOutcome.Unable or SubmissionOperatorOutcome.Cancelled) ||
     (response.Reason!=null && (response.SchemaVersion!=2 || response.Outcome!=SubmissionOperatorOutcome.Unable || response.Reason.Length>2048)) ||
     (response.Outcome==SubmissionOperatorOutcome.Unable && string.IsNullOrWhiteSpace(response.Reason)))
     throw new InvalidDataException("Readiness response differs from its request.");
   }
   var status=new SubmissionOperatorStatus(r,response);
   if(match!=null)throw new InvalidDataException("Ambiguous prepared readiness request.");
   match=status;
  }
  return match;
 }
 public bool IsPending()=>ReadStatus()?.Waiting==true;
 private static byte[] ReadBounded(string path) {
  if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Readiness record is a link.");
  using var input=File.OpenRead(path);
  if(input.Length>32768)throw new InvalidDataException("Readiness record exceeds its bound.");
  byte[] bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);return bytes;
 }
}

/// <summary>A monotonic active-work budget. Only a validated pending readiness request pauses it.
/// Cancellation remains active while the operator is away. Reservations remain held to protect prepared UI state.</summary>
public sealed class WorkflowActiveDeadline : IAsyncDisposable
{
 private readonly CancellationTokenSource _deadline;
 private readonly CancellationTokenSource _stop = new();
 private readonly Task _monitor;
 private Exception? _failure;
 public CancellationToken Token => _deadline.Token;
 public WorkflowActiveDeadline(TimeSpan limit, CancellationToken token, Func<bool>? waiting = null)
  : this(limit, token, waiting, TimeProvider.System) { }
 internal WorkflowActiveDeadline(TimeSpan limit, CancellationToken token, Func<bool>? waiting, TimeProvider clock) {
  if (limit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(limit));
  _deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
  _monitor = Monitor(limit, waiting, clock);
 }
 private async Task Monitor(TimeSpan limit, Func<bool>? waiting, TimeProvider clock) {
  long previous = clock.GetTimestamp();
  TimeSpan active = TimeSpan.Zero;
  bool wasWaiting = false;
  try {
   while (!_stop.IsCancellationRequested && !_deadline.IsCancellationRequested) {
    bool pending = waiting?.Invoke() == true;
    long now = clock.GetTimestamp();
    // The preceding interval belongs to the preceding state. A reply after an overnight
    // scheduler/sleep gap must not turn the entire human wait into elapsed test time.
    if (!wasWaiting) active += clock.GetElapsedTime(previous, now);
    previous = now; wasWaiting = pending;
    if (active >= limit) { await _deadline.CancelAsync().ConfigureAwait(false); return; }
    await Task.Delay(TimeSpan.FromMilliseconds(100), clock, _stop.Token).ConfigureAwait(false);
   }
  } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
  catch (Exception error) when (error is not OutOfMemoryException) {
   _failure = error;
   await _deadline.CancelAsync().ConfigureAwait(false);
  }
 }
 public void ThrowIfFaulted() {
  if (_failure != null) throw new InvalidDataException("Prepared readiness could not be validated; inspect the retained request.", _failure);
 }
 public async ValueTask DisposeAsync() {
  await _stop.CancelAsync().ConfigureAwait(false);
  await _monitor.ConfigureAwait(false);
  _stop.Dispose(); _deadline.Dispose();
 }
}
