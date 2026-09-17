// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeNUnit.Android;

/// <summary>Inspect configured navigation pages and selection lists without selecting an option.</summary>
public sealed class CrestronHomeExtensionNavigation
	{
	private readonly AndroidWorkflowSession _session;
	private readonly List<(string Title, AndroidSelector? Close)> _pages;
	private readonly TimeSpan _timeout;
	private Action<AndroidHierarchy>? _pending;
	private bool _selectionOpen;

	internal CrestronHomeExtensionNavigation (AndroidWorkflowSession session, string title, TimeSpan? timeout = null)
		{
		_session = session;
		_pages = [(title, null)];
		_timeout = timeout ?? TimeSpan.FromSeconds (25);
		}

	private string[] Titles => _pages.Select (page => page.Title).ToArray ();
	private AndroidHierarchy Page (AndroidHierarchy hierarchy) => CrestronHomeExtensionPages.RequirePage (hierarchy, Titles);
	private AndroidHierarchy Selection (AndroidHierarchy hierarchy) => CrestronHomeExtensionPages.RequireSelection (hierarchy, Titles);
	private Action<AndroidHierarchy> PageGuard ()
		{
		var titles = Titles;
		return hierarchy => CrestronHomeExtensionPages.RequirePage (hierarchy, titles);
		}
	private Action<AndroidHierarchy> SelectionGuard ()
		{
		var titles = Titles;
		return hierarchy => CrestronHomeExtensionPages.RequireSelection (hierarchy, titles);
		}

	private async Task WaitAsync (Action<AndroidHierarchy> verify, CancellationToken token)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (_timeout);
		try
			{
			while (true)
				{
				_session.VerifyActive ();
				try
					{
					verify (await _session.Device.CaptureAsync (deadline.Token).ConfigureAwait (false));
					return;
					}
				catch (Exception e) when (e is IOException or InvalidOperationException)
					{
					await Task.Delay (250, deadline.Token).ConfigureAwait (false);
					}
				}
			}
		catch (OperationCanceledException) when (!token.IsCancellationRequested)
			{
			throw new TimeoutException ("The expected extension page did not appear. No input was retried.");
			}
		}

	private async Task ConfirmDepartureAsync (CancellationToken token)
		{
		if (_pending is not Action<AndroidHierarchy> previous)
			return;
		await WaitAsync (hierarchy =>
			{
			try { previous (hierarchy); }
			catch (Exception e) when (e is InvalidOperationException or InvalidDataException) { return; }
			throw new InvalidOperationException ("The previous navigation input is still pending.");
			}, token).ConfigureAwait (false);
		_pending = null;
		}

	/// <summary>Use only controls known to navigate. The caller must explicitly supply a non-saving close/cancel control.</summary>
	public async Task OpenPageAsync (AndroidSelector open, string title, AndroidSelector close, CancellationToken token = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (title);
		ArgumentNullException.ThrowIfNull (open);
		ArgumentNullException.ThrowIfNull (close);
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		if (_selectionOpen || _pages.Count >= 8)
			throw new InvalidOperationException ("Close the selection list or reduce navigation depth before opening another page.");
		var titles = Titles;
		var guard = PageGuard ();
		_session.VerifyActive ();
		await _session.Device.TapAsync (h => CrestronHomeExtensionPages.RequirePage (h, titles).RequireUnique (open), guard,
			() => { _pending = guard; _pages.Add ((title, close)); }, token).ConfigureAwait (false);
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		await WaitAsync (PageGuard (), token).ConfigureAwait (false);
		}

	public async Task InspectAsync (string checkId, Action<AndroidHierarchy> verify, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (verify);
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		await _session.CaptureAsync (checkId, hierarchy => verify (Page (hierarchy)), token).ConfigureAwait (false);
		}

	/// <summary>Send one downward viewport gesture on the verified front page. The caller verifies newly visible content separately.</summary>
	public Task ScrollDownAsync (Action<AndroidHierarchy> validatePage, CancellationToken token = default)
		=> ScrollPageAsync (true, validatePage, token);

	/// <summary>Send one upward viewport gesture on the verified front page. This does not assert the original scroll position was restored.</summary>
	public Task ScrollUpAsync (Action<AndroidHierarchy> validatePage, CancellationToken token = default)
		=> ScrollPageAsync (false, validatePage, token);

	private async Task ScrollPageAsync (bool down, Action<AndroidHierarchy> validatePage, CancellationToken token)
		{
		ArgumentNullException.ThrowIfNull (validatePage);
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		if (_selectionOpen)
			throw new InvalidOperationException ("Close the selection list before scrolling the extension page.");
		var titles = Titles;
		void Guard (AndroidHierarchy hierarchy)
			{
			_session.VerifyActive ();
			validatePage (CrestronHomeExtensionPages.RequirePage (hierarchy, titles));
			}
		AndroidElement Select (AndroidHierarchy hierarchy) => CrestronHomeExtensionPages.RequirePage (hierarchy, titles)
			.RequireUnique (CrestronHomePages.Resource ("customdevices_componentRecyclerView"));
		_session.VerifyActive ();
		if (down)
			await _session.Device.ScrollDownAsync (Select, Guard, _session.VerifyActive, token).ConfigureAwait (false);
		else
			await _session.Device.ScrollUpAsync (Select, Guard, _session.VerifyActive, token).ConfigureAwait (false);
		await WaitAsync (PageGuard (), token).ConfigureAwait (false);
		// The gesture is never repeated and returning does not imply movement, full coverage or a restored position.
		}

	public async Task ClosePageAsync (CancellationToken token = default)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		if (_selectionOpen || _pages.Count <= 1)
			throw new InvalidOperationException ("No nested page is ready to close.");
		var close = _pages[^1].Close!;
		var titles = Titles;
		var guard = PageGuard ();
		_session.VerifyActive ();
		await _session.Device.TapAsync (h => CrestronHomeExtensionPages.RequirePage (h, titles).RequireUnique (close), guard,
			() => { _pending = guard; _pages.RemoveAt (_pages.Count - 1); }, token).ConfigureAwait (false);
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		await WaitAsync (PageGuard (), token).ConfigureAwait (false);
		}

	/// <summary>Read every option and its selected state, then dismiss the list without choosing anything.</summary>
	public async Task InspectSelectionAsync (string checkId, AndroidSelector open, IReadOnlyList<string> expectedLabels, string expectedSelected, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (expectedLabels);
		if (expectedLabels.Count == 0 || expectedLabels.Any (string.IsNullOrWhiteSpace) ||
			expectedLabels.Distinct (StringComparer.Ordinal).Count () != expectedLabels.Count || !expectedLabels.Contains (expectedSelected, StringComparer.Ordinal))
			throw new ArgumentException ("Provide distinct expected labels including the selected option.", nameof (expectedLabels));
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		if (_selectionOpen)
			throw new InvalidOperationException ("A selection list is already open.");
		var guard = PageGuard ();
		var titles = Titles;
		Exception? failure = null;
		try
			{
			_session.VerifyActive ();
			await _session.Device.TapAsync (h => CrestronHomeExtensionPages.RequirePage (h, titles).RequireUnique (open), guard,
				() => { _pending = guard; _selectionOpen = true; }, token).ConfigureAwait (false);
			await ConfirmDepartureAsync (token).ConfigureAwait (false);
			await WaitAsync (SelectionGuard (), token).ConfigureAwait (false);
			var observed = new Dictionary<string, bool> (StringComparer.Ordinal);
			IReadOnlyList<AndroidSelectionOption>? previous = null;
			bool reachedEnd = false;
			for (int page = 0; page < 64; page++)
				{
				IReadOnlyList<AndroidSelectionOption> current = [];
				await _session.CaptureAsync (checkId + ".options-" + page, hierarchy =>
					{
					current = CrestronHomeExtensionPages.ReadSelectionOptions (Selection (hierarchy));
					foreach (var option in current)
						{
						if (!expectedLabels.Contains (option.Label, StringComparer.Ordinal) || option.Selected != (option.Label == expectedSelected) ||
							observed.TryGetValue (option.Label, out var old) && old != option.Selected)
							throw new InvalidDataException ("The selection options or selected state differ from the expected device state.");
						observed[option.Label] = option.Selected;
						}
					}, token).ConfigureAwait (false);
				if (previous != null && previous.SequenceEqual (current))
					{
					reachedEnd = true;
					break;
					}
				previous = current;
				_session.VerifyActive ();
				await _session.Device.ScrollDownAsync (CrestronHomePages.Resource ("customdevice_selectionRecyclerView"), SelectionGuard (), static () => { }, token).ConfigureAwait (false);
				// One deliberate next-page gesture. An uncertain gesture is never replayed.
				}
			if (!reachedEnd || observed.Count != expectedLabels.Count || !observed.TryGetValue (expectedSelected, out var selected) || !selected)
				throw new InvalidDataException ("The complete expected selection list was not observed.");
			}
		catch (Exception e) { failure = e; throw; }
		finally
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (1));
			try
				{
				if (_selectionOpen)
					await CloseSelectionAsync (cleanup.Token).ConfigureAwait (false);
				}
			catch (Exception cleanupError) when (failure != null)
				{
				throw new AggregateException ("Selection inspection and restoration both failed.", failure, cleanupError);
				}
			}
		}

	private async Task CloseSelectionAsync (CancellationToken token)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		var guard = SelectionGuard ();
		_session.VerifyActive ();
		await _session.Device.BackAsync (guard, () => { _pending = guard; _selectionOpen = false; }, token).ConfigureAwait (false);
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		await WaitAsync (PageGuard (), token).ConfigureAwait (false);
		}

	internal async Task RestoreRootAsync (CancellationToken token)
		{
		await ConfirmDepartureAsync (token).ConfigureAwait (false);
		if (_selectionOpen)
			await CloseSelectionAsync (token).ConfigureAwait (false);
		while (_pages.Count > 1)
			await ClosePageAsync (token).ConfigureAwait (false);
		await WaitAsync (PageGuard (), token).ConfigureAwait (false);
		}
	}