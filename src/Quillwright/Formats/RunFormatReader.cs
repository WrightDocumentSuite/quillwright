using System.Xml;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Reads character formatting (<c>w:rPr</c>).</summary>
internal static class RunFormatReader
{
    /// <summary>Reads a <c>w:rPr</c> element, consuming it.</summary>
    public static RunFormat Read(XmlReader xml)
    {
        var format = RunFormat.Default;
        XmlHelp.ForEachChild(xml, (reader, name) => format = ReadChild(reader, name, format));
        return format;
    }

    private static RunFormat ReadChild(XmlReader xml, string name, RunFormat format)
    {
        switch (name)
        {
            case "rStyle": return Consume(xml, format with { StyleId = XmlHelp.Val(xml) });
            case "rFonts": return ReadFonts(xml, format);
            case "b": return Consume(xml, format with { Bold = XmlHelp.Toggle(xml) });
            case "bCs": return Consume(xml, format with { BoldComplexScript = XmlHelp.Toggle(xml) });
            case "i": return Consume(xml, format with { Italic = XmlHelp.Toggle(xml) });
            case "iCs": return Consume(xml, format with { ItalicComplexScript = XmlHelp.Toggle(xml) });
            case "caps": return Consume(xml, format with { Caps = XmlHelp.Toggle(xml) });
            case "smallCaps": return Consume(xml, format with { SmallCaps = XmlHelp.Toggle(xml) });
            case "strike": return Consume(xml, format with { Strike = XmlHelp.Toggle(xml) });
            case "dstrike": return Consume(xml, format with { DoubleStrike = XmlHelp.Toggle(xml) });
            case "outline": return Consume(xml, format with { Outline = XmlHelp.Toggle(xml) });
            case "shadow": return Consume(xml, format with { Shadow = XmlHelp.Toggle(xml) });
            case "emboss": return Consume(xml, format with { Emboss = XmlHelp.Toggle(xml) });
            case "imprint": return Consume(xml, format with { Imprint = XmlHelp.Toggle(xml) });
            case "noProof": return Consume(xml, format with { NoProof = XmlHelp.Toggle(xml) });
            case "snapToGrid": return Consume(xml, format with { SnapToGrid = XmlHelp.Toggle(xml) });
            case "vanish": return Consume(xml, format with { Hidden = XmlHelp.Toggle(xml) });
            case "webHidden": return Consume(xml, format with { WebHidden = XmlHelp.Toggle(xml) });
            case "color": return Consume(xml, format with { Color = SharedFormatReader.ReadColor(xml, "val") });
            case "spacing": return Consume(xml, format with { CharacterSpacing = XmlHelp.ValTwips(xml) });
            case "w": return Consume(xml, format with { Scale = ParsePercent(XmlHelp.Val(xml)) });
            case "kern": return Consume(xml, format with { Kerning = XmlHelp.ValHalfPoints(xml) });
            case "position": return Consume(xml, format with { Position = XmlHelp.ValHalfPoints(xml) });
            case "sz": return Consume(xml, format with { Size = XmlHelp.ValHalfPoints(xml) });
            case "szCs": return Consume(xml, format with { SizeComplexScript = XmlHelp.ValHalfPoints(xml) });
            case "highlight": return Consume(xml, format with { Highlight = OoxmlEnums.ParseHighlight(XmlHelp.Val(xml)) });
            case "u": return ReadUnderline(xml, format);
            case "bdr": return format with { Border = SharedFormatReader.ReadBorder(xml) };
            case "shd": return format with { Shading = SharedFormatReader.ReadShading(xml) };
            case "vertAlign": return Consume(xml, format with { VerticalAlignment = OoxmlEnums.ParseVerticalAlign(XmlHelp.Val(xml)) });
            case "rtl": return Consume(xml, format with { RightToLeft = XmlHelp.Toggle(xml) });
            case "cs": return Consume(xml, format with { ComplexScript = XmlHelp.Toggle(xml) });
            case "lang": return ReadLanguage(xml, format);
            case "specVanish": return Consume(xml, format with { SpecialHidden = XmlHelp.Toggle(xml) });
            case "oMath": return Consume(xml, format with { OfficeMath = XmlHelp.Toggle(xml) });

            // Elements kept verbatim in their own slot so that preserving them does not
            // disturb the order the schema demands.
            case "ins" or "del" or "moveFrom" or "moveTo":
                return format with { MarkRevisionXml = (format.MarkRevisionXml ?? string.Empty) + xml.ReadOuterXml() };
            case "rPrChange": return format with { ChangeXml = xml.ReadOuterXml() };
            case "effect": return format with { EffectXml = xml.ReadOuterXml() };
            case "fitText": return format with { FitTextXml = xml.ReadOuterXml() };
            case "em": return format with { EmphasisXml = xml.ReadOuterXml() };
            case "eastAsianLayout": return format with { EastAsianLayoutXml = xml.ReadOuterXml() };
            default: return format with { Extensions = (format.Extensions ?? string.Empty) + xml.ReadOuterXml() };
        }
    }

    private static RunFormat Consume(XmlReader xml, RunFormat format)
    {
        xml.Skip();
        return format;
    }

    private static RunFormat ReadFonts(XmlReader xml, RunFormat format)
    {
        RunFormat result = format with
        {
            FontHint = XmlHelp.Attr(xml, "hint") ?? format.FontHint,
            FontAscii = XmlHelp.Attr(xml, "ascii") ?? format.FontAscii,
            FontHighAnsi = XmlHelp.Attr(xml, "hAnsi") ?? format.FontHighAnsi,
            FontEastAsia = XmlHelp.Attr(xml, "eastAsia") ?? format.FontEastAsia,
            FontComplexScript = XmlHelp.Attr(xml, "cs") ?? format.FontComplexScript,
            FontAsciiTheme = XmlHelp.Attr(xml, "asciiTheme") ?? format.FontAsciiTheme,
            FontHighAnsiTheme = XmlHelp.Attr(xml, "hAnsiTheme") ?? format.FontHighAnsiTheme,
            FontEastAsiaTheme = XmlHelp.Attr(xml, "eastAsiaTheme") ?? format.FontEastAsiaTheme,
            FontComplexScriptTheme = XmlHelp.Attr(xml, "cstheme") ?? format.FontComplexScriptTheme,
        };

        xml.Skip();
        return result;
    }

    private static RunFormat ReadUnderline(XmlReader xml, RunFormat format)
    {
        UnderlineStyle? style = OoxmlEnums.ParseUnderline(XmlHelp.Val(xml));
        WordColor? color = XmlHelp.Attr(xml, "color") is null && XmlHelp.Attr(xml, "themeColor") is null
            ? null
            : SharedFormatReader.ReadColor(xml, "color");
        xml.Skip();
        return format with { Underline = style, UnderlineColor = color };
    }

    private static RunFormat ReadLanguage(XmlReader xml, RunFormat format)
    {
        RunFormat result = format with
        {
            Language = XmlHelp.Attr(xml, "val") ?? format.Language,
            LanguageEastAsia = XmlHelp.Attr(xml, "eastAsia") ?? format.LanguageEastAsia,
            LanguageComplexScript = XmlHelp.Attr(xml, "bidi") ?? format.LanguageComplexScript,
        };

        xml.Skip();
        return result;
    }

    private static int? ParsePercent(string? value)
    {
        if (value is null)
            return null;
        ReadOnlySpan<char> span = value.AsSpan().TrimEnd('%');
        return int.TryParse(span, System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }
}
