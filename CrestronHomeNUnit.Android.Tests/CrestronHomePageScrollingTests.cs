// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

public sealed partial class CrestronHomeExtensionNavigationTests
	{
	[TestCase (true)]
	[TestCase (false)]
	public async Task PageScrollUsesOnlyTheFrontViewportAndChecksItBeforeInput (bool down)
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		await _navigation.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"));
		bool guarded = false;
		void Guard (AndroidHierarchy page)
			{
			Assert.That (page.RequireUnique (CrestronHomePages.Resource ("customdevices_toolbarTitle")).Text, Is.EqualTo ("Edit Schedule"));
			Assert.That (_transport.PageScrollArguments, Is.Empty);
			guarded = true;
			}
		if (down) await _navigation.ScrollDownAsync (Guard);
		else await _navigation.ScrollUpAsync (Guard);
		Assert.That (guarded, Is.True);
		Assert.That (_transport.PageScrollArguments, Has.Count.EqualTo (1));
		Assert.That (_transport.PageScrollArguments[0], Is.EqualTo (new[] { "shell", "input", "swipe", "450", down ? "600" : "400", "450", down ? "400" : "600", "350" }));
		await _navigation.RestoreRootAsync (CancellationToken.None);
		Assert.That (_transport.Depth, Is.EqualTo (1));
		}

	[TestCase ("viewport-disabled")]
	[TestCase ("viewport-small")]
	[TestCase ("viewport-duplicate")]
	public async Task InvalidFrontViewportPreventsScrolling (string error)
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		_transport.Error = error;
		Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.ScrollDownAsync (_ => { }));
		Assert.That (_transport.PageScrollArguments, Is.Empty);
		await _navigation.RestoreRootAsync (CancellationToken.None);
		}

	[Test]
	public async Task PageAssertionFailurePreventsScrollingAndNavigationCanStillRestore ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		Assert.ThrowsAsync<InvalidDataException> (() => _navigation.ScrollDownAsync (_ => throw new InvalidDataException ("State changed.")));
		Assert.That (_transport.PageScrollArguments, Is.Empty);
		await _navigation.RestoreRootAsync (CancellationToken.None);
		Assert.That (_transport.Depth, Is.EqualTo (1));
		}

	[Test]
	public async Task CancellationAfterPageInspectionPreventsTheGesture ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		using var cancellation = new CancellationTokenSource ();
		Assert.ThrowsAsync<OperationCanceledException> (() => _navigation.ScrollDownAsync (_ => cancellation.Cancel (), cancellation.Token));
		Assert.That (_transport.PageScrollArguments, Is.Empty);
		await _navigation.RestoreRootAsync (CancellationToken.None);
		}

	[Test]
	public async Task UnexpectedSelectionOverlayCannotReceiveAPageGesture ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		_transport.Selection = true;
		Assert.ThrowsAsync<InvalidOperationException> (() => _navigation.ScrollDownAsync (_ => { }));
		Assert.That (_transport.PageScrollArguments, Is.Empty);
		_transport.Selection = false;
		await _navigation.RestoreRootAsync (CancellationToken.None);
		}

	[Test]
	public async Task UncertainPageGestureIsNotRepeatedDuringCleanup ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		_transport.Error = "page-scroll";
		Assert.ThrowsAsync<IOException> (() => _navigation.ScrollDownAsync (_ => { }));
		await _navigation.RestoreRootAsync (CancellationToken.None);
		Assert.That (_transport.PageScrollArguments, Has.Count.EqualTo (1));
		Assert.That (_transport.Depth, Is.EqualTo (1));
		}
	}