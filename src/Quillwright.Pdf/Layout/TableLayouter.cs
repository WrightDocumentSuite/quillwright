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

        TableFormat tableFormat = _context.Resolver.ResolveTableFormat(table);
        double[] columns = TableColumns.Compute(
            table,
            tableFormat,
            _context.Resolver,
            available,
            NaturalWidths(table, available));
        var box = new TableBox
        {
            Source = table,
            Format = tableFormat,
            Columns = columns,
            Rows = [],
            Offset = Offset(tableFormat, available, columns.Sum()),
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
            TableRowFormat rowFormat = _context.Resolver.ResolveTableRowFormat(row);
            var cells = new List<CellBox>(row.Cells.Count);
            int column = Math.Max(0, rowFormat.GridBefore ?? 0);

            for (int position = 0; position < row.Cells.Count; position++)
            {
                TableCell cell = row.Cells[position];
                TableCellFormat cellFormat = _context.Resolver.ResolveTableCellFormat(cell);
                int span = Math.Max(1, cellFormat.GridSpan ?? 1);

                if (cellFormat.VerticalMerge != VerticalMerge.Continue)
                    cells.Add(Measure(box, table, cell, cellFormat, index, position, column, span));

                column += span;
            }

            box.Rows.Add(new RowBox
            {
                Source = row,
                Format = rowFormat,
                Cells = cells,
                IsHeader = rowFormat.IsHeader == true,
                StartsNewPage = _context.Options.HonorLastRenderedPageBreaks && StartsWithRenderedPageBreak(row),
                CanSplit = rowFormat.CannotSplit != true,
            });
        }

        Merge(box, table);
    }

    private static bool StartsWithRenderedPageBreak(TableRow row)
    {
        foreach (TableCell cell in row.Cells)
        {
            foreach (Paragraph paragraph in cell.Blocks.Paragraphs)
            {
                foreach ((int offset, InlineObject value) in paragraph.Objects)
                {
                    if (offset == 0 && value is RenderedPageBreak)
                        return true;
                }
            }
        }

        return false;
    }

    private CellBox Measure(
        TableBox box,
        Table table,
        TableCell cell,
        TableCellFormat cellFormat,
        int rowIndex,
        int position,
        int column,
        int span)
    {
        CellPadding padding = PaddingOf(box.Format, cellFormat);
        double width = Math.Max(1, box.WidthOf(column, span) - padding.Left - padding.Right);
        TextDirection direction = cellFormat.TextDirection ?? TextDirection.LeftToRightTopToBottom;
        bool turned = direction is TextDirection.TopToBottomRightToLeft or TextDirection.BottomToTopLeftToRight;

        TableRow row = table.Rows[rowIndex];
        TableCellFormat? above = rowIndex > 0 ? Neighbour(table.Rows[rowIndex - 1], column) : null;
        TableCellFormat? before = position > 0 ? _context.Resolver.ResolveTableCellFormat(row.Cells[position - 1]) : null;

        var measured = new CellBox
        {
            Source = cell,
            Format = cellFormat,
            Column = column,
            Span = span,
            Content = turned ? [] : Content(cell, width),
            Padding = padding,
            Direction = turned ? direction : TextDirection.LeftToRightTopToBottom,
            VerticalAlignment = cellFormat.VerticalAlignment ?? VerticalCellAlignment.Top,
            Fill = FillOf(box.Format, cellFormat),
            Edges = TableBorders.EdgesOf(
                box.Format,
                cellFormat,
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
    private void Merge(TableBox box, Table table)
    {
        for (int index = 0; index < box.Rows.Count; index++)
        {
            foreach (CellBox cell in box.Rows[index].Cells)
            {
                if (cell.Format.VerticalMerge != VerticalMerge.Restart)
                    continue;

                int span = 1;
                for (int below = index + 1; below < table.Rows.Count; below++)
                {
                    if (Neighbour(table.Rows[below], cell.Column)?.VerticalMerge != VerticalMerge.Continue)
                        break;

                    span++;
                }

                cell.RowSpan = span;
            }
        }
    }

    /// <summary>The cell that covers a grid column in a row, following the spans across it.</summary>
    private TableCellFormat? Neighbour(TableRow row, int column)
    {
        int at = Math.Max(0, _context.Resolver.ResolveTableRowFormat(row).GridBefore ?? 0);
        foreach (TableCell cell in row.Cells)
        {
            TableCellFormat format = _context.Resolver.ResolveTableCellFormat(cell);
            int span = Math.Max(1, format.GridSpan ?? 1);
            if (column >= at && column < at + span)
                return format;

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
    private static double Stated(RowBox row, double natural) => row.Format switch
    {
        { HeightRule: HeightRule.Exact, Height: { } exact } => Math.Max(1, exact.Points),
        { HeightRule: HeightRule.AtLeast, Height: { } least } => Math.Max(least.Points, natural),
        { Height: { } stated } when row.Format.HeightRule is null => Math.Max(stated.Points, natural),
        _ => Math.Max(1, natural),
    };

    private static double Offset(TableFormat format, double available, double width)
    {
        if (format.Indent is { Unit: WidthUnit.Twips } indent)
            return Math.Max(0, indent.Length.Points);

        return (format.Alignment ?? TableAlignment.Left) switch
        {
            TableAlignment.Center => Math.Max(0, (available - width) / 2),
            TableAlignment.Right => Math.Max(0, available - width),
            _ => 0,
        };
    }

    private static CellPadding PaddingOf(TableFormat table, TableCellFormat cell)
    {
        CellMargins? margins = cell.Margins;
        CellMargins? defaults = table.CellMargins;
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

    private PdfColor? FillOf(TableFormat table, TableCellFormat cell)
    {
        Shading? shading = cell.Shading ?? table.Shading;
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
            int column = Math.Max(0, _context.Resolver.ResolveTableRowFormat(row).GridBefore ?? 0);
            foreach (TableCell cell in row.Cells)
            {
                TableCellFormat cellFormat = _context.Resolver.ResolveTableCellFormat(cell);
                int span = Math.Max(1, cellFormat.GridSpan ?? 1);
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
        Table table = cell.Row?.Table ?? throw new InvalidOperationException("A measured cell must belong to a table.");
        CellPadding padding = PaddingOf(
            _context.Resolver.ResolveTableFormat(table),
            _context.Resolver.ResolveTableCellFormat(cell));

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
