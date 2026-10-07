// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CrestronHomeNUnit.Android;

public interface IAndroidCommandTransport
	{
	Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken);
	}

/// <summary>Uses an existing ADB installation and explicit device serial. Never installs or starts an emulator.</summary>
public sealed class AdbCommandTransport : IAndroidStreamingCommandTransport
	{
	private readonly string _executable;
	private readonly string _serial;
	private readonly TimeSpan _timeout;

	public AdbCommandTransport (string executable, string serial, TimeSpan timeout)
		{
		if (!Path.IsPathFullyQualified (executable) || !File.Exists (executable))
			throw new ArgumentException ("Provide the full path to an existing ADB executable.", nameof (executable));
		if (string.IsNullOrWhiteSpace (serial) || serial.Any (c => !char.IsAsciiLetterOrDigit (c) && c is not ('.' or ':' or '-' or '_')))
			throw new ArgumentException ("Provide one explicit Android device serial.", nameof (serial));
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (2))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		_executable = executable;
		_serial = serial;
		_timeout = timeout;
		}

	internal AdbCommandTransport ForRecording (int durationSeconds)
		=> new (_executable, _serial, TimeSpan.FromSeconds (Math.Min (120, _timeout.TotalSeconds + durationSeconds)));

	public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		=> ExecuteCoreAsync (arguments, null, cancellationToken);

	public Task<byte[]> ExecuteStreamingAsync (IReadOnlyList<string> arguments, Action<ReadOnlyMemory<byte>> output,
		CancellationToken cancellationToken)
		=> ExecuteCoreAsync (arguments, output, cancellationToken);

	private async Task<byte[]> ExecuteCoreAsync (IReadOnlyList<string> arguments, Action<ReadOnlyMemory<byte>>? observeOutput,
		CancellationToken cancellationToken)
		{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		timeout.CancelAfter (_timeout);
		var start = new ProcessStartInfo (_executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add ("-s");
		start.ArgumentList.Add (_serial);
		foreach (var argument in arguments)
			start.ArgumentList.Add (argument);
		using var process = new Process { StartInfo = start };
		timeout.Token.ThrowIfCancellationRequested ();
		if (!process.Start ())
			throw new IOException ("ADB could not be started.");
		try
			{
			using var output = new MemoryStream ();
			async Task ReadOutput ()
				{
				var buffer = new byte[8192];
				int count;
				while ((count = await process.StandardOutput.BaseStream.ReadAsync (buffer, timeout.Token).ConfigureAwait (false)) != 0)
					{
					output.Write (buffer, 0, count);
					observeOutput?.Invoke (buffer.AsMemory (0, count));
					}
				}
			var readOutput = ReadOutput ();
			// Drain stderr to prevent deadlocks, but never include arbitrary device text in errors.
			var readError = process.StandardError.BaseStream.CopyToAsync (Stream.Null, timeout.Token);
			await Task.WhenAll (readOutput, readError, process.WaitForExitAsync (timeout.Token)).ConfigureAwait (false);
			if (process.ExitCode != 0)
				throw new IOException ($"ADB exited with code {process.ExitCode}. The command was not retried.");
			return output.ToArray ();
			}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			throw new TimeoutException ("ADB timed out. An input command may have completed; it was not retried.");
			}
		finally
			{
			if (!process.HasExited)
				{
				try
					{
					process.Kill ();
					}
				catch (InvalidOperationException) { }
				}
			}
		}
	}

