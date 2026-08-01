using Quillwright.Primitives;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Writes character formatting (<c>w:rPr</c>). The properties come out in the order
/// <c>CT_RPr</c> declares them; anything the model keeps verbatim occupies its own slot so
/// that preserving it does not push the sequence out of order.
/// </summary>
internal static class RunFormatWriter
{
    /// <summary>Writes a complete <c>w:rPr</c>, or nothing when the format overrides nothing.</summary>
    public static void Write(Utf8XmlWriter writer, RunFormat? format)
    {
        if (format is null || format.IsEmpty)
            return;

        writer.WriteRaw("<w:rPr>"u8);
        WriteBody(writer, format);
        writer.WriteRaw("</w:rPr>"u8);
    }

    /// <summary>Writes the children of <c>w:rPr</c> without the wrapper.</summary>
    public static void WriteBody(Utf8XmlWriter writer, RunFormat format)
    {
        RawXml.Write(writer, format.MarkRevisionXml);
        WordXml.Value(writer, "rStyle"u8, format.StyleId);
        WriteFonts(writer, format);
        WordXml.Toggle(writer, "b"u8, format.Bold);
        WordXml.Toggle(writer, "bCs"u8, format.BoldComplexScript);
        WordXml.Toggle(writer, "i"u8, format.Italic);
        WordXml.Toggle(writer, "iCs"u8, format.ItalicComplexScript);
        WordXml.Toggle(writer, "caps"u8, format.Caps);
        WordXml.Toggle(writer, "smallCaps"u8, format.SmallCaps);
        WordXml.Toggle(writer, "strike"u8, format.Strike);
        WordXml.Toggle(writer, "dstrike"u8, format.DoubleStrike);
        WordXml.Toggle(writer, "outline"u8, format.Outline);
        WordXml.Toggle(writer, "shadow"u8, format.Shadow);
        WordXml.Toggle(writer, "emboss"u8, format.Emboss);
        WordXml.Toggle(writer, "imprint"u8, format.Imprint);
        WordXml.Toggle(writer, "noProof"u8, format.NoProof);
        WordXml.Toggle(writer, "snapToGrid"u8, format.SnapToGrid);
        WordXml.Toggle(writer, "vanish"u8, format.Hidden);
        WordXml.Toggle(writer, "webHidden"u8, format.WebHidden);
        WriteColor(writer, format.Color);
        WordXml.Twips(writer, "spacing"u8, format.CharacterSpacing);
        WordXml.Value(writer, "w"u8, format.Scale);
        WordXml.HalfPoints(writer, "kern"u8, format.Kerning);
        WordXml.HalfPoints(writer, "position"u8, format.Position);
        WordXml.HalfPoints(writer, "sz"u8, format.Size);
        WordXml.HalfPoints(writer, "szCs"u8, format.SizeComplexScript);
        if (format.Highlight is { } highlight)
            WordXml.Value(writer, "highlight"u8, OoxmlEnums.Name(highlight));
        WriteUnderline(writer, format);
        RawXml.Write(writer, format.EffectXml);
        SharedFormatWriter.Border(writer, "bdr"u8, format.Border);
        SharedFormatWriter.Shading(writer, format.Shading);
        RawXml.Write(writer, format.FitTextXml);
        if (format.VerticalAlignment is { } vertical)
            WordXml.Value(writer, "vertAlign"u8, OoxmlEnums.Name(vertical));
        WordXml.Toggle(writer, "rtl"u8, format.RightToLeft);
        WordXml.Toggle(writer, "cs"u8, format.ComplexScript);
        RawXml.Write(writer, format.EmphasisXml);
        WriteLanguage(writer, format);
        RawXml.Write(writer, format.EastAsianLayoutXml);
        WordXml.Toggle(writer, "specVanish"u8, format.SpecialHidden);
        WordXml.Toggle(writer, "oMath"u8, format.OfficeMath);
        RawXml.Write(writer, format.Extensions);
        RawXml.Write(writer, format.ChangeXml);
    }

    private static void WriteFonts(Utf8XmlWriter writer, RunFormat format)
    {
        if (format is
            {
                FontAscii: null, FontHighAnsi: null, FontEastAsia: null, FontComplexScript: null, FontHint: null,
                FontAsciiTheme: null, FontHighAnsiTheme: null, FontEastAsiaTheme: null, FontComplexScriptTheme: null,
            })
        {
            return;
        }

        writer.WriteRaw("<w:rFonts"u8);
        WordXml.Attribute(writer, "w:hint"u8, format.FontHint);
        WordXml.Attribute(writer, "w:ascii"u8, format.FontAscii);
        WordXml.Attribute(writer, "w:hAnsi"u8, format.FontHighAnsi);
        WordXml.Attribute(writer, "w:eastAsia"u8, format.FontEastAsia);
        WordXml.Attribute(writer, "w:cs"u8, format.FontComplexScript);
        WordXml.Attribute(writer, "w:asciiTheme"u8, format.FontAsciiTheme);
        WordXml.Attribute(writer, "w:hAnsiTheme"u8, format.FontHighAnsiTheme);
        WordXml.Attribute(writer, "w:eastAsiaTheme"u8, format.FontEastAsiaTheme);
        WordXml.Attribute(writer, "w:cstheme"u8, format.FontComplexScriptTheme);
        writer.WriteRaw("/>"u8);
    }

    private static void WriteColor(Utf8XmlWriter writer, WordColor? color)
    {
        if (color is not { } value)
            return;

        writer.WriteRaw("<w:color"u8);
        SharedFormatWriter.ColorAttributes(writer, "w:val"u8, value);
        writer.WriteRaw("/>"u8);
    }

    private static void WriteUnderline(Utf8XmlWriter writer, RunFormat format)
    {
        if (format.Underline is not { } underline)
            return;

        writer.WriteRaw("<w:u w:val=\""u8);
        writer.WriteAttributeText(OoxmlEnums.Name(underline));
        writer.WriteRaw("\""u8);
        if (format.UnderlineColor is { } color)
            SharedFormatWriter.ColorAttributes(writer, "w:color"u8, color);
        writer.WriteRaw("/>"u8);
    }

    private static void WriteLanguage(Utf8XmlWriter writer, RunFormat format)
    {
        if (format is { Language: null, LanguageEastAsia: null, LanguageComplexScript: null })
            return;

        writer.WriteRaw("<w:lang"u8);
        WordXml.Attribute(writer, "w:val"u8, format.Language);
        WordXml.Attribute(writer, "w:eastAsia"u8, format.LanguageEastAsia);
        WordXml.Attribute(writer, "w:bidi"u8, format.LanguageComplexScript);
        writer.WriteRaw("/>"u8);
    }
}
