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
		}

	[Test]
	public async Task CapturedEvidenceUsesActualRunIdentityAndMasksPasswordHierarchy ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		var session = new AndroidWorkflowSession (context, new (new CaptureTransport (), "example.app"));
		await session.CaptureAsync ("home", hierarchy => Assert.That (hierarchy.RequireUnique (new (AndroidSelectorKind.Text, "Example Home")).Enabled, Is.True));
		using var record = JsonDocument.Parse (File.ReadAllText (Path.Combine (_directory, "home", "observation.json")));
		Assert.That (record.RootElement.GetProperty ("RunId").GetString (), Is.EqualTo (_owner));
		Assert.That (record.RootElement.GetProperty ("PackageSha256").GetString (), Is.EqualTo (context.PackageSha256));
		Assert.That (File.ReadAllText (Path.Combine (_directory, "home", "hierarchy.xml")), Does.Not.Contain ("private-sentinel"));
		Assert.ThrowsAsync<IOException> (() => session.CaptureAsync ("home", _ => { }));
		session.Complete (restorationConfirmed: true);
		var completion = AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json"));
		Assert.That (completion.RestorationConfirmed, Is.True);
		Assert.That (completion.PackageSha256, Is.EqualTo (context.PackageSha256));
		Assert.ThrowsAsync<InvalidOperationException> (() => session.CaptureAsync ("later", _ => { }));
		}

	[Test]
	public async Task SessionCanOnlyOpenOnceEvenAfterReportingRestoration ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		var device = new AndroidDevice (new CaptureTransport (), "example.app");
		var session = await AndroidWorkflowSession.OpenAsync (context, device, CancellationToken.None);
		session.Complete (restorationConfirmed: true);
		Assert.ThrowsAsync<IOException> (() => AndroidWorkflowSession.OpenAsync (context, device, CancellationToken.None));
		}

	[Test]
	public void FailedReadOnlyReadinessReportsRestorationButExposesNoSession ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		context = context with { Profile = context.Profile with { ExpectedHomeText = "Wrong Home" } };
		Assert.CatchAsync (() => AndroidWorkflowSession.OpenAsync (context, new (new CaptureTransport (), "example.app"), CancellationToken.None));
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.True);
		Assert.That (Directory.GetFiles (_directory, "observation.json", SearchOption.AllDirectories), Is.Empty);
		Assert.That (File.Exists (_lock), Is.True);
		}

	[Test]
	public void CrestronSessionRejectsHomeTextBehindAnOpenDriverPanel ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var context = Context ();
		context = context with { Profile = context.Profile with { Application = "com.crestron.phoenix.app" } };
		var xml = "<hierarchy><node package=\"com.crestron.phoenix.app\" resource-id=\"com.crestron.phoenix.app:id/home_wholeHouse_name\" text=\"Example Home\" enabled=\"true\" bounds=\"[0,0][100,100]\"/><node package=\"com.crestron.phoenix.app\" resource-id=\"com.crestron.phoenix.app:id/customdevices_toolbarClose\" enabled=\"true\" bounds=\"[0,0][20,20]\"/></hierarchy>";
		Assert.ThrowsAsync<InvalidOperationException> (() => AndroidWorkflowSession.OpenAsync (context, new (new CaptureTransport (xml), context.Profile.Application), CancellationToken.None));
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.True);
		}

	[Test]
	public void FailedPageAssertionKeepsCaptureButCannotCreatePassingObservation ()
		{
		using var lease = AndroidSessionLease.Acquire (_lock, _owner);
		var session = new AndroidWorkflowSession (Context (), new (new CaptureTransport (), "example.app"));
		Assert.ThrowsAsync<InvalidOperationException> (() => session.CaptureAsync ("wrong-page", _ => throw new InvalidOperationException ()));
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-page", "screen.png")), Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-page", "observation.json")), Is.False);
		session.Complete (restorationConfirmed: false);
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.False);
		}

	private sealed class CaptureTransport (string? hierarchy = null) : IAndroidCommandTransport
		{
		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (arguments.Contains ("input")) throw new AssertionException ("Read-only evidence tests must not send input.");
			if (arguments.Contains ("screencap")) return Task.FromResult (Convert.FromBase64String ("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ1cAAAAASUVORK5CYII="));
			var text = arguments.Contains ("uiautomator") ? "UI hierarchy dumped to: fixture" : arguments.Contains ("cat") ?
				(hierarchy ?? "<hierarchy><node package=\"example.app\" text=\"Example Home\" enabled=\"true\" bounds=\"[0,0][100,100]\"/><node password=\"true\" text=\"private-sentinel\" content-desc=\"private-sentinel\"/></hierarchy>") : "";
			return Task.FromResult (Encoding.UTF8.GetBytes (text));
			}
		}
	}