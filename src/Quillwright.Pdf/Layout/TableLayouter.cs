using Inkwright;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Measures a table: settles the column widths, lays out everything in the cells against them and
/// works out how tall each row comes to.
/// </summary>
internal sealed class TableLayouter
{
    /// <summary>How deep a table inside a table inside a table may go before the nesting is refused.</summary>
    private const int MaxNesting = 12;

    private readonly PdfExportContext _context;
    private readonly ParagraphLayouter _layouter;
    private readonly Action<bool> _rehearse;

    private List<(CellBox Cell, int Row)> _turned = [];
    private int _depth;

    internal TableLayouter(PdfExportContext context, ParagraphLayouter layouter, Action<bool> rehearse)
    {
        _context = context;
        _layouter = layouter;
        _rehearse = rehearse;
    }

    /// <summary>Measures a table against the room its container leaves.</summary>
    /// <param name="table">The table to measure.</param>
    /// <param name="available">How wide the container is, in points.</param>
    public TableBox Measure(Table table, double available)
    {
        ArgumentNullException.ThrowIfNull(table);

        double[] columns = TableColumns.Compute(table, available, NaturalWidths(table, available));
        var box = new TableBox
        {
            Source = table,
            Columns = columns,
            Rows = [],
            Offset = Offset(table, available, columns.Sum()),
            SpacingBefore = 0,
            SpacingAfter = 0,
        };

        // A nested table measures inside this one, so the turned cells collected here are kept
        // apart from any the caller is still gathering.
        List<(CellBox Cell, int Row)> outer = _turned;
        _turned = [];

        try
        {
            Fill(box, table);
            Heights(box);
            FillTurnedCells(box);
        }
        finally
        {
            _turned = outer;
        }

        return box;
    }

    /// <summary>Lays out every cell of every row against the settled column widths.</summary>
    private void Fill(TableBox box, Table table)
    {
        for (int index = 0; index < table.Rows.Count; index++)
        {
            TableRow row = table.Rows[index];
            var cells = new List<CellBox>(row.Cells.Count);
            int column = Math.Max(0, row.Format.GridBefore ?? 0);

            for (int position = 0; position < row.Cells.Count; position++)
            {
                TableCell cell = row.Cells[position];
                int span = Math.Max(1, cell.Format.GridSpan ?? 1);

                if (cell.Format.VerticalMerge != VerticalMerge.Continue)
                    cells.Add(Measure(box, table, cell, index, position, column, span));

                column += span;
            }

            box.Rows.Add(new RowBox
            {
                Source = row,
                Cells = cells,
                IsHeader = row.Format.IsHeader == true,
                CanSplit = row.Format.CannotSplit != true,
            });
        }

        Merge(box, table);
    }

