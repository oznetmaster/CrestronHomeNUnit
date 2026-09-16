// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeNUnit.Android;

/// <summary>Read-only connection inspection with explicit, observed Home restoration.</summary>
public sealed class CrestronHomeNavigation
	{
	private readonly AndroidWorkflowSession _session;
	private readonly TimeSpan _readinessTimeout;
	private Action<AndroidHierarchy>? _pendingInputPage;
	private string? _extensionTitle;
	private string? _roomName;
	private bool _scrolledDetails;
	public bool HomeRestored
		{
		get; private set;
		}

	public CrestronHomeNavigation (AndroidWorkflowSession session) : this (session, TimeSpan.FromSeconds (25)) { }

	internal CrestronHomeNavigation (AndroidWorkflowSession session, TimeSpan readinessTimeout)
		{
		if (session.Context.Profile.Application != "com.crestron.phoenix.app")
			throw new ArgumentException ("This navigation sequence requires the Crestron Home Android application.", nameof (session));
		if (readinessTimeout <= TimeSpan.Zero || readinessTimeout > TimeSpan.FromMinutes (1))
			throw new ArgumentOutOfRangeException (nameof (readinessTimeout));
		_session = session;
		_readinessTimeout = readinessTimeout;
		}

	private static AndroidSelector Id (string id) => CrestronHomePages.Resource (id);
	private AndroidSelector HomeCard => new (AndroidSelectorKind.ContentDescription, _session.Context.Profile.ExpectedHomeText);
	private static AndroidSelector MySystems => new (AndroidSelectorKind.ContentDescription, "home_wholeHouse_popoverButtonMySystemsLabel");
	private void Home (AndroidHierarchy hierarchy) => CrestronHomePages.RequireHome (hierarchy, _session.Context.Profile.ExpectedHomeText);
	private void Extension (AndroidHierarchy hierarchy) => CrestronHomePages.RequireExtensionPage (hierarchy,
		_extensionTitle ?? throw new InvalidOperationException ("No extension page was selected by this navigation session."));
	private static void Rooms (AndroidHierarchy hierarchy) => CrestronHomePages.RequireRooms (hierarchy);
	private void Room (AndroidHierarchy hierarchy) => CrestronHomePages.RequireRoom (hierarchy,
		_roomName ?? throw new InvalidOperationException ("No room was selected by this navigation session."));
	private void Menu (AndroidHierarchy hierarchy)
		{
		if (hierarchy.RequireUnique (Id ("home_wholeHouse_name")).Text != _session.Context.Profile.ExpectedHomeText)
			throw new InvalidOperationException ("The Home menu belongs to a different system.");
		_ = hierarchy.RequireUnique (MySystems);
		}
	private void Systems (AndroidHierarchy hierarchy)
		{
		_ = hierarchy.RequireUnique (Id ("homeswitcher_title"));
		_ = hierarchy.RequireUnique (HomeCard);
		hierarchy.RequireAbsent (Id ("bottomSheet_infoBar"));
		hierarchy.RequireAbsent (Id ("mobileclaimhome_title"));
		}
	private void Options (AndroidHierarchy hierarchy)
		{
		if (hierarchy.RequireUnique (Id ("bottomSheet_infoBar")).Text != _session.Context.Profile.ExpectedHomeText)
			throw new InvalidOperationException ("The system menu belongs to a different Home.");
		}
	private void Details (AndroidHierarchy hierarchy)
		{
		if (_scrolledDetails)
			{
			// This editor was bound to the expected Home before our single scroll.
			// Its title/name may now be outside the viewport. Only cancel/close is permitted here.
			_ = hierarchy.RequireUnique (Id ("mobileclaimhome_scrollView"));
			_ = hierarchy.RequireUnique (Id ("mobileclaimhome_content"));
			_ = hierarchy.RequireUnique (Id ("mobileclaimhome_back"));
			return;
			}
		_ = hierarchy.RequireUnique (Id ("mobileclaimhome_title"));
		var name = hierarchy.RequireUnique (Id ("commonui_animatedEditText_editText") with
			{
			AncestorResourceId = CrestronHomePages.ResourcePrefix + "mobileclaimhome_friendlyNameOrLocation"
			});
		if (name.Text != _session.Context.Profile.ExpectedHomeText)
			throw new InvalidOperationException ("The connection editor belongs to a different Home.");
		}

	private async Task WaitAsync (Action<AndroidHierarchy> verify, CancellationToken token)
		{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource (token);
		timeout.CancelAfter (_readinessTimeout);
		try
			{
			while (true)
				{
				// Ownership failures must not be treated as a transient page transition.
				_session.VerifyActive ();
				try
					{
					verify (await _session.Device.CaptureAsync (timeout.Token).ConfigureAwait (false));
					return;
					}
				catch (Exception exception) when (exception is IOException or InvalidOperationException)
					{
					await Task.Delay (TimeSpan.FromMilliseconds (250), timeout.Token).ConfigureAwait (false);
					}
				}
			}
		catch (OperationCanceledException) when (!token.IsCancellationRequested)
			{
			throw new TimeoutException ("The expected Android page did not become ready; no input was retried.");
			}
		}

	private async Task TapAsync (AndroidSelector selector, Action<AndroidHierarchy> guard, CancellationToken token)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		_session.VerifyActive ();
		HomeRestored = false;
		await _session.Device.TapAsync (selector, guard, () => _pendingInputPage = guard, token).ConfigureAwait (false);
		}

	private async Task ConfirmDepartureAsync (CancellationToken token)
		{
		if (_pendingInputPage is not Action<AndroidHierarchy> previous)
			return;
		await WaitAsync (hierarchy =>
			{
				try
					{
					previous (hierarchy);
					}
				catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { return; }
				throw new InvalidOperationException ("A navigation command is still pending on the previous page.");
			}, token).ConfigureAwait (false);
		_pendingInputPage = null;
		}

	/// <summary>Inspect saved local settings without editing them. This is not active-route or installed-instance proof.</summary>
	public async Task VerifySavedEndpointAsync (string checkId, int localPort, CancellationToken token = default)
		{
		if (localPort is < 1 or > 65535)
			throw new ArgumentOutOfRangeException (nameof (localPort));
		await WaitAsync (Home, token).ConfigureAwait (false);
		HomeRestored = true;
		try
			{
			await _session.CaptureAsync (checkId + ".home-before", Home, token).ConfigureAwait (false);
			await TapAsync (Id ("home_wholeHouse_topbarMenuButton"), Home, token).ConfigureAwait (false);
			await WaitAsync (Menu, token).ConfigureAwait (false);
			await TapAsync (MySystems, Menu, token).ConfigureAwait (false);
			await WaitAsync (Systems, token).ConfigureAwait (false);
			// A repeated visible menu button is ambiguous; RequireUnique rejects it.
			await TapAsync (Id ("homeview_more"), Systems, token).ConfigureAwait (false);
			await WaitAsync (Options, token).ConfigureAwait (false);
			await TapAsync (new (AndroidSelectorKind.Text, "Edit"), Options, token).ConfigureAwait (false);
			await WaitAsync (Details, token).ConfigureAwait (false);
			var current = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
			Details (current);
			CrestronHomePages.RequireSavedLocalAddress (current, _session.Context.Profile.ExpectedHomeText, _session.Context.ProcessorAddress);
			bool portOutsideView = false;
			try
				{
				current.RequireAbsent (Id ("mobileclaimhome_localPort"));
				portOutsideView = true;
				}
			catch (InvalidOperationException) { }
			if (portOutsideView)
				{
				await _session.CaptureAsync (checkId + ".local-address", h => CrestronHomePages.RequireSavedLocalAddress
					(h, _session.Context.Profile.ExpectedHomeText, _session.Context.ProcessorAddress), token).ConfigureAwait (false);
				await ConfirmDepartureAsync (token).ConfigureAwait (false);
				_session.VerifyActive ();
				await _session.Device.ScrollDownAsync (Id ("mobileclaimhome_scrollView"), Details, () => _scrolledDetails = true, token).ConfigureAwait (false);
				await WaitAsync (h => { Details (h); CrestronHomePages.RequireSavedLocalPort (h, localPort); }, token).ConfigureAwait (false);
				}
			await _session.CaptureAsync (checkId + ".local-endpoint", hierarchy =>
				{
					Details (hierarchy);
					if (!portOutsideView)
						CrestronHomePages.RequireSavedLocalAddress (hierarchy, _session.Context.Profile.ExpectedHomeText, _session.Context.ProcessorAddress);
					CrestronHomePages.RequireSavedLocalPort (hierarchy, localPort);
				}, token).ConfigureAwait (false);
			}
		finally
			{
			// A canceled test still gets bounded navigation-only cleanup while its coordinator/leases remain valid.
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
			await RestoreHomeAsync (cleanup.Token).ConfigureAwait (false);
			await _session.CaptureAsync (checkId + ".home-restored", Home, cleanup.Token).ConfigureAwait (false);
			}
		}

	/// <summary>Navigate to one uniquely named Home tile, inspect its page, then restore Home. Sends no device commands.</summary>
	public async Task InspectHomeExtensionAsync (string checkId, string tileName, string pageTitle, Action<AndroidHierarchy> verify, CancellationToken token = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (tileName);
		ArgumentException.ThrowIfNullOrWhiteSpace (pageTitle);
		ArgumentNullException.ThrowIfNull (verify);
		await WaitAsync (Home, token).ConfigureAwait (false);
		_extensionTitle = pageTitle;
		var tile = new AndroidSelector (AndroidSelectorKind.Text, tileName) { AncestorResourceId = CrestronHomePages.ResourcePrefix + "fragmentHomeContainer" };
		void HomeTile (AndroidHierarchy hierarchy)
			{
			Home (hierarchy);
			if (hierarchy.RequireUnique (tile).ResourceId != CrestronHomePages.ResourcePrefix + "titleSubtitle_title")
				throw new InvalidOperationException ("The matching Home text is not a tile title.");
			}
		try
			{
			await TapAsync (tile, HomeTile, token).ConfigureAwait (false);
			await WaitAsync (Extension, token).ConfigureAwait (false);
			await ConfirmDepartureAsync (token).ConfigureAwait (false);
			await _session.CaptureAsync (checkId + ".controls", hierarchy => { Extension (hierarchy); verify (hierarchy); }, token).ConfigureAwait (false);
			}
		finally
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
			await RestoreHomeAsync (cleanup.Token).ConfigureAwait (false);
			await _session.CaptureAsync (checkId + ".home-restored", Home, cleanup.Token).ConfigureAwait (false);
			}
		}

	/// <summary>Inspect one named room extension and restore Home, including after assertion failures. Sends no device control commands.</summary>
	public Task InspectRoomExtensionAsync (string checkId, string roomName, string tileName, string pageTitle, Action<AndroidHierarchy> verify, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (verify);
		return InspectRoomCoreAsync (checkId, roomName, tileName, pageTitle,
			() => _session.CaptureAsync (checkId + ".controls", hierarchy => { Extension (hierarchy); verify (hierarchy); }, token), token);
		}

	/// <summary>Visit explicitly configured nested navigation pages, then restore the root page and Home.</summary>
	public Task InspectRoomExtensionPagesAsync (string checkId, string roomName, string tileName, string pageTitle,
		Func<CrestronHomeExtensionNavigation, CancellationToken, Task> inspect, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (inspect);
		return InspectRoomCoreAsync (checkId, roomName, tileName, pageTitle, async () =>
			{
				var pages = new CrestronHomeExtensionNavigation (_session, pageTitle);
				Exception? failure = null;
				try
					{
					await inspect (pages, token).ConfigureAwait (false);
					}
				catch (Exception e) { failure = e; throw; }
				finally
					{
					using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
					try
						{
						await pages.RestoreRootAsync (cleanup.Token).ConfigureAwait (false);
						}
					catch (Exception cleanupError) when (failure != null)
						{
						throw new AggregateException ("Extension inspection and restoration both failed.", failure, cleanupError);
						}
					}
			}, token);
		}

	private async Task InspectRoomCoreAsync (string checkId, string roomName, string tileName, string pageTitle, Func<Task> inspect, CancellationToken token)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (roomName);
		ArgumentException.ThrowIfNullOrWhiteSpace (tileName);
		ArgumentException.ThrowIfNullOrWhiteSpace (pageTitle);
		await WaitAsync (Home, token).ConfigureAwait (false);
		HomeRestored = true;
		_roomName = roomName;
		_extensionTitle = pageTitle;
		var room = new AndroidSelector (AndroidSelectorKind.Text, roomName);
		void RoomChoice (AndroidHierarchy hierarchy)
			{
			Rooms (hierarchy);
			if (hierarchy.RequireUnique (room).ResourceId != CrestronHomePages.ResourcePrefix + "itemRoomTitle")
				throw new InvalidOperationException ("The matching text is not a room title.");
			}
		Exception? failure = null;
		try
			{
			await TapBottomTabAsync (true, Home, token).ConfigureAwait (false);
			await WaitAsync (Rooms, token).ConfigureAwait (false);
			await TapAsync (room, RoomChoice, token).ConfigureAwait (false);
			await WaitAsync (Room, token).ConfigureAwait (false);
			var tile = new AndroidSelector (AndroidSelectorKind.ContentDescription, "room_service_" + tileName);
			await RevealRoomTileAsync (tile, token).ConfigureAwait (false);
			await TapAsync (tile, hierarchy =>
				{
					Room (hierarchy);
					if (!CrestronHomePages.RoomTileVisible (hierarchy, hierarchy.RequireUnique (tile)))
						throw new InvalidOperationException ("The room tile moved outside the visible area; no input was sent.");
				}, token).ConfigureAwait (false);
			await WaitAsync (Extension, token).ConfigureAwait (false);
			await ConfirmDepartureAsync (token).ConfigureAwait (false);
			await inspect ().ConfigureAwait (false);
			}
		catch (Exception e) { failure = e; throw; }
		finally
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
			try
				{
				await RestoreHomeAsync (cleanup.Token).ConfigureAwait (false);
				await _session.CaptureAsync (checkId + ".home-restored", Home, cleanup.Token).ConfigureAwait (false);
				}
			catch (Exception cleanupError) when (failure != null)
				{
				throw new AggregateException ("Room inspection and Home restoration both failed.", failure, cleanupError);
				}
			}
		}

	private async Task RevealRoomTileAsync (AndroidSelector tile, CancellationToken token)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		var seen = new HashSet<string> (StringComparer.Ordinal);
		for (int viewport = 0; viewport <= 12; viewport++)
			{
			_session.VerifyActive ();
			var hierarchy = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
			Room (hierarchy);
			var matches = hierarchy.Find (tile);
			if (matches.Length > 1 || matches.Length == 1 && !matches[0].Enabled)
				throw new InvalidOperationException ("The room tile is ambiguous or disabled; no input was sent.");
			if (matches.Length == 1 && CrestronHomePages.RoomTileVisible (hierarchy, matches[0]))
				return;
			_ = CrestronHomePages.RoomViewport (hierarchy);
			string signature = CrestronHomePages.RoomViewportSignature (hierarchy);
			if (viewport == 12 || !seen.Add (signature))
				throw new InvalidOperationException ("The requested room tile was not found within the bounded observed scroll.");
			await _session.Device.ScrollDownAsync (CrestronHomePages.RoomViewport, current =>
				{
					Room (current);
					if (CrestronHomePages.RoomViewportSignature (current) != signature)
						throw new InvalidOperationException ("The room viewport changed before scrolling; no input was sent.");
				}, static () => { }, token).ConfigureAwait (false);
			// Each gesture requests the next observed viewport. A failed gesture is never replayed.
			// Allow the animation to settle using reads only, never another swipe.
			for (int read = 0; read < 3; read++)
				{
				_session.VerifyActive ();
				var after = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
				Room (after);
				if (CrestronHomePages.RoomViewportSignature (after) != signature)
					break;
				if (read < 2)
					await Task.Delay (100, token).ConfigureAwait (false);
				}
			}
		}

	private async Task TapBottomTabAsync (bool rooms, Action<AndroidHierarchy> guard, CancellationToken token)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		_session.VerifyActive ();
		HomeRestored = false;
		await _session.Device.TapAsync (hierarchy => CrestronHomePages.BottomTab (hierarchy, rooms), guard, () => _pendingInputPage = guard, token).ConfigureAwait (false);
		}

	/// <summary>Restore only recognized navigation pages of the expected Home; never dismiss an unknown screen.</summary>
	public async Task RestoreHomeAsync (CancellationToken token = default)
		{
		HomeRestored = false;
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		for (int step = 0; step < 4; step++)
			{
			int page = -1;
			var guards = new Action<AndroidHierarchy>[] { Home, Details, Options, Systems, Menu, Extension, Room, Rooms };
			await WaitAsync (hierarchy =>
				{
					for (int index = 0; index < guards.Length; index++)
						{
						try
							{
							guards[index] (hierarchy);
							page = index;
							return;
							}
						catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { }
						}
					throw new InvalidOperationException ("The current screen is not a recognized navigation page of the expected Home.");
				}, token).ConfigureAwait (false);
			if (page == 0)
				{
				HomeRestored = true;
				_extensionTitle = null;
				_roomName = null;
				_scrolledDetails = false;
				return;
				}
			if (page == 1)
				await TapAsync (Id ("mobileclaimhome_back"), Details, token).ConfigureAwait (false);
			else if (page == 3)
				await TapAsync (HomeCard, Systems, token).ConfigureAwait (false);
			else if (page == 5)
				await TapAsync (Id ("customdevices_toolbarClose"), Extension, token).ConfigureAwait (false);
			else if (page == 6)
				await TapAsync (Id ("room_back"), Room, token).ConfigureAwait (false);
			else if (page == 7)
				await TapBottomTabAsync (false, Rooms, token).ConfigureAwait (false);
			else
				{
				_session.VerifyActive ();
				await _session.Device.BackAsync (guards[page], () => _pendingInputPage = guards[page], token).ConfigureAwait (false);
				}
			// Observe departure before considering another input. A slow/uncertain input is never replayed.
			await ConfirmDepartureAsync (token).ConfigureAwait (false);
			}
		throw new InvalidOperationException ("Home restoration did not complete within the permitted navigation sequence.");
		}
	}