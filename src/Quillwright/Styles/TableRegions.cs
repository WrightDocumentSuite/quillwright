using Quillwright.Model;

namespace Quillwright.Styles;

/// <summary>
/// Decides which conditional parts of a table style apply to a cell.
/// </summary>
/// <remarks>
/// A table style defines formatting for the header row, the total row, the first and last
/// columns, the banded stripes and the four corner cells. Which of those are switched on is
/// the table's own <c>w:tblLook</c>, and they layer in a fixed order: the bands first, then
/// the edge rows and columns, then the corners, so a header cell in the first column wins
/// over the plain header formatting.
/// </remarks>
internal static class TableRegions
{
    /// <summary>The regions that apply to a cell, in the order their formatting layers.</summary>
    public static IEnumerable<TableStyleRegion> For(Table table, TableCell cell)
    {
        if (cell.Row is not { } row)
            yield break;

        int rowIndex = table.Rows.IndexOf(row);
        int columnIndex = row.Cells.IndexOf(cell);
        if (rowIndex < 0 || columnIndex < 0)
            yield break;

        TableStyleOptions options = table.Format.StyleOptions ?? TableStyleOptions.None;
        int rowCount = table.Rows.Count;
        int columnCount = row.Cells.Count;

        bool firstRow = options.HasFlag(TableStyleOptions.FirstRow) && rowIndex == 0;
        bool lastRow = options.HasFlag(TableStyleOptions.LastRow) && rowIndex == rowCount - 1;
        bool firstColumn = options.HasFlag(TableStyleOptions.FirstColumn) && columnIndex == 0;
        bool lastColumn = options.HasFlag(TableStyleOptions.LastColumn) && columnIndex == columnCount - 1;

        if (!options.HasFlag(TableStyleOptions.NoVerticalBanding))
        {
            int band = Band(columnIndex, firstColumn ? 1 : 0, table.Format.ColumnBandSize ?? 1);
            yield return band % 2 == 0 ? TableStyleRegion.Band1Vertical : TableStyleRegion.Band2Vertical;
        }

        if (!options.HasFlag(TableStyleOptions.NoHorizontalBanding))
        {
            int band = Band(rowIndex, firstRow ? 1 : 0, table.Format.RowBandSize ?? 1);
            yield return band % 2 == 0 ? TableStyleRegion.Band1Horizontal : TableStyleRegion.Band2Horizontal;
        }

        if (firstColumn)
            yield return TableStyleRegion.FirstColumn;
        if (lastColumn)
            yield return TableStyleRegion.LastColumn;
        if (firstRow)
            yield return TableStyleRegion.FirstRow;
        if (lastRow)
            yield return TableStyleRegion.LastRow;

        if (firstRow && firstColumn)
            yield return TableStyleRegion.NorthWestCell;
        if (firstRow && lastColumn)
            yield return TableStyleRegion.NorthEastCell;
        if (lastRow && firstColumn)
            yield return TableStyleRegion.SouthWestCell;
        if (lastRow && lastColumn)
            yield return TableStyleRegion.SouthEastCell;
    }

    private static int Band(int index, int offset, int size) =>
        size <= 0 ? 0 : Math.Max(0, index - offset) / size;
}
