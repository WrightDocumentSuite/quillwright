using System.Buffers.Binary;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Writes a table as the run of marked paragraphs the binary format stores it as.
/// </summary>
/// <remarks>
/// There is no table element. A cell is a group of paragraphs whose last mark is a cell
/// mark, a row is a group of cells followed by a paragraph whose mark carries the row's
/// properties, and the table is however many of those rows happen to be adjacent. At depth
/// one the marks are the cell character; deeper in, they are ordinary paragraph marks
/// distinguished by a property, because a nested table's marks have to survive being read as
/// content of the cell that contains it.
/// </remarks>
internal static class TableWriter
{
    private const int MaximumColumns = 63;
    private const int ShadedCells = 22;
    private static readonly Length DefaultTableWidth = Length.FromTwips(9360);

    /// <summary>Writes every row of a table at the given depth.</summary>
    public static void Write(DocWriteContext context, StoryAssembler story, Table table, int depth)
    {
        if (table.Rows.Count == 0)
            return;

        bool nested = depth > 1;
        foreach (TableRow row in table.Rows)
        {
            foreach (TableCell cell in row.Cells)
                WriteCell(context, story, cell, depth, nested);

            WriteRowMark(context, story, table, row, depth, nested);
        }
    }

    private static void WriteCell(DocWriteContext context, StoryAssembler story, TableCell cell, int depth, bool nested)
    {
        List<Block> blocks = [.. cell.Blocks];
        if (blocks.Count == 0 || blocks[^1] is not Paragraph)
            blocks.Add(new Paragraph());

        for (int i = 0; i < blocks.Count; i++)
        {
            bool last = i == blocks.Count - 1;
            switch (blocks[i])
            {
                case Paragraph paragraph when last:
                    story.WriteParagraph(
                        paragraph,
                        depth,
                        nested ? DocChar.ParagraphMark : DocChar.CellMark,
                        new DocParagraphFlags(true, false, depth, EndsInnerCell: nested));
                    break;
                case Paragraph paragraph:
                    story.WriteParagraph(paragraph, depth, DocChar.ParagraphMark, new DocParagraphFlags(true, false, depth));
                    break;
                case Table inner:
                    Write(context, story, inner, depth + 1);
                    break;
                case BlockContentControl control:
                    foreach (Block block in control.Blocks)
                    {
                        if (block is Paragraph nestedParagraph)
                            story.WriteParagraph(nestedParagraph, depth, DocChar.ParagraphMark, new DocParagraphFlags(true, false, depth));
                    }

                    break;
            }
        }
    }

    /// <summary>Writes the paragraph whose mark ends a row and carries the row's properties.</summary>
    private static void WriteRowMark(DocWriteContext context, StoryAssembler story, Table table, TableRow row, int depth, bool nested)
    {
        var mark = new Paragraph();
        var extra = new GrpprlWriter();

        extra.Int16(SprmCode.TableIndent, 0);
        extra.Int16(SprmCode.TableGapHalf, 108);

        if (row.Format.IsHeader == true)
            extra.Toggle(SprmCode.TableHeaderRow, true);
        if (row.Format.CannotSplit == true)
            extra.Toggle(SprmCode.TableCannotSplit, true);
        if (row.Format.Height is { } height && height != Length.Zero)
            extra.Int16(SprmCode.TableRowHeight, (short)Math.Clamp(height.Twips, -31680, 31680));

        extra.Variable(SprmCode.TableDefinition, Definition(table, row));
        if (Shading(row) is { Length: > 0 } shading)
            extra.Variable(SprmCode.TableShading, shading);

        story.WriteParagraph(
            mark,
            depth,
            nested ? DocChar.ParagraphMark : DocChar.CellMark,
            new DocParagraphFlags(true, IsRowEnd: !nested, depth, EndsInnerRow: nested));

        story.AppendToLastParagraph(extra.ToArray());
        _ = context;
    }

    /// <summary>
    /// Builds the backgrounds of a row's cells, or nothing when none of them is shaded. The
    /// modifier covers the first twenty-two cells; the rest have modifiers of their own that
    /// are not written here.
    /// </summary>
    private static byte[] Shading(TableRow row)
    {
        List<TableCell> cells = [.. row.Cells.Take(ShadedCells)];
        if (!cells.Any(static cell => cell.Format.Shading is { IsEmpty: false }))
            return [];

        var bytes = new byte[cells.Count * DocShapes.ShadingBytes];
        for (int i = 0; i < cells.Count; i++)
            DocShapes.WriteShading(bytes.AsSpan(i * DocShapes.ShadingBytes), cells[i].Format.Shading);
        return bytes;
    }

    /// <summary>
    /// Builds the row definition: where each column boundary falls and what each cell looks
    /// like. A cell that spans grid columns still occupies one entry per column it covers,
    /// with the first flagged as the start of the span.
    /// </summary>
    private static byte[] Definition(Table table, TableRow row)
    {
        List<Length> boundaries = Boundaries(table, row);
        int columns = Math.Min(boundaries.Count - 1, MaximumColumns);

        var bytes = new List<byte>(1 + ((columns + 1) * 2) + (columns * 20));
        bytes.Add((byte)columns);
        for (int i = 0; i <= columns; i++)
            Append16(bytes, (short)Math.Clamp(boundaries[i].Twips, short.MinValue, short.MaxValue));

        int written = 0;
        foreach (TableCell cell in row.Cells)
        {
            int span = Math.Max(1, cell.Format.GridSpan ?? 1);
            for (int i = 0; i < span && written < columns; i++, written++)
                bytes.AddRange(CellDefinition(table, cell, first: i == 0, merged: span > 1));
        }

        while (written < columns)
        {
            bytes.AddRange(CellDefinition(table, cell: null, first: true, merged: false));
            written++;
        }

        return [.. bytes];
    }

    private static byte[] CellDefinition(Table table, TableCell? cell, bool first, bool merged)
    {
        var flags = (ushort)0;
        if (merged && first)
            flags |= 1 << 0;
        if (merged && !first)
            flags |= 1 << 1;

        switch (cell?.Format.VerticalMerge)
        {
            case VerticalMerge.Restart:
                flags |= 1 << 6;
                break;
            case VerticalMerge.Continue:
                flags |= 1 << 5;
                break;
        }

        flags |= (ushort)(VerticalAlignmentCode(cell?.Format.VerticalAlignment) << 7);

        var bytes = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, flags);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)Math.Clamp(cell?.Format.Width?.Value ?? 0, 0, ushort.MaxValue));

        BorderSet borders = cell?.Format.Borders ?? table.Format.Borders ?? BorderSet.Empty;
        WriteBorder(bytes.AsSpan(4), borders.Top ?? borders.InsideHorizontal);
        WriteBorder(bytes.AsSpan(8), borders.Left ?? borders.InsideVertical);
        WriteBorder(bytes.AsSpan(12), borders.Bottom ?? borders.InsideHorizontal);
        WriteBorder(bytes.AsSpan(16), borders.Right ?? borders.InsideVertical);
        return bytes;
    }

    /// <summary>Writes a border edge in the four-byte shape the table definition uses.</summary>
    private static void WriteBorder(Span<byte> destination, BorderLine? line)
    {
        if (line is null || line.IsEmpty)
            return;

        destination[0] = (byte)Math.Clamp(line.Width.EighthPoints is 0 ? 4 : line.Width.EighthPoints, 2, 0xFF);
        destination[1] = 1;
        destination[2] = 0;
        destination[3] = 0;
    }

    private static int VerticalAlignmentCode(VerticalCellAlignment? alignment) => alignment switch
    {
        VerticalCellAlignment.Center => 1,
        VerticalCellAlignment.Bottom => 2,
        _ => 0,
    };

    /// <summary>
    /// The horizontal position of every column boundary, measured from the left margin. The
    /// grid is used when the table has one; otherwise the width is split evenly.
    /// </summary>
    private static List<Length> Boundaries(Table table, TableRow row)
    {
        int columns = Math.Max(1, row.Cells.Sum(static cell => Math.Max(1, cell.Format.GridSpan ?? 1)));
        var boundaries = new List<Length> { Length.Zero };

        if (table.Grid.Count >= columns)
        {
            Length running = Length.Zero;
            for (int i = 0; i < columns; i++)
            {
                running += table.Grid[i];
                boundaries.Add(running);
            }

            return boundaries;
        }

        Length step = DefaultTableWidth / columns;
        for (int i = 1; i <= columns; i++)
            boundaries.Add(step * i);
        return boundaries;
    }

    private static void Append16(List<byte> bytes, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }
}
