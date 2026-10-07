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

	// Observation tooling budget: hierarchy reads have their own bounded transport.
    // This overall page deadline includes read retries and validation; it is not
    // a driver response/recovery criterion. Caller deadlines still take precedence.
    public CrestronHomeNavigation (AndroidWorkflowSession session) : this (session, TimeSpan.FromSeconds (90)) { }

	internal CrestronHomeNavigation (AndroidWorkflowSession session, TimeSpan readinessTimeout)
		{
		if (session.Context.Profile.Application != "com.crestron.phoenix.app")
			throw new ArgumentException ("This navigation sequence requires the Crestron Home Android application.", nameof (session));
		if (readinessTimeout <= TimeSpan.Zero || readinessTimeout > TimeSpan.FromMinutes (2))
			throw new ArgumentOutOfRangeException (nameof (readinessTimeout));
		_session = session;
		_readinessTimeout = readinessTimeout;
		}

	internal async Task RestoreAndCaptureHomeAsync (string checkId, TimeSpan? phaseBudget = null, CancellationToken cancellationToken = default)
        {
        var budget = phaseBudget ?? TimeSpan.FromMinutes (2);
        if (budget <= TimeSpan.Zero || budget > TimeSpan.FromMinutes (2))
            throw new ArgumentOutOfRangeException (nameof (phaseBudget));
        // Each recognized navigation step and final proof receive a bounded tooling
        // budget. Earlier page transitions must not consume a later screen read's
        // allowance. The caller deadline and four-step limit remain authoritative.
        await RestoreHomeAsync (budget, cancellationToken).ConfigureAwait (false);
        using var evidence = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
        evidence.CancelAfter (budget);
        await _session.CaptureAsync (checkId + ".home-restored", Home, evidence.Token).ConfigureAwait (false);
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
			// This editor was bound to the expected Home before our bounded scrolling.
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
		AndroidHierarchy? lastPage = null;
		Exception? lastRejection = null;
		var elapsed = System.Diagnostics.Stopwatch.StartNew ();
		int capturesStarted = 0, capturesCompleted = 0;
		try
			{
			while (true)
				{
				// Ownership failures must not be treated as a transient page transition.
				_session.VerifyActive ();
				try
					{
					capturesStarted++;
					lastPage = await _session.Device.CaptureAsync (timeout.Token).ConfigureAwait (false);
					capturesCompleted++;
					verify (lastPage);
					return;
					}
				catch (Exception exception) when (exception is IOException or InvalidOperationException)
					{
					lastRejection = exception;
					await Task.Delay (TimeSpan.FromMilliseconds (250), timeout.Token).ConfigureAwait (false);
					}
				}
			}
		catch (OperationCanceledException cancellation) when (!token.IsCancellationRequested)
			{
			var failure = new TimeoutException ("The expected Android page did not become ready; no input was retried.", lastRejection ?? cancellation);
            if (lastPage is not null)
                {
                try
                    {
                    string path = Path.Combine (_session.Context.EvidenceDirectory, "navigation-timeout-" + Guid.NewGuid ().ToString ("N") + ".xml");
                    await File.WriteAllTextAsync (path, lastPage.MaskedXml, CancellationToken.None).ConfigureAwait (false);
                    failure.Data["LastObservedPage"] = path;
                    }
                catch (Exception evidenceError) when (evidenceError is IOException or UnauthorizedAccessException)
                    { failure.Data["EvidenceWriteFailure"] = evidenceError.GetType ().Name; }
                }
            try
                {
                string diagnostic = Path.Combine (_session.Context.EvidenceDirectory, "navigation-timeout-" + Guid.NewGuid ().ToString ("N") + ".json");
                await File.WriteAllTextAsync (diagnostic, System.Text.Json.JsonSerializer.Serialize (new
                    {
                    SchemaVersion = 1,
                    ElapsedMilliseconds = elapsed.ElapsedMilliseconds,
                    ReadinessBudgetMilliseconds = _readinessTimeout.TotalMilliseconds,
                    CapturesStarted = capturesStarted,
                    CapturesCompleted = capturesCompleted,
                    LastGuardRejection = lastRejection?.GetType ().Name,
                    CaptureAttemptsJson = cancellation.Data[AndroidDevice.CAPTURE_ATTEMPTS_KEY] as string,
                    LastObservedPage = failure.Data["LastObservedPage"] as string
                    }), CancellationToken.None).ConfigureAwait (false);
                failure.Data["NavigationDiagnostic"] = diagnostic;
                }
            catch (Exception evidenceError) when (evidenceError is IOException or UnauthorizedAccessException)
                { failure.Data["EvidenceWriteFailure"] = evidenceError.GetType ().Name; }
            throw failure;
			}
		}

	private void RetainNavigationInput (string operation, AndroidHierarchy hierarchy, AndroidElement target)
		{
		_session.VerifyActive ();
		string directory = Path.Combine (_session.Context.EvidenceDirectory, "navigation-inputs");
		Directory.CreateDirectory (directory);
		string stem = Path.Combine (directory, Guid.NewGuid ().ToString ("N"));
		File.WriteAllText (stem + ".xml", hierarchy.MaskedXml);
		using var receipt = new FileStream (stem + ".json", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		System.Text.Json.JsonSerializer.Serialize (receipt, new { Operation = operation, Target = target, Utc = DateTimeOffset.UtcNow, State = "BeforeInput", RunId = _session.Context.RunId });
		receipt.Flush (flushToDisk: true);
		}

	private async Task TapAsync (AndroidSelector selector, Action<AndroidHierarchy> guard, CancellationToken token)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		_session.VerifyActive ();
		HomeRestored = false;
		AndroidHierarchy? observed = null;
		await _session.Device.TapAsync (selector, hierarchy => { guard (hierarchy); observed = hierarchy; },
            () => { RetainNavigationInput ("tap", observed!, observed!.RequireUnique (selector)); _pendingInputPage = guard; }, token).ConfigureAwait (false);
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

	// Called only by opted-in session opening, while the workflow owns the emulator lease.
	internal async Task SelectExpectedHomeAsync (Action beforeNavigation, CancellationToken token)
		{
		_session.VerifyActive ();
		var initial = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
		string startingHome = initial.RequireUnique (Id ("home_wholeHouse_name")).Text;
		CrestronHomePages.RequireHome (initial, startingHome);
		if (startingHome != _session.Context.Profile.ExpectedHomeText &&
			!_session.Context.Profile.AllowedStartingHomes.Contains (startingHome, StringComparer.Ordinal))
			throw new InvalidOperationException ("The active Home is not an approved starting Home; no input was sent.");
		void StartingHome (AndroidHierarchy hierarchy) => CrestronHomePages.RequireHome (hierarchy, startingHome);
		void StartingMenu (AndroidHierarchy hierarchy)
			{
			if (hierarchy.RequireUnique (Id ("home_wholeHouse_name")).Text != startingHome)
				throw new InvalidOperationException ("The Home changed before opening My Systems.");
			_ = hierarchy.RequireUnique (MySystems);
			}
		await _session.CaptureAsync ("session-selection.before", StartingHome, token).ConfigureAwait (false);
		if (startingHome != _session.Context.Profile.ExpectedHomeText)
			{
			beforeNavigation ();
			await TapAsync (Id ("home_wholeHouse_topbarMenuButton"), StartingHome, token).ConfigureAwait (false);
			await WaitAsync (StartingMenu, token).ConfigureAwait (false);
			await TapAsync (MySystems, StartingMenu, token).ConfigureAwait (false);
			await WaitAsync (Systems, token).ConfigureAwait (false);
			await _session.CaptureAsync ("session-selection.systems", Systems, token).ConfigureAwait (false);
			await TapAsync (HomeCard, Systems, token).ConfigureAwait (false);
			await WaitAsync (Home, token).ConfigureAwait (false);
			await ConfirmDepartureAsync (token).ConfigureAwait (false);
			}
		// A matching label is insufficient: inspect the selected saved endpoint before exposing the session.
		beforeNavigation ();
		await VerifySavedEndpointAsync ("session-selection.endpoint", _session.Context.Profile.LocalPort, token).ConfigureAwait (false);
		}

	/// <summary>Inspect saved local settings without editing them. This is not active-route or installed-instance proof.</summary>
	public async Task VerifySavedEndpointAsync (string checkId, int localPort, CancellationToken token = default)
		{
		if (localPort is < 1 or > 65535)
			throw new ArgumentOutOfRangeException (nameof (localPort));
		await WaitAsync (Home, token).ConfigureAwait (false);
		HomeRestored = true;
		Exception? primaryFailure = null;
		try
			{
			await _session.CaptureAsync (checkId + ".home-before", Home, token).ConfigureAwait (false);
			await TapAsync (Id ("home_wholeHouse_topbarMenuButton"), Home, token).ConfigureAwait (false);
			await WaitAsync (Menu, token).ConfigureAwait (false);
			await TapAsync (MySystems, Menu, token).ConfigureAwait (false);
			await WaitAsync (Systems, token).ConfigureAwait (false);
			await ConfirmDepartureAsync (token).ConfigureAwait (false);
			_session.VerifyActive ();
			HomeRestored = false;
			await _session.Device.TapAsync (hierarchy => CrestronHomePages.HomeMenu (hierarchy, _session.Context.Profile.ExpectedHomeText),
				Systems, () => _pendingInputPage = Systems, token).ConfigureAwait (false);
			await WaitAsync (Options, token).ConfigureAwait (false);
			await TapAsync (new (AndroidSelectorKind.Text, "Edit"), Options, token).ConfigureAwait (false);
			await WaitAsync (Details, token).ConfigureAwait (false);
			var current = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
			Details (current);
			CrestronHomePages.RequireSavedLocalAddress (current, _session.Context.Profile.ExpectedHomeText, _session.Context.ProcessorAddress);
			bool HasPort (AndroidHierarchy hierarchy)
				{
				var fields = hierarchy.Find (Id ("commonui_animatedEditText_editText") with
					{
					AncestorResourceId = CrestronHomePages.ResourcePrefix + "mobileclaimhome_localPort"
					});
				if (fields.Length > 1) throw new InvalidOperationException ("The saved local port field is ambiguous.");
				return fields.Length == 1;
				}
			bool portOutsideView = !HasPort (current);
			if (portOutsideView)
				{
				await _session.CaptureAsync (checkId + ".local-address", h => CrestronHomePages.RequireSavedLocalAddress
					(h, _session.Context.Profile.ExpectedHomeText, _session.Context.ProcessorAddress), token).ConfigureAwait (false);
				await ConfirmDepartureAsync (token).ConfigureAwait (false);
                int stationaryGestures = 0;
                for (int gesture = 0; gesture < 8 && !HasPort (current); gesture++)
                    {
                    AndroidHierarchy? inputPage = null;
                    AndroidElement? lane = null;
                    _session.VerifyActive ();
                    await _session.Device.ScrollDownAsync (h => lane = CrestronHomePages.SavedEndpointScrollLane (h),
                        h => { _session.VerifyActive (); Details (h); inputPage = h; },
                        () => { RetainNavigationInput ("saved-endpoint-scroll-down", inputPage!, lane!); _scrolledDetails = true; }, token).ConfigureAwait (false);
                    string previous = inputPage!.MaskedXml;
                    await _session.CaptureAsync (checkId + ".local-scroll-" + gesture, h => { Details (h); current = h; }, token).ConfigureAwait (false);
                    if (!HasPort (current) && current.MaskedXml == previous)
                        {
                        // A successful transport can still deliver an ignored scroll.
                        // Settle using two fresh observations before another bounded,
                        // navigation-only swipe in the revalidated inert form gutter.
                        // Transport uncertainty, cancellation or a changed page aborts;
                        // editable fields, device controls and failed taps are never replayed.
                        for (int read = 0; read < 2 && !HasPort (current) && current.MaskedXml == previous; read++)
                            {
                            await Task.Delay (250, token).ConfigureAwait (false);
                            current = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
                            _session.VerifyActive (); Details (current);
                            }
                        if (HasPort (current) || current.MaskedXml != previous)
                            await _session.CaptureAsync (checkId + ".local-settled-" + gesture, h => { Details (h); current = h; }, token).ConfigureAwait (false);
                        }
                    if (HasPort (current)) CrestronHomePages.RequireSavedLocalPort (current, localPort);
                    else if (current.MaskedXml == previous)
                        {
                        if (++stationaryGestures >= 3)
                            throw new InvalidOperationException ("The saved endpoint editor remained unchanged after three verified navigation-only scrolls.");
                        }
                    else stationaryGestures = 0;
                    }
				if (!HasPort (current)) throw new InvalidOperationException ("The saved local port was not observed within the bounded scroll limit.");
				}

			await _session.CaptureAsync (checkId + ".local-endpoint", hierarchy =>
				{
					Details (hierarchy);
					if (!portOutsideView)
						CrestronHomePages.RequireSavedLocalAddress (hierarchy, _session.Context.Profile.ExpectedHomeText, _session.Context.ProcessorAddress);
					CrestronHomePages.RequireSavedLocalPort (hierarchy, localPort);
				}, token).ConfigureAwait (false);
			}
		catch (Exception error) { primaryFailure = error; throw; }
		finally
			{
			// A canceled test still gets bounded navigation-only cleanup while its coordinator/leases remain valid.
			try
                {
                await RestoreAndCaptureHomeAsync (checkId).ConfigureAwait (false);
                }
            catch (Exception cleanupFailure) when (primaryFailure is not null)
                { throw new AggregateException ("Saved endpoint verification failed and Home restoration also failed.", primaryFailure, cleanupFailure); }
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
			await RestoreAndCaptureHomeAsync (checkId).ConfigureAwait (false);
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
        => InspectRoomPagesCoreAsync(checkId, roomName, tileName, pageTitle, inspect, token, false);

    /// <summary>Visit native details and explicitly configured nested pages through the named details button.</summary>
    public Task InspectRoomDetailsPagesAsync (string checkId, string roomName, string tileName, string pageTitle,
        Func<CrestronHomeExtensionNavigation, CancellationToken, Task> inspect, CancellationToken token = default)
        => InspectRoomPagesCoreAsync(checkId, roomName, tileName, pageTitle, inspect, token, true);

    private Task InspectRoomPagesCoreAsync (string checkId, string roomName, string tileName, string pageTitle,
        Func<CrestronHomeExtensionNavigation, CancellationToken, Task> inspect, CancellationToken token, bool detailsButton)
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
			}, token, detailsButton);
		}

	/// <summary>Inspect native room details through the named tile's details button, never its power-control surface.</summary>
	public Task InspectRoomDetailsAsync (string checkId, string roomName, string tileName, string pageTitle,
		Action<AndroidHierarchy> verify, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (verify);
		return InspectRoomCoreAsync (checkId, roomName, tileName, pageTitle,
			() => _session.CaptureAsync (checkId + ".controls", hierarchy => { Extension (hierarchy); verify (hierarchy); }, token), token, true);
		}

	/// <summary>Open the named room from the verified Home using the shared observed navigation path.
	/// The caller must close any native controls and call RestoreHomeAndCaptureAsync in its cleanup.</summary>
	public async Task OpenRoomAsync (string roomName, CancellationToken token = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (roomName);
		await WaitAsync (Home, token).ConfigureAwait (false);
		HomeRestored = true;
		_roomName = roomName;
		var room = new AndroidSelector (AndroidSelectorKind.Text, roomName);
		void RoomChoice (AndroidHierarchy hierarchy)
			{
			Rooms (hierarchy);
			if (!CrestronHomePages.RoomChoiceVisible (hierarchy, hierarchy.RequireUnique (room)))
				throw new InvalidOperationException ("The room title is outside the unobstructed room list; no input was sent.");
			}
			await TapBottomTabAsync (true, Home, token).ConfigureAwait (false);
			await WaitAsync (Rooms, token).ConfigureAwait (false);
			await RevealRoomChoiceAsync (room, token).ConfigureAwait (false);
			await TapAsync (room, RoomChoice, token).ConfigureAwait (false);
			await WaitAsync (Room, token).ConfigureAwait (false);
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		}

	/// <summary>Restore the observed navigation path and retain independent Home proof.</summary>
	public Task RestoreHomeAndCaptureAsync (string checkId, CancellationToken token = default) => RestoreAndCaptureHomeAsync (checkId, cancellationToken: token);

	private async Task InspectRoomCoreAsync (string checkId, string roomName, string tileName, string pageTitle, Func<Task> inspect, CancellationToken token, bool detailsButton = false)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (roomName);
		ArgumentException.ThrowIfNullOrWhiteSpace (tileName);
		ArgumentException.ThrowIfNullOrWhiteSpace (pageTitle);
		_extensionTitle = pageTitle;
		Exception? failure = null;
		try
			{
			await OpenRoomAsync (roomName, token).ConfigureAwait (false);
			var tile = new AndroidSelector (AndroidSelectorKind.ContentDescription, "room_service_" + tileName);
			await RevealRoomTileAsync (tile, token).ConfigureAwait (false);
			var details = Id ("serviceDots") with { SiblingText = tileName };
			await TapAsync (detailsButton ? details : tile, hierarchy =>
				{
					Room (hierarchy);
					if (!CrestronHomePages.RoomTileVisible (hierarchy, hierarchy.RequireUnique (tile)))
						throw new InvalidOperationException ("The room tile moved outside the visible area; no input was sent.");
					if (detailsButton)
						{
						if (!CrestronHomePages.RoomTileVisible (hierarchy, hierarchy.RequireUnique (details)))
							throw new InvalidOperationException ("The details button is not visible; no input was sent.");
						}
					else
						hierarchy.RequireAbsent (details); // Native tile surfaces can switch equipment; callers must select details explicitly.
				}, token).ConfigureAwait (false);
			await WaitAsync (Extension, token).ConfigureAwait (false);
			await ConfirmDepartureAsync (token).ConfigureAwait (false);
			await inspect ().ConfigureAwait (false);
			}
		catch (Exception e) { failure = e; throw; }
		finally
			{
			try
				{
				await RestoreAndCaptureHomeAsync (checkId).ConfigureAwait (false);
				}
			catch (Exception cleanupError) when (failure != null)
				{
				throw new AggregateException ("Room inspection and Home restoration both failed.", failure, cleanupError);
				}
			}
		}

	private async Task RevealRoomChoiceAsync (AndroidSelector room, CancellationToken token)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		// The app preserves the room list's scroll position. Search both directions,
		// bounding gestures and observing every viewport rather than assuming the list starts at the top.
		foreach (bool down in new[] { true, false })
			{
			var seen = new HashSet<string> (StringComparer.Ordinal);
			for (int viewport = 0; viewport <= 12; viewport++)
				{
				_session.VerifyActive ();
				var hierarchy = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
				Rooms (hierarchy);
				var matches = hierarchy.Find (room);
				if (matches.Length > 1 || matches.Length == 1 && (!matches[0].Enabled || matches[0].ResourceId != CrestronHomePages.ResourcePrefix + "itemRoomTitle"))
					throw new InvalidOperationException ("The room name is ambiguous, disabled or not a room title; no input was sent.");
				if (matches.Length == 1 && CrestronHomePages.RoomChoiceVisible (hierarchy, matches[0]))
					return;
				_ = CrestronHomePages.RoomsViewport (hierarchy);
				string signature = CrestronHomePages.RoomsViewportSignature (hierarchy);
				if (viewport == 12 || !seen.Add (signature))
					break;
				AndroidHierarchy? observed = null;
				void Guard (AndroidHierarchy current)
					{
					Rooms (current);
					observed = current;
					if (CrestronHomePages.RoomsViewportSignature (current) != signature)
						throw new InvalidOperationException ("The room list changed before scrolling; no input was sent.");
					}
				if (down)
					await _session.Device.ScrollDownAsync (CrestronHomePages.RoomsViewport, Guard, () => RetainNavigationInput (down ? "rooms-scroll-down" : "rooms-scroll-up", observed!, CrestronHomePages.RoomsViewport (observed!)), token).ConfigureAwait (false);
				else
					await _session.Device.ScrollUpAsync (CrestronHomePages.RoomsViewport, Guard, () => RetainNavigationInput (down ? "rooms-scroll-down" : "rooms-scroll-up", observed!, CrestronHomePages.RoomsViewport (observed!)), token).ConfigureAwait (false);
				for (int read = 0; read < 3; read++)
					{
					_session.VerifyActive ();
					var after = await _session.Device.CaptureAsync (token).ConfigureAwait (false);
					Rooms (after);
					if (CrestronHomePages.RoomsViewportSignature (after) != signature)
						break;
					if (read < 2)
						await Task.Delay (100, token).ConfigureAwait (false);
					}
				}
			}
		throw new InvalidOperationException ("The requested room was not visible within the bounded room-list search.");
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
		AndroidHierarchy? observed = null;
		await _session.Device.TapAsync (hierarchy => CrestronHomePages.BottomTab (hierarchy, rooms),
            hierarchy => { guard (hierarchy); observed = hierarchy; },
            () => { RetainNavigationInput (rooms ? "rooms-tab" : "home-tab", observed!, CrestronHomePages.BottomTab (observed!, rooms)); _pendingInputPage = guard; }, token).ConfigureAwait (false);
		}

	/// <summary>Restore only recognized navigation pages of the expected Home; never dismiss an unknown screen.</summary>
	public Task RestoreHomeAsync (CancellationToken token = default) => RestoreHomeAsync (null, token);

    private async Task RestoreHomeAsync (TimeSpan? stepBudget, CancellationToken cancellationToken)
		{
		HomeRestored = false;
		for (int step = 0; step < 4; step++)
			{
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
            if (stepBudget is { } budget) deadline.CancelAfter (budget);
            var token = deadline.Token;
            await ConfirmDepartureAsync (token).ConfigureAwait (false);
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