/// <summary>Low-level navigation primitives. The caller owns processor/Android leases and page-specific assertions.</summary>
public sealed class AndroidDevice
	{
    private readonly IAndroidCommandTransport transport;
    private readonly IAndroidCommandTransport hierarchyTransport;
    private readonly string application;
    public AndroidDevice(IAndroidCommandTransport transport, string application) : this(transport, application, transport) { }
    /// <summary>Use an independently bounded read transport for hierarchy capture. Input, screenshot and recording transport remain unchanged.</summary>
    public AndroidDevice(IAndroidCommandTransport transport, string application, IAndroidCommandTransport hierarchyTransport)
        {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.application = application;
        this.hierarchyTransport = hierarchyTransport ?? throw new ArgumentNullException(nameof(hierarchyTransport));
        }

	internal const string CAPTURE_ATTEMPTS_KEY = "AndroidCaptureAttempts";
	private sealed record CaptureAttempt (int Attempt, long ElapsedMilliseconds, string Outcome, string FailureCode);

    private static IOException CaptureFailure(string code, string message, Exception? inner = null)
        {
        var error = new IOException(message, inner);
        error.Data["AndroidCaptureFailureCode"] = code;
        return error;
        }

    private static string FailureCode(Exception error)
        {
        // Only known categories leave this boundary, never device output or arbitrary exception text.
        if (error is OperationCanceledException) return "cancelled";
        if (error is TimeoutException) return "command-timeout";
        return error.Data["AndroidCaptureFailureCode"] is string code && code is
            "stream-too-large" or "invalid-utf8" or "null-root" or "missing-status" or "invalid-xml"
            ? code : "transport-io";
        }

	public async Task<AndroidHierarchy> CaptureAsync (CancellationToken cancellationToken = default)
		{
		var attempts = new List<CaptureAttempt> (3);
		try
			{
			for (int attempt = 0; ; attempt++)
				{
				var elapsed = Stopwatch.StartNew ();
				try
					{
					return await CaptureOnceAsync (cancellationToken).ConfigureAwait (false);
					}
				catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException)
					{
					attempts.Add (new (attempt + 1, elapsed.ElapsedMilliseconds, error.GetType ().Name, FailureCode(error)));
					if (attempt >= 2 || cancellationToken.IsCancellationRequested || error is OperationCanceledException)
						throw;
					// Only reads are repeated. No input operation occurs in this loop.
					await Task.Delay (TimeSpan.FromMilliseconds (250), cancellationToken).ConfigureAwait (false);
					}
				}
			}
		catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException)
			{
			// Keep cancellation/exception identity intact. Never retain device output,
			// command arguments or credentials in this bounded diagnostic record.
			error.Data[CAPTURE_ATTEMPTS_KEY] = System.Text.Json.JsonSerializer.Serialize (attempts);
			throw;
			}
		}

	private async Task<AndroidHierarchy> CaptureOnceAsync (CancellationToken cancellationToken)
        {
        // UIAutomator writes directly to its own stdout. No shared-storage file,
        // follow-up cat, or independently timed cleanup command can invalidate a read.
        var bytes = await hierarchyTransport.ExecuteAsync (["exec-out", "uiautomator", "dump", "/proc/self/fd/1"], cancellationToken).ConfigureAwait (false);
        if (bytes.Length > 4 * 1024 * 1024)
            throw CaptureFailure("stream-too-large", "Android hierarchy stream exceeded its size limit; no input was sent.");
        string response;
        try { response = new UTF8Encoding (false, true).GetString (bytes).TrimEnd (); }
        catch (DecoderFallbackException error) { throw CaptureFailure("invalid-utf8", "Android hierarchy stream was not valid UTF-8; no input was sent.", error); }
        // Android's original status message contains 'hierchary'; accept the
        // corrected spelling as well, but never strip arbitrary trailing output.
        string? trailer = new[] { "UI hierchary dumped to: /proc/self/fd/1", "UI hierarchy dumped to: /proc/self/fd/1" }
            .FirstOrDefault (suffix => response.EndsWith (suffix, StringComparison.Ordinal));
        if (trailer is null)
            throw CaptureFailure(response.StartsWith("ERROR: null root node", StringComparison.Ordinal) ? "null-root" : "missing-status", "Android could not capture its UI hierarchy; no input was sent.");
        try { return new AndroidHierarchy (response[..^trailer.Length].TrimEnd (), application); }
        catch (System.Xml.XmlException error) { throw CaptureFailure("invalid-xml", "Android returned an invalid hierarchy stream; no input was sent.", error); }
        }


	/// <summary>Sends one guarded tap while a cancellable, read-only observer watches for its attributed completion.
	/// The result is returned only after both input transport and observation succeed. The measured endpoint may
	/// precede transport return; transport startup and observer polling remain included. This is not physical latency.</summary>
	public Task<AndroidInputObservation<T>> ObserveTapAsync<T> (AndroidSelector selector, Action<AndroidHierarchy> validatePage,
		Func<CancellationToken, Task<T>> observeCompletion, CancellationToken cancellationToken = default)
		=> ObserveTapAsync (selector, validatePage, observeCompletion, static _ => Task.CompletedTask, cancellationToken);

	/// <summary>Prepares read-only evidence after page validation, before starting the input clock or sending the single tap.
	/// Preparation must not navigate or alter the validated page. The page and selected control are captured and validated again afterwards. A failed preparation or changed page sends no input.</summary>
	public async Task<AndroidInputObservation<T>> ObserveTapAsync<T> (AndroidSelector selector, Action<AndroidHierarchy> validatePage,
		Func<CancellationToken, Task<T>> observeCompletion, Func<CancellationToken, Task> prepareInput, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (prepareInput);
		ArgumentNullException.ThrowIfNull (observeCompletion);
		using var lifetime = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		Task<(T Value, long Timestamp, DateTimeOffset Utc)>? observation = null;
		bool observationFailed = false;
		long started = 0;
		DateTimeOffset inputUtc = default;
		async Task<(T Value, long Timestamp, DateTimeOffset Utc)> Observe ()
			{
			try
				{
				var value = await observeCompletion (lifetime.Token).ConfigureAwait (false);
				return (value, Stopwatch.GetTimestamp (), DateTimeOffset.UtcNow);
				}
			catch
				{
				observationFailed = true;
				await lifetime.CancelAsync ().ConfigureAwait (false);
				throw;
				}
			}
		try
			{
			await TapAsync (hierarchy => hierarchy.RequireUnique (selector), validatePage, () =>
				{
				inputUtc = DateTimeOffset.UtcNow;
				started = Stopwatch.GetTimestamp ();
				observation = Observe ();
				if (observation.IsCompleted)
					{
					// A synchronous failure is preserved; an already-complete observation cannot describe an unsent tap.
					observation.GetAwaiter ().GetResult ();
					throw new InvalidOperationException ("Completion was reported before input; no tap was sent.");
					}
				}, lifetime.Token, prepareInput).ConfigureAwait (false);
			long returned = Stopwatch.GetTimestamp ();
			var returnedUtc = DateTimeOffset.UtcNow;
			var observed = await (observation ?? throw new InvalidOperationException ("Input did not start.")).ConfigureAwait (false);
			return new (observed.Value, inputUtc, observed.Utc, returnedUtc,
				Stopwatch.GetElapsedTime (started, observed.Timestamp).TotalMilliseconds,
				Stopwatch.GetElapsedTime (started, returned).TotalMilliseconds);
			}
		catch (OperationCanceledException) when (observationFailed && !cancellationToken.IsCancellationRequested)
			{
			// Observer failure cancels an in-flight input, but its original attribution error remains the cause.
			if (observation != null) await observation.ConfigureAwait (false);
			throw;
			}
		finally
			{
			await lifetime.CancelAsync ().ConfigureAwait (false);
			// No orphan observer and no input replay after failure/cancellation. Preserve the original exception.
			if (observation != null)
				try { await observation.ConfigureAwait (false); } catch { }
			}
		}

	public Task TapAsync (AndroidSelector selector, Action<AndroidHierarchy> validatePage, CancellationToken cancellationToken = default)
		=> TapAsync (selector, validatePage, static () => { }, cancellationToken);

	internal async Task TapAsync (AndroidSelector selector, Action<AndroidHierarchy> validatePage, Action inputStarting, CancellationToken cancellationToken)
		{
		await TapAsync (hierarchy => hierarchy.RequireUnique (selector), validatePage, inputStarting, cancellationToken).ConfigureAwait (false);
		}

	internal async Task TapAsync (Func<AndroidHierarchy, AndroidElement> select, Action<AndroidHierarchy> validatePage, Action inputStarting, CancellationToken cancellationToken, Func<CancellationToken, Task>? prepareInput = null)
		{
		ArgumentNullException.ThrowIfNull (validatePage);
		ArgumentNullException.ThrowIfNull (select);
		var hierarchy = await CaptureAsync (cancellationToken).ConfigureAwait (false);
		validatePage (hierarchy);
		var element = select (hierarchy);
		if (!element.Enabled)
			throw new InvalidOperationException ("Android element is disabled; no input was sent.");
		if (prepareInput != null)
			{
			await prepareInput (cancellationToken).ConfigureAwait (false);
			// Recorder startup can outlive the page or its connection. Read again before
			// starting the observer or sending input; never use the earlier coordinates.
			hierarchy = await CaptureAsync (cancellationToken).ConfigureAwait (false);
			validatePage (hierarchy);
			element = select (hierarchy);
			if (!element.Enabled)
				throw new InvalidOperationException ("Android element became disabled during preparation; no input was sent.");
			}
		cancellationToken.ThrowIfCancellationRequested ();
		inputStarting ();
		await transport.ExecuteAsync (["shell", "input", "tap",
			((element.Left + element.Right) / 2).ToString (CultureInfo.InvariantCulture),
			((element.Top + element.Bottom) / 2).ToString (CultureInfo.InvariantCulture)], cancellationToken).ConfigureAwait (false);
		// Do not retry an input or silently infer a successful page transition.
		}

	internal async Task ScrollDownAsync (AndroidSelector container, Action<AndroidHierarchy> validatePage, Action inputStarting, CancellationToken token)
		=> await ScrollDownAsync (hierarchy => hierarchy.RequireUnique (container), validatePage, inputStarting, token).ConfigureAwait (false);

	internal async Task ScrollDownAsync (Func<AndroidHierarchy, AndroidElement> select, Action<AndroidHierarchy> validatePage, Action inputStarting, CancellationToken token)
		=> await ScrollAsync (select, validatePage, inputStarting, true, token).ConfigureAwait (false);

	internal async Task ScrollUpAsync (Func<AndroidHierarchy, AndroidElement> select, Action<AndroidHierarchy> validatePage, Action inputStarting, CancellationToken token)
		=> await ScrollAsync (select, validatePage, inputStarting, false, token).ConfigureAwait (false);

	private async Task ScrollAsync (Func<AndroidHierarchy, AndroidElement> select, Action<AndroidHierarchy> validatePage, Action inputStarting, bool down, CancellationToken token)
		{
		var hierarchy = await CaptureAsync (token).ConfigureAwait (false);
		validatePage (hierarchy);
		var element = select (hierarchy);
		if (!element.Enabled || element.Bottom - element.Top < 80)
			throw new InvalidOperationException ("The observed scroll container is unavailable or too small.");
		var x = ((element.Left + element.Right) / 2).ToString (CultureInfo.InvariantCulture);
		var start = (element.Top + (element.Bottom - element.Top) * 3 / 4).ToString (CultureInfo.InvariantCulture);
		var end = (element.Top + (element.Bottom - element.Top) / 4).ToString (CultureInfo.InvariantCulture);
		if (!down)
			(start, end) = (end, start);
		token.ThrowIfCancellationRequested ();
		inputStarting ();
		await transport.ExecuteAsync (["shell", "input", "swipe", x, start, x, end, "350"], token).ConfigureAwait (false);
		// One gesture within observed bounds. Uncertain inputs are never repeated.
		}

	/// <summary>Starts recording and waits for the first encoded display frame before returning.
	/// Await Completion to retain the stream, then dispose the handle. Disposal cancels and joins unfinished recording.
	/// The caller owns Android reservation and must verify before/input/after coverage when reviewing the recording.</summary>
	public async Task<AndroidScreenRecording> StartScreenRecordingAsync (int durationSeconds, CancellationToken cancellationToken = default)
		{
		var failures = new List<string> (2);
		for (int attempt = 0; ; attempt++)
			{
			try
				{
				var recording = await StartScreenRecordingOnceAsync (durationSeconds, cancellationToken).ConfigureAwait (false);
				recording.StartupFailures = failures.ToArray ();
				return recording;
				}
			catch (Exception error) when (error is InvalidDataException or IOException or TimeoutException && !cancellationToken.IsCancellationRequested)
				{
				// This retries only recorder startup, before returning a ready handle and before any caller input.
				// Keep the failed startup visible even when a second read-only attempt succeeds.
				failures.Add (error.GetType ().Name);
				error.Data["AndroidRecordingStartupFailures"] = System.Text.Json.JsonSerializer.Serialize (failures);
				if (attempt >= 1) throw;
				await Task.Delay (250, cancellationToken).ConfigureAwait (false);
				}
			}
		}

	/// <summary>Record until StopAsync is called after feedback observation. A 90-second tooling
	/// watchdog prevents an abandoned capture. Watchdog expiry is incomplete evidence, never a response-time verdict.
	/// Uses a private, random stop marker; no input is issued and no unrelated Android process is stopped.</summary>
	public async Task<AndroidScreenRecording> StartScreenRecordingUntilStoppedAsync(CancellationToken cancellationToken = default)
	{
		var failures = new List<string>(2);
		for(int attempt=0;;attempt++) {
			try { var recording=await StartControlledScreenRecordingOnceAsync(cancellationToken).ConfigureAwait(false); recording.StartupFailures=failures.ToArray(); return recording; }
			catch(Exception error) when(error is InvalidDataException or IOException or TimeoutException &&
				error.Data["AndroidRecorderCleanupConfirmed"] is true && !cancellationToken.IsCancellationRequested) {
				failures.Add(error.GetType().Name);
				error.Data["AndroidRecordingStartupFailures"]=System.Text.Json.JsonSerializer.Serialize(failures);
				if(attempt>=1)throw;
				await Task.Delay(250,cancellationToken).ConfigureAwait(false);
			}
		}
	}

	private async Task<AndroidScreenRecording> StartControlledScreenRecordingOnceAsync(CancellationToken cancellationToken)
	{
		if (transport is not IAndroidStreamingCommandTransport streaming)
			throw new NotSupportedException("Recording readiness requires a streaming Android transport.");
		string folder = "/data/local/tmp/chn-recording-" + Guid.NewGuid().ToString("N");
		// exec-out forwards the sh -c argument directly. Do not add shell quotes around the entire script.
		// Only generated alphanumeric path content is substituted.
		// The wrapper owns the child PID until wait completes, so the stop cannot target a reused/unrelated PID.
		string script = $"mkdir {folder} || exit 70; " +
			$"screenrecord --output-format=h264 --show-frame-time --bit-rate 2000000 --time-limit 90 - & child=$!; " +
			$"cleanup() {{ kill -INT $child 2>/dev/null; wait $child 2>/dev/null; echo aborted > {folder}/outcome; }}; " +
			"trap cleanup EXIT; trap 'exit 71' HUP INT TERM; requested=0; " +
			$"while kill -0 $child 2>/dev/null; do if [ -f {folder}/stop ]; then requested=1; kill -INT $child; break; fi; sleep 0.2; done; " +
			"wait $child; result=$?; trap - EXIT; " +
			$"if [ $requested = 1 ]; then echo stopped:$result > {folder}/outcome; else echo expired:$result > {folder}/outcome; fi; exit $result";
		var commands = new[] { "exec-out", "sh", "-c", script };
		var recordingTransport = transport is AdbCommandTransport adb ? adb.ForRecording(90) : streaming;
		return await StartControlledRecordingCoreAsync(recordingTransport, commands,
			async token => { await transport.ExecuteAsync(["shell", $"test -d {folder} && touch {folder}/stop"], token).ConfigureAwait(false); },
			async token => {
				// exec-out does not reliably propagate the remote shell exit code. Read the owned outcome explicitly.
				var outcome = await transport.ExecuteAsync(["shell", $"cat {folder}/outcome"], token).ConfigureAwait(false);
				if(Encoding.UTF8.GetString(outcome).Trim() != "stopped:0") throw new InvalidDataException("Recorder did not confirm a successful caller-requested stop.");
			},
			async token => { var result = await transport.ExecuteAsync(["shell", $"rm -f {folder}/stop {folder}/outcome; rmdir {folder}; test ! -d {folder} && echo removed"], token).ConfigureAwait(false); if(Encoding.UTF8.GetString(result).Trim() != "removed") throw new IOException("Recorder cleanup could not be confirmed."); },
			cancellationToken).ConfigureAwait(false);
	}

	private static async Task<AndroidScreenRecording> StartControlledRecordingCoreAsync(IAndroidStreamingCommandTransport transport,
		IReadOnlyList<string> commands, Func<CancellationToken, Task> stop, Func<CancellationToken, Task> verifyOutcome,
		Func<CancellationToken, Task> cleanup, CancellationToken cancellationToken)
	{
		var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var ready = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
		uint window = 0;
		void Observe(ReadOnlyMemory<byte> bytes) {
			foreach(byte value in bytes.Span) {
				window = (window << 8) | value;
				if((window & 0xffffff00) == 0x00000100 && (value & 31) is 1 or 5) ready.TrySetResult(DateTimeOffset.UtcNow);
			}
		}
		Task<byte[]>? recording = null;
		async Task<byte[]> Record() {
			var bytes = await transport.ExecuteStreamingAsync(commands, Observe, lifetime.Token).ConfigureAwait(false);
			await verifyOutcome(lifetime.Token).ConfigureAwait(false);
			if(bytes.Length < 5 || bytes[0] != 0 || bytes[1] != 0 || !(bytes[2] == 1 || (bytes[2] == 0 && bytes[3] == 1)))
				throw new InvalidDataException("Android did not return an H.264 screen recording.");
			return bytes;
		}
		try {
			recording = Record();
			await Task.WhenAny(ready.Task, recording).ConfigureAwait(false);
			if(recording.IsCompleted) { await recording.ConfigureAwait(false); throw new InvalidDataException("Recording ended before it was ready for input."); }
			return new(await ready.Task.ConfigureAwait(false), recording, lifetime, stop, cleanup);
		} catch(Exception error) {
			using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			try { await stop(cleanupDeadline.Token).ConfigureAwait(false); } catch { }
			await lifetime.CancelAsync().ConfigureAwait(false);
			if(recording != null) try { await recording.ConfigureAwait(false); } catch { }
			await cleanup(cleanupDeadline.Token).ConfigureAwait(false);
			lifetime.Dispose(); error.Data["AndroidRecorderCleanupConfirmed"]=true; throw;
		}
	}

	private async Task<AndroidScreenRecording> StartScreenRecordingOnceAsync (int durationSeconds, CancellationToken cancellationToken)
		{
		if (durationSeconds is < 1 or > 30) throw new ArgumentOutOfRangeException (nameof (durationSeconds));
		if (transport is not IAndroidStreamingCommandTransport streaming)
			throw new NotSupportedException ("Recording readiness requires a streaming Android transport.");
		var recordingTransport = transport is AdbCommandTransport adb ? adb.ForRecording (durationSeconds) : streaming;
		var lifetime = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		var ready = new TaskCompletionSource<DateTimeOffset> (TaskCreationOptions.RunContinuationsAsynchronously);
		uint window = 0;
		void Observe (ReadOnlyMemory<byte> bytes)
			{
			foreach (byte value in bytes.Span)
				{
				window = (window << 8) | value;
				// A VCL NAL is emitted after an actual display frame has reached the encoder.
				// SPS/PPS headers alone do not establish that recording is ready for input.
				if ((window & 0xffffff00) == 0x00000100 && (value & 31) is 1 or 5)
					ready.TrySetResult (DateTimeOffset.UtcNow);
				}
			}
		async Task<byte[]> Record ()
			{
			var bytes = await recordingTransport.ExecuteStreamingAsync (["exec-out", "screenrecord", "--output-format=h264",
				"--show-frame-time", "--bit-rate", "2000000", "--time-limit",
				durationSeconds.ToString (CultureInfo.InvariantCulture), "-"], Observe, lifetime.Token).ConfigureAwait (false);
			if (bytes.Length < 5 || bytes[0] != 0 || bytes[1] != 0 ||
				!(bytes[2] == 1 || (bytes[2] == 0 && bytes[3] == 1)))
				throw new InvalidDataException ("Android did not return an H.264 screen recording.");
			return bytes;
			}
		Task<byte[]>? recording = null;
		try
			{
			recording = Record ();
			await Task.WhenAny (ready.Task, recording).ConfigureAwait (false);
			if (recording.IsCompleted)
				{
				await recording.ConfigureAwait (false);
				throw new InvalidDataException ("Recording ended before it was ready for input.");
				}
			return new (await ready.Task.ConfigureAwait (false), recording, lifetime);
			}
		catch
			{
			await lifetime.CancelAsync ().ConfigureAwait (false);
			if (recording != null) try { await recording.ConfigureAwait (false); } catch { }
			lifetime.Dispose ();
			throw;
			}
		}

	/// <summary>Records a bounded H.264 display stream with Android frame timestamps for visual review.
	/// The caller owns the Android lease and must retain the stream, verify it covers the action, and review
	/// the visible transition. Starting this task is not proof that the first frame has been captured.
	/// Duration limits recording/storage only; it is not a device response acceptance threshold.</summary>
	public async Task<byte[]> CaptureScreenRecordingAsync (int durationSeconds, CancellationToken cancellationToken = default)
		{
		if (durationSeconds is < 1 or > 30)
			throw new ArgumentOutOfRangeException (nameof (durationSeconds));
		cancellationToken.ThrowIfCancellationRequested ();
		var bytes = await transport.ExecuteAsync (["exec-out", "screenrecord", "--output-format=h264",
			"--show-frame-time", "--bit-rate", "2000000", "--time-limit",
			durationSeconds.ToString (CultureInfo.InvariantCulture), "-"], cancellationToken).ConfigureAwait (false);
		// Annex B start code, not a complete codec/coverage check. A reviewer must decode the retained stream.
		if (bytes.Length < 5 || bytes[0] != 0 || bytes[1] != 0 ||
			!(bytes[2] == 1 || (bytes[2] == 0 && bytes[3] == 1)))
			throw new InvalidDataException ("Android did not return an H.264 screen recording.");
		return bytes;
		}

	public async Task<byte[]> CaptureScreenshotAsync (CancellationToken cancellationToken = default)
		{
		var bytes = await transport.ExecuteAsync (["exec-out", "screencap", "-p"], cancellationToken).ConfigureAwait (false);
		if (bytes.Length < 8 || !bytes.AsSpan (0, 8).SequenceEqual (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
			throw new InvalidDataException ("Android did not return a PNG screenshot.");
		return bytes;
		}

	public Task BackAsync (Action<AndroidHierarchy> validatePage, CancellationToken cancellationToken = default)
		=> BackAsync (validatePage, static () => { }, cancellationToken);

	internal async Task BackAsync (Action<AndroidHierarchy> validatePage, Action inputStarting, CancellationToken cancellationToken)
		{
		ArgumentNullException.ThrowIfNull (validatePage);
		validatePage (await CaptureAsync (cancellationToken).ConfigureAwait (false));
		cancellationToken.ThrowIfCancellationRequested ();
		inputStarting ();
		await transport.ExecuteAsync (["shell", "input", "keyevent", "KEYCODE_BACK"], cancellationToken).ConfigureAwait (false);
		// Like TapAsync, this never retries an input with an uncertain outcome.
		}
	}