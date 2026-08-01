using Quillwright.Primitives;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// The handful of shapes every WordprocessingML property element takes: an on/off toggle, a
/// single <c>w:val</c> attribute, a measurement. Writing them through one place keeps the
/// part writers free of angle brackets.
/// </summary>
internal static class WordXml
{
    /// <summary>
    /// The name of the leading edge in the vocabulary this writer is emitting: <c>start</c>
    /// for a Strict package, <c>left</c> for a Transitional one.
    /// </summary>
    /// <remarks>
    /// Strict replaced the direction words that assume a left-to-right page. The rename is
    /// not uniform — <c>CT_PBdr</c> and <c>CT_PageMar</c> kept <c>left</c> and <c>right</c>
    /// while <c>CT_Ind</c>, <c>CT_TblBorders</c>, <c>CT_TcBorders</c>, <c>CT_TcMar</c> and
    /// <c>CT_TblCellMar</c> did not — so this is applied case by case rather than to every
    /// occurrence of the word.
    /// </remarks>
    public static ReadOnlySpan<byte> Leading(Utf8XmlWriter writer) => writer.Strict ? "start"u8 : "left"u8;

    /// <summary>The name of the trailing edge in the vocabulary this writer is emitting.</summary>
    /// <seealso cref="Leading"/>
    public static ReadOnlySpan<byte> Trailing(Utf8XmlWriter writer) => writer.Strict ? "end"u8 : "right"u8;

    /// <summary>Writes <c>&lt;w:name/&gt;</c> for <see langword="true"/> and the explicit off form for <see langword="false"/>.</summary>
    public static void Toggle(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, bool? value)
    {
        if (value is null)
            return;

        writer.WriteRaw("<w:"u8);
        writer.WriteRaw(name);
        if (value == false)
            writer.WriteRaw(" w:val=\"0\""u8);
        writer.WriteRaw("/>"u8);
    }

    /// <summary>Writes <c>&lt;w:name w:val="…"/&gt;</c>.</summary>
    public static void Value(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, string? value)
    {
        if (value is null)
            return;

        writer.WriteRaw("<w:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(" w:val=\""u8);
        writer.WriteAttributeText(value);
        writer.WriteRaw("\"/>"u8);
    }

    /// <summary>Writes <c>&lt;w:name w:val="…"/&gt;</c> with an integer value.</summary>
    public static void Value(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, int? value)
    {
        if (value is null)
            return;

        writer.WriteRaw("<w:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(" w:val=\""u8);
        writer.WriteInt32(value.Value);
        writer.WriteRaw("\"/>"u8);
    }

    /// <summary>Writes <c>&lt;w:name w:val="…"/&gt;</c> with a measurement in twips.</summary>
    public static void Twips(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, Length? value) =>
        Value(writer, name, value?.Twips);

    /// <summary>Writes <c>&lt;w:name w:val="…"/&gt;</c> with a measurement in half-points.</summary>
    public static void HalfPoints(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, Length? value) =>
        Value(writer, name, value?.HalfPoints);

    /// <summary>Writes an attribute with a string value, preceded by a space.</summary>
    public static void Attribute(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, string? value)
    {
        if (value is null)
            return;

        writer.WriteRaw(" "u8);
        writer.WriteRaw(name);
        writer.WriteRaw("=\""u8);
        writer.WriteAttributeText(value);
        writer.WriteRaw("\""u8);
    }

    /// <summary>Writes an attribute with an integer value, preceded by a space.</summary>
    public static void Attribute(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, int? value)
    {
        if (value is null)
            return;

        writer.WriteRaw(" "u8);
        writer.WriteRaw(name);
        writer.WriteRaw("=\""u8);
        writer.WriteInt32(value.Value);
        writer.WriteRaw("\""u8);
    }

    /// <summary>Writes an attribute holding <see langword="true"/> or <see langword="false"/> as <c>1</c> or <c>0</c>.</summary>
    public static void Attribute(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, bool? value)
    {
        if (value is null)
            return;

        writer.WriteRaw(" "u8);
        writer.WriteRaw(name);
        writer.WriteRaw(value.Value ? "=\"1\""u8 : "=\"0\""u8);
    }

