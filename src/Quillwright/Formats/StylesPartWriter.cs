using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>Writes the styles part (<c>styles.xml</c>).</summary>
internal static class StylesPartWriter
{
    /// <summary>Writes the whole part.</summary>
    public static void Write(Utf8XmlWriter writer, StyleSheet styles)
    {
        WordXml.OpenRoot(writer, "styles"u8, styles.Attributes);
        WriteDefaults(writer, styles);
        RawXml.Write(writer, styles.LatentStylesXml);

        foreach (Style style in styles.All.OrderBy(static s => s.Kind).ThenBy(static s => s.Id, StringComparer.Ordinal))
            WriteStyle(writer, style);

        writer.WriteRaw("</w:styles>"u8);
    }

    private static void WriteDefaults(Utf8XmlWriter writer, StyleSheet styles)
    {
        writer.WriteRaw("<w:docDefaults><w:rPrDefault>"u8);
        RunFormatWriter.Write(writer, styles.DefaultRunFormat);
        writer.WriteRaw("</w:rPrDefault><w:pPrDefault>"u8);
        if (!styles.DefaultParagraphFormat.IsEmpty)
        {
            writer.WriteRaw("<w:pPr>"u8);
            ParagraphFormatWriter.WriteBody(writer, styles.DefaultParagraphFormat);
            writer.WriteRaw("</w:pPr>"u8);
        }

        writer.WriteRaw("</w:pPrDefault></w:docDefaults>"u8);
    }

    private static void WriteStyle(Utf8XmlWriter writer, Style style)
    {
        writer.WriteRaw("<w:style"u8);
        WordXml.Attribute(writer, "w:type"u8, OoxmlEnums.Name(style.Kind));
        if (style.IsDefault)
            WordXml.Attribute(writer, "w:default"u8, true);
        if (style.IsCustom)
            WordXml.Attribute(writer, "w:customStyle"u8, true);
        WordXml.Attribute(writer, "w:styleId"u8, style.Id);
        if (style.Attributes is { } attributes)
            writer.WriteRawXml(attributes);
        writer.WriteRaw(">"u8);

        WordXml.Value(writer, "name"u8, style.Name ?? style.Id);
        WordXml.Value(writer, "aliases"u8, style.Aliases);
        WordXml.Value(writer, "basedOn"u8, style.BasedOn);
        WordXml.Value(writer, "next"u8, style.NextStyle);
        WordXml.Value(writer, "link"u8, style.LinkedStyle);
        if (style.AutoRedefine)
            writer.WriteRaw("<w:autoRedefine/>"u8);
        if (style.Hidden)
            writer.WriteRaw("<w:hidden/>"u8);
        WordXml.Value(writer, "uiPriority"u8, style.Priority);
        if (style.SemiHidden)
            writer.WriteRaw("<w:semiHidden/>"u8);
        if (style.UnhideWhenUsed)
            writer.WriteRaw("<w:unhideWhenUsed/>"u8);
        if (style.QuickFormat)
            writer.WriteRaw("<w:qFormat/>"u8);
        if (style.Locked)
            writer.WriteRaw("<w:locked/>"u8);
        if (style.Personal)
            writer.WriteRaw("<w:personal/>"u8);
        if (style.PersonalCompose)
            writer.WriteRaw("<w:personalCompose/>"u8);
        if (style.PersonalReply)
            writer.WriteRaw("<w:personalReply/>"u8);
        RawXml.Write(writer, style.RsidXml);
        RawXml.Write(writer, style.Extensions);

        if (style.Kind == StyleKind.Numbering && style.NumberingId is { } numberingId)
        {
            writer.WriteRaw("<w:pPr><w:numPr>"u8);
            WordXml.Value(writer, "numId"u8, numberingId);
            writer.WriteRaw("</w:numPr></w:pPr>"u8);
        }
        else if (!style.ParagraphFormat.IsEmpty)
        {
            writer.WriteRaw("<w:pPr>"u8);
            ParagraphFormatWriter.WriteBody(writer, style.ParagraphFormat);
            writer.WriteRaw("</w:pPr>"u8);
        }

        RunFormatWriter.Write(writer, style.RunFormat);

        if (style.Kind == StyleKind.Table)
        {
            if (!style.TableFormat.IsEmpty)
                TableFormatWriter.WriteTable(writer, style.TableFormat);
            if (!style.RowFormat.IsEmpty)
                TableFormatWriter.WriteRow(writer, style.RowFormat);
            if (!style.CellFormat.IsEmpty)
                TableFormatWriter.WriteCell(writer, style.CellFormat);
            foreach (ConditionalTableStyle conditional in style.ConditionalFormats)
                WriteConditional(writer, conditional);
        }

        writer.WriteRaw("</w:style>"u8);
    }

    private static void WriteConditional(Utf8XmlWriter writer, ConditionalTableStyle conditional)
    {
        writer.WriteRaw("<w:tblStylePr"u8);
        WordXml.Attribute(writer, "w:type"u8, OoxmlEnums.Name(conditional.Region));
        writer.WriteRaw(">"u8);

        if (!conditional.ParagraphFormat.IsEmpty)
        {
            writer.WriteRaw("<w:pPr>"u8);
            ParagraphFormatWriter.WriteBody(writer, conditional.ParagraphFormat);
            writer.WriteRaw("</w:pPr>"u8);
        }

        RunFormatWriter.Write(writer, conditional.RunFormat);
        if (!conditional.TableFormat.IsEmpty)
        {
            writer.WriteRaw("<w:tblPr>"u8);
            TableFormatWriter.WriteTableBody(writer, conditional.TableFormat);
            writer.WriteRaw("</w:tblPr>"u8);
        }

        if (!conditional.RowFormat.IsEmpty)
            TableFormatWriter.WriteRow(writer, conditional.RowFormat);
        if (!conditional.CellFormat.IsEmpty)
            TableFormatWriter.WriteCell(writer, conditional.CellFormat);
        writer.WriteRaw("</w:tblStylePr>"u8);
    }
}