    private CellBox Measure(TableBox box, Table table, TableCell cell, int rowIndex, int position, int column, int span)
    {
        CellPadding padding = PaddingOf(table, cell);
        double width = Math.Max(1, box.WidthOf(column, span) - padding.Left - padding.Right);
        TextDirection direction = cell.Format.TextDirection ?? TextDirection.LeftToRightTopToBottom;
        bool turned = direction is TextDirection.TopToBottomRightToLeft or TextDirection.BottomToTopLeftToRight;

        TableRow row = table.Rows[rowIndex];
        TableCell? above = rowIndex > 0 ? Neighbour(table.Rows[rowIndex - 1], column) : null;
        TableCell? before = position > 0 ? row.Cells[position - 1] : null;

        var measured = new CellBox
        {
            Source = cell,
            Column = column,
            Span = span,
            Content = turned ? [] : Content(cell, width),
            Padding = padding,
            Direction = turned ? direction : TextDirection.LeftToRightTopToBottom,
            VerticalAlignment = cell.Format.VerticalAlignment ?? VerticalCellAlignment.Top,
            Fill = FillOf(table, cell),
            Edges = TableBorders.EdgesOf(
                table,
                cell,
                above,
                before,
                isFirstRow: rowIndex == 0,
                isLastRow: rowIndex == table.Rows.Count - 1,
                isFirstColumn: column == 0,
                isLastColumn: column + span >= box.Columns.Length),
        };

        if (turned)
        {
            measured.NeededOverride = LongestLine(cell) + padding.Top + padding.Bottom;
            _turned.Add((measured, rowIndex));
        }
        else if (direction != TextDirection.LeftToRightTopToBottom)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.LayoutApproximated,
                "A cell whose text flows in a rotated East Asian direction is laid out the ordinary way.",
                "vertical-flow");
        }

        return measured;
    }

    /// <summary>
    /// The longest natural line of a turned cell, which is how tall the cell asks its row to be:
    /// the text runs along the cell's height, so its length is the height it needs. Measured as a
    /// rehearsal — the real measurement happens once the row heights are settled.
    /// </summary>
    private double LongestLine(TableCell cell)
    {
        _rehearse(true);

        try
        {
            double longest = 0;
            foreach (Block block in cell.Blocks)
            {
                if (block is not Paragraph paragraph)
                    continue;

                ParagraphBox probe = _layouter.Layout(paragraph, 10_000);
                foreach (LineBox line in probe.Lines)
                    longest = Math.Max(longest, line.Width);
            }

            return longest;
        }
        finally
        {
            _rehearse(false);
        }
    }

    /// <summary>
    /// Measures the content of the turned cells, now that the row heights are settled and the
    /// length their lines run along is known. This is the one real measurement the cell gets,
    /// so nothing inside it is counted twice.
    /// </summary>
    private void FillTurnedCells(TableBox box)
    {
        foreach ((CellBox cell, int row) in _turned)
        {
            double length = 0;
            for (int i = row; i < Math.Min(box.Rows.Count, row + cell.RowSpan); i++)
                length += box.Rows[i].Height;

            length = Math.Max(1, length - cell.Padding.Top - cell.Padding.Bottom);

            foreach (Block block in cell.Source.Blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        cell.Content.Add(_layouter.Layout(paragraph, length));
                        break;

                    case Table:
                        _context.Diagnostics.Add(
                            PdfExportWarningKind.ContentSkipped,
                            "A table inside a turned cell is not drawn.",
                            "vertical-table");
                        break;

                    default:
                        break;
                }
            }
        }

        _turned.Clear();
    }

    /// <summary>The blocks inside a cell, measured against the room left after its padding.</summary>
    private List<BlockBox> Content(TableCell cell, double width)
    {
        List<BlockBox> content = [];

        foreach (Block block in cell.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    content.Add(_layouter.Layout(paragraph, width));
                    break;

                case Table nested when _depth < MaxNesting:
                    _depth++;
                    content.Add(Measure(nested, width));
                    _depth--;
                    break;

                case Table:
                    _context.Diagnostics.Add(
                        PdfExportWarningKind.LayoutApproximated,
                        $"Tables nested more than {MaxNesting} deep are not laid out.",
                        "nesting");
                    break;

                default:
                    break;
            }
        }

        return content;
    }

    /// <summary>Finds how far down a merged cell reaches and gives it those rows.</summary>
    private static void Merge(TableBox box, Table table)
    {
        for (int index = 0; index < box.Rows.Count; index++)
        {
            foreach (CellBox cell in box.Rows[index].Cells)
            {
                if (cell.Source.Format.VerticalMerge != VerticalMerge.Restart)
                    continue;

                int span = 1;
                for (int below = index + 1; below < table.Rows.Count; below++)
                {
                    if (Neighbour(table.Rows[below], cell.Column)?.Format.VerticalMerge != VerticalMerge.Continue)
                        break;

                    span++;
                }

                cell.RowSpan = span;
            }
        }
    }

    /// <summary>The cell that covers a grid column in a row, following the spans across it.</summary>
    private static TableCell? Neighbour(TableRow row, int column)
    {
        int at = Math.Max(0, row.Format.GridBefore ?? 0);
        foreach (TableCell cell in row.Cells)
        {
            int span = Math.Max(1, cell.Format.GridSpan ?? 1);
            if (column >= at && column < at + span)
                return cell;

            at += span;
        }

        return null;
    }

    /// <summary>
    /// Gives every row a height. A row is as tall as the tallest cell that ends in it, and a cell
    /// that reaches across several rows only stretches the last of them, which is where the extra
    /// room it needs has to come from.
    /// </summary>
    private static void Heights(TableBox box)
    {
        foreach (RowBox row in box.Rows)
        {
            double height = 0;
            foreach (CellBox cell in row.Cells)
            {
                if (cell.RowSpan == 1)
                    height = Math.Max(height, cell.NeededHeight);
            }

            row.Height = Stated(row, height);
        }

        for (int index = 0; index < box.Rows.Count; index++)
        {
            foreach (CellBox cell in box.Rows[index].Cells)
            {
                if (cell.RowSpan <= 1)
                    continue;

                int last = Math.Min(box.Rows.Count - 1, index + cell.RowSpan - 1);
                double spanned = 0;
                for (int i = index; i <= last; i++)
                    spanned += box.Rows[i].Height;

                double missing = cell.NeededHeight - spanned;
                if (missing > 0)
                    box.Rows[last].Height += missing;
            }
        }
    }

    /// <summary>The height a row asks for, which may be a floor or an exact figure.</summary>
    private static double Stated(RowBox row, double natural) => row.Source.Format switch
    {
        { HeightRule: HeightRule.Exact, Height: { } exact } => Math.Max(1, exact.Points),
        { HeightRule: HeightRule.AtLeast, Height: { } least } => Math.Max(least.Points, natural),
        { Height: { } stated } when row.Source.Format.HeightRule is null => Math.Max(stated.Points, natural),
        _ => Math.Max(1, natural),
    };

    private static double Offset(Table table, double available, double width)
    {
        if (table.Format.Indent is { Unit: WidthUnit.Twips } indent)
            return Math.Max(0, indent.Length.Points);

        return (table.Format.Alignment ?? TableAlignment.Left) switch
        {
            TableAlignment.Center => Math.Max(0, (available - width) / 2),
            TableAlignment.Right => Math.Max(0, available - width),
            _ => 0,
        };
    }

    private static CellPadding PaddingOf(Table table, TableCell cell)
    {
        CellMargins? margins = cell.Format.Margins;
        CellMargins? defaults = table.Format.CellMargins;
        CellPadding fallback = CellPadding.Default;

        return new CellPadding(
            Side(margins?.Left, defaults?.Left, fallback.Left),
            Side(margins?.Top, defaults?.Top, fallback.Top),
            Side(margins?.Right, defaults?.Right, fallback.Right),
            Side(margins?.Bottom, defaults?.Bottom, fallback.Bottom));

        static double Side(TableWidth? own, TableWidth? table, double fallback)
        {
            TableWidth? value = own ?? table;
            return value is { Unit: WidthUnit.Twips } width ? Math.Max(0, width.Length.Points) : fallback;
        }
    }

    private PdfColor? FillOf(Table table, TableCell cell)
    {
        Shading? shading = cell.Format.Shading ?? table.Format.Shading;
        if (shading is null || shading.IsEmpty || shading.Pattern == ShadingPattern.Nil)
            return null;

        Primitives.WordColor value = shading.Pattern == ShadingPattern.Solid ? shading.Color : shading.Fill;
        return value.IsAuto ? null : _context.ColorOf(value, PdfColor.White);
    }

    /// <summary>
    /// How much room each column's content would like: the widest word it must not break, and the
    /// width at which it would not wrap at all. Only asked for when the table brought no grid.
    /// </summary>
    private IReadOnlyList<ContentWidth> NaturalWidths(Table table, double available)
    {
        if (table.Grid.Count >= table.ColumnCount && table.ColumnCount > 0)
            return [];

        int count = Math.Max(1, table.ColumnCount);
        double[] minimum = new double[count];
        double[] maximum = new double[count];

        foreach (TableRow row in table.Rows)
        {
            int column = Math.Max(0, row.Format.GridBefore ?? 0);
            foreach (TableCell cell in row.Cells)
            {
                int span = Math.Max(1, cell.Format.GridSpan ?? 1);
                if (span == 1 && column < count)
                {
                    (double least, double most) = Wanted(cell, available);
                    minimum[column] = Math.Max(minimum[column], least);
                    maximum[column] = Math.Max(maximum[column], most);
                }

                column += span;
            }
        }

        var widths = new ContentWidth[count];
        for (int i = 0; i < count; i++)
            widths[i] = new ContentWidth(Math.Max(1, minimum[i]), Math.Max(1, Math.Max(minimum[i], maximum[i])));

        return widths;
    }

    /// <summary>What one cell's text wants: laid out as narrow as it goes, and as wide as it goes.</summary>
    private (double Minimum, double Maximum) Wanted(TableCell cell, double available)
    {
        double minimum = 0;
        double maximum = 0;
        CellPadding padding = CellPadding.Default;

        _rehearse(true);
        try
        {
            foreach (Paragraph paragraph in cell.Blocks.Paragraphs)
            {
                ParagraphBox wide = _layouter.Layout(paragraph, Math.Max(available, 1));
                foreach (LineBox line in wide.Lines)
                    maximum = Math.Max(maximum, line.Width);

                ParagraphBox narrow = _layouter.Layout(paragraph, 1);
                foreach (LineBox line in narrow.Lines)
                    minimum = Math.Max(minimum, line.Width);
            }
        }
        finally
        {
            _rehearse(false);
        }

        return (minimum + padding.Left + padding.Right, maximum + padding.Left + padding.Right);
    }
}
