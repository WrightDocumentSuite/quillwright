using System.Globalization;
using System.Xml;
using Quillwright.Primitives;

namespace Quillwright.Formats;

/// <summary>
/// Reading helpers shared by every part reader: child iteration that cannot run away on a
/// malformed file, and attribute access that works whether the file is Transitional,
/// Strict, or written by a producer that forgot the namespace.
/// </summary>
internal static class XmlHelp
{
    /// <summary>
    /// Walks the child elements of the current element and hands each to
    /// <paramref name="onChild"/>, which must consume it. The reader ends up just past the
    /// element's end tag.
    /// </summary>
    public static void ForEachChild(XmlReader xml, Action<XmlReader, string> onChild)
    {
        if (xml.IsEmptyElement)
        {
            xml.Read();
            return;
        }

        var lineInfo = xml as IXmlLineInfo;
        xml.ReadStartElement();
        while (xml.NodeType is not (XmlNodeType.EndElement or XmlNodeType.None))
        {
            if (xml.NodeType != XmlNodeType.Element)
            {
                xml.Read();
                continue;
            }

            (int line, int column) = (lineInfo?.LineNumber ?? 0, lineInfo?.LinePosition ?? 0);
            onChild(xml, xml.LocalName);

            // A handler that forgot to consume its element would spin here forever, so the
            // reader is nudged past it when nothing moved.
            if (lineInfo is not null && lineInfo.LineNumber == line && lineInfo.LinePosition == column)
                xml.Skip();
        }

        if (xml.NodeType == XmlNodeType.EndElement)
            xml.ReadEndElement();
    }

    /// <summary>Reads an attribute in the WordprocessingML namespace.</summary>
    public static string? Attr(XmlReader xml, string name) =>
        xml.GetAttribute(name, DocxSchema.NsWord) ??
        xml.GetAttribute(name, DocxSchema.NsWordStrict) ??
        xml.GetAttribute(name);

    /// <summary>Reads a relationship-namespace attribute, normally <c>r:id</c>.</summary>
    public static string? RelAttr(XmlReader xml, string name = "id") =>
        xml.GetAttribute(name, DocxSchema.NsRelationships) ??
        xml.GetAttribute(name, DocxSchema.NsRelationshipsStrict);

    /// <summary>Reads the <c>w:val</c> attribute.</summary>
    public static string? Val(XmlReader xml) => Attr(xml, "val");

    /// <summary>Reads an integer attribute in the WordprocessingML namespace.</summary>
    public static int? AttrInt(XmlReader xml, string name) =>
        int.TryParse(Attr(xml, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    /// <summary>Reads the <c>w:val</c> attribute as an integer.</summary>
    public static int? ValInt(XmlReader xml) => AttrInt(xml, "val");

    /// <summary>Reads a measurement attribute expressed in twips.</summary>
    public static Length? AttrTwips(XmlReader xml, string name) =>
        Length.TryParse(Attr(xml, name), CultureInfo.InvariantCulture, out Length value) ? value : null;

    /// <summary>Reads the <c>w:val</c> attribute as a measurement in twips.</summary>
    public static Length? ValTwips(XmlReader xml) => AttrTwips(xml, "val");

    /// <summary>Reads the <c>w:val</c> attribute as a measurement in half-points.</summary>
    public static Length? ValHalfPoints(XmlReader xml) => ParseHalfPoints(Val(xml));

    /// <summary>Parses an <c>ST_HpsMeasure</c> value (ISO/IEC 29500-1 §22.9.2.9).</summary>
    /// <remarks>
    /// The type is a union: a bare number is half-points, which is how Word writes a font
    /// size, but a number carrying a unit is the length it names. <c>12.7mm</c> is therefore
    /// 36 points rather than 18 — reading it as half-points would halve every size a Strict
    /// producer wrote.
    /// </remarks>
    public static Length? ParseHalfPoints(string? value)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        if (Length.HasUnit(text))
            return Length.TryParse(text, CultureInfo.InvariantCulture, out Length measure) ? measure : null;

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double halfPoints)
            ? Length.FromPoints(halfPoints / 2)
            : null;
    }

