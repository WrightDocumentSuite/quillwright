using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>Writes table, row and cell properties in their schema order.</summary>
internal static class TableFormatWriter
{
    /// <summary>Writes a complete <c>w:tblPr</c>.</summary>
    public static void WriteTable(Utf8XmlWriter writer, TableFormat format)
    {
        writer.WriteRaw("<w:tblPr>"u8);
        WriteTableBody(writer, format);
        RawXml.Write(writer, format.ChangeXml);
        writer.WriteRaw("</w:tblPr>"u8);
    }

    /// <summary>Writes the children of <c>w:tblPr</c> that a table style also uses.</summary>
    public static void WriteTableBody(Utf8XmlWriter writer, TableFormat format)
    {
        WordXml.Value(writer, "tblStyle"u8, format.StyleId);
        RawXml.Write(writer, format.FloatingPositionXml);
        RawXml.Write(writer, format.OverlapXml);
        WordXml.Toggle(writer, "bidiVisual"u8, format.RightToLeft);
        WordXml.Value(writer, "tblStyleRowBandSize"u8, format.RowBandSize);
        WordXml.Value(writer, "tblStyleColBandSize"u8, format.ColumnBandSize);
        SharedFormatWriter.Width(writer, "tblW"u8, format.Width);
        if (format.Alignment is { } alignment)
            WordXml.Value(writer, "jc"u8, OoxmlEnums.Name(alignment, writer.Strict));
        SharedFormatWriter.Width(writer, "tblCellSpacing"u8, format.CellSpacing);
        SharedFormatWriter.Width(writer, "tblInd"u8, format.Indent);
        SharedFormatWriter.TableBorders(writer, format.Borders);
        SharedFormatWriter.Shading(writer, format.Shading);
        if (format.Layout is { } layout)
        {
            writer.WriteRaw("<w:tblLayout w:type=\""u8);
            writer.WriteAttributeText(OoxmlEnums.Name(layout));
            writer.WriteRaw("\"/>"u8);
        }

        SharedFormatWriter.CellMargins(writer, "tblCellMar"u8, format.CellMargins);
        WriteLook(writer, format.StyleOptions);
        WordXml.Value(writer, "tblCaption"u8, format.Caption);
        WordXml.Value(writer, "tblDescription"u8, format.Description);
        RawXml.Write(writer, format.Extensions);
    }

    /// <summary>Writes a complete <c>w:trPr</c>, or nothing when the format overrides nothing.</summary>
    public static void WriteRow(Utf8XmlWriter writer, TableRowFormat format)
    {
        if (format.IsEmpty)
            return;

        writer.WriteRaw("<w:trPr>"u8);
        WriteRowBody(writer, format);
        RawXml.Write(writer, format.InsertedXml);
        RawXml.Write(writer, format.DeletedXml);
        RawXml.Write(writer, format.ChangeXml);
        writer.WriteRaw("</w:trPr>"u8);
    }

    /// <summary>Writes the children of <c>w:trPr</c> that a table style also uses.</summary>
    public static void WriteRowBody(Utf8XmlWriter writer, TableRowFormat format)
    {
        RawXml.Write(writer, format.ConditionalFormattingXml);
        RawXml.Write(writer, format.DivIdXml);
        WordXml.Value(writer, "gridBefore"u8, format.GridBefore);
        WordXml.Value(writer, "gridAfter"u8, format.GridAfter);
        SharedFormatWriter.Width(writer, "wBefore"u8, format.WidthBefore);
        SharedFormatWriter.Width(writer, "wAfter"u8, format.WidthAfter);
        WordXml.Toggle(writer, "cantSplit"u8, format.CannotSplit);
        if (format.Height is { } height)
        {
            writer.WriteRaw("<w:trHeight"u8);
            WordXml.AttributeTwips(writer, "w:val"u8, height);
            if (format.HeightRule is { } rule)
                WordXml.Attribute(writer, "w:hRule"u8, OoxmlEnums.Name(rule));
            writer.WriteRaw("/>"u8);
        }

        WordXml.Toggle(writer, "tblHeader"u8, format.IsHeader);
        SharedFormatWriter.Width(writer, "tblCellSpacing"u8, format.CellSpacing);
        if (format.Alignment is { } alignment)
            WordXml.Value(writer, "jc"u8, OoxmlEnums.Name(alignment, writer.Strict));
        WordXml.Toggle(writer, "hidden"u8, format.Hidden);
        RawXml.Write(writer, format.Extensions);
    }

    /// <summary>Writes a complete <c>w:tcPr</c>.</summary>
    public static void WriteCell(Utf8XmlWriter writer, TableCellFormat format)
    {
        writer.WriteRaw("<w:tcPr>"u8);
        WriteCellBody(writer, format);
        RawXml.Write(writer, format.HeadersXml);
        RawXml.Write(writer, format.RevisionXml);
        writer.WriteRaw("</w:tcPr>"u8);
    }

    /// <summary>Writes the children of <c>w:tcPr</c> that a table style also uses.</summary>
    public static void WriteCellBody(Utf8XmlWriter writer, TableCellFormat format)
    {
        RawXml.Write(writer, format.ConditionalFormattingXml);
        SharedFormatWriter.Width(writer, "tcW"u8, format.Width);
        WordXml.Value(writer, "gridSpan"u8, format.GridSpan);
        RawXml.Write(writer, format.HorizontalMergeXml);
        if (format.VerticalMerge is { } merge)
        {
            writer.WriteRaw(merge == VerticalMerge.Restart
                ? "<w:vMerge w:val=\"restart\"/>"u8
                : "<w:vMerge/>"u8);
        }

        SharedFormatWriter.CellBorders(writer, format.Borders);
        SharedFormatWriter.Shading(writer, format.Shading);
        WordXml.Toggle(writer, "noWrap"u8, format.NoWrap);
        SharedFormatWriter.CellMargins(writer, "tcMar"u8, format.Margins);
        if (format.TextDirection is { } direction)
            WordXml.Value(writer, "textDirection"u8, OoxmlEnums.Name(direction));
        WordXml.Toggle(writer, "tcFitText"u8, format.FitText);
        if (format.VerticalAlignment is { } vertical)
            WordXml.Value(writer, "vAlign"u8, OoxmlEnums.Name(vertical));
        WordXml.Toggle(writer, "hideMark"u8, format.HideMark);
        RawXml.Write(writer, format.Extensions);
    }

    private static void WriteLook(Utf8XmlWriter writer, TableStyleOptions? options)
    {
        if (options is not { } value)
            return;

        writer.WriteRaw("<w:tblLook"u8);
        WordXml.Attribute(writer, "w:firstRow"u8, value.HasFlag(TableStyleOptions.FirstRow));
        WordXml.Attribute(writer, "w:lastRow"u8, value.HasFlag(TableStyleOptions.LastRow));
        WordXml.Attribute(writer, "w:firstColumn"u8, value.HasFlag(TableStyleOptions.FirstColumn));
        WordXml.Attribute(writer, "w:lastColumn"u8, value.HasFlag(TableStyleOptions.LastColumn));
        WordXml.Attribute(writer, "w:noHBand"u8, value.HasFlag(TableStyleOptions.NoHorizontalBanding));
        WordXml.Attribute(writer, "w:noVBand"u8, value.HasFlag(TableStyleOptions.NoVerticalBanding));
        writer.WriteRaw("/>"u8);
    }
}
