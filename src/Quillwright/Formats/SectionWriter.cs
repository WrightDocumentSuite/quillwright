using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// The header and footer references a section emits. They come from the package layer, so
/// the section writer stays free of relationship bookkeeping.
/// </summary>
internal sealed class SectionWriteContext
{
    /// <summary>Header and footer references in the order they must be written.</summary>
    public List<(bool IsFooter, HeaderFooterKind Kind, string RelationshipId)> References { get; } = [];

    /// <summary>
    /// The kind of break which starts the following section.  A section's own
    /// <see cref="SectionProperties.Start"/> describes how that section starts in the model,
    /// while OOXML stores the value on the preceding section's <c>w:sectPr</c>.
    /// </summary>
    public SectionStart? FollowingSectionStart { get; init; }
}

/// <summary>Writes section properties (<c>w:sectPr</c>) in the order <c>CT_SectPr</c> declares.</summary>
internal static class SectionWriter
{
    /// <summary>Writes a complete <c>w:sectPr</c>.</summary>
    public static void Write(Utf8XmlWriter writer, SectionProperties properties, SectionWriteContext context)
    {
        writer.WriteRaw("<w:sectPr"u8);
        if (properties.Attributes is { } attributes)
            writer.WriteRawXml(attributes);
        writer.WriteRaw(">"u8);

        foreach ((bool isFooter, HeaderFooterKind kind, string relationshipId) in context.References)
        {
            writer.WriteRaw(isFooter ? "<w:footerReference w:type=\""u8 : "<w:headerReference w:type=\""u8);
            writer.WriteRawXml(kind switch
            {
                HeaderFooterKind.First => "first",
                HeaderFooterKind.Even => "even",
                _ => "default",
            });

            writer.WriteRaw("\" r:id=\""u8);
            writer.WriteAttributeText(relationshipId);
            writer.WriteRaw("\"/>"u8);
        }

        RawXml.Write(writer, properties.FootnotePropertiesXml);
        RawXml.Write(writer, properties.EndnotePropertiesXml);
        SectionStart start = context.FollowingSectionStart ?? properties.Start;
        if (start != SectionStart.NextPage)
            WordXml.Value(writer, "type"u8, OoxmlEnums.Name(start));

        WritePageSize(writer, properties);
        WriteMargins(writer, properties);
        RawXml.Write(writer, properties.PaperSourceXml);
        WritePageBorders(writer, properties);
        RawXml.Write(writer, properties.LineNumberingXml);
        WritePageNumbering(writer, properties);
        WriteColumns(writer, properties);
        WordXml.Toggle(writer, "formProt"u8, properties.FormProtection);
        if (properties.VerticalAlignment is { } vertical)
            WordXml.Value(writer, "vAlign"u8, OoxmlEnums.Name(vertical));
        WordXml.Toggle(writer, "noEndnote"u8, properties.SuppressEndnotes);
        if (properties.DifferentFirstPage)
            writer.WriteRaw("<w:titlePg/>"u8);
        if (properties.TextDirection is { } direction)
            WordXml.Value(writer, "textDirection"u8, OoxmlEnums.Name(direction));
        if (properties.RightToLeft)
            writer.WriteRaw("<w:bidi/>"u8);
        if (properties.RightToLeftGutter)
            writer.WriteRaw("<w:rtlGutter/>"u8);
        RawXml.Write(writer, properties.DocumentGridXml);
        RawXml.Write(writer, properties.PrinterSettingsXml);
        RawXml.Write(writer, properties.Extensions);
        RawXml.Write(writer, properties.ChangeXml);
        writer.WriteRaw("</w:sectPr>"u8);
    }