    /// <summary>Writes an attribute with a measurement in twips, preceded by a space.</summary>
    public static void AttributeTwips(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, Length? value) =>
        Attribute(writer, name, value?.Twips);

    /// <summary>Opens a start tag: <c>&lt;w:name</c>.</summary>
    public static void Open(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name)
    {
        writer.WriteRaw("<w:"u8);
        writer.WriteRaw(name);
    }

    /// <summary>Writes a closing tag: <c>&lt;/w:name&gt;</c>.</summary>
    public static void Close(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name)
    {
        writer.WriteRaw("</w:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
    }

    /// <summary>
    /// Writes a document's root element start tag with the full namespace block. A loaded
    /// part brings its own <c>mc:Ignorable</c> list, which then replaces the default one
    /// rather than sitting beside it as a duplicate attribute.
    /// </summary>
    public static void OpenRoot(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, string? extraAttributes = null)
    {
        writer.WriteDeclaration();
        Open(writer, name);

        // A start tag captured from a loaded part already carries a complete set of
        // declarations; adding ours would rebind prefixes its preserved children rely on.
        // This is also what keeps a Strict package Strict: the generated markup binds w: to
        // whatever the source did, so it never ends up speaking a different vocabulary from
        // the parts copied alongside it.
        if (extraAttributes is not null && extraAttributes.Contains("xmlns:w=", StringComparison.Ordinal))
        {
            writer.WriteRawXml(extraAttributes);
            WriteMissingPrefixes(writer, extraAttributes);
            writer.WriteRaw(">"u8);
            return;
        }

        string? plain = StripNamespaceDeclarations(extraAttributes);
        writer.WriteRaw(DocxSchema.RootNamespaces);
        if (plain is null || !plain.Contains("mc:Ignorable", StringComparison.Ordinal))
            writer.WriteRaw(DocxSchema.IgnorablePrefixes);
        if (plain is not null)
            writer.WriteRawXml(plain);
        writer.WriteRaw(">"u8);
    }

    /// <summary>
    /// Declares the prefixes the generated markup uses that a reused start tag left out.
    /// A part that had no hyperlinks never declared <c>r</c>, and adding one now would
    /// otherwise produce an undeclared prefix.
    /// </summary>
    private static void WriteMissingPrefixes(Utf8XmlWriter writer, string attributes)
    {
        bool strict = attributes.Contains(DocxSchema.NsWordStrict, StringComparison.Ordinal);
        if (!attributes.Contains("xmlns:r=", StringComparison.Ordinal))
        {
            writer.WriteRaw(" xmlns:r=\""u8);
            writer.WriteRawXml(strict ? DocxSchema.NsRelationshipsStrict : DocxSchema.NsRelationships);
            writer.WriteRaw("\""u8);
        }

        if (!attributes.Contains("xmlns:mc=", StringComparison.Ordinal))
        {
            writer.WriteRaw(" xmlns:mc=\""u8);
            writer.WriteRawXml(DocxSchema.NsMarkupCompatibility);
            writer.WriteRaw("\""u8);
        }
    }

    /// <summary>
    /// Removes the namespace declarations from a captured start tag, leaving the ordinary
    /// attributes. Used when the declarations are being replaced rather than reused, so that
    /// a prefix is never bound twice in one tag.
    /// </summary>
    private static string? StripNamespaceDeclarations(string? attributes)
    {
        if (attributes is null || !attributes.Contains("xmlns", StringComparison.Ordinal))
            return attributes;

        var kept = new System.Text.StringBuilder(attributes.Length);
        int index = 0;
        while (index < attributes.Length)
        {
            int start = attributes.IndexOf(" xmlns", index, StringComparison.Ordinal);
            if (start < 0)
            {
                kept.Append(attributes, index, attributes.Length - index);
                break;
            }

            int quote = attributes.IndexOf('"', start);
            int end = quote < 0 ? -1 : attributes.IndexOf('"', quote + 1);
            if (end < 0)
            {
                kept.Append(attributes, index, attributes.Length - index);
                break;
            }

            kept.Append(attributes, index, start - index);
            index = end + 1;
        }

        return kept.Length == 0 ? null : kept.ToString();
    }
}