    /// <summary>Reads the <c>w:val</c> attribute as a measurement in eighths of a point.</summary>
    public static Length? ValEighthPoints(XmlReader xml) =>
        int.TryParse(Val(xml), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Length.FromEighthPoints(value)
            : null;

    /// <summary>Reads an <c>ST_OnOff</c> attribute.</summary>
    public static bool? AttrBool(XmlReader xml, string name) => ParseOnOff(Attr(xml, name));

    /// <summary>Reads an on/off element: present means on unless <c>w:val</c> says otherwise.</summary>
    public static bool Toggle(XmlReader xml) => ParseOnOff(Val(xml)) ?? true;

    /// <summary>Parses an <c>ST_OnOff</c> value (ISO/IEC 29500-1 §22.9.2.7).</summary>
    /// <remarks>
    /// ISO narrowed the type to <c>xsd:boolean</c> — <c>1</c>, <c>0</c>, <c>true</c>,
    /// <c>false</c> — where ECMA-376 also allowed <c>on</c> and <c>off</c>, which Word still
    /// writes. Both spellings are read, as is the whitespace the boolean datatype collapses.
    /// Anything else is not a value at all and reads as if the attribute were absent.
    /// </remarks>
    public static bool? ParseOnOff(string? value)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        if (text is "1" || Matches(text, "true") || Matches(text, "on"))
            return true;
        if (text is "0" || Matches(text, "false") || Matches(text, "off"))
            return false;

        return null;

        static bool Matches(ReadOnlySpan<char> text, string word) =>
            text.Equals(word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parses a <c>W3CDTF</c> timestamp.</summary>
    public static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset result)
            ? result
            : null;

    /// <summary>
    /// Captures the complete start tag of a part's root element, namespace declarations
    /// included.
    /// </summary>
    /// <remarks>
    /// A part whose children are preserved verbatim has to keep its own namespace context.
    /// Word bound <c>w14</c> to a 2007 beta namespace before it meant the 2010 one, and
    /// re-declaring the prefix ourselves would silently move every preserved element into a
    /// namespace <c>mc:Ignorable</c> no longer covers.
    /// </remarks>
    public static string? CaptureRootAttributes(XmlReader xml)
    {
        var builder = new System.Text.StringBuilder();
        if (xml.MoveToFirstAttribute())
        {
            do
            {
                builder.Append(' ').Append(xml.Name).Append("=\"")
                    .Append(System.Security.SecurityElement.Escape(xml.Value)).Append('"');
            }
            while (xml.MoveToNextAttribute());

            xml.MoveToElement();
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// Captures the attributes of the current element as markup ready to re-emit inside its
    /// start tag. Namespace declarations are kept: an element that declares a prefix does so
    /// because its own attributes use it, and dropping the declaration would leave the
    /// re-emitted attribute pointing at nothing.
    /// </summary>
    public static string? CaptureAttributes(XmlReader xml, params ReadOnlySpan<string> skip)
    {
        if (!xml.HasAttributes)
            return null;

        System.Text.StringBuilder? builder = null;
        if (xml.MoveToFirstAttribute())
        {
            do
            {
                if (skip.Contains(xml.LocalName))
                    continue;

                builder ??= new System.Text.StringBuilder();
                builder.Append(' ').Append(xml.Name).Append("=\"")
                    .Append(System.Security.SecurityElement.Escape(xml.Value)).Append('"');
            }
            while (xml.MoveToNextAttribute());

            xml.MoveToElement();
        }

        return builder?.ToString();
    }

    /// <summary>Appends the current element's markup to a preserved slot and consumes it.</summary>
    public static void Preserve(ref string? slot, XmlReader xml) =>
        slot = slot is null ? xml.ReadOuterXml() : slot + xml.ReadOuterXml();

    /// <summary>The namespace declarations in scope at the current element.</summary>
    /// <remarks>
    /// <see cref="XmlReader.ReadOuterXml"/> carries over only the prefixes the captured
    /// markup uses in element and attribute names. A prefix named inside an attribute
    /// <em>value</em> — which is how <c>mc:Choice/@Requires</c> names a vocabulary — is left
    /// behind, so a fragment parsed again later needs the surrounding context handed to it.
    /// </remarks>
    public static IDictionary<string, string> NamespacesInScope(XmlReader xml) =>
        xml is IXmlNamespaceResolver resolver
            ? resolver.GetNamespacesInScope(XmlNamespaceScope.All)
            : new Dictionary<string, string>();
}
