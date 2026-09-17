// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

using NUnit.Framework;

namespace CrestronHomeNUnit.Android.Tests;

[TestFixture]
public sealed partial class CrestronHomeExtensionNavigationTests
	{
	private const string APPLICATION = "com.crestron.phoenix.app";
	private string _directory = null!;
	private AndroidSessionLease _lease = null!;
	private StackTransport _transport = null!;
	private CrestronHomeExtensionNavigation _navigation = null!;
	private static AndroidSelector Text (string value) => new (AndroidSelectorKind.Text, value);
	private static AndroidSelector Close => CrestronHomePages.Resource ("customdevices_toolbarClose");

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (Path.GetTempPath (), Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		var owner = Guid.NewGuid ().ToString ("N");
		var path = Path.Combine (_directory, "worker.lease");
		_lease = AndroidSessionLease.Acquire (path, owner);
		using var process = Process.GetCurrentProcess ();
		var context = new AndroidRunContext (1, owner, Environment.MachineName, process.Id, process.StartTime.ToUniversalTime ().Ticks,
			"192.0.2.1", 7, Guid.NewGuid ().ToString (), "1.0.0.1", new ('A', 64), new ('B', 64),
			new (Environment.ProcessPath!, "fake-serial", APPLICATION, "Example Home", path), _directory);
		_transport = new ();
		_navigation = new (new (context, new (_transport, APPLICATION)), "Thermostat", TimeSpan.FromMilliseconds (60));
		}

	[TearDown]
	public void TearDown ()
		{
		_lease.Release ();
		Directory.Delete (_directory, true);
		}

	[Test]
	public async Task DuplicateBackgroundControlIdsAreScopedToTheExpectedFrontPage ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		await _navigation.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"));
		await _navigation.InspectAsync ("edit", h => Assert.That (h.RequireUnique (CrestronHomePages.Resource ("customdevices_toolbarTitle")).Text, Is.EqualTo ("Edit Schedule")));
		await _navigation.RestoreRootAsync (CancellationToken.None);
		Assert.That (_transport.Depth, Is.EqualTo (1));
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "open", "edit", "cancel", "close" }));
		}

	[Test]
	public async Task SelectionTraversalChecksAllOptionsAndRestoresWithoutSelecting ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		await _navigation.InspectSelectionAsync ("options", Text ("PICK"), ["First", "Second", "Third"], "Second");
		Assert.That (_transport.Selection, Is.False);
		Assert.That (_transport.Scrolls, Is.EqualTo (2));
		await _navigation.RestoreRootAsync (CancellationToken.None);
		Assert.That (_transport.Inputs, Is.EqualTo (new[] { "open", "picker", "scroll", "scroll", "back", "close" }));
		}

	[TestCase ("unexpected")]
	[TestCase ("wrong-selection")]
	[TestCase ("missing")]
	public async Task InvalidSelectionStillDismissesThePicker (string error)
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		_transport.Error = error;
		Assert.ThrowsAsync<InvalidDataException> (() => _navigation.InspectSelectionAsync ("bad", Text ("PICK"), ["First", "Second", "Third"], "Second"));
		Assert.That (_transport.Selection, Is.False);
		await _navigation.RestoreRootAsync (CancellationToken.None);
		Assert.That (_transport.Depth, Is.EqualTo (1));
		}

	[Test]
	public async Task UncertainScrollIsNotRepeatedAndPickerIsClosed ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		_transport.Error = "scroll";
		Assert.ThrowsAsync<IOException> (() => _navigation.InspectSelectionAsync ("scroll", Text ("PICK"), ["First", "Second", "Third"], "Second"));
		Assert.That (_transport.Scrolls, Is.EqualTo (1));
		Assert.That (_transport.Selection, Is.False);
		await _navigation.RestoreRootAsync (CancellationToken.None);
		}

	[Test]
	public async Task OriginalInspectionFailureIsRetainedWhenCleanupAlsoFails ()
		{
		await _navigation.OpenPageAsync (Text ("Open"), "Schedule", Close);
		_transport.Error = "unexpected";
		_transport.FailBack = true;
		var failure = Assert.ThrowsAsync<AggregateException> (() => _navigation.InspectSelectionAsync ("cleanup", Text ("PICK"), ["First", "Second", "Third"], "Second"));
		Assert.That (failure!.InnerExceptions[0], Is.TypeOf<InvalidDataException> ());
		Assert.That (failure.InnerExceptions[1], Is.TypeOf<IOException> ());
		Assert.That (_transport.Inputs.Count (input => input == "back"), Is.EqualTo (1));
		}

	[Test]
	public void BackgroundPageOrUnrecognizedOverlayCannotBeAccepted ()
		{
		_transport.Depth = 2;
		var h = new AndroidHierarchy (_transport.Xml (), APPLICATION);
		Assert.Throws<InvalidOperationException> (() => CrestronHomeExtensionPages.RequirePage (h, ["Thermostat"]));
		Assert.Throws<InvalidOperationException> (() => CrestronHomeExtensionPages.RequirePage (h, ["Thermostat", "Wrong"]));
		_transport.Selection = true;
		h = new (_transport.Xml (), APPLICATION);
		Assert.Throws<InvalidOperationException> (() => CrestronHomeExtensionPages.RequirePage (h, ["Thermostat", "Schedule"]));
		CrestronHomeExtensionPages.RequireSelection (h, ["Thermostat", "Schedule"]);
		}

	[TestCase ("[0,480][500,500]", true)]
	[TestCase ("[0,100][500,120]", true)]
	[TestCase ("[0,350][500,370]", false)]
	[TestCase ("[0,400][500,500]", false)]
	public void OnlyShortClippedBoundaryRowsMayOmitOffscreenControls (string bounds, bool clipped)
		{
		_transport.Depth = 2;
		_transport.Selection = true;
		var document = XDocument.Parse (_transport.Xml ());
		var list = document.Descendants ("node").Single (node => (string?)node.Attribute ("resource-id") == CrestronHomePages.ResourcePrefix + "customdevice_selectionRecyclerView");
		var fragment = new XElement (list.Elements ("node").First ());
		if (bounds == "[0,100][500,120]")
			fragment.Elements ().First ().SetAttributeValue ("bounds", "[0,100][100,90]");
		else
			fragment.Elements ().Remove ();
		fragment.SetAttributeValue ("bounds", bounds);
		list.Add (fragment);
		var selection = CrestronHomeExtensionPages.RequireSelection (new (document.ToString (), APPLICATION), ["Thermostat", "Schedule"]);
		if (clipped)
			Assert.That (CrestronHomeExtensionPages.ReadSelectionOptions (selection).Select (option => option.Label), Is.EqualTo (new[] { "First", "Second" }));
		else
			Assert.Throws<InvalidOperationException> (() => CrestronHomeExtensionPages.ReadSelectionOptions (selection));
		}

	private sealed class StackTransport : IAndroidCommandTransport
		{
		internal int Depth = 1;
		internal bool Selection;
		internal int Scrolls;
		internal List<string[]> PageScrollArguments = [];
		internal string? Error;
		internal bool FailBack;
		internal List<string> Inputs = [];
		private static XElement Node (string id, string text = "", int left = 0, string bounds = "") => new ("node",
			new XAttribute ("package", APPLICATION), new XAttribute ("resource-id", CrestronHomePages.ResourcePrefix + id),
			new XAttribute ("text", text), new XAttribute ("enabled", "true"),
			new XAttribute ("bounds", bounds == "" ? $"[{left},0][{left + 100},100]" : bounds));
		internal string Xml ()
			{
			var container = Node ("main_container");
			for (int i = 0; i < Depth; i++)
				{
				var layer = Node (i == 0 ? "customdevice_pulley" : "");
				layer.SetAttributeValue ("class", i == 0 ? "android.view.ViewGroup" : "android.widget.LinearLayout");
				layer.Add (Node ("customdevices_toolbarTitle", new[] { "Thermostat", "Schedule", "Edit Schedule" }[i]), Node ("customdevices_toolbarClose", left: 100));
				layer.Add (Node ("action", i == 0 ? "Open" : "Edit", left: i == 0 ? 0 : 200), Node ("picker", "PICK", left: 300));
				if (i == 2)
					layer.Add (Node ("cancel", "Cancel", left: 400));
				var viewport = Node ("customdevices_componentRecyclerView", bounds: $"[{i * 100},{100 + i * 100}][{500 + i * 100},{500 + i * 100}]");
				if (i == Depth - 1 && Error == "viewport-disabled") viewport.SetAttributeValue ("enabled", "false");
				if (i == Depth - 1 && Error == "viewport-small") viewport.SetAttributeValue ("bounds", "[0,100][500,120]");
				if (i == Depth - 1 && Error == "viewport-duplicate") layer.Add (new XElement (viewport));
				layer.Add (viewport);
				container.Add (layer);
				}
			if (Selection)
				{
				var layer = Node ("");
				layer.SetAttributeValue ("class", "android.widget.LinearLayout");
				layer.Add (Node ("customdevice_selectionToolbar_backButton"));
				var list = Node ("customdevice_selectionRecyclerView", bounds: "[0,100][500,500]");
				var labels = Scrolls == 0 || Error == "missing" ? new[] { "First", "Second" } : new[] { "Second", "Third" };
				if (Error == "unexpected")
					labels = ["Other", "Second"];
				for (int i = 0; i < labels.Length; i++)
					{
					var row = Node ("customdevice_selectionView", bounds: $"[0,{100 + i * 100}][500,{200 + i * 100}]");
					var radio = Node ("customdevice_selectionElement_radioButton");
					radio.SetAttributeValue ("checked", labels[i] == "Second" && Error != "wrong-selection" ? "true" : "false");
					row.Add (Node ("customdevice_selectionElement_title", labels[i]), radio);
					list.Add (row);
					}
				layer.Add (list);
				container.Add (layer);
				}
			return new XElement ("hierarchy", container).ToString (SaveOptions.DisableFormatting);
			}

		public Task<byte[]> ExecuteAsync (IReadOnlyList<string> arguments, CancellationToken cancellationToken)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			if (arguments[0] == "exec-out")
				return Task.FromResult (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
			if (arguments[1] == "uiautomator")
				return Task.FromResult (Encoding.UTF8.GetBytes ("UI hierarchy dumped to: " + arguments[3]));
			if (arguments[1] == "rm")
				return Task.FromResult (Array.Empty<byte> ());
			if (arguments[1] == "cat")
				return Task.FromResult (Encoding.UTF8.GetBytes (Xml ()));
			if (arguments[1] != "input")
				throw new InvalidOperationException ("Unexpected command.");
			if (arguments[2] == "swipe")
				{
				if (!Selection)
					{
					PageScrollArguments.Add (arguments.ToArray ());
					Inputs.Add ("page-scroll");
					if (Error == "page-scroll") throw new IOException ("Uncertain completed page scroll.");
					return Task.FromResult (Array.Empty<byte> ());
					}
				Inputs.Add ("scroll");
				Scrolls++;
				if (Error == "scroll")
					throw new IOException ("Uncertain completed scroll.");
				}
			else if (arguments[2] == "keyevent")
				{
				if (!Selection || arguments[3] != "KEYCODE_BACK")
					throw new InvalidOperationException ("Unexpected Back.");
				Inputs.Add ("back");
				if (FailBack)
					throw new IOException ("Uncertain Back.");
				Selection = false;
				}
			else if (arguments[2] == "tap" && !Selection)
				{
				switch (arguments[3])
					{
					case "50" when Depth == 1: Inputs.Add ("open"); Depth++; break;
					case "250" when Depth == 2: Inputs.Add ("edit"); Depth++; break;
					case "450" when Depth == 3: Inputs.Add ("cancel"); Depth--; break;
					case "150" when Depth == 2: Inputs.Add ("close"); Depth--; break;
					case "350": Inputs.Add ("picker"); Selection = true; Scrolls = 0; break;
					default: throw new InvalidOperationException ("Unexpected tap.");
					}
				}
			else
				throw new InvalidOperationException ("No option selection is permitted.");
			return Task.FromResult (Array.Empty<byte> ());
			}
		}
	}