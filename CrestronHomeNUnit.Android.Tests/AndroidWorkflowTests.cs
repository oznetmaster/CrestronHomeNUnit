// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class AndroidWorkflowTests
	{
	private string _directory = null!;
	private string _lock = null!;
	private string _owner = null!;

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		_lock = Path.Combine (_directory, "android.lease");
		_owner = Guid.NewGuid ().ToString ("N");
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_directory, recursive: true);

	private AndroidRunContext Context ()
		{
		using var process = Process.GetCurrentProcess ();
		return new (1, _owner, Environment.MachineName, process.Id, process.StartTime.ToUniversalTime ().Ticks,
			"192.0.2.1", 7, Guid.NewGuid ().ToString (), "1.0.0.1", new ('A', 64), new ('B', 64),
			new (Environment.ProcessPath!, "fixture-serial", "example.app", "Example Home", _lock), _directory);
		}

	[Test]
	public void ConcurrentSessionIsRejectedAndOnlyExplicitReleaseRemovesReservation ()
		{
		using (var first = AndroidSessionLease.Acquire (_lock, _owner))
			{
			Assert.Throws<IOException> (() => AndroidSessionLease.Acquire (_lock, Guid.NewGuid ().ToString ("N")));
			AndroidSessionLease.VerifyOwner (_lock, _owner);
			first.Release ();
			}
		Assert.That (File.Exists (_lock), Is.False);
		using var second = AndroidSessionLease.Acquire (_lock, Guid.NewGuid ().ToString ("N"));
		second.Release ();
		}

	[Test]
	public void AbandonedReservationDoesNotExpireOrGetStolen ()
		{
		using (var first = AndroidSessionLease.Acquire (_lock, _owner)) { }
		Assert.That (File.ReadAllText (_lock), Is.EqualTo (_owner));
		Assert.Throws<IOException> (() => AndroidSessionLease.Acquire (_lock, Guid.NewGuid ().ToString ("N")));
		Assert.Throws<IOException> (() => AndroidSessionLease.VerifyOwner (_lock, Guid.NewGuid ().ToString ("N")));
		}

	[Test]
	public void ContextRequiresLiveCoordinatorAndWellFormedIdentity ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		AndroidWorkflowSession.VerifyContext (context);
		Assert.Throws<IOException> (() => AndroidWorkflowSession.VerifyContext (context with { CoordinatorStartUtcTicks = 1 }));
		Assert.Throws<InvalidDataException> (() => AndroidWorkflowSession.VerifyContext (context with { PackageSha256 = "invalid" }));
		Assert.Throws<InvalidDataException> (() => AndroidWorkflowSession.VerifyContext (context with { Machine = "another-worker" }));
		Assert.Throws<InvalidDataException> (() => AndroidWorkflowSession.VerifyContext (context with { ReleaseSourceCommit = "branch-name" }));
		}

	[Test]
	public void ManagedChildBindingsRoundTripWithoutSelectingAnotherDevice ()
		{
		var binding = new AndroidManagedDeviceBinding ("room", 19, 7, "Example Child", "CI Child", 3);
		var context = Context () with { ManagedDevices = [binding] };
		string file = Path.Combine (_directory, "context.json");
		File.WriteAllText (file, JsonSerializer.Serialize (context));
		var restored = AndroidWorkflowSession.Read<AndroidRunContext> (file);
		Assert.That (restored.RequireManagedDevice ("room"), Is.EqualTo (binding));
		Assert.Throws<InvalidDataException> (() => restored.RequireManagedDevice ("another-room"));
		}

	[Test]
	public void LegacyContextHasNoImplicitManagedChild ()
		{
		var json = System.Text.Json.Nodes.JsonNode.Parse (JsonSerializer.Serialize (Context ()))!.AsObject ();
		json.Remove ("ManagedDevices");
		string file = Path.Combine (_directory, "legacy.json");
		File.WriteAllText (file, json.ToJsonString ());
		var context = AndroidWorkflowSession.Read<AndroidRunContext> (file);
		Assert.That (context.ManagedDevices, Is.Empty);
		Assert.Throws<InvalidDataException> (() => context.RequireManagedDevice ("room"));
		}

	[TestCase ("parent")]
	[TestCase ("root")]
	[TestCase ("alias")]
	[TestCase ("duplicate-alias")]
	[TestCase ("duplicate-id")]
	[TestCase ("location")]
	public void InvalidManagedChildBindingsRejectTheSessionBeforeUiAccess (string fault)
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var binding = new AndroidManagedDeviceBinding ("room", 19, 7, "Example Child", "CI Child", 3);
		IReadOnlyList<AndroidManagedDeviceBinding> bindings = fault switch
			{
			"parent" => [binding with { ParentDriverId = 8 }],
			"root" => [binding with { DeviceId = 7 }],
			"alias" => [binding with { Alias = "../room" }],
			"duplicate-alias" => [binding, binding with { Alias = "ROOM", DeviceId = 20 }],
			"duplicate-id" => [binding, binding with { Alias = "second" }],
			_ => [binding with { LocationId = 0 }]
			};
		Assert.Throws<InvalidDataException> (() => AndroidWorkflowSession.VerifyContext (Context () with { ManagedDevices = bindings }));
		}

	[Test]
	public async Task CapturedEvidenceUsesActualRunIdentityAndMasksPasswordHierarchy ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context () with { ReleaseSourceCommit = new ('d', 40) };
		var session = new AndroidWorkflowSession (context, new (new CaptureTransport (), "example.app"));
		await session.CaptureAsync ("home", hierarchy => Assert.That (hierarchy.RequireUnique (new (AndroidSelectorKind.Text, "Example Home")).Enabled, Is.True));
		using var record = JsonDocument.Parse (File.ReadAllText (Path.Combine (_directory, "home", "observation.json")));
		Assert.That (record.RootElement.GetProperty ("RunId").GetString (), Is.EqualTo (_owner));
		Assert.That (record.RootElement.GetProperty ("PackageSha256").GetString (), Is.EqualTo (context.PackageSha256));
		Assert.That (record.RootElement.GetProperty ("ReleaseSourceCommit").GetString (), Is.EqualTo (context.ReleaseSourceCommit));
		Assert.That (File.ReadAllText (Path.Combine (_directory, "home", "hierarchy.xml")), Does.Not.Contain ("private-sentinel"));
		await Assert.ThrowsAsync<IOException> (() => session.CaptureAsync ("home", _ => { }));
		session.Complete (restorationConfirmed: true);
		var completion = AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json"));
		Assert.That (completion.RestorationConfirmed, Is.True);
		Assert.That (completion.PackageSha256, Is.EqualTo (context.PackageSha256));
		await Assert.ThrowsAsync<InvalidOperationException> (() => session.CaptureAsync ("later", _ => { }));
		}

	[Test]
	public async Task SessionCanOnlyOpenOnceEvenAfterReportingRestoration ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		var device = new AndroidDevice (new CaptureTransport (), "example.app");
		var session = await AndroidWorkflowSession.OpenAsync (context, device, CancellationToken.None);
		session.Complete (restorationConfirmed: true);
		await Assert.ThrowsAsync<IOException> (() => AndroidWorkflowSession.OpenAsync (context, device, CancellationToken.None));
		}

	[Test]
	public async Task FailedReadOnlyReadinessReportsRestorationButExposesNoSession ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		context = context with { Profile = context.Profile with { ExpectedHomeText = "Wrong Home" } };
		await Assert.CatchAsync (() => AndroidWorkflowSession.OpenAsync (context, new (new CaptureTransport (), "example.app"), CancellationToken.None));
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.True);
		Assert.That (Directory.GetFiles (_directory, "observation.json", SearchOption.AllDirectories), Is.Empty);
		Assert.That (File.Exists (_lock), Is.True);
		}

	[Test]
	public async Task CrestronSessionRejectsHomeTextBehindAnOpenDriverPanel ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		context = context with { Profile = context.Profile with { Application = "com.crestron.phoenix.app" } };
		var xml = "<hierarchy><node package=\"com.crestron.phoenix.app\" resource-id=\"com.crestron.phoenix.app:id/home_wholeHouse_name\" text=\"Example Home\" enabled=\"true\" bounds=\"[0,0][100,100]\"/><node package=\"com.crestron.phoenix.app\" resource-id=\"com.crestron.phoenix.app:id/customdevices_toolbarClose\" enabled=\"true\" bounds=\"[0,0][20,20]\"/></hierarchy>";
		await Assert.ThrowsAsync<InvalidOperationException> (() => AndroidWorkflowSession.OpenAsync (context, new (new CaptureTransport (xml), context.Profile.Application), CancellationToken.None));
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.True);
		}

	[Test]
	public async Task FailedPageAssertionKeepsCaptureButCannotCreatePassingObservation ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var session = new AndroidWorkflowSession (Context (), new (new CaptureTransport (), "example.app"));
		await Assert.ThrowsAsync<InvalidOperationException> (() => session.CaptureAsync ("wrong-page", _ => throw new InvalidOperationException ()));
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-page", "screen.png")), Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-page", "observation.json")), Is.False);
		session.Complete (restorationConfirmed: false);
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.False);
		}

	[Test]
	public async Task MatchingCaptureRetainsOneSuccessfulReadWithoutDumpingItAgain ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var transport = new ObservedCaptureTransport ();
		var session = new AndroidWorkflowSession (Context (), new (transport, "example.app"));
		int attempts = 0;
		var observation = await session.CaptureWhenAsync ("recovery", _ => ++attempts == 2, CancellationToken.None);
		Assert.That (transport.Dumps, Is.EqualTo (2), "One waiting read and one successful read; no third proof read.");
		Assert.That (transport.Screenshots, Is.EqualTo (1));
		Assert.That (observation.ObservedUtc, Is.LessThanOrEqualTo (transport.ScreenshotStarted));
		using var record = JsonDocument.Parse (File.ReadAllText (Path.Combine (_directory, "recovery", "observation.json")));
		Assert.That (record.RootElement.GetProperty ("HierarchyObservedUtc").GetDateTimeOffset (), Is.EqualTo (observation.ObservedUtc));
		Assert.That (record.RootElement.GetProperty ("FinishedUtc").GetDateTimeOffset (), Is.GreaterThanOrEqualTo (observation.ObservedUtc));
		await Assert.ThrowsAsync<IOException> (() => session.CaptureWhenAsync ("recovery", _ => true, CancellationToken.None));
		}

	[Test]
	public async Task CancelledWaitingCaptureProducesNoPassingEvidence ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		using var cancellation = new CancellationTokenSource ();
		var transport = new ObservedCaptureTransport ();
		var session = new AndroidWorkflowSession (Context (), new (transport, "example.app"));
		await Assert.CatchAsync<OperationCanceledException> (() => session.CaptureWhenAsync ("waiting", _ => {
			cancellation.Cancel ();
			return false;
			}, cancellation.Token));
		Assert.That (transport.Dumps, Is.EqualTo (1));
		Assert.That (transport.Screenshots, Is.Zero);
		Assert.That (File.Exists (Path.Combine (_directory, "waiting", "observation.json")), Is.False);
		}

	[Test]
	public async Task MatchingCaptureFailureCannotBecomeAPassOrRetryAnAssertion ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var transport = new ObservedCaptureTransport ();
		var session = new AndroidWorkflowSession (Context (), new (transport, "example.app"));
		await Assert.ThrowsAsync<InvalidOperationException> (() => session.CaptureWhenAsync ("failure", _ => throw new InvalidOperationException (), CancellationToken.None));
		Assert.That (transport.Dumps, Is.EqualTo (1));
		Assert.That (transport.Screenshots, Is.Zero);
		Assert.That (File.Exists (Path.Combine (_directory, "failure", "observation.json")), Is.False);
		}

	private sealed class ObservedCaptureTransport : IAndroidCommandTransport
		{
		private readonly CaptureTransport _inner = new ();
		public int Dumps { get; private set; }
		public int Screenshots { get; private set; }
		public DateTimeOffset ScreenshotStarted { get; private set; }
		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			if (arguments.Contains ("uiautomator")) Dumps++;
			if (arguments.Contains ("screencap")) { Screenshots++; ScreenshotStarted = DateTimeOffset.UtcNow; }
			return _inner.ExecuteAsync (arguments, cancellationToken);
			}
		}

	private sealed class CaptureTransport (string? hierarchy = null) : IAndroidCommandTransport
		{
		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (arguments.Contains ("input")) throw new AssertionException ("Read-only evidence tests must not send input.");
			if (arguments.Contains ("screencap")) return Task.FromResult (Convert.FromBase64String ("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1cAAAAASUVORK5CYII="));
			var text = arguments.Contains ("uiautomator") ?
				(hierarchy ?? "<hierarchy><node package=\"example.app\" text=\"Example Home\" enabled=\"true\" bounds=\"[0,0][100,100]\"/><node password=\"true\" text=\"private-sentinel\" content-desc=\"private-sentinel\"/></hierarchy>") + "UI hierchary dumped to: /proc/self/fd/1" : "";
			return Task.FromResult (Encoding.UTF8.GetBytes (text));
			}
		}
	}
