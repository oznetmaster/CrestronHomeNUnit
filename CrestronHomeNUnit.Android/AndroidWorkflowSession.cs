// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeNUnit.Android;

public sealed record AndroidSessionProfile (string AdbExecutable, string DeviceSerial, string Application, string ExpectedHomeText, string LockPath)
	{
	public void Validate ()
		{
		if (!Path.IsPathFullyQualified (AdbExecutable) || !File.Exists (AdbExecutable) ||
			string.IsNullOrWhiteSpace (DeviceSerial) || string.IsNullOrWhiteSpace (Application) || string.IsNullOrWhiteSpace (ExpectedHomeText) ||
			!Path.IsPathFullyQualified (LockPath) || !Directory.Exists (Path.GetDirectoryName (LockPath)))
			throw new ArgumentException ("Android tests require an existing ADB installation, explicit device/application/home and an existing private lock directory.");
		}
	}

public sealed record AndroidRunContext (int SchemaVersion, string RunId, string Machine, int CoordinatorPid, long CoordinatorStartUtcTicks,
	string ProcessorAddress, int InstalledDriverId, string DriverGuid, string DriverVersion, string PackageSha256, string SourceSha256,
	AndroidSessionProfile Profile, string EvidenceDirectory);
public sealed record AndroidRunCompletion (int SchemaVersion, string RunId, string PackageSha256, bool RestorationConfirmed);

/// <summary>A workflow-owned session; ordinary desktop tests have no implicit Android connection.</summary>
public sealed class AndroidWorkflowSession
	{
	public const string CONTEXT_VARIABLE = "CRESTRON_HOME_ANDROID_CONTEXT";
	private static readonly JsonSerializerOptions Options = new ()
		{
		PropertyNameCaseInsensitive = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		RespectRequiredConstructorParameters = true,
		RespectNullableAnnotations = true
		};
	private bool _completed;
	public AndroidRunContext Context { get; }
	public AndroidDevice Device { get; }
	internal AndroidWorkflowSession (AndroidRunContext context, AndroidDevice device)
		{
		Context = context;
		Device = device;
		}

	public static T Read<T> (string path) => JsonSerializer.Deserialize<T> (File.ReadAllText (path), Options) ?? throw new InvalidDataException ("Missing Android workflow data.");

	public static Task<AndroidWorkflowSession> OpenFromEnvironmentAsync (CancellationToken token = default)
		{
		var path = Environment.GetEnvironmentVariable (CONTEXT_VARIABLE);
		if (string.IsNullOrWhiteSpace (path) || !Path.IsPathFullyQualified (path))
			throw new InvalidOperationException ("Android tests must be invoked by an opted-in processor workflow.");
		var context = Read<AndroidRunContext> (path);
		return OpenAsync (context, new (new AdbCommandTransport (context.Profile.AdbExecutable, context.Profile.DeviceSerial, TimeSpan.FromSeconds (25)), context.Profile.Application), token);
		}

	internal static async Task<AndroidWorkflowSession> OpenAsync (AndroidRunContext context, AndroidDevice device, CancellationToken token)
		{
		VerifyContext (context);
		using (var started = new FileStream (Path.Combine (context.EvidenceDirectory, "session-started"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
			started.Flush (flushToDisk: true);
		var session = new AndroidWorkflowSession (context, device);
		try
			{
			var hierarchy = await session.Device.CaptureAsync (token).ConfigureAwait (false);
			if (context.Profile.Application == "com.crestron.phoenix.app")
				CrestronHomePages.RequireHome (hierarchy, context.Profile.ExpectedHomeText);
			else
				_ = hierarchy.RequireUnique (new (AndroidSelectorKind.Text, context.Profile.ExpectedHomeText));
			return session;
			}
		catch
			{
			// Opening only reads the UI. No input or physical control has been sent.
			session.Complete (restorationConfirmed: true);
			throw;
			}
		}

	public static void VerifyContext (AndroidRunContext context)
		{
		context.Profile.Validate ();
		if (context.SchemaVersion != 1 || context.Machine != Environment.MachineName || context.InstalledDriverId <= 0 ||
			!Guid.TryParse (context.DriverGuid, out _) || !Version.TryParse (context.DriverVersion, out _) ||
			!IsHash (context.PackageSha256) || !IsHash (context.SourceSha256) ||
			!Path.IsPathFullyQualified (context.EvidenceDirectory) || !Directory.Exists (context.EvidenceDirectory))
			throw new InvalidDataException ("Invalid Android workflow identity.");
		using var coordinator = Process.GetProcessById (context.CoordinatorPid);
		if (coordinator.HasExited || coordinator.StartTime.ToUniversalTime ().Ticks != context.CoordinatorStartUtcTicks)
			throw new IOException ("The Android workflow coordinator is no longer active.");
		AndroidSessionLease.VerifyOwner (context.Profile.LockPath, context.RunId);
		}

	private static bool IsHash (string value) => value.Length == 64 && value.All (char.IsAsciiHexDigit);

	public async Task CaptureAsync (string checkId, Action<AndroidHierarchy> verify, CancellationToken token = default)
		{
		if (_completed || string.IsNullOrWhiteSpace (checkId) || checkId.Length > 100 || checkId.Any (c => !char.IsAsciiLetterOrDigit (c) && c is not ('-' or '_' or '.')))
			throw new InvalidOperationException ("Use a unique check ID within an active Android workflow session.");
		VerifyContext (Context);
		var directory = Path.Combine (Context.EvidenceDirectory, checkId);
		if (Directory.Exists (directory))
			throw new IOException ("Android evidence already exists for this check; it cannot be replaced.");
		Directory.CreateDirectory (directory);
		var started = DateTimeOffset.UtcNow;
		var hierarchy = await Device.CaptureAsync (token).ConfigureAwait (false);
		await File.WriteAllTextAsync (Path.Combine (directory, "hierarchy.xml"), hierarchy.MaskedXml, token).ConfigureAwait (false);
		var screenshot = await Device.CaptureScreenshotAsync (token).ConfigureAwait (false);
		await File.WriteAllBytesAsync (Path.Combine (directory, "screen.png"), screenshot, token).ConfigureAwait (false);
		verify (hierarchy);
		VerifyContext (Context);
		var record = new
			{
			SchemaVersion = 1, Context.RunId, Context.PackageSha256, Context.SourceSha256, Context.InstalledDriverId,
			Context.DriverGuid, Context.DriverVersion, CheckId = checkId, StartedUtc = started, FinishedUtc = DateTimeOffset.UtcNow,
			HierarchySha256 = Convert.ToHexString (SHA256.HashData (await File.ReadAllBytesAsync (Path.Combine (directory, "hierarchy.xml"), token).ConfigureAwait (false))),
			ScreenshotSha256 = Convert.ToHexString (SHA256.HashData (screenshot)), Outcome = "Passed"
			};
		await File.WriteAllTextAsync (Path.Combine (directory, "observation.json"), JsonSerializer.Serialize (record), token).ConfigureAwait (false);
		}

	/// <summary>Call after all inputs have completed and physical starting state has been restored, including on a failed assertion.</summary>
	public void Complete (bool restorationConfirmed)
		{
		if (_completed)
			throw new InvalidOperationException ("Android session completion was already reported.");
		VerifyContext (Context);
		using var stream = new FileStream (Path.Combine (Context.EvidenceDirectory, "completion.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		JsonSerializer.Serialize (stream, new AndroidRunCompletion (1, Context.RunId, Context.PackageSha256, restorationConfirmed));
		stream.Flush (flushToDisk: true);
		_completed = true;
		}
	}