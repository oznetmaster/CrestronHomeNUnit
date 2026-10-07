// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeNUnit.Android;

public sealed record AndroidSessionProfile (string AdbExecutable, string DeviceSerial, string Application, string ExpectedHomeText, string LockPath)
	{
	public int LocalPort { get; init; } = 50001;
	/// <summary>Explicit opt-in to navigation from these saved Homes. Empty preserves read-only session opening.</summary>
	public IReadOnlyList<string> AllowedStartingHomes { get; init; } = [];

	// Profiles are compared after independent JSON reads (including final removal).
	// A list's object identity must not make otherwise identical settings unequal.
	public bool Equals (AndroidSessionProfile? other) => other is not null &&
		AdbExecutable == other.AdbExecutable && DeviceSerial == other.DeviceSerial &&
		Application == other.Application && ExpectedHomeText == other.ExpectedHomeText &&
		LockPath == other.LockPath && LocalPort == other.LocalPort &&
		(ReferenceEquals (AllowedStartingHomes, other.AllowedStartingHomes) ||
		AllowedStartingHomes is not null && other.AllowedStartingHomes is not null &&
		AllowedStartingHomes.SequenceEqual (other.AllowedStartingHomes, StringComparer.Ordinal));
	public override int GetHashCode ()
		{
		var hash = new HashCode ();
		hash.Add (AdbExecutable, StringComparer.Ordinal); hash.Add (DeviceSerial, StringComparer.Ordinal);
		hash.Add (Application, StringComparer.Ordinal); hash.Add (ExpectedHomeText, StringComparer.Ordinal);
		hash.Add (LockPath, StringComparer.Ordinal); hash.Add (LocalPort);
		if (AllowedStartingHomes is not null) foreach (string home in AllowedStartingHomes) hash.Add (home, StringComparer.Ordinal);
		return hash.ToHashCode ();
		}

	public void Validate ()
		{
		if (AllowedStartingHomes == null || AllowedStartingHomes.Any (string.IsNullOrWhiteSpace) ||
			AllowedStartingHomes.Distinct (StringComparer.Ordinal).Count () != AllowedStartingHomes.Count ||
			AllowedStartingHomes.Count > 0 && Application != "com.crestron.phoenix.app")
			throw new ArgumentException ("Saved-Home selection requires explicit unique Home names and the Crestron Home application.");
		if (!Path.IsPathFullyQualified (AdbExecutable) || !File.Exists (AdbExecutable) ||
			string.IsNullOrWhiteSpace (DeviceSerial) || string.IsNullOrWhiteSpace (Application) || string.IsNullOrWhiteSpace (ExpectedHomeText) ||
			!Path.IsPathFullyQualified (LockPath) || !Directory.Exists (Path.GetDirectoryName (LockPath)) || LocalPort is < 1 or > 65535)
			throw new ArgumentException ("Android tests require an existing ADB installation, explicit device/application/home, a valid local port and an existing private lock directory.");
		}
	}

public sealed record AndroidRunContext (int SchemaVersion, string RunId, string Machine, int CoordinatorPid, long CoordinatorStartUtcTicks,
	string ProcessorAddress, int InstalledDriverId, string DriverGuid, string DriverVersion, string PackageSha256, string SourceSha256,
	AndroidSessionProfile Profile, string EvidenceDirectory)
	{
	public string? ReleaseSourceCommit { get; init; }
	public IReadOnlyList<AndroidManagedDeviceBinding> ManagedDevices { get; init; } = [];

	/// <summary>Resolve only a child supplied by this workflow; no inventory lookup or fallback is performed.</summary>
	public AndroidManagedDeviceBinding RequireManagedDevice (string alias)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (alias);
		ValidateManagedDevices ();
		return ManagedDevices.SingleOrDefault (binding => binding.Alias.Equals (alias, StringComparison.Ordinal))
			?? throw new InvalidDataException ("The requested managed-child alias was not supplied by this workflow.");
		}

	internal void ValidateManagedDevices ()
		{
		if (ManagedDevices == null || ManagedDevices.Any (binding => binding == null ||
			string.IsNullOrWhiteSpace (binding.Alias) || binding.Alias.Length > 64 ||
			binding.Alias.Any (c => !char.IsAsciiLetterOrDigit (c) && c is not ('_' or '-')) ||
			binding.DeviceId <= 0 || binding.DeviceId == InstalledDriverId || binding.ParentDriverId != InstalledDriverId ||
			binding.LocationId <= 0 || string.IsNullOrWhiteSpace (binding.Model) || string.IsNullOrWhiteSpace (binding.Name)) ||
			ManagedDevices.Select (binding => binding.Alias).Distinct (StringComparer.OrdinalIgnoreCase).Count () != ManagedDevices.Count ||
			ManagedDevices.Select (binding => binding.DeviceId).Distinct ().Count () != ManagedDevices.Count)
			throw new InvalidDataException ("Invalid or ambiguous managed-child workflow bindings.");
		}
	}

