// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CrestronHomeNUnit.Mac;

/// <summary>Literal accessibility selector, restricted to the active SceneWindow.
/// Labels are escaped as XPath literals, never executable XPath.</summary>
public sealed record MacSelector(string ElementType, string Identifier, string? Label = null)
{
    public string XPath
    {
        get
        {
            if (!ElementType.StartsWith("XCUIElementType", StringComparison.Ordinal) || !ElementType.All(char.IsAsciiLetter))
                throw new ArgumentException("Use an XCTest element type.");
            if (string.IsNullOrEmpty(Identifier) && string.IsNullOrEmpty(Label)) throw new ArgumentException("An accessibility identifier or literal label is required.");
            return $"//XCUIElementTypeWindow[@identifier='SceneWindow']//{ElementType}[@identifier={Literal(Identifier)}" +
                (Label == null ? "]" : $" and @label={Literal(Label)}]");
        }
    }
    private static string Literal(string value) => !value.Contains('\'') ? $"'{value}'" : !value.Contains('"') ? $"\"{value}\"" :
        "concat(" + string.Join(",\"'\",", value.Split('\'').Select(part => $"'{part}'")) + ")";
}

public sealed record MacElement(string Identifier, string Label, string Value, bool Selected);

/// <summary>Validated snapshot. Hidden duplicate Dialog trees are deliberately excluded.</summary>
public sealed class MacHierarchy
{
    private readonly XDocument document;
    public string Xml { get; }
    public MacHierarchy(string xml)
    {
        if (xml.Length > 4 * 1024 * 1024) throw new InvalidDataException("UI hierarchy exceeds 4 MiB.");
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        document = XDocument.Load(reader);
        if (document.Descendants("XCUIElementTypeWindow").Count(e => (string?)e.Attribute("identifier") == "SceneWindow") != 1)
            throw new InvalidDataException("Expected exactly one Crestron Home SceneWindow.");
        Xml = xml;
    }

    public MacElement Require(MacSelector selector)
    {
        var matches = document.XPathSelectElements(selector.XPath).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected one unambiguous Mac UI control.");
        var e = matches[0];
        if ((string?)e.Attribute("enabled") != "true" || (string?)e.Attribute("visible") == "false" ||
            !Positive(e, "width") || !Positive(e, "height"))
            throw new InvalidOperationException("Mac UI control is disabled or not visible.");
        return new MacElement((string?)e.Attribute("identifier") ?? "", (string?)e.Attribute("label") ?? "",
            (string?)e.Attribute("value") ?? "", (string?)e.Attribute("selected") == "true");
    }
    public bool Contains(MacSelector selector)
    {
        try { Require(selector); return true; }
        catch (InvalidOperationException) { return false; }
    }
    private static bool Positive(XElement e, string attribute) => double.TryParse((string?)e.Attribute(attribute),
        NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0;
}
