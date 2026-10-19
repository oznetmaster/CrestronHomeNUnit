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
 private readonly bool _hasElapsedTimeout=true;
 public bool HasElapsedTimeout=>_hasElapsedTimeout;
 private readonly CancellationTokenSource _stop = new();
 private readonly Task _monitor;
 private readonly object _sync = new();
 private readonly TimeSpan _limit;
 private readonly TimeProvider _clock;
 private readonly Func<bool>? _waiting;
 private long _previous;
 private TimeSpan _active;
 private bool _wasWaiting;
 private Exception? _failure;
 public CancellationToken Token => _deadline.Token;
 /// <summary>Current monotonic active allowance. Canceled or expired deadlines return zero;
 /// validated readiness retains its existing pause semantics. Reading never resets the deadline.</summary>
 public TimeSpan Remaining {
  get {
   if (!_hasElapsedTimeout) throw new InvalidOperationException("Attended physical sessions have no finite remaining allowance.");
   if (_deadline.IsCancellationRequested) return TimeSpan.Zero;
   try {
    var remaining=Sample();
    if (remaining==TimeSpan.Zero) _deadline.Cancel();
    return _deadline.IsCancellationRequested ? TimeSpan.Zero : remaining;
   } catch (Exception error) when (error is not OutOfMemoryException) {
    Interlocked.CompareExchange(ref _failure,error,null); _deadline.Cancel(); return TimeSpan.Zero;
   }
  }
 }
 internal TimeSpan RequireRemaining(TimeSpan required) {
  if (!_hasElapsedTimeout) throw new InvalidOperationException("A finite reserve cannot constrain attended physical observation.");
  if (required<=TimeSpan.Zero || required>TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(required));
  ThrowIfFaulted(); Token.ThrowIfCancellationRequested();
  var remaining=Remaining;
  ThrowIfFaulted(); Token.ThrowIfCancellationRequested();
  if(remaining<required)throw new InvalidDataException("Insufficient remaining active allowance for the declared Android execution and cleanup reserve; execution was not started.");
  return remaining;
 }
 /// <summary>No elapsed timer or readiness pause monitor; the explicit caller cancellation remains live.</summary>
 public static WorkflowActiveDeadline WithoutElapsedTimeout(CancellationToken token=default)=>WithoutElapsedTimeout(token,TimeProvider.System);
 internal static WorkflowActiveDeadline WithoutElapsedTimeout(CancellationToken token,TimeProvider clock)=>new(token,clock);
 private WorkflowActiveDeadline(CancellationToken token,TimeProvider clock){
  _hasElapsedTimeout=false;_limit=Timeout.InfiniteTimeSpan;_clock=clock;_previous=clock.GetTimestamp();
  _deadline=CancellationTokenSource.CreateLinkedTokenSource(token);_monitor=Task.CompletedTask;
 }
 public WorkflowActiveDeadline(TimeSpan limit, CancellationToken token, Func<bool>? waiting = null)
  : this(limit, token, waiting, TimeProvider.System) { }
 internal WorkflowActiveDeadline(TimeSpan limit, CancellationToken token, Func<bool>? waiting, TimeProvider clock) {
  if (limit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(limit));
  _limit=limit;_clock=clock;_waiting=waiting;_previous=clock.GetTimestamp();
  _deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
  _monitor = Monitor();
 }
 private TimeSpan Sample() {
  lock(_sync) {
   bool pending=_waiting?.Invoke()==true;
   long now=_clock.GetTimestamp();
   // Charge the preceding state; a late human reply never consumes the prior paused interval.
   var elapsed=_clock.GetElapsedTime(_previous,now);
   if(elapsed<TimeSpan.Zero)throw new InvalidDataException("Monotonic clock moved backwards.");
   if(!_wasWaiting)_active+=elapsed;
   _previous=now;_wasWaiting=pending;
   return _active>=_limit ? TimeSpan.Zero : _limit-_active;
  }
 }
 private async Task Monitor() {
  try {
   while (!_stop.IsCancellationRequested && !_deadline.IsCancellationRequested) {
    if(Sample()==TimeSpan.Zero){await _deadline.CancelAsync().ConfigureAwait(false);return;}
    await Task.Delay(TimeSpan.FromMilliseconds(100), _clock, _stop.Token).ConfigureAwait(false);
   }
  } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
  catch (Exception error) when (error is not OutOfMemoryException) {
   Interlocked.CompareExchange(ref _failure,error,null);
   await _deadline.CancelAsync().ConfigureAwait(false);
  }
 }
 public void ThrowIfFaulted() {
  if (Volatile.Read(ref _failure) is {} failure) throw new InvalidDataException("Prepared readiness could not be validated; inspect the retained request.", failure);
 }
 public async ValueTask DisposeAsync() {
  await _stop.CancelAsync().ConfigureAwait(false);
  await _monitor.ConfigureAwait(false);
  _stop.Dispose(); _deadline.Dispose();
 }
}
