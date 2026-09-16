// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Xml.Linq;

namespace CrestronHomeNUnit.Android;

/// <summary>Scope controls to the observed front layer, while verifying every expected background page.</summary>
public static class CrestronHomeExtensionPages
	{
	private const string APPLICATION = "com.crestron.phoenix.app";
	private static bool Is (XElement node, string id) => (string?)node.Attribute ("package") == APPLICATION &&
		(string?)node.Attribute ("resource-id") == CrestronHomePages.ResourcePrefix + id;
	private static AndroidHierarchy Scope (XElement node) => new (new XElement ("hierarchy", new XElement (node)).ToString (SaveOptions.DisableFormatting), APPLICATION);

	public static AndroidHierarchy RequirePage (AndroidHierarchy hierarchy, IReadOnlyList<string> titles) => Layers (hierarchy, titles, false);
	public static AndroidHierarchy RequireSelection (AndroidHierarchy hierarchy, IReadOnlyList<string> titles) => Layers (hierarchy, titles, true);

	private static AndroidHierarchy Layers (AndroidHierarchy hierarchy, IReadOnlyList<string> titles, bool selection)
		{
		ArgumentNullException.ThrowIfNull (titles);
		if (titles.Count == 0 || titles.Any (string.IsNullOrWhiteSpace))
			throw new ArgumentException ("Provide the complete ordered extension page titles.", nameof (titles));
		foreach (var overlay in new[] { "mobileclaimhome_content", "fragmentPulleyContainer", "bottomSheet_infoBar", "homeswitcher_title", "featureMoreActionRoot" })
			hierarchy.RequireAbsent (CrestronHomePages.Resource (overlay));
		var document = XDocument.Parse (hierarchy.MaskedXml);
		var containers = document.Descendants ("node").Where (node => Is (node, "main_container")).ToArray ();
		if (containers.Length != 1)
			throw new InvalidOperationException ("The extension container is missing or ambiguous.");
		var layers = containers[0].Elements ("node").ToArray ();
		if (layers.Length != titles.Count + (selection ? 1 : 0))
			throw new InvalidOperationException ("The observed extension stack does not match the expected pages.");
		for (int i = 0; i < layers.Length; i++)
			{
			if ((string?)layers[i].Attribute ("package") != APPLICATION ||
				(i == 0 ? !Is (layers[i], "customdevice_pulley") : (string?)layers[i].Attribute ("class") != "android.widget.LinearLayout"))
				throw new InvalidOperationException ("The extension layer layout has changed.");
			var scoped = Scope (layers[i]);
			if (i < titles.Count)
				{
				CrestronHomePages.RequireExtensionPage (scoped, titles[i]);
				scoped.RequireAbsent (CrestronHomePages.Resource ("customdevice_selectionRecyclerView"));
				}
			else
				{
				scoped.RequireAbsent (CrestronHomePages.Resource ("customdevices_toolbarTitle"));
				if (!scoped.RequireUnique (CrestronHomePages.Resource ("customdevice_selectionRecyclerView")).Enabled ||
					!scoped.RequireUnique (CrestronHomePages.Resource ("customdevice_selectionToolbar_backButton")).Enabled)
					throw new InvalidOperationException ("The expected selection list is not open.");
				}
			}
		return Scope (layers[^1]);
		}

	/// <summary>Read complete visible option rows, retaining literal labels and selected state.</summary>
	public static IReadOnlyList<AndroidSelectionOption> ReadSelectionOptions (AndroidHierarchy selection)
		{
		var container = selection.RequireUnique (CrestronHomePages.Resource ("customdevice_selectionRecyclerView"));
		var document = XDocument.Parse (selection.MaskedXml);
		var rows = document.Descendants ("node").Where (node => Is (node, "customdevice_selectionView")).ToArray ();
		var completeHeights = rows.Where (row =>
			row.Descendants ("node").Count (node => Is (node, "customdevice_selectionElement_title")) == 1 &&
			row.Descendants ("node").Count (node => Is (node, "customdevice_selectionElement_radioButton")) == 1)
			.Select (row => AndroidHierarchy.ReadElement (row)).Where (bounds => bounds.Top > container.Top && bounds.Bottom < container.Bottom)
			.Select (bounds => bounds.Bottom - bounds.Top).ToArray ();
		int minimumCompleteHeight = completeHeights.Length == 0 ? 0 : completeHeights.Min ();
		var result = new List<AndroidSelectionOption> ();
		foreach (var row in rows)
			{
			var bounds = AndroidHierarchy.ReadElement (row);
			if (bounds.Top < container.Top || bounds.Bottom > container.Bottom)
				continue;
			var titles = row.Descendants ("node").Where (node => Is (node, "customdevice_selectionElement_title")).ToArray ();
			var radios = row.Descendants ("node").Where (node => Is (node, "customdevice_selectionElement_radioButton")).ToArray ();
			// Accessibility may include the clipped edge of the next recycled row,
			// with missing or inverted child bounds outside the viewport. Never ignore an interior,
			// full-height or duplicated row: only a demonstrably shorter boundary fragment.
			if (titles.Length <= 1 && radios.Length <= 1 &&
				(bounds.Top == container.Top || bounds.Bottom == container.Bottom) && bounds.Bottom - bounds.Top < minimumCompleteHeight)
				continue;
			if (titles.Length != 1 || radios.Length != 1 || !bool.TryParse ((string?)radios[0].Attribute ("checked"), out var selected))
				throw new InvalidOperationException ("The selection row is missing or ambiguous.");
			var title = AndroidHierarchy.ReadElement (titles[0]);
			if (!bounds.Enabled || !title.Enabled || string.IsNullOrWhiteSpace (title.Text) || result.Any (option => option.Label == title.Text))
				throw new InvalidOperationException ("The selection list has an unavailable or duplicate label.");
			result.Add (new (title.Text, selected));
			}
		if (result.Count == 0)
			throw new InvalidOperationException ("The selection list has no complete readable options.");
		return result;
		}
	}

public sealed record AndroidSelectionOption (string Label, bool Selected);