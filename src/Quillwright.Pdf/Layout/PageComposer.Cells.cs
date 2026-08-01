using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// What goes inside a table cell. A cell is a small page of its own: it holds blocks, it can run
/// out of room, and when it does it remembers where it left off so the rest can be drawn on the
/// next page under the same row.
/// </summary>
internal sealed partial class PageComposer
{
    private void DrawCellContent(CellBox cell, double left, double top, double width, double height, bool repeated)
    {
        if (cell.Direction is TextDirection.TopToBottomRightToLeft or TextDirection.BottomToTopLeftToRight)
        {
            DrawTurnedCellContent(cell, left, top, width, height);
            return;
        }

        (int Block, int Line) start = repeated ? default : _resume.GetValueOrDefault(cell);
        double contentLeft = left + cell.Padding.Left;
        double contentWidth = Math.Max(1, width - cell.Padding.Left - cell.Padding.Right);
        double limit = top + height - cell.Padding.Bottom;
        double y = top + cell.Padding.Top + Offset(cell, height, start);

        for (int index = start.Block; index < cell.Content.Count; index++)
        {
            BlockBox block = cell.Content[index];

            if (block is TableBox nested)
            {
                y = DrawNested(nested, contentLeft, y);
                continue;
            }

            if (block is not ParagraphBox paragraph)
                continue;

            int first = index == start.Block ? start.Line : 0;
            if (first == 0)
                y += paragraph.SpacingBefore;

            for (int line = first; line < paragraph.Lines.Count; line++)
            {
                bool anythingDrawn = index > start.Block || line > first;
                if (anythingDrawn && y + paragraph.Lines[line].Height > limit + 0.01)
                {
                    _resume[cell] = (index, line);
                    return;
                }

                Decorate(paragraph, line, 1, contentLeft, y, paragraph.Lines[line].Height);
                if (line == 0)
                {
                    PlaceFloats(paragraph, contentLeft, y, wrap: false);
                    PlaceFloatingShapes(paragraph, contentLeft, y, wrap: false);
                }

                // A note referenced from inside a cell belongs to the page the cell is drawn on,
                // like any other, so it is claimed as its line goes down.
                CommitNotes(paragraph, line, count: 1);
                PlaceLine(paragraph, paragraph.Lines[line], contentLeft, y);
                y += paragraph.Lines[line].Height;
            }

            y += paragraph.SpacingAfter;
        }

        _resume.Remove(cell);
    }

    /// <summary>
    /// Draws the content of a turned cell: the lines run along the cell's height and stack
    /// across its width — towards the left when the text reads downwards, towards the right when
    /// it reads upwards. Lines that stack past the cell's width are cut off and said.
    /// </summary>
    private void DrawTurnedCellContent(CellBox cell, double left, double top, double width, double height)
    {
        bool downwards = cell.Direction == TextDirection.TopToBottomRightToLeft;
        double length = Math.Max(1, height - cell.Padding.Top - cell.Padding.Bottom);
        double room = width - cell.Padding.Left - cell.Padding.Right;
        double offset = 0;

        foreach (BlockBox block in cell.Content)
        {
            if (block is not ParagraphBox paragraph)
                continue;

            offset += paragraph.SpacingBefore;

            for (int index = 0; index < paragraph.Lines.Count; index++)
            {
                LineBox line = paragraph.Lines[index];
                if (offset + line.Height > room + 0.01)
                {
                    _context.Diagnostics.Add(
                        PdfExportWarningKind.LayoutApproximated,
                        "The lines of a turned cell stack wider than the cell and were cut off.",
                        "vertical-overflow");
                    return;
                }

                double stripLeft = downwards
                    ? left + width - cell.Padding.Right - offset - line.Height
                    : left + cell.Padding.Left + offset;

                CommitNotes(paragraph, index, count: 1);
                Current.Items.Add(new TextLineItem
                {
                    Line = line,
                    X = stripLeft,
                    Y = top + cell.Padding.Top,
                    Length = length,
                    Rotation = downwards ? 90 : 270,
                    Tag = TagOf(paragraph),
                });

                offset += line.Height;
            }

            offset += paragraph.SpacingAfter;
        }
    }

    /// <summary>
    /// How far down the content starts. Only a cell that is drawn whole can centre or bottom-align
    /// what it holds; one that is being continued starts at the top, because its content is the
    /// remainder of something that began on the page before.
    /// </summary>
    private static double Offset(CellBox cell, double height, (int Block, int Line) start)
    {
        if (start != default || cell.VerticalAlignment == VerticalCellAlignment.Top)
            return 0;

        double slack = height - cell.NeededHeight;
        if (slack <= 0)
            return 0;

        return cell.VerticalAlignment == VerticalCellAlignment.Bottom ? slack : slack / 2;
    }

    /// <summary>
    /// Draws a table inside a cell. It is drawn whole: a nested table that would not fit is a rare
    /// enough thing that breaking it is not worth the machinery, and running past the cell is
    /// nearer the truth than dropping it.
    /// </summary>
    private double DrawNested(TableBox nested, double left, double top)
    {
        double y = top;
        double x = left + nested.Offset;
        double saved = _cursor;

        for (int index = 0; index < nested.Rows.Count; index++)
        {
            _cursor = y;
            DrawRow(nested, index, x, nested.Rows[index].Height);
            y += nested.Rows[index].Height;
        }

        _cursor = saved;
        return y;
    }
}
