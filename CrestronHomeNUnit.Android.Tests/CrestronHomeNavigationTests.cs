// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed class CrestronHomeNavigationTests
	{
	private string _directory = null!;
	private AndroidSessionLease _lease = null!;
	private AndroidRunContext _context = null!;
	private NavigationTransport _transport = null!;
	private CrestronHomeNavigation _navigation = null!;

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		var owner = Guid.NewGuid ().ToString ("N");
		var path = Path.Combine (_directory, "worker.lease");
		_lease = AndroidSessionLease.Acquire (path, owner);
		using var process = Process.GetCurrentProcess ();
		_context = new (1, owner, Environment.MachineName, process.Id, process.StartTime.ToUniversalTime ().Ticks,
			"192.0.2.1", 7, Guid.NewGuid ().ToString (), "1.0.0.1", new ('A', 64), new ('B', 64),
			new (Environment.ProcessPath!, "fake-serial", "com.crestron.phoenix.app", "Example Home", path), _directory);
		_transport = new ();
		_navigation = new (new (_context, new (_transport, _context.Profile.Application)), TimeSpan.FromMilliseconds (60));
		}

	[TearDown]
	public void TearDown ()
		{
		try { _lease.Release (); }
		finally { _lease.Dispose (); }
		Directory.Delete (_directory, recursive: true);
		}

	[Test]
	public async Task MatchingTextOutsideATileCannotBeTapped ()
		{
		_transport.NonTileText = true;
		await Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.InspectHomeExtensionAsync ("not-a-tile", "Example Driver", "Example Options", _ => { }));
		Assert.That (_transport.Inputs, Is.Empty);
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task ExtensionInspectionIsReadOnlyAndRepeatable ()
		{
		for (int run = 0; run < 2; run++)
			await _navigation.InspectHomeExtensionAsync ($"extension-{run}", "Example Driver", "Example Options", h => CrestronHomePages.RequireExtensionPage (h, "Example Options"));
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "home", "extension", "home", "extension" }));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task FailedControlAssertionStillClosesTheSelectedExtension ()
		{
		await Assert.ThrowsAsync<InvalidDataException> (() => _navigation.InspectHomeExtensionAsync ("extension-failure", "Example Driver", "Example Options", _ => throw new InvalidDataException ("Unexpected status")));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "home", "extension" }));
		Assert.That (File.Exists (Path.Combine (_directory, "extension-failure.controls", "observation.json")), Is.False);
		}

	[Test]
	public async Task UncertainExtensionOpenIsNotReplayed ()
		{
		_transport.ThrowAfterInput = 1;
		await Assert.ThrowsAsync<IOException> (() => _navigation.InspectHomeExtensionAsync ("extension-uncertain", "Example Driver", "Example Options", _ => { }));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "home", "extension" }));
		}

	[Test]
	public async Task ReadsOnlyLocalEndpointAndRestoresHomeTwice ()
		{
		await _navigation.VerifySavedEndpointAsync ("first", 50001);
		await _navigation.VerifySavedEndpointAsync ("second", 50001);
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (_transport.Inputs, Has.Count.EqualTo (12));
		Assert.That (Directory.GetFiles (_directory, "observation.json", SearchOption.AllDirectories), Has.Length.EqualTo (6));
		}

	[Test]
	public async Task WrongLocalAddressFailsButStillRestoresHome ()
		{
		_transport.LocalAddress = "192.0.2.99";
		await Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("wrong-address", 50001));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-address.local-endpoint", "observation.json")), Is.False);
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-address.home-restored", "observation.json")), Is.True);
		}

	[Test]
	public async Task PortraitEditorScrollsOnceAndRetainsAddressAndPortEvidence ()
		{
		_transport.PortBelowFold = true;
		await _navigation.VerifySavedEndpointAsync ("portrait", 50001);
		Assert.That (_transport.Swipes, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "portrait.local-address", "observation.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "portrait.local-endpoint", "observation.json")), Is.True);
		}

	[TestCase (2)]
	[TestCase (3)]
	public async Task SmallerConnectionEditorRevealsPortThroughObservedViewports (int needed)
		{
		_transport.PortBelowFold = true;
		_transport.PortAppearsAfter = needed;
		await _navigation.VerifySavedEndpointAsync ("small", 50001);
		Assert.That (_transport.Swipes, Is.EqualTo (needed));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "small.local-scroll-" + (needed - 1), "observation.json")), Is.True);
		}

	[Test]
	public async Task ConnectionEditorThatDoesNotMoveStopsAfterThreeVerifiedScrolls ()
		{
		_transport.PortBelowFold = true;
		_transport.NoScrollProgress = true;
		await Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("stationary", 50001));
		Assert.That (_transport.Swipes, Is.EqualTo (3));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task DelayedScrollObservationWaitsWithoutRepeatingInput ()
		{
		_transport.PortBelowFold = true;
		_transport.StaleReadsAfterSwipe = 1;
		await _navigation.VerifySavedEndpointAsync ("delayed-scroll", 50001);
		Assert.That (_transport.Swipes, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "delayed-scroll.local-settled-0", "observation.json")), Is.True);
		}

	[Test]
	public async Task ReadOnlySessionOpeningDoesNotClaimEndpointInspection ()
		{
		var session = await AndroidWorkflowSession.OpenAsync (_context, new (_transport, _context.Profile.Application), CancellationToken.None);
		Assert.That (session.SavedEndpointVerifiedOnOpen, Is.False);
		Assert.That (_transport.Inputs, Is.Empty);
		session.Complete (true);
		}

	[Test]
	public async Task UnreachableSavedPortHasABoundedGestureCountAndRestoresHome ()
		{
		_transport.PortBelowFold = true;
		_transport.PortAppearsAfter = 20;
		await Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("bounded", 50001));
		Assert.That (_transport.Swipes, Is.EqualTo (8));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task UncertainScrollIsNotRepeatedAndEditorIsCancelled ()
		{
		_transport.PortBelowFold = true;
		_transport.ThrowAfterInput = 5;
		await Assert.ThrowsAsync<IOException> (() => _navigation.VerifySavedEndpointAsync ("uncertain-scroll", 50001));
		Assert.That (_transport.Swipes, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task UncertainTapIsNotReplayedAndObservedPageIsRestored ()
		{
		_transport.ThrowAfterInput = 4;
		await Assert.ThrowsAsync<IOException> (() => _navigation.VerifySavedEndpointAsync ("uncertain", 50001));
		Assert.That (_transport.Inputs.Count (page => page == "options"), Is.EqualTo (1));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task UnknownDialogIsNotDismissedAndRestorationRemainsUnconfirmed ()
		{
		_transport.UnknownAfterInput = 4;
		var error = await Assert.ThrowsAsync<AggregateException> (() => _navigation.VerifySavedEndpointAsync ("unknown", 50001));
		Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
		Assert.That(error.InnerExceptions.All(e => e is TimeoutException), Is.True);
		Assert.That (_transport.Inputs, Has.Count.EqualTo (4));
		Assert.That (_navigation.HomeRestored, Is.False);
		}

	[TestCase ("menu")]
	[TestCase ("options")]
	public async Task RecognizedMenuCanBeDismissedWithoutEditingSettings (string page)
		{
		_transport.Page = page;
		await _navigation.RestoreHomeAsync ();
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_transport.BackInputs, Is.EqualTo (1));
		}

	[Test]
	public async Task FailedBackIsNotRepeatedDuringRestoration ()
		{
		_transport.Page = "menu";
		_transport.IgnoreBack = true;
		await Assert.ThrowsAsync<TimeoutException> (() => _navigation.RestoreHomeAsync ());
		Assert.That (_transport.BackInputs, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.False);
		await Assert.ThrowsAsync<TimeoutException> (() => _navigation.RestoreHomeAsync ());
		Assert.That (_transport.BackInputs, Is.EqualTo (1), "A subsequent cleanup attempt must not replay the pending Back command.");
		}

	[Test]
	public async Task UncertainInputThatHasNotChangedPageCannotClaimRestoration ()
		{
		_transport.ThrowBeforeInput = 1;
		var error = await Assert.ThrowsAsync<AggregateException> (() => _navigation.VerifySavedEndpointAsync ("still-pending", 50001));
		Assert.That(error!.InnerExceptions[0], Is.TypeOf<IOException>());
		Assert.That(error.InnerExceptions[1], Is.TypeOf<TimeoutException>());
		Assert.That (_transport.Inputs, Has.Count.EqualTo (1));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.False, "The old Home can still be visible while an input is pending.");
		await Assert.ThrowsAsync<TimeoutException> (() => _navigation.RestoreHomeAsync ());
		Assert.That (_transport.Inputs, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task AmbiguousMenuIsRejectedBeforeInputAndHomeCanStillBeRestored ()
		{
		_transport.DuplicateMenu = true;
		await Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("ambiguous", 50001));
		Assert.That (_transport.Inputs, Has.Count.EqualTo (3), "Only Home menu, My Systems and the restoration Home card may be tapped.");
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public async Task DeadCoordinatorPreventsAnyNavigation ()
		{
		_navigation = new (new (_context with { CoordinatorStartUtcTicks = 1 }, new (_transport, _context.Profile.Application)));
		await Assert.ThrowsAsync<IOException> (() => _navigation.VerifySavedEndpointAsync ("lost-owner", 50001));
		Assert.That (_transport.Inputs, Is.Empty);
		}

	[Test]
	public async Task CompletedSessionCannotNavigateEvenWhileReservationStillExists ()
		{
		var session = new AndroidWorkflowSession (_context, new (_transport, _context.Profile.Application));
		session.Complete (true);
		_navigation = new (session);
		await Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("completed", 50001));
		Assert.That (_transport.Inputs, Is.Empty);
		}

	[TestCase (0)]
	[TestCase (65536)]
	public void ProfileRejectsInvalidLocalPortBeforeConnecting (int port) =>
		Assert.Throws<ArgumentException> (() => (_context.Profile with { LocalPort = port }).Validate ());

	[Test]
	public async Task OptedInSessionSelectsApprovedHomeAndVerifiesEndpointBeforeReturning ()
		{
		_transport.CurrentHome = "Other Home";
		var context = _context with { Profile = _context.Profile with { AllowedStartingHomes = ["Other Home"] } };
		var session = await AndroidWorkflowSession.OpenAsync (context, new (_transport, context.Profile.Application), CancellationToken.None);
		Assert.That (_transport.CurrentHome, Is.EqualTo ("Example Home"));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_transport.Inputs, Has.Count.EqualTo (9));
		Assert.That (session.SavedEndpointVerifiedOnOpen, Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "session-selection.endpoint.local-endpoint", "observation.json")), Is.True);
		session.Complete (true);
		}

	[Test]
	public async Task SelectionIsNotImplicitInAnOrdinaryProfile ()
		{
		_transport.CurrentHome = "Other Home";
		await Assert.ThrowsAsync<InvalidOperationException> (() => AndroidWorkflowSession.OpenAsync (_context, new (_transport, _context.Profile.Application), CancellationToken.None));
		Assert.That (_transport.Inputs, Is.Empty);
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.True);
		}

	[TestCase ("Unapproved Home", "home")]
	[TestCase ("Other Home", "extension")]
	public async Task UnapprovedOrObscuredStartingHomeGetsNoInput (string home, string page)
		{
		_transport.CurrentHome = home;
		_transport.Page = page;
		var context = _context with { Profile = _context.Profile with { AllowedStartingHomes = ["Other Home"] } };
		await Assert.ThrowsAsync<InvalidOperationException> (() => AndroidWorkflowSession.OpenAsync (context, new (_transport, context.Profile.Application), CancellationToken.None));
		Assert.That (_transport.Inputs, Is.Empty);
		}

	[Test]
	public async Task SelectedHomeWithWrongEndpointDoesNotExposeSession ()
		{
		_transport.CurrentHome = "Other Home";
		_transport.LocalAddress = "192.0.2.99";
		var context = _context with { Profile = _context.Profile with { AllowedStartingHomes = ["Other Home"] } };
		await Assert.ThrowsAsync<InvalidOperationException> (() => AndroidWorkflowSession.OpenAsync (context, new (_transport, context.Profile.Application), CancellationToken.None));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (File.Exists (Path.Combine (_directory, "session-selection.endpoint.local-endpoint", "observation.json")), Is.False);
		Assert.That (File.Exists (Path.Combine (_directory, "session-selection.endpoint.home-restored", "observation.json")), Is.True);
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.True);
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (3)]
	public async Task UncertainHomeSwitchIsNeverReplayedOrReportedRestored (int input)
		{
		_transport.CurrentHome = "Other Home";
		_transport.ThrowAfterInput = input;
		var context = _context with { Profile = _context.Profile with { AllowedStartingHomes = ["Other Home"] } };
		await Assert.ThrowsAsync<IOException> (() => AndroidWorkflowSession.OpenAsync (context, new (_transport, context.Profile.Application), CancellationToken.None));
		Assert.That (_transport.Inputs, Has.Count.EqualTo (input));
		Assert.That (AndroidWorkflowSession.Read<AndroidRunCompletion> (Path.Combine (_directory, "completion.json")).RestorationConfirmed, Is.False);
		}

	[Test]
	public async Task AlreadySelectedHomeStillRequiresEndpointVerificationWhenOptedIn ()
		{
		var context = _context with { Profile = _context.Profile with { AllowedStartingHomes = ["Other Home"] } };
		var session = await AndroidWorkflowSession.OpenAsync (context, new (_transport, context.Profile.Application), CancellationToken.None);
		Assert.That (_transport.Inputs, Has.Count.EqualTo (6));
		Assert.That (session.SavedEndpointVerifiedOnOpen, Is.True);
		session.Complete (true);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task MissingOrAmbiguousDestinationStopsAtSystemsWithoutSelectingAnotherHome (bool duplicate)
		{
		_transport.CurrentHome = "Other Home";
		_transport.MissingHomeCard = !duplicate;
		_transport.DuplicateHomeCard = duplicate;
		var context = _context with { Profile = _context.Profile with { AllowedStartingHomes = ["Other Home"] } };
		var navigation = new CrestronHomeNavigation (new (context, new (_transport, context.Profile.Application)), TimeSpan.FromMilliseconds (60));
		await Assert.ThrowsAsync<TimeoutException> (() => navigation.SelectExpectedHomeAsync (() => { }, CancellationToken.None));
		Assert.That (_transport.Page, Is.EqualTo ("systems"));
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "home", "menu" }));
		}


    [Test]
    public async Task EndpointMismatchAndRestorationFailureAreBothPreserved ()
        {
        _transport.LocalAddress = "192.0.2.99";
        _transport.UnknownAfterInput = 5;
        var error = await Assert.ThrowsAsync<AggregateException> (() => _navigation.VerifySavedEndpointAsync ("wrong-and-cleanup", 50001));
        Assert.That (error!.InnerExceptions, Has.Count.EqualTo (2));
        Assert.That (error.InnerExceptions[0], Is.TypeOf<InvalidOperationException> ());
        Assert.That (error.InnerExceptions[1], Is.TypeOf<TimeoutException> ());
        Assert.That (_transport.Inputs, Has.Count.EqualTo (5));
        Assert.That (_navigation.HomeRestored, Is.False);
        }

    [Test]
    public async Task FailedDepartureRetainsObservedPageAndGuardRejectionWithoutReplay ()
        {
        _transport.Page = "menu";
        _transport.IgnoreBack = true;
        var error = await Assert.ThrowsAsync<TimeoutException> (() => _navigation.RestoreHomeAsync ());
        Assert.That (error!.InnerException, Is.TypeOf<InvalidOperationException> ());
        Assert.That (error.InnerException!.Message, Does.Contain ("navigation command is still pending"));
        string path = (string) error.Data["LastObservedPage"]!;
        Assert.That (File.Exists (path), Is.True);
        Assert.That (File.ReadAllText (path), Does.Contain ("home_wholeHouse_popoverButtonMySystemsLabel"));
        Assert.That (_transport.BackInputs, Is.EqualTo (1));
        }


    [TestCase(1)][TestCase(2)]
    public async Task CompletedButIgnoredScrollCanAdvanceAfterFreshPageVerification(int ignored)
    {
        _transport.PortBelowFold=true;_transport.IgnoredScrolls=ignored;
        await _navigation.VerifySavedEndpointAsync("ignored",50001);
        Assert.That(_transport.Swipes,Is.EqualTo(ignored+1));
        Assert.That(_navigation.HomeRestored,Is.True);
        Assert.That(File.Exists(Path.Combine(_directory,"ignored.local-endpoint","observation.json")),Is.True);
        var intents=Directory.GetFiles(Path.Combine(_directory,"navigation-inputs"),"*.json")
            .Select(File.ReadAllText).Count(x=>x.Contains("saved-endpoint-scroll-down",StringComparison.Ordinal));
        Assert.That(intents,Is.EqualTo(ignored+1));
    }
    [Test] public async Task CancellationAfterAnIgnoredScrollDoesNotSendAnotherGesture()
    {
        using var cancel=new CancellationTokenSource();
        _transport.PortBelowFold=true;_transport.NoScrollProgress=true;_transport.AfterSwipe=cancel.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(()=>_navigation.VerifySavedEndpointAsync("cancel-scroll",50001,cancel.Token));
        Assert.That(_transport.Swipes,Is.EqualTo(1));Assert.That(_navigation.HomeRestored,Is.True);
    }
    [Test] public async Task ChangedPageAfterAnIgnoredScrollDoesNotSendAnotherGesture()
    {
        _transport.PortBelowFold=true;_transport.NoScrollProgress=true;_transport.UnknownAfterInput=5;
        await Assert.ThrowsAsync<AggregateException>(()=>_navigation.VerifySavedEndpointAsync("changed-scroll",50001));
        Assert.That(_transport.Swipes,Is.EqualTo(1));
    }

    [Test]
    public async Task MultipleRestorationPagesReceiveIndependentBudgets()
    {
        _transport.Page="details";
        var slow=new DelayedNavigationTransport(_transport);
        var navigation=new CrestronHomeNavigation(new(_context,new(slow,_context.Profile.Application)));
        var elapsed=Stopwatch.StartNew();
        await navigation.RestoreAndCaptureHomeAsync("multi-page",TimeSpan.FromMilliseconds(650));
        Assert.That(elapsed.Elapsed,Is.GreaterThan(TimeSpan.FromMilliseconds(650)));
        Assert.That(_transport.Inputs,Is.EqualTo(new[]{"details","systems"}));
        Assert.That(navigation.HomeRestored,Is.True);
        Assert.That(File.Exists(Path.Combine(_directory,"multi-page.home-restored","observation.json")),Is.True);
    }

    [Test]
    public async Task CallerCancellationAcrossRestorationPagesStopsFurtherInput()
    {
        _transport.Page="details";
        using var cancellation=new CancellationTokenSource();
        var slow=new DelayedNavigationTransport(_transport){AfterInput=cancellation.Cancel};
        var navigation=new CrestronHomeNavigation(new(_context,new(slow,_context.Profile.Application)));
        await Assert.CatchAsync<OperationCanceledException>(()=>navigation.RestoreAndCaptureHomeAsync("cancel-pages",TimeSpan.FromMilliseconds(650),cancellation.Token));
        Assert.That(_transport.Inputs,Is.EqualTo(new[]{"details"}));
        Assert.That(navigation.HomeRestored,Is.False);
        Assert.That(Directory.Exists(Path.Combine(_directory,"cancel-pages.home-restored")),Is.False);
    }

    private sealed class DelayedNavigationTransport(NavigationTransport inner):IAndroidCommandTransport
    {
        public Action? AfterInput;
        public async Task<byte[]> ExecuteAsync(IReadOnlyList<string> arguments,CancellationToken token)
        {
            if(arguments.Contains("uiautomator"))await Task.Delay(175,token);
            var result=await inner.ExecuteAsync(arguments,token);
            if(arguments.Contains("input"))AfterInput?.Invoke();
            return result;
        }
    }

	private sealed class NavigationTransport : IAndroidCommandTransport
		{
		public string Page = "home";
		public string CurrentHome = "Example Home";
		public string LocalAddress = "192.0.2.1";
		public int ThrowAfterInput;
		public int ThrowBeforeInput;
		public int UnknownAfterInput;
		public bool IgnoreBack;
		public int BackInputs;
		public bool DuplicateMenu;
		public bool MissingHomeCard;
		public bool DuplicateHomeCard;
		public bool NonTileText;
		public bool PortBelowFold;
		public int PortAppearsAfter = 1;
		public bool NoScrollProgress;
        public int IgnoredScrolls;
        public Action? AfterSwipe;
		public int Swipes;
		public int StaleReadsAfterSwipe;
		public List<string> Inputs { get; } = [];
		private static XElement Node (string id, string text = "", string description = "", int left = 0) => new ("node",
			new XAttribute ("package", "com.crestron.phoenix.app"), new XAttribute ("resource-id", CrestronHomePages.ResourcePrefix + id),
			new XAttribute ("text", text), new XAttribute ("content-desc", description), new XAttribute ("enabled", "true"), new XAttribute ("bounds", $"[{left},0][{left + 100},100]"));
		private static XElement Field (string id, string value)
			{
			var parent = Node (id);
			parent.Add (Node ("commonui_animatedEditText_editText", value));
			return parent;
			}
		private XElement HomeTree ()
			{
			var parent = Node ("fragmentHomeContainer");
			parent.Add (Node ("home_wholeHouse_name", CurrentHome), Node ("home_wholeHouse_topbarMenuButton"), Node (NonTileText ? "otherText" : "titleSubtitle_title", "Example Driver", left: 400));
			return parent;
			}
		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (arguments[0] == "exec-out" && arguments[1] == "screencap") return Task.FromResult (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
			if (arguments[1] == "rm") return Task.FromResult (Array.Empty<byte> ());
			if (arguments[1] == "uiautomator")
				{
				IEnumerable<XElement> nodes = Page switch
					{
					"home" => [HomeTree ()],
					"extension" => [Node ("customdevices_toolbarTitle", "Example Options"), Node ("customdevices_toolbarClose")],
					"menu" => [Node ("home_wholeHouse_name", CurrentHome), Node ("fragmentPulleyContainer"), Node ("menu", description: "home_wholeHouse_popoverButtonMySystemsLabel")],
					"systems" => [Node ("homeswitcher_title"), Node ("card", description: "Example Home", left: 200), Node ("homeview_more")],
					"options" => [Node ("bottomSheet_infoBar", "Example Home"), Node ("action", "Edit")],
					"details" => [Node ("mobileclaimhome_title"), Field ("mobileclaimhome_friendlyNameOrLocation", "Example Home"), Field ("mobileclaimhome_localIpAddressOrHostName", LocalAddress), Field ("mobileclaimhome_localPort", "50001"), Field ("mobileclaimhome_remoteIpAddressOrHostName", "192.0.2.1"), Node ("mobileclaimhome_back")],
					_ => [Node ("unknown")]
					};
				if (Page == "systems" && DuplicateMenu) nodes = nodes.Append (Node ("homeview_more"));
				if (Page == "systems" && MissingHomeCard) nodes = nodes.Where (node => (string?)node.Attribute ("content-desc") != "Example Home");
				if (Page == "systems" && DuplicateHomeCard) nodes = nodes.Append (Node ("card", description: "Example Home", left: 350));
				if (Page == "details" && PortBelowFold)
					{
					bool stale = Swipes > 0 && StaleReadsAfterSwipe-- > 0;
					int position = NoScrollProgress || stale ? 0 : Math.Max(0, Swipes-IgnoredScrolls);
					if (position < PortAppearsAfter) nodes = nodes.Where (node => (string?)node.Attribute ("resource-id") != CrestronHomePages.ResourcePrefix + "mobileclaimhome_localPort");
					if (position > 0) nodes = nodes.Where (node => (string?)node.Attribute ("resource-id") != CrestronHomePages.ResourcePrefix + "mobileclaimhome_friendlyNameOrLocation");
					nodes = nodes.Append (Node ("observed-viewport", position.ToString ()));
					var content = Node ("mobileclaimhome_content");
					content.SetAttributeValue ("bounds", "[20,0][80,100]");
					foreach (var field in nodes.SelectMany (n => n.DescendantsAndSelf ()).Where (n => (string?)n.Attribute ("resource-id") == CrestronHomePages.ResourcePrefix + "commonui_animatedEditText_editText"))
                        field.SetAttributeValue ("bounds", "[40,0][60,100]");
                    content.Add (nodes);
                    nodes = new[] { Node ("mobileclaimhome_scrollView"), content };
					}
				return Task.FromResult (Encoding.UTF8.GetBytes (new XElement ("hierarchy", nodes).ToString (SaveOptions.DisableFormatting) + "UI hierchary dumped to: /proc/self/fd/1"));
				}
			if (arguments[1] != "input") throw new InvalidOperationException ("Unexpected test transport command.");
			Inputs.Add (Page);
			if (ThrowBeforeInput == Inputs.Count) throw new IOException ("Input outcome unknown before page transition.");
			if (arguments[2] == "keyevent")
				{
				if (arguments[3] != "KEYCODE_BACK") throw new InvalidOperationException ("Only Back is permitted.");
				BackInputs++;
				if (!IgnoreBack)
					{
					Page = Page == "menu" ? "home" : "systems";
					}
				}
			else if (arguments[2] == "swipe")
				{
				if (Page != "details") throw new InvalidOperationException ("Unexpected scroll outside editor.");
				Assert.That (arguments[3], Is.EqualTo ("30"), "The swipe must stay inside the form padding and outside editable fields.");
				Swipes++;
                AfterSwipe?.Invoke();
				}
			else
				{
				if (Page == "systems" && arguments[3] == "250") CurrentHome = "Example Home";
				Page = Page switch { "home" => arguments[3] == "450" ? "extension" : "menu", "extension" => "home", "menu" => "systems", "systems" => arguments[3] == "250" ? "home" : "options", "options" => "details", "details" => "systems", _ => throw new InvalidOperationException ("Unexpected tap") };
				}
			if (UnknownAfterInput == Inputs.Count) Page = "unknown";
			if (ThrowAfterInput == Inputs.Count) throw new IOException ("Input outcome unknown.");
			return Task.FromResult (Array.Empty<byte> ());
			}
		}
	}
