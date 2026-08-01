using System.Text;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Model;

/// <summary>A table: a grid definition and a list of rows, each holding cells of blocks.</summary>
public sealed class Table : Block
{
    /// <summary>Creates an empty table.</summary>
    public Table() => Rows = new OwnedList<TableRow>(row => row.Table = this, row => row.Table = null);

    /// <summary>Table-level formatting (<c>w:tblPr</c>).</summary>
    public TableFormat Format { get; set; } = TableFormat.Default;

    /// <summary>Widths of the grid columns (<c>w:tblGrid</c>).</summary>
    public List<Length> Grid { get; } = [];

    /// <summary>The rows of the table, in order.</summary>
    public OwnedList<TableRow> Rows { get; }

    /// <summary>Attributes of <c>w:tbl</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>The grid-change revision record, kept verbatim (<c>w:tblGridChange</c>).</summary>
    public string? GridChangeXml { get; set; }

    /// <summary>
    /// Children of <c>w:tbl</c> this version does not model — a bookmark spanning rows, a
    /// structured tag around them — kept verbatim and written after the last row.
    /// </summary>
    public string? PreservedXml { get; set; }

    /// <summary>The cell at the given row and position within that row.</summary>
    /// <param name="row">Zero-based row index.</param>
    /// <param name="column">Zero-based position of the cell in the row.</param>
    public TableCell this[int row, int column] => Rows[row].Cells[column];

    /// <summary>Number of grid columns, taken from the grid or from the widest row.</summary>
    public int ColumnCount => Grid.Count > 0
        ? Grid.Count
        : Rows.Count == 0 ? 0 : Rows.Max(static row => row.Cells.Sum(static cell => cell.Format.GridSpan ?? 1));

    /// <summary>Creates a table of the given shape, every cell holding one empty paragraph.</summary>
    /// <param name="rows">Number of rows.</param>
    /// <param name="columns">Number of columns.</param>
    /// <param name="totalWidth">Width to split between the columns, or <see langword="null"/> to leave the grid unsized.</param>
    public static Table Create(int rows, int columns, Length? totalWidth = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(columns);

        var table = new Table
        {
            Format = TableFormat.Default with
            {
                Borders = BorderSet.AllWithInside(BorderLine.Single(Length.FromEighthPoints(4), WordColor.Auto)),
                StyleOptions = TableStyleOptions.Default,
                Width = totalWidth is { } width ? TableWidth.FromLength(width) : TableWidth.FromPercent(100),
            },
        };

        if (columns > 0 && totalWidth is { } total)
        {
            Length columnWidth = total / columns;
            for (int i = 0; i < columns; i++)
                table.Grid.Add(columnWidth);
        }

        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow();
            for (int c = 0; c < columns; c++)
            {
                var cell = new TableCell();
                cell.AddParagraph();
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    /// <summary>Appends a row of empty cells matching the current column count.</summary>
    public TableRow AddRow()
    {
        var row = new TableRow();
        int columns = Math.Max(ColumnCount, 1);
        for (int c = 0; c < columns; c++)
        {
            var cell = new TableCell();
            cell.AddParagraph();
            row.Cells.Add(cell);
        }

        Rows.Add(row);
        return row;
    }

    /// <summary>Appends a row whose cells hold the given texts.</summary>
    /// <param name="values">One text per cell.</param>
    public TableRow AddRow(params ReadOnlySpan<string> values)
    {
        var row = new TableRow();
        foreach (string value in values)
        {
            var cell = new TableCell();
            cell.AddParagraph(value);
            row.Cells.Add(cell);
        }

        Rows.Add(row);
        return row;
    }

    /// <summary>Inserts a column of empty cells at the given position in every row.</summary>
    /// <param name="index">Zero-based position, or the column count to append.</param>
    /// <param name="width">Width of the new grid column.</param>
    public void InsertColumn(int index, Length? width = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        foreach (TableRow row in Rows)
        {
            var cell = new TableCell();
            cell.AddParagraph();
            row.Cells.Insert(Math.Min(index, row.Cells.Count), cell);
        }

        if (index <= Grid.Count)
            Grid.Insert(index, width ?? (Grid.Count > 0 ? Grid[Math.Min(index, Grid.Count - 1)] : Length.FromInches(1)));
    }

    /// <summary>Removes a column from every row.</summary>
    /// <param name="index">Zero-based position.</param>
    public void RemoveColumn(int index)
    {
        foreach (TableRow row in Rows)
        {
            if (index < row.Cells.Count)
                row.Cells.RemoveAt(index);
        }

        if (index < Grid.Count)
            Grid.RemoveAt(index);
    }

    /// <summary>
    /// Merges a rectangle of cells: the cells to the right collapse into the first one of
    /// each row through <c>w:gridSpan</c>, and the rows below join it through <c>w:vMerge</c>.
    /// </summary>
    /// <param name="firstRow">Zero-based index of the top row.</param>
    /// <param name="firstColumn">Zero-based index of the leading column.</param>
    /// <param name="rowCount">How many rows the merged region covers.</param>
    /// <param name="columnCount">How many columns the merged region covers.</param>
    public void MergeCells(int firstRow, int firstColumn, int rowCount, int columnCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rowCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columnCount, 1);

        for (int r = firstRow; r < Math.Min(firstRow + rowCount, Rows.Count); r++)
        {
            TableRow row = Rows[r];
            if (firstColumn >= row.Cells.Count)
                continue;

            TableCell anchor = row.Cells[firstColumn];
            int absorbed = 0;
            while (absorbed < columnCount - 1 && firstColumn + 1 < row.Cells.Count)
            {
                absorbed++;
                row.Cells.RemoveAt(firstColumn + 1);
            }

            int span = (anchor.Format.GridSpan ?? 1) + absorbed;
            anchor.Format = anchor.Format with
            {
                GridSpan = span > 1 ? span : null,
                VerticalMerge = rowCount > 1 ? (r == firstRow ? VerticalMerge.Restart : VerticalMerge.Continue) : anchor.Format.VerticalMerge,
            };
        }
    }

    /// <inheritdoc />
    public override string GetText()
    {
        var builder = new StringBuilder();
        foreach (TableRow row in Rows)
        {
            if (builder.Length > 0)
                builder.Append('\n');
            for (int i = 0; i < row.Cells.Count; i++)
            {
                if (i > 0)
                    builder.Append('\t');
                builder.Append(row.Cells[i].GetText());
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public override Block Clone()
    {
        var clone = new Table
        {
            Format = Format,
            Attributes = Attributes,
            GridChangeXml = GridChangeXml,
            PreservedXml = PreservedXml,
        };

        clone.Grid.AddRange(Grid);
        foreach (TableRow row in Rows)
            clone.Rows.Add(row.Clone());
        return clone;
    }
}
