// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CrestronHomeNUnit.Android;

public enum AndroidSelectorKind
	{
	ResourceId, Text, ContentDescription
	}
public sealed record AndroidSelector (AndroidSelectorKind Kind, string Value)
	{
	public string? AncestorResourceId { get; init; }
	}
public sealed record AndroidElement (string ResourceId, string Text, string Description, bool Enabled, int Left, int Top, int Right, int Bottom);

/// <summary>A private, password-masked view of a captured Android accessibility hierarchy.</summary>
public sealed class AndroidHierarchy
	{
	private readonly XDocument _document;
	private readonly string _application;

	public AndroidHierarchy (string xml, string application)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (application);
		_application = application;
		using var reader = XmlReader.Create (new StringReader (xml), new XmlReaderSettings
			{
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
			MaxCharactersInDocument = 5 * 1024 * 1024
			});
		_document = XDocument.Load (reader);
		if (_document.Root?.Name != "hierarchy")
			throw new InvalidDataException ("Android did not return a UI hierarchy.");
		foreach (var node in _document.Descendants ("node").Where (node => (string?)node.Attribute ("password") == "true"))
			{
			node.SetAttributeValue ("text", "[masked]");
			// Some controls repeat text in their accessibility description.
			node.SetAttributeValue ("content-desc", "[masked]");
			}
		}

	public string MaskedXml => _document.ToString (SaveOptions.DisableFormatting);

	public AndroidElement RequireUnique (AndroidSelector selector)
		{
		var matches = FindMatches (selector);
		if (matches.Length != 1)
			throw new InvalidOperationException ($"Android selector matched {matches.Length} elements; no input was sent.");
		return ReadElement (matches[0]);
		}

	public void RequireAbsent (AndroidSelector selector)
		{
		if (FindMatches (selector).Length != 0)
			throw new InvalidOperationException ("Unexpected Android element or overlay is present; no input was sent.");
		}

	private XElement[] FindMatches (AndroidSelector selector)
		{
		ArgumentNullException.ThrowIfNull (selector);
		ArgumentException.ThrowIfNullOrWhiteSpace (selector.Value);
		var attribute = selector.Kind switch
			{
				AndroidSelectorKind.ResourceId => "resource-id",
				AndroidSelectorKind.Text => "text",
				AndroidSelectorKind.ContentDescription => "content-desc",
				_ => throw new ArgumentException ("Unknown Android selector kind.", nameof (selector))
				};
		if (selector.AncestorResourceId != null) ArgumentException.ThrowIfNullOrWhiteSpace (selector.AncestorResourceId);
		return _document.Descendants ("node").Where (node =>
			(string?)node.Attribute ("package") == _application && (string?)node.Attribute (attribute) == selector.Value &&
			(selector.AncestorResourceId == null || node.Ancestors ("node").Any (ancestor =>
				(string?)ancestor.Attribute ("package") == _application && (string?)ancestor.Attribute ("resource-id") == selector.AncestorResourceId))).ToArray ();
		}

	private static AndroidElement ReadElement (XElement selected)
		{
		if ((string?)selected.Attribute ("password") == "true")
			throw new InvalidOperationException ("Password fields are outside this navigation interface.");
		var bounds = Regex.Match ((string?)selected.Attribute ("bounds") ?? "", @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds (1));
		if (!bounds.Success || !int.TryParse (bounds.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var left) ||
			!int.TryParse (bounds.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var top) ||
			!int.TryParse (bounds.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var right) ||
			!int.TryParse (bounds.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bottom) ||
			right <= left || bottom <= top || right > 32768 || bottom > 32768)
			throw new InvalidDataException ("Android element bounds are invalid; no input was sent.");
		return new ((string?)selected.Attribute ("resource-id") ?? "", (string?)selected.Attribute ("text") ?? "",
			(string?)selected.Attribute ("content-desc") ?? "", (string?)selected.Attribute ("enabled") == "true", left, top, right, bottom);
		}
	}