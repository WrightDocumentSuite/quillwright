using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>What the body writer needs from the package layer to turn model references into markup.</summary>
internal sealed class BodyWriteContext
{
    /// <summary>Returns the relationship id of the part holding a picture's image.</summary>
    public required Func<Picture, string?> ResolvePicture { get; init; }

    /// <summary>Returns the relationship id of a hyperlink's external target.</summary>
    public required Func<Hyperlink, string?> ResolveHyperlink { get; init; }

    /// <summary>Supplies the section properties of the paragraph currently being written.</summary>
    public Func<Paragraph, (SectionProperties Properties, SectionWriteContext Context)?>? SectionBreakAt { get; init; }
}

/// <summary>
/// Writes blocks — paragraphs and tables — into a part.
/// </summary>
/// <remarks>
/// Turning a paragraph back into markup is the inverse of how it is stored: the text is one
/// buffer, and runs, wrappers and marks are offsets over it, so writing walks the offsets in
/// order and opens or closes elements at every boundary. Wrappers are kept on a stack, which
/// is what re-creates the nesting WordprocessingML requires from a flat set of ranges.
/// </remarks>
internal static class BodyWriter
{
    /// <summary>The width a table with no stated one is laid out across: a page less its margins.</summary>
    private static readonly Length DefaultTableWidth = Length.FromTwips(9360);

