using System.Xml;
using Quillwright.Styles;

namespace Quillwright.Formats;

/// <summary>Reads table, row and cell properties.</summary>
internal static class TableFormatReader
{
    /// <summary>Reads a <c>w:tblPr</c> element, consuming it.</summary>
    public static TableFormat ReadTable(XmlReader xml)
    {
        var format = TableFormat.Default;
        XmlHelp.ForEachChild(xml, (reader, name) => format = ReadTableChild(reader, name, format));
        return format;
    }

    /// <summary>Reads one child of <c>w:tblPr</c>, consuming it.</summary>
    public static TableFormat ReadTableChild(XmlReader xml, string name, TableFormat format)
    {
        switch (name)
        {
            case "tblStyle": return Consume(xml, format with { StyleId = XmlHelp.Val(xml) });
            case "bidiVisual": return Consume(xml, format with { RightToLeft = XmlHelp.Toggle(xml) });
            case "tblStyleRowBandSize": return Consume(xml, format with { RowBandSize = XmlHelp.ValInt(xml) });
            case "tblStyleColBandSize": return Consume(xml, format with { ColumnBandSize = XmlHelp.ValInt(xml) });
            case "tblW": return format with { Width = SharedFormatReader.ReadWidth(xml) };
            case "jc": return Consume(xml, format with { Alignment = OoxmlEnums.ParseTableAlignment(XmlHelp.Val(xml)) });
            case "tblCellSpacing": return format with { CellSpacing = SharedFormatReader.ReadWidth(xml) };
            case "tblInd": return format with { Indent = SharedFormatReader.ReadWidth(xml) };
            case "tblBorders": return format with { Borders = SharedFormatReader.ReadBorderSet(xml) };
            case "shd": return format with { Shading = SharedFormatReader.ReadShading(xml) };
            case "tblLayout": return Consume(xml, format with { Layout = OoxmlEnums.ParseTableLayout(XmlHelp.Attr(xml, "type")) });
            case "tblCellMar": return format with { CellMargins = SharedFormatReader.ReadCellMargins(xml) };
            case "tblLook": return Consume(xml, format with { StyleOptions = ReadLook(xml) });
            case "tblCaption": return Consume(xml, format with { Caption = XmlHelp.Val(xml) });
            case "tblDescription": return Consume(xml, format with { Description = XmlHelp.Val(xml) });
            case "tblpPr": return format with { FloatingPositionXml = xml.ReadOuterXml() };
            case "tblOverlap": return format with { OverlapXml = xml.ReadOuterXml() };
            case "tblPrChange": return format with { ChangeXml = xml.ReadOuterXml() };
            default: return format with { Extensions = (format.Extensions ?? string.Empty) + xml.ReadOuterXml() };
        }
    }

    /// <summary>Reads a <c>w:trPr</c> element, consuming it.</summary>
    public static TableRowFormat ReadRow(XmlReader xml)
    {
        var format = TableRowFormat.Default;
        XmlHelp.ForEachChild(xml, (reader, name) => format = ReadRowChild(reader, name, format));
        return format;
    }

    /// <summary>Reads one child of <c>w:trPr</c>, consuming it.</summary>
    public static TableRowFormat ReadRowChild(XmlReader xml, string name, TableRowFormat format)
    {
        switch (name)
        {
            case "gridBefore": return Consume(xml, format with { GridBefore = XmlHelp.ValInt(xml) });
            case "gridAfter": return Consume(xml, format with { GridAfter = XmlHelp.ValInt(xml) });
            case "wBefore": return format with { WidthBefore = SharedFormatReader.ReadWidth(xml) };
            case "wAfter": return format with { WidthAfter = SharedFormatReader.ReadWidth(xml) };
            case "cantSplit": return Consume(xml, format with { CannotSplit = XmlHelp.Toggle(xml) });
            case "trHeight":
                return Consume(xml, format with
                {
                    Height = XmlHelp.ValTwips(xml),
                    HeightRule = OoxmlEnums.ParseHeightRule(XmlHelp.Attr(xml, "hRule")),
                });
            case "tblHeader": return Consume(xml, format with { IsHeader = XmlHelp.Toggle(xml) });
            case "tblCellSpacing": return format with { CellSpacing = SharedFormatReader.ReadWidth(xml) };
            case "jc": return Consume(xml, format with { Alignment = OoxmlEnums.ParseTableAlignment(XmlHelp.Val(xml)) });
            case "hidden": return Consume(xml, format with { Hidden = XmlHelp.Toggle(xml) });
            case "cnfStyle": return format with { ConditionalFormattingXml = xml.ReadOuterXml() };
            case "divId": return format with { DivIdXml = xml.ReadOuterXml() };
            case "ins": return format with { InsertedXml = xml.ReadOuterXml() };
            case "del": return format with { DeletedXml = xml.ReadOuterXml() };
            case "trPrChange": return format with { ChangeXml = xml.ReadOuterXml() };
            default: return format with { Extensions = (format.Extensions ?? string.Empty) + xml.ReadOuterXml() };
        }
    }