/// <summary>The actual identity of a child created for this run. Consumers must still verify its current processor state.</summary>
public sealed record AndroidManagedDeviceBinding (string Alias, int DeviceId, int ParentDriverId, string Model, string Name, int LocationId);
public sealed record AndroidRunCompletion (int SchemaVersion, string RunId, string PackageSha256, bool RestorationConfirmed);
/// <summary>Bounds of the successful hierarchy read, excluding later screenshot and evidence storage.</summary>
public sealed record AndroidHierarchyObservation (DateTimeOffset StartedUtc, DateTimeOffset ObservedUtc);

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
	/// <summary>True only when opening this session verified its configured saved address and port,
	/// retained the evidence, and restored Home. This is startup evidence, not a current route guarantee.</summary>
	public bool SavedEndpointVerifiedOnOpen { get; private set; }
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
		return OpenAsync (context, token);
		}

	/// <summary>Open an explicitly supplied workflow context under its existing Android reservation.</summary>
	public static Task<AndroidWorkflowSession> OpenAsync (AndroidRunContext context, CancellationToken token = default)
		=> OpenAsync (context, new (new AdbCommandTransport (context.Profile.AdbExecutable, context.Profile.DeviceSerial, TimeSpan.FromSeconds (25)), context.Profile.Application,
            // Hierarchy generation on the evidence emulator can approach the input transport's 25-second deadline.
            // This bounds observation work only; driver response timing starts after guarded preparation.
            new AdbCommandTransport (context.Profile.AdbExecutable, context.Profile.DeviceSerial, TimeSpan.FromSeconds (55))), token);

	internal static async Task<AndroidWorkflowSession> OpenAsync (AndroidRunContext context, AndroidDevice device, CancellationToken token)
		{
		VerifyContext (context);
		using (var started = new FileStream (Path.Combine (context.EvidenceDirectory, "session-started"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
			started.Flush (flushToDisk: true);
		var session = new AndroidWorkflowSession (context, device);
		bool navigationAttempted = false;
		CrestronHomeNavigation? openingNavigation = null;
		try
			{
			if (context.Profile.AllowedStartingHomes.Count > 0)
				{
				openingNavigation = new (session);
				await openingNavigation.SelectExpectedHomeAsync (() => navigationAttempted = true, token).ConfigureAwait (false);
				session.SavedEndpointVerifiedOnOpen = true;
				return session;
				}
			var hierarchy = await session.Device.CaptureAsync (token).ConfigureAwait (false);
			if (context.Profile.Application == "com.crestron.phoenix.app")
				CrestronHomePages.RequireHome (hierarchy, context.Profile.ExpectedHomeText);
			else
				_ = hierarchy.RequireUnique (new (AndroidSelectorKind.Text, context.Profile.ExpectedHomeText));
			return session;
			}
		catch
			{
			// A failed endpoint assertion can still have completed verified Home restoration.
			// Uncertain navigation without that proof must retain its reservation.
			session.Complete (restorationConfirmed: !navigationAttempted || openingNavigation?.HomeRestored == true);
			throw;
			}
		}

	public static void VerifyContext (AndroidRunContext context)
		{
		context.Profile.Validate ();
		context.ValidateManagedDevices ();
		if (context.SchemaVersion != 1 || context.Machine != Environment.MachineName || context.InstalledDriverId <= 0 ||
			!Guid.TryParse (context.DriverGuid, out _) || !Version.TryParse (context.DriverVersion, out _) ||
			!IsHash (context.PackageSha256) || !IsHash (context.SourceSha256) ||
			context.ReleaseSourceCommit != null && (context.ReleaseSourceCommit.Length is not (40 or 64) || !context.ReleaseSourceCommit.All (char.IsAsciiHexDigit)) ||
			!Path.IsPathFullyQualified (context.EvidenceDirectory) || !Directory.Exists (context.EvidenceDirectory))
			throw new InvalidDataException ("Invalid Android workflow identity.");
		using var coordinator = Process.GetProcessById (context.CoordinatorPid);
		if (coordinator.HasExited || coordinator.StartTime.ToUniversalTime ().Ticks != context.CoordinatorStartUtcTicks)
			throw new IOException ("The Android workflow coordinator is no longer active.");
		AndroidSessionLease.VerifyOwner (context.Profile.LockPath, context.RunId);
		}

	private static bool IsHash (string value) => value.Length == 64 && value.All (char.IsAsciiHexDigit);

	internal void VerifyActive ()
		{
		if (_completed) throw new InvalidOperationException ("The Android workflow session is already complete.");
		VerifyContext (Context);
		}

	public async Task CaptureAsync (string checkId, Action<AndroidHierarchy> verify, CancellationToken token = default)
		=> await CaptureCoreAsync (checkId, verify, null, token).ConfigureAwait (false);

	/// <summary>Wait for a matching UI state and retain that exact hierarchy plus a screenshot.
	/// Polling performs reads only; callers must supply cancellation to bound the wait.
	/// The returned time records the successful read, not completion of evidence storage.</summary>
	public Task<AndroidHierarchyObservation> CaptureWhenAsync (string checkId, Func<AndroidHierarchy, bool> matches, CancellationToken token)
		{
		ArgumentNullException.ThrowIfNull (matches);
		return CaptureCoreAsync (checkId, _ => { }, matches, token);
		}

	private async Task<AndroidHierarchyObservation> CaptureCoreAsync (string checkId, Action<AndroidHierarchy> verify,
		Func<AndroidHierarchy, bool>? matches, CancellationToken token)
		{
		if (_completed || string.IsNullOrWhiteSpace (checkId) || checkId.Length > 100 || checkId.Any (c => !char.IsAsciiLetterOrDigit (c) && c is not ('-' or '_' or '.')))
			throw new InvalidOperationException ("Use a unique check ID within an active Android workflow session.");
		VerifyContext (Context);
		var directory = Path.Combine (Context.EvidenceDirectory, checkId);
		if (Directory.Exists (directory))
			throw new IOException ("Android evidence already exists for this check; it cannot be replaced.");
		Directory.CreateDirectory (directory);
		DateTimeOffset started, observed;
		AndroidHierarchy hierarchy;
		while (true)
			{
			VerifyActive ();
			token.ThrowIfCancellationRequested ();
			started = DateTimeOffset.UtcNow;
			hierarchy = await Device.CaptureAsync (token).ConfigureAwait (false);
			observed = DateTimeOffset.UtcNow;
			if (matches == null || matches (hierarchy)) break;
			await Task.Delay (TimeSpan.FromMilliseconds (500), token).ConfigureAwait (false);
			}
		VerifyActive ();
		await File.WriteAllTextAsync (Path.Combine (directory, "hierarchy.xml"), hierarchy.MaskedXml, token).ConfigureAwait (false);
		var screenshot = await Device.CaptureScreenshotAsync (token).ConfigureAwait (false);
		await File.WriteAllBytesAsync (Path.Combine (directory, "screen.png"), screenshot, token).ConfigureAwait (false);
		verify (hierarchy);
		VerifyContext (Context);
		var record = new
			{
			SchemaVersion = 1, Context.RunId, Context.PackageSha256, Context.SourceSha256, Context.ReleaseSourceCommit, Context.InstalledDriverId,
			Context.DriverGuid, Context.DriverVersion, CheckId = checkId, StartedUtc = started, HierarchyObservedUtc = observed, FinishedUtc = DateTimeOffset.UtcNow,
			HierarchySha256 = Convert.ToHexString (SHA256.HashData (await File.ReadAllBytesAsync (Path.Combine (directory, "hierarchy.xml"), token).ConfigureAwait (false))),
			ScreenshotSha256 = Convert.ToHexString (SHA256.HashData (screenshot)), Outcome = "Passed"
			};
		await File.WriteAllTextAsync (Path.Combine (directory, "observation.json"), JsonSerializer.Serialize (record), token).ConfigureAwait (false);
		return new (started, observed);
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