    /// <summary>Writes a sequence of blocks.</summary>
    public static void WriteBlocks(Utf8XmlWriter writer, IEnumerable<Block> blocks, BodyWriteContext context)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    WriteParagraph(writer, paragraph, context);
                    break;
                case Table table:
                    WriteTable(writer, table, context);
                    break;
                case BlockContentControl control:
                    WriteContentControl(writer, control, context);
                    break;
                case AlternateContentBlock alternate:
                    // The branches that were not selected come back as the bytes they arrived
                    // as, so only the one this version modelled reflects an edit.
                    writer.WriteRawXml(alternate.Prefix);
                    WriteBlocks(writer, alternate.Blocks, context);
                    writer.WriteRawXml(alternate.Suffix);
                    break;
                case RawBlock raw:
                    writer.WriteRawXml(raw.Xml);
                    break;
            }
        }
    }

    /// <summary>Writes a block-level structured document tag around its content.</summary>
    public static void WriteContentControl(Utf8XmlWriter writer, BlockContentControl control, BodyWriteContext context)
    {
        writer.WriteRaw("<w:sdt>"u8);
        if (control.PropertiesXml is { } properties)
        {
            writer.WriteRawXml(properties);
        }
        else
        {
            writer.WriteRaw("<w:sdtPr>"u8);
            WordXml.Value(writer, "alias"u8, control.Alias);
            WordXml.Value(writer, "tag"u8, control.Tag);
            WordXml.Value(writer, "id"u8, control.Id);
            writer.WriteRaw("</w:sdtPr>"u8);
        }

        RawXml.Write(writer, control.EndPropertiesXml);
        writer.WriteRaw("<w:sdtContent>"u8);
        WriteBlocks(writer, control.Blocks, context);
        writer.WriteRaw("</w:sdtContent></w:sdt>"u8);
    }

    /// <summary>Writes one paragraph, including its properties and any section break it carries.</summary>
    public static void WriteParagraph(Utf8XmlWriter writer, Paragraph paragraph, BodyWriteContext context)
    {
        writer.WriteRaw("<w:p"u8);
        if (paragraph.Attributes is { } attributes)
            writer.WriteRawXml(attributes);
        writer.WriteRaw(">"u8);

        (SectionProperties Properties, SectionWriteContext Context)? section = context.SectionBreakAt?.Invoke(paragraph);
        ParagraphFormatWriter.Write(writer, paragraph.Format, paragraph.MarkFormat, section?.Properties, section?.Context);
        WriteContent(writer, paragraph, context);
        writer.WriteRaw("</w:p>"u8);
    }

    /// <summary>Writes one table.</summary>
    public static void WriteTable(Utf8XmlWriter writer, Table table, BodyWriteContext context)
    {
        writer.WriteRaw("<w:tbl"u8);
        if (table.Attributes is { } attributes)
            writer.WriteRawXml(attributes);
        writer.WriteRaw(">"u8);
        TableFormatWriter.WriteTable(writer, table.Format);

        writer.WriteRaw("<w:tblGrid>"u8);
        foreach (Length width in GridColumns(table))
        {
            writer.WriteRaw("<w:gridCol"u8);
            WordXml.AttributeTwips(writer, "w:w"u8, width);
            writer.WriteRaw("/>"u8);
        }

        RawXml.Write(writer, table.GridChangeXml);
        writer.WriteRaw("</w:tblGrid>"u8);

        foreach (TableRow row in table.Rows)
        {
            writer.WriteRaw("<w:tr"u8);
            if (row.Attributes is { } rowAttributes)
                writer.WriteRawXml(rowAttributes);
            writer.WriteRaw(">"u8);

            // The schema puts a row's overrides of the table's formatting before its own.
            RawXml.Write(writer, row.PropertyExceptionsXml);
            TableFormatWriter.WriteRow(writer, row.Format);

            foreach (TableCell cell in row.Cells)
            {
                writer.WriteRaw("<w:tc"u8);
                if (cell.Attributes is { } cellAttributes)
                    writer.WriteRawXml(cellAttributes);
                writer.WriteRaw(">"u8);
                TableFormatWriter.WriteCell(writer, cell.Format);

                // A cell must end with a paragraph; an empty one would make Word repair the file.
                if (cell.Blocks.Count == 0 || cell.Blocks[^1] is not Paragraph)
                    cell.Blocks.Add(new Paragraph());
                WriteBlocks(writer, cell.Blocks, context);
                writer.WriteRaw("</w:tc>"u8);
            }

            RawXml.Write(writer, row.PreservedXml);
            writer.WriteRaw("</w:tr>"u8);
        }

        RawXml.Write(writer, table.PreservedXml);
        writer.WriteRaw("</w:tbl>"u8);
    }

    /// <summary>
    /// The column widths to declare before the rows.
    /// </summary>
    /// <remarks>
    /// The schema requires a grid, and a table built through the API often has none of its
    /// own. Declaring the columns unsized is legal but useless: Word lays the table out from
    /// the grid, and a grid of zero-width columns draws as a hairline. The widths are
    /// therefore taken from the cells of the widest row where they are known, and the rest of
    /// a page's usable width is split evenly between the columns that are not.
    /// </remarks>
    private static IEnumerable<Length> GridColumns(Table table)
    {
        if (table.Grid.Count > 0)
            return table.Grid;

        int columns = table.ColumnCount;
        if (columns == 0)
            return [];

        List<Length?> declared = DeclaredWidths(table, columns);
        int known = declared.Count(static width => width is not null);
        Length remaining = DefaultTableWidth - declared.Aggregate(Length.Zero, static (total, width) => total + (width ?? Length.Zero));
        Length share = known == columns ? Length.Zero : remaining / (columns - known);

        return declared.Select(width => width ?? Length.FromTwips(Math.Max(share.Twips, 0)));
    }

    /// <summary>The width each grid column is given by a cell, where a cell declares one.</summary>
    private static List<Length?> DeclaredWidths(Table table, int columns)
    {
        var widths = new List<Length?>(new Length?[columns]);
        TableRow? widest = table.Rows.MaxBy(static row => row.Cells.Sum(static cell => cell.Format.GridSpan ?? 1));
        if (widest is null)
            return widths;

        int index = 0;
        foreach (TableCell cell in widest.Cells)
        {
            int span = Math.Max(1, cell.Format.GridSpan ?? 1);
            if (cell.Format.Width is { Unit: WidthUnit.Twips } width && index + span <= columns)
            {
                for (int i = 0; i < span; i++)
                    widths[index + i] = width.Length / span;
            }

            index += span;
        }

        return widths;
    }

    private static void WriteContent(Utf8XmlWriter writer, Paragraph paragraph, BodyWriteContext context)
    {
        var emitter = new ParagraphEmitter(writer, paragraph, context);
        emitter.Emit();
    }
}
