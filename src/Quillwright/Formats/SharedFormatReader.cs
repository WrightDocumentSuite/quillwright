using System.Globalization;
using System.Xml;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Reads the property shapes that appear in more than one place.</summary>
internal static class SharedFormatReader
{
    /// <summary>Reads a colour from the attributes of the current element, without consuming it.</summary>
    public static WordColor ReadColor(XmlReader xml, string valueAttribute)
    {
        if (XmlHelp.Attr(xml, "themeColor") is { } theme)
        {
            return WordColor.FromTheme(
                WordColor.ParseThemeSlot(theme),
                ParseHexByte(XmlHelp.Attr(xml, "themeTint")),
                ParseHexByte(XmlHelp.Attr(xml, "themeShade")));
        }

        return WordColor.Parse(XmlHelp.Attr(xml, valueAttribute) ?? "auto");
    }

    /// <summary>Reads a border edge and consumes the element.</summary>
    public static BorderLine ReadBorder(XmlReader xml)
    {
        (BorderStyle style, string? custom) = OoxmlEnums.ParseBorder(XmlHelp.Val(xml));
        var line = new BorderLine
        {
            Style = style,
            CustomStyle = custom,
            Width = Length.FromEighthPoints(XmlHelp.AttrInt(xml, "sz") ?? 0),
            Space = Length.FromPoints(XmlHelp.AttrInt(xml, "space") ?? 0),
            Color = ReadColor(xml, "color"),
            Shadow = XmlHelp.AttrBool(xml, "shadow") ?? false,
            Frame = XmlHelp.AttrBool(xml, "frame") ?? false,
        };

        xml.Skip();
        return line;
    }

    /// <summary>Reads a <c>w:shd</c> element and consumes it.</summary>
    public static Shading ReadShading(XmlReader xml)
    {
        (ShadingPattern pattern, string? custom) = OoxmlEnums.ParseShading(XmlHelp.Val(xml));
        var shading = new Shading
        {
            Pattern = pattern,
            CustomPattern = custom,
            Color = WordColor.Parse(XmlHelp.Attr(xml, "color") ?? "auto"),
            Fill = XmlHelp.Attr(xml, "themeFill") is { } themeFill
                ? WordColor.FromTheme(
                    WordColor.ParseThemeSlot(themeFill),
                    ParseHexByte(XmlHelp.Attr(xml, "themeFillTint")),
                    ParseHexByte(XmlHelp.Attr(xml, "themeFillShade")))
                : WordColor.Parse(XmlHelp.Attr(xml, "fill") ?? "auto"),
        };

        xml.Skip();
        return shading;
    }

    /// <summary>Reads a border container such as <c>w:pBdr</c> and consumes it.</summary>
    public static BorderSet ReadBorderSet(XmlReader xml)
    {
        var borders = BorderSet.Empty;
        XmlHelp.ForEachChild(xml, (reader, name) => borders = name switch
        {
            "top" => borders with { Top = ReadBorder(reader) },
            "left" or "start" => borders with { Left = ReadBorder(reader) },
            "bottom" => borders with { Bottom = ReadBorder(reader) },
            "right" or "end" => borders with { Right = ReadBorder(reader) },
            "insideH" or "between" => borders with { InsideHorizontal = ReadBorder(reader) },
            "insideV" => borders with { InsideVertical = ReadBorder(reader) },
            "tl2br" => borders with { DiagonalDown = ReadBorder(reader) },
            "tr2bl" => borders with { DiagonalUp = ReadBorder(reader) },
            "bar" => borders with { Bar = ReadBorder(reader) },
            _ => Skip(reader, borders),
        });

        return borders;
    }

    /// <summary>Reads a <c>CT_TblWidth</c> element and consumes it.</summary>
    public static TableWidth ReadWidth(XmlReader xml)
    {
        var width = new TableWidth(
            OoxmlEnums.ParseWidthUnit(XmlHelp.Attr(xml, "type")),
            ParseWidth(XmlHelp.Attr(xml, "w")) ?? 0);
        xml.Skip();
        return width;
    }

    /// <summary>
    /// Parses an <c>ST_MeasurementOrPercent</c> value (ISO/IEC 29500-1 §17.18.107) into the
    /// unit <see cref="TableWidth.Value"/> is kept in.
    /// </summary>
    /// <remarks>
    /// The type is a union of three spellings of the same width, and which one a producer used
    /// is readable from the value itself: <c>2880</c> is already twips or fiftieths of a
    /// percent, <c>50%</c> is a percentage to be scaled into fiftieths, and <c>2in</c> is a
    /// universal measure to be converted to twips.
    /// </remarks>
    internal static int? ParseWidth(string? value)
    {
        ReadOnlySpan<char> text = value.AsSpan().Trim();
        if (text.IsEmpty)
            return null;

        if (text[^1] == '%')
        {
            return double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
                ? (int)Math.Round(percent * 50, MidpointRounding.AwayFromZero)
                : null;
        }

        if (Length.HasUnit(text))
            return Length.TryParse(text, CultureInfo.InvariantCulture, out Length measure) ? measure.Twips : null;

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : null;
    }

    /// <summary>Reads a cell-margin container and consumes it.</summary>
    public static CellMargins ReadCellMargins(XmlReader xml)
    {
        var margins = CellMargins.Empty;
        XmlHelp.ForEachChild(xml, (reader, name) => margins = name switch
        {
            "top" => margins with { Top = ReadWidth(reader) },
            "left" or "start" => margins with { Left = ReadWidth(reader) },
            "bottom" => margins with { Bottom = ReadWidth(reader) },
            "right" or "end" => margins with { Right = ReadWidth(reader) },
            _ => Skip(reader, margins),
        });

        return margins;
    }

    /// <summary>Reads a <c>w:tabs</c> element and consumes it.</summary>
    public static TabStop[] ReadTabs(XmlReader xml)
    {
        var stops = new List<TabStop>();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "tab")
            {
                stops.Add(new TabStop(
                    XmlHelp.AttrTwips(reader, "pos") ?? Length.Zero,
                    OoxmlEnums.ParseTabAlignment(XmlHelp.Val(reader)),
                    OoxmlEnums.ParseTabLeader(XmlHelp.Attr(reader, "leader"))));
            }

            reader.Skip();
        });

        return [.. stops];
    }

    private static T Skip<T>(XmlReader xml, T value)
    {
        xml.Skip();
        return value;
    }

    private static byte ParseHexByte(string? value) =>
        byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte parsed) ? parsed : (byte)0;
}
