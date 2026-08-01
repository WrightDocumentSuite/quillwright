using Quillwright.Primitives;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// The property shapes that appear in more than one place: colours, borders, shading and
/// the <c>CT_TblWidth</c> pattern.
/// </summary>
internal static class SharedFormatWriter
{
    /// <summary>Writes the colour attributes of an element that already has its start tag open.</summary>
    public static void ColorAttributes(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> valueName, WordColor color)
    {
        writer.WriteRaw(" "u8);
        writer.WriteRaw(valueName);
        writer.WriteRaw("=\""u8);
        writer.WriteRawXml(color.Kind == ColorKind.Rgb ? color.ToHex() : "auto");
        writer.WriteRaw("\""u8);

        if (color.Kind != ColorKind.Theme)
            return;

        WordXml.Attribute(writer, "w:themeColor"u8, WordColor.ThemeSlotToString(color.ThemeSlot));
        if (color.ThemeTint != 0)
            WordXml.Attribute(writer, "w:themeTint"u8, color.ThemeTint.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        if (color.ThemeShade != 0)
            WordXml.Attribute(writer, "w:themeShade"u8, color.ThemeShade.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a <c>w:shd</c> element.</summary>
    public static void Shading(Utf8XmlWriter writer, Shading? shading)
    {
        if (shading is null || shading.IsEmpty)
            return;

        writer.WriteRaw("<w:shd w:val=\""u8);
        writer.WriteAttributeText(OoxmlEnums.Name(shading.Pattern, shading.CustomPattern));
        writer.WriteRaw("\""u8);
        ColorAttributes(writer, "w:color"u8, shading.Color);
        ColorAttributes(writer, "w:fill"u8, shading.Fill);
        writer.WriteRaw("/>"u8);
    }

    /// <summary>Writes one edge of a border box.</summary>
    public static void Border(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, BorderLine? line)
    {
        if (line is null)
            return;

        WordXml.Open(writer, name);
        writer.WriteRaw(" w:val=\""u8);
        writer.WriteAttributeText(OoxmlEnums.Name(line.Style, line.CustomStyle));
        writer.WriteRaw("\""u8);
        if (line.Width != Length.Zero)
            WordXml.Attribute(writer, "w:sz"u8, line.Width.EighthPoints);
        if (line.Space != Length.Zero)
            WordXml.Attribute(writer, "w:space"u8, (int)Math.Round(line.Space.Points));
        ColorAttributes(writer, "w:color"u8, line.Color);
        if (line.Shadow)
            WordXml.Attribute(writer, "w:shadow"u8, true);
        if (line.Frame)
            WordXml.Attribute(writer, "w:frame"u8, true);
        writer.WriteRaw("/>"u8);
    }

    /// <summary>Writes the border box of a paragraph (<c>w:pBdr</c>).</summary>
    public static void ParagraphBorders(Utf8XmlWriter writer, BorderSet? borders)
    {
        if (borders is null || borders.IsEmpty)
            return;

        writer.WriteRaw("<w:pBdr>"u8);
        Border(writer, "top"u8, borders.Top);
        Border(writer, "left"u8, borders.Left);
        Border(writer, "bottom"u8, borders.Bottom);
        Border(writer, "right"u8, borders.Right);
        Border(writer, "between"u8, borders.InsideHorizontal);
        Border(writer, "bar"u8, borders.Bar);
        writer.WriteRaw("</w:pBdr>"u8);
    }

    /// <summary>Writes the border box of a table (<c>w:tblBorders</c>).</summary>
    public static void TableBorders(Utf8XmlWriter writer, BorderSet? borders)
    {
        if (borders is null || borders.IsEmpty)
            return;

        writer.WriteRaw("<w:tblBorders>"u8);
        Border(writer, "top"u8, borders.Top);
        Border(writer, WordXml.Leading(writer), borders.Left);
        Border(writer, "bottom"u8, borders.Bottom);
        Border(writer, WordXml.Trailing(writer), borders.Right);
        Border(writer, "insideH"u8, borders.InsideHorizontal);
        Border(writer, "insideV"u8, borders.InsideVertical);
        writer.WriteRaw("</w:tblBorders>"u8);
    }

    /// <summary>Writes the border box of a cell (<c>w:tcBorders</c>).</summary>
    public static void CellBorders(Utf8XmlWriter writer, BorderSet? borders)
    {
        if (borders is null || borders.IsEmpty)
            return;

        writer.WriteRaw("<w:tcBorders>"u8);
        Border(writer, "top"u8, borders.Top);
        Border(writer, WordXml.Leading(writer), borders.Left);
        Border(writer, "bottom"u8, borders.Bottom);
        Border(writer, WordXml.Trailing(writer), borders.Right);
        Border(writer, "insideH"u8, borders.InsideHorizontal);
        Border(writer, "insideV"u8, borders.InsideVertical);
        Border(writer, "tl2br"u8, borders.DiagonalDown);
        Border(writer, "tr2bl"u8, borders.DiagonalUp);
        writer.WriteRaw("</w:tcBorders>"u8);
    }

    /// <summary>Writes a <c>CT_TblWidth</c> element such as <c>w:tblW</c> or <c>w:tcW</c>.</summary>
    public static void Width(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, TableWidth? width)
    {
        if (width is not { } value)
            return;

        WordXml.Open(writer, name);
        WordXml.Attribute(writer, "w:w"u8, value.Value);
        WordXml.Attribute(writer, "w:type"u8, OoxmlEnums.Name(value.Unit));
        writer.WriteRaw("/>"u8);
    }

    /// <summary>Writes a cell-margin container such as <c>w:tblCellMar</c> or <c>w:tcMar</c>.</summary>
    public static void CellMargins(Utf8XmlWriter writer, scoped ReadOnlySpan<byte> name, CellMargins? margins)
    {
        if (margins is null || margins.IsEmpty)
            return;

        WordXml.Open(writer, name);
        writer.WriteRaw(">"u8);
        Width(writer, "top"u8, margins.Top);
        Width(writer, WordXml.Leading(writer), margins.Left);
        Width(writer, "bottom"u8, margins.Bottom);
        Width(writer, WordXml.Trailing(writer), margins.Right);
        WordXml.Close(writer, name);
    }
}