    /// <summary>Reads a <c>w:tcPr</c> element, consuming it.</summary>
    public static TableCellFormat ReadCell(XmlReader xml)
    {
        var format = TableCellFormat.Default;
        XmlHelp.ForEachChild(xml, (reader, name) => format = ReadCellChild(reader, name, format));
        return format;
    }

    /// <summary>Reads one child of <c>w:tcPr</c>, consuming it.</summary>
    public static TableCellFormat ReadCellChild(XmlReader xml, string name, TableCellFormat format)
    {
        switch (name)
        {
            case "tcW": return format with { Width = SharedFormatReader.ReadWidth(xml) };
            case "gridSpan": return Consume(xml, format with { GridSpan = XmlHelp.ValInt(xml) });
            case "vMerge":
                return Consume(xml, format with
                {
                    VerticalMerge = XmlHelp.Val(xml) == "restart" ? VerticalMerge.Restart : VerticalMerge.Continue,
                });
            case "tcBorders": return format with { Borders = SharedFormatReader.ReadBorderSet(xml) };
            case "shd": return format with { Shading = SharedFormatReader.ReadShading(xml) };
            case "noWrap": return Consume(xml, format with { NoWrap = XmlHelp.Toggle(xml) });
            case "tcMar": return format with { Margins = SharedFormatReader.ReadCellMargins(xml) };
            case "textDirection": return Consume(xml, format with { TextDirection = OoxmlEnums.ParseTextDirection(XmlHelp.Val(xml)) });
            case "tcFitText": return Consume(xml, format with { FitText = XmlHelp.Toggle(xml) });
            case "vAlign": return Consume(xml, format with { VerticalAlignment = OoxmlEnums.ParseCellAlign(XmlHelp.Val(xml)) });
            case "hideMark": return Consume(xml, format with { HideMark = XmlHelp.Toggle(xml) });
            case "cnfStyle": return format with { ConditionalFormattingXml = xml.ReadOuterXml() };
            case "hMerge": return format with { HorizontalMergeXml = xml.ReadOuterXml() };
            case "headers": return format with { HeadersXml = xml.ReadOuterXml() };
            case "cellIns" or "cellDel" or "cellMerge" or "tcPrChange":
                return format with { RevisionXml = (format.RevisionXml ?? string.Empty) + xml.ReadOuterXml() };
            default: return format with { Extensions = (format.Extensions ?? string.Empty) + xml.ReadOuterXml() };
        }
    }

    private static TableStyleOptions ReadLook(XmlReader xml)
    {
        // Word wrote the flags as a hex bitmask before 2010 and as attributes since.
        if (XmlHelp.Val(xml) is { } packed &&
            int.TryParse(packed, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int mask) &&
            XmlHelp.Attr(xml, "firstRow") is null)
        {
            var fromMask = TableStyleOptions.None;
            if ((mask & 0x0020) != 0) fromMask |= TableStyleOptions.FirstRow;
            if ((mask & 0x0040) != 0) fromMask |= TableStyleOptions.LastRow;
            if ((mask & 0x0080) != 0) fromMask |= TableStyleOptions.FirstColumn;
            if ((mask & 0x0100) != 0) fromMask |= TableStyleOptions.LastColumn;
            if ((mask & 0x0200) != 0) fromMask |= TableStyleOptions.NoHorizontalBanding;
            if ((mask & 0x0400) != 0) fromMask |= TableStyleOptions.NoVerticalBanding;
            return fromMask;
        }

        var options = TableStyleOptions.None;
        if (XmlHelp.AttrBool(xml, "firstRow") == true) options |= TableStyleOptions.FirstRow;
        if (XmlHelp.AttrBool(xml, "lastRow") == true) options |= TableStyleOptions.LastRow;
        if (XmlHelp.AttrBool(xml, "firstColumn") == true) options |= TableStyleOptions.FirstColumn;
        if (XmlHelp.AttrBool(xml, "lastColumn") == true) options |= TableStyleOptions.LastColumn;
        if (XmlHelp.AttrBool(xml, "noHBand") == true) options |= TableStyleOptions.NoHorizontalBanding;
        if (XmlHelp.AttrBool(xml, "noVBand") == true) options |= TableStyleOptions.NoVerticalBanding;
        return options;
    }

    private static T Consume<T>(XmlReader xml, T value)
    {
        xml.Skip();
        return value;
    }
}