    private static void WritePageSize(Utf8XmlWriter writer, SectionProperties properties)
    {
        writer.WriteRaw("<w:pgSz"u8);
        WordXml.AttributeTwips(writer, "w:w"u8, properties.PageWidth);
        WordXml.AttributeTwips(writer, "w:h"u8, properties.PageHeight);
        if (properties.Orientation == PageOrientation.Landscape)
            WordXml.Attribute(writer, "w:orient"u8, "landscape");
        WordXml.Attribute(writer, "w:code"u8, properties.PaperCode);
        writer.WriteRaw("/>"u8);
    }

    private static void WriteMargins(Utf8XmlWriter writer, SectionProperties properties)
    {
        PageMargins margins = properties.Margins;
        writer.WriteRaw("<w:pgMar"u8);
        WordXml.AttributeTwips(writer, "w:top"u8, margins.Top);
        WordXml.AttributeTwips(writer, "w:right"u8, margins.Right);
        WordXml.AttributeTwips(writer, "w:bottom"u8, margins.Bottom);
        WordXml.AttributeTwips(writer, "w:left"u8, margins.Left);
        WordXml.AttributeTwips(writer, "w:header"u8, margins.Header);
        WordXml.AttributeTwips(writer, "w:footer"u8, margins.Footer);
        WordXml.AttributeTwips(writer, "w:gutter"u8, margins.Gutter);
        writer.WriteRaw("/>"u8);
    }

    private static void WritePageBorders(Utf8XmlWriter writer, SectionProperties properties)
    {
        if (properties.PageBorders is not { IsEmpty: false } borders)
            return;

        writer.WriteRaw("<w:pgBorders"u8);
        if (properties.PageBordersAttributes is { } attributes)
            writer.WriteRawXml(attributes);
        writer.WriteRaw(">"u8);
        SharedFormatWriter.Border(writer, "top"u8, borders.Top);
        SharedFormatWriter.Border(writer, "left"u8, borders.Left);
        SharedFormatWriter.Border(writer, "bottom"u8, borders.Bottom);
        SharedFormatWriter.Border(writer, "right"u8, borders.Right);
        writer.WriteRaw("</w:pgBorders>"u8);
    }

    private static void WritePageNumbering(Utf8XmlWriter writer, SectionProperties properties)
    {
        PageNumbering numbering = properties.PageNumbering;
        if (numbering.IsEmpty)
            return;

        writer.WriteRaw("<w:pgNumType"u8);
        if (numbering.Format is { } format)
            WordXml.Attribute(writer, "w:fmt"u8, OoxmlEnums.Name(format, numbering.CustomFormat));
        WordXml.Attribute(writer, "w:start"u8, numbering.Start);
        WordXml.Attribute(writer, "w:chapStyle"u8, numbering.ChapterStyleLevel);
        WordXml.Attribute(writer, "w:chapSep"u8, numbering.ChapterSeparator);
        writer.WriteRaw("/>"u8);
    }

    private static void WriteColumns(Utf8XmlWriter writer, SectionProperties properties)
    {
        ColumnLayout columns = properties.Columns;
        if (columns is { Count: 1, Separator: false, EqualWidth: true } && columns.Columns.Count == 0)
            return;

        writer.WriteRaw("<w:cols"u8);
        if (columns.Count > 1)
            WordXml.Attribute(writer, "w:num"u8, columns.Count);
        if (columns.Separator)
            WordXml.Attribute(writer, "w:sep"u8, true);
        if (!columns.EqualWidth && columns.Columns.Count > 0)
        {
            WordXml.Attribute(writer, "w:equalWidth"u8, false);
            writer.WriteRaw(">"u8);
            foreach (TextColumn column in columns.Columns)
            {
                writer.WriteRaw("<w:col"u8);
                WordXml.AttributeTwips(writer, "w:w"u8, column.Width);
                WordXml.AttributeTwips(writer, "w:space"u8, column.Space);
                writer.WriteRaw("/>"u8);
            }

            writer.WriteRaw("</w:cols>"u8);
            return;
        }

        WordXml.AttributeTwips(writer, "w:space"u8, columns.Space);
        writer.WriteRaw("/>"u8);
    }
}
