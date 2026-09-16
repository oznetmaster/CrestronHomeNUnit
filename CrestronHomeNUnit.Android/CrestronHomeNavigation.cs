// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeNUnit.Android;

/// <summary>Read-only connection inspection with explicit, observed Home restoration.</summary>
public sealed class CrestronHomeNavigation
	{
	private readonly AndroidWorkflowSession _session;
	private readonly TimeSpan _readinessTimeout;
	private Action<AndroidHierarchy>? _pendingInputPage;
	public bool HomeRestored { get; private set; }

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
		_ = hierarchy.RequireUnique (Id ("mobileclaimhome_title"));
		var name = hierarchy.RequireUnique (Id ("commonui_animatedEditText_editText") with
			{ AncestorResourceId = CrestronHomePages.ResourcePrefix + "mobileclaimhome_friendlyNameOrLocation" });
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
		if (_pendingInputPage is not Action<AndroidHierarchy> previous) return;
		await WaitAsync (hierarchy =>
			{
			try { previous (hierarchy); }
			catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { return; }
			throw new InvalidOperationException ("A navigation command is still pending on the previous page.");
			}, token).ConfigureAwait (false);
		_pendingInputPage = null;
		}

	/// <summary>Inspect saved local settings without editing them. This is not active-route or installed-instance proof.</summary>
	public async Task VerifySavedEndpointAsync (string checkId, int localPort, CancellationToken token = default)
		{
		if (localPort is < 1 or > 65535) throw new ArgumentOutOfRangeException (nameof (localPort));
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
			await _session.CaptureAsync (checkId + ".local-endpoint", hierarchy => CrestronHomePages.RequireSavedLocalEndpoint
				(hierarchy, _session.Context.Profile.ExpectedHomeText, _session.Context.ProcessorAddress, localPort), token).ConfigureAwait (false);
			}
		finally
			{
			// A canceled test still gets bounded navigation-only cleanup while its coordinator/leases remain valid.
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
			await RestoreHomeAsync (cleanup.Token).ConfigureAwait (false);
			await _session.CaptureAsync (checkId + ".home-restored", Home, cleanup.Token).ConfigureAwait (false);
			}
		}

	/// <summary>Restore only recognized navigation pages of the expected Home; never dismiss an unknown screen.</summary>
	public async Task RestoreHomeAsync (CancellationToken token = default)
		{
		HomeRestored = false;
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		for (int step = 0; step < 4; step++)
			{
			int page = -1;
			var guards = new Action<AndroidHierarchy>[] { Home, Details, Options, Systems, Menu };
			await WaitAsync (hierarchy =>
				{
				for (int index = 0; index < guards.Length; index++)
					{
					try { guards[index] (hierarchy); page = index; return; }
					catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { }
					}
				throw new InvalidOperationException ("The current screen is not a recognized navigation page of the expected Home.");
				}, token).ConfigureAwait (false);
			if (page == 0) { HomeRestored = true; return; }
			if (page == 1)
				await TapAsync (Id ("mobileclaimhome_back"), Details, token).ConfigureAwait (false);
			else if (page == 3)
				await TapAsync (HomeCard, Systems, token).ConfigureAwait (false);
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