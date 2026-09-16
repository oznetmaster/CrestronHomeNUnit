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
		_lease.Release ();
		Directory.Delete (_directory, recursive: true);
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
	public void WrongLocalAddressFailsButStillRestoresHome ()
		{
		_transport.LocalAddress = "192.0.2.99";
		Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("wrong-address", 50001));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.True);
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-address.local-endpoint", "observation.json")), Is.False);
		Assert.That (File.Exists (Path.Combine (_directory, "wrong-address.home-restored", "observation.json")), Is.True);
		}

	[Test]
	public void UncertainTapIsNotReplayedAndObservedPageIsRestored ()
		{
		_transport.ThrowAfterInput = 4;
		Assert.ThrowsAsync<IOException> (() => _navigation.VerifySavedEndpointAsync ("uncertain", 50001));
		Assert.That (_transport.Inputs.Count (page => page == "options"), Is.EqualTo (1));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void UnknownDialogIsNotDismissedAndRestorationRemainsUnconfirmed ()
		{
		_transport.UnknownAfterInput = 4;
		Assert.ThrowsAsync<TimeoutException> (() => _navigation.VerifySavedEndpointAsync ("unknown", 50001));
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
	public void FailedBackIsNotRepeatedDuringRestoration ()
		{
		_transport.Page = "menu";
		_transport.IgnoreBack = true;
		Assert.ThrowsAsync<TimeoutException> (() => _navigation.RestoreHomeAsync ());
		Assert.That (_transport.BackInputs, Is.EqualTo (1));
		Assert.That (_navigation.HomeRestored, Is.False);
		Assert.ThrowsAsync<TimeoutException> (() => _navigation.RestoreHomeAsync ());
		Assert.That (_transport.BackInputs, Is.EqualTo (1), "A subsequent cleanup attempt must not replay the pending Back command.");
		}

	[Test]
	public void UncertainInputThatHasNotChangedPageCannotClaimRestoration ()
		{
		_transport.ThrowBeforeInput = 1;
		Assert.ThrowsAsync<TimeoutException> (() => _navigation.VerifySavedEndpointAsync ("still-pending", 50001));
		Assert.That (_transport.Inputs, Has.Count.EqualTo (1));
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.False, "The old Home can still be visible while an input is pending.");
		Assert.ThrowsAsync<TimeoutException> (() => _navigation.RestoreHomeAsync ());
		Assert.That (_transport.Inputs, Has.Count.EqualTo (1));
		}

	[Test]
	public void AmbiguousMenuIsRejectedBeforeInputAndHomeCanStillBeRestored ()
		{
		_transport.DuplicateMenu = true;
		Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("ambiguous", 50001));
		Assert.That (_transport.Inputs, Has.Count.EqualTo (3), "Only Home menu, My Systems and the restoration Home card may be tapped.");
		Assert.That (_transport.Page, Is.EqualTo ("home"));
		Assert.That (_navigation.HomeRestored, Is.True);
		}

	[Test]
	public void DeadCoordinatorPreventsAnyNavigation ()
		{
		_navigation = new (new (_context with { CoordinatorStartUtcTicks = 1 }, new (_transport, _context.Profile.Application)));
		Assert.ThrowsAsync<IOException> (() => _navigation.VerifySavedEndpointAsync ("lost-owner", 50001));
		Assert.That (_transport.Inputs, Is.Empty);
		}

	[Test]
	public void CompletedSessionCannotNavigateEvenWhileReservationStillExists ()
		{
		var session = new AndroidWorkflowSession (_context, new (_transport, _context.Profile.Application));
		session.Complete (true);
		_navigation = new (session);
		Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.VerifySavedEndpointAsync ("completed", 50001));
		Assert.That (_transport.Inputs, Is.Empty);
		}

	[TestCase (0)]
	[TestCase (65536)]
	public void ProfileRejectsInvalidLocalPortBeforeConnecting (int port) =>
		Assert.Throws<ArgumentException> (() => (_context.Profile with { LocalPort = port }).Validate ());

	private sealed class NavigationTransport : IAndroidCommandTransport
		{
		public string Page = "home";
		public string LocalAddress = "192.0.2.1";
		public int ThrowAfterInput;
		public int ThrowBeforeInput;
		public int UnknownAfterInput;
		public bool IgnoreBack;
		public int BackInputs;
		public bool DuplicateMenu;
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
		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (arguments[0] == "exec-out") return Task.FromResult (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
			if (arguments[1] == "uiautomator") return Task.FromResult (Encoding.UTF8.GetBytes ("UI hierarchy dumped to: " + arguments[3]));
			if (arguments[1] == "rm") return Task.FromResult (Array.Empty<byte> ());
			if (arguments[1] == "cat")
				{
				IEnumerable<XElement> nodes = Page switch
					{
					"home" => [Node ("home_wholeHouse_name", "Example Home"), Node ("home_wholeHouse_topbarMenuButton")],
					"menu" => [Node ("home_wholeHouse_name", "Example Home"), Node ("fragmentPulleyContainer"), Node ("menu", description: "home_wholeHouse_popoverButtonMySystemsLabel")],
					"systems" => [Node ("homeswitcher_title"), Node ("card", description: "Example Home", left: 200), Node ("homeview_more")],
					"options" => [Node ("bottomSheet_infoBar", "Example Home"), Node ("action", "Edit")],
					"details" => [Node ("mobileclaimhome_title"), Field ("mobileclaimhome_friendlyNameOrLocation", "Example Home"), Field ("mobileclaimhome_localIpAddressOrHostName", LocalAddress), Field ("mobileclaimhome_localPort", "50001"), Field ("mobileclaimhome_remoteIpAddressOrHostName", "192.0.2.1"), Node ("mobileclaimhome_back")],
					_ => [Node ("unknown")]
					};
				if (Page == "systems" && DuplicateMenu) nodes = nodes.Append (Node ("homeview_more"));
				return Task.FromResult (Encoding.UTF8.GetBytes (new XElement ("hierarchy", nodes).ToString (SaveOptions.DisableFormatting)));
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
			else
				{
				Page = Page switch { "home" => "menu", "menu" => "systems", "systems" => arguments[3] == "250" ? "home" : "options", "options" => "details", "details" => "systems", _ => throw new InvalidOperationException ("Unexpected tap") };
				}
			if (UnknownAfterInput == Inputs.Count) Page = "unknown";
			if (ThrowAfterInput == Inputs.Count) throw new IOException ("Input outcome unknown.");
			return Task.FromResult (Array.Empty<byte> ());
			}
		}
	}