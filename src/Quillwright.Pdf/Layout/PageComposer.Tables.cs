using Inkwright;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Table placement: rows go down the page in order, and a row that will not fit either moves to
/// the next page whole or is broken across the boundary, depending on what it allows.
/// </summary>
internal sealed partial class PageComposer
{
    private readonly TableLayouter _tables;

    /// <summary>Where a cell's content had got to when the page ran out.</summary>
    private readonly Dictionary<CellBox, (int Block, int Line)> _resume = [];

    private void PlaceTable(TableBox box)
    {
        // The table was measured against the column it starts in; when it continues into the
        // next one, only its left edge moves, which is exactly right when the columns are uniform.
        List<RowBox> headers = [.. box.HeaderRows];

        _resume.Clear();

        for (int index = 0; index < box.Rows.Count;)
        {
            RowBox row = box.Rows[index];
            double x = CurrentLeft + box.Offset;

            // Whatever notes the row owes have to come out of the page before the row is offered
            // the rest. Reserving for the whole row rather than for the part that will fit errs
            // towards leaving too much room, which is the harmless direction.
            double room = NoteAwareBottom - RowNoteReserve(row) - _cursor;

            if (row.Height <= room + 0.01)
            {
                DrawRow(box, index, x, row.Height);
                _cursor += row.Height;
                MarkFilled();
                index++;
                continue;
            }

            if (Splittable(row) && room >= Smallest(row))
            {
                DrawRow(box, index, x, room);
                row.Height -= room;
                _cursor += room;
                MarkFilled();
                NewColumn();
                Repeat(box, headers);
                continue;
            }

            if (_hasContent)
            {
                NewColumn();
                Repeat(box, headers);
                continue;
            }

            // A row taller than a column of its own has nowhere to go, so it is drawn where it is.
            _context.Diagnostics.Add(
                PdfExportWarningKind.LayoutApproximated,
                "A table row is taller than the page and could not be broken, so it runs past the margin.",
                "row-height");

            DrawRow(box, index, x, row.Height);
            _cursor += row.Height;
            MarkFilled();
            index++;
        }
    }

    /// <summary>
    /// Whether a row may be broken. A row carrying part of a vertical merge may not: the cell it
    /// belongs to is drawn as one box, and a box cannot start on one page and end on another.
    /// A turned cell may not either: its lines run along the row's height, and a line cannot.
    /// </summary>
    private static bool Splittable(RowBox row)
    {
        if (!row.CanSplit)
            return false;

        foreach (CellBox cell in row.Cells)
        {
            if (cell.RowSpan > 1 || cell.Direction != TextDirection.LeftToRightTopToBottom)
                return false;
        }

        return true;
    }

    /// <summary>The least a broken row must be given for the break to be worth making.</summary>
    private static double Smallest(RowBox row)
    {
        double needed = 0;
        foreach (CellBox cell in row.Cells)
        {
            double first = FirstLineHeight(cell);
            if (first > 0)
                needed = Math.Max(needed, cell.Padding.Top + first);
        }

        return needed > 0 ? needed : 1;
    }

    private static double FirstLineHeight(CellBox cell)
    {
        foreach (BlockBox block in cell.Content)
        {
            if (block is ParagraphBox { Lines.Count: > 0 } paragraph)
                return paragraph.SpacingBefore + paragraph.Lines[0].Height;

            if (block is TableBox { Rows.Count: > 0 } nested)
                return nested.Rows[0].Height;
        }

        return 0;
    }

    /// <summary>Draws the header rows again at the top of a column the table has continued into.</summary>
    private void Repeat(TableBox box, List<RowBox> headers)
    {
        foreach (RowBox header in headers)
        {
            int index = box.Rows.IndexOf(header);
            if (index < 0 || _cursor + header.Height > Current.Geometry.ContentBottom)
                break;

            DrawRow(box, index, CurrentLeft + box.Offset, header.Height, repeated: true);
            _cursor += header.Height;
            MarkFilled();
        }
    }

    /// <summary>Draws one row, or as much of it as the given height allows.</summary>
    /// <param name="box">The measured table.</param>
    /// <param name="index">Which row to draw.</param>
    /// <param name="x">The table's left edge on the page.</param>
    /// <param name="height">How tall the drawn part is.</param>
    /// <param name="repeated">Whether this is a header being shown again rather than reached.</param>
    private void DrawRow(TableBox box, int index, double x, double height, bool repeated = false)
    {
        RowBox row = box.Rows[index];
        double top = _cursor;
        bool mirrored = box.Source.Format.RightToLeft == true;

        foreach (CellBox cell in row.Cells)
        {
            // A right-to-left table reads its columns from the right, so the grid is mirrored:
            // the first logical column is drawn at the far edge (w:bidiVisual).
            double cellWidth = box.WidthOf(cell.Column, cell.Span);
            double cellLeft = mirrored
                ? x + box.Width - box.LeftOf(cell.Column) - cellWidth
                : x + box.LeftOf(cell.Column);
            double cellHeight = Spanned(box, index, cell, height);

            if (cell.Fill is { } fill)
            {
                Current.Items.Add(new FillItem
                {
                    X = cellLeft,
                    Y = top,
                    Width = cellWidth,
                    Height = cellHeight,
                    Color = fill,
                });
            }

            DrawEdges(cell, cellLeft, top, cellWidth, cellHeight);

            TagRef? outer = _cellTag;
            _cellTag = CellTag(box.Source, row.Source, cell);
            DrawCellContent(cell, cellLeft, top, cellWidth, cellHeight, repeated);
            _cellTag = outer;
        }
    }

    /// <summary>
    /// How tall a cell is drawn: its own row, or every row it reaches across, never past the
    /// bottom of the page it is being drawn on.
    /// </summary>
    private double Spanned(TableBox box, int index, CellBox cell, double height)
    {
        if (cell.RowSpan <= 1)
            return height;

        double total = 0;
        for (int i = index; i < box.Rows.Count && i < index + cell.RowSpan; i++)
            total += box.Rows[i].Height;

        return Math.Min(total, Current.Geometry.ContentBottom - _cursor);
    }

    private void DrawEdges(CellBox cell, double left, double top, double width, double height)
    {
        double right = left + width;
        double bottom = top + height;

        Edge(cell.Edges.Top, left, top, right, top);
        Edge(cell.Edges.Bottom, left, bottom, right, bottom);
        Edge(cell.Edges.Left, left, top, left, bottom);
        Edge(cell.Edges.Right, right, top, right, bottom);

        void Edge(BorderLine? line, double ax, double ay, double bx, double by)
        {
            if (line is null || line.IsEmpty)
                return;

            Current.Items.Add(new StrokeItem
            {
                X = ax,
                Y = ay,
                X2 = bx,
                Y2 = by,
                Thickness = Math.Max(0.25, line.Width.Points),
                Color = _context.ColorOf(line.Color, PdfColor.Black),
                Style = line.Style,
            });
        }
    }
}
