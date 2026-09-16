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
public sealed class AdbCommandTransport : IAndroidCommandTransport
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

	public async Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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
			var readOutput = process.StandardOutput.BaseStream.CopyToAsync (output, timeout.Token);
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
public sealed class AndroidDevice (IAndroidCommandTransport transport, string application)
	{
	public async Task<AndroidHierarchy> CaptureAsync (CancellationToken cancellationToken = default)
		{
		var path = "/sdcard/ch-ui-" + Guid.NewGuid ().ToString ("N") + ".xml";
		try
			{
			var response = await transport.ExecuteAsync (["shell", "uiautomator", "dump", path], cancellationToken).ConfigureAwait (false);
			if (!Encoding.UTF8.GetString (response).Contains ("dumped to:", StringComparison.Ordinal))
				throw new IOException ("Android could not capture its UI hierarchy; no input was sent.");
			var bytes = await transport.ExecuteAsync (["shell", "cat", path], cancellationToken).ConfigureAwait (false);
			return new (Encoding.UTF8.GetString (bytes), application);
			}
		finally
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (3));
			// Only this capture's unique temporary XML can be removed.
			await transport.ExecuteAsync (["shell", "rm", path], cleanup.Token).ConfigureAwait (false);
			}
		}

	public Task TapAsync (AndroidSelector selector, Action<AndroidHierarchy> validatePage, CancellationToken cancellationToken = default)
		=> TapAsync (selector, validatePage, static () => { }, cancellationToken);

	internal async Task TapAsync (AndroidSelector selector, Action<AndroidHierarchy> validatePage, Action inputStarting, CancellationToken cancellationToken)
		{
		ArgumentNullException.ThrowIfNull (validatePage);
		var hierarchy = await CaptureAsync (cancellationToken).ConfigureAwait (false);
		validatePage (hierarchy);
		var element = hierarchy.RequireUnique (selector);
		if (!element.Enabled)
			throw new InvalidOperationException ("Android element is disabled; no input was sent.");
		cancellationToken.ThrowIfCancellationRequested ();
		inputStarting ();
		await transport.ExecuteAsync (["shell", "input", "tap",
			((element.Left + element.Right) / 2).ToString (CultureInfo.InvariantCulture),
			((element.Top + element.Bottom) / 2).ToString (CultureInfo.InvariantCulture)], cancellationToken).ConfigureAwait (false);
		// Do not retry an input or silently infer a successful page transition.
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