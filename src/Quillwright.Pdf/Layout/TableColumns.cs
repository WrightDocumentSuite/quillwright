using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Works out how wide each grid column of a table is.
/// </summary>
/// <remarks>
/// A table states its width three ways at once and none of them has to agree: the grid says how
/// wide each column was drawn, the table says how wide it prefers to be, and every cell may state
/// a preference of its own. The grid is taken as the shape of the table, the preferences correct
/// it, and the result is scaled to whatever room there actually is. A table with no grid at all
/// falls back to measuring its content, which is what "autofit" means.
/// </remarks>
internal static class TableColumns
{
    /// <summary>Computes the width of every grid column.</summary>
    /// <param name="table">The table.</param>
    /// <param name="format">Its resolved table formatting.</param>
    /// <param name="resolver">The style resolver used for row and cell widths.</param>
    /// <param name="available">How much room the container leaves, in points.</param>
    /// <param name="content">The natural widths of the content, or empty to ignore them.</param>
    public static double[] Compute(
        Table table,
        TableFormat format,
        StyleResolver resolver,
        double available,
        IReadOnlyList<ContentWidth> content)
    {
        int count = Math.Max(1, table.ColumnCount);
        double target = Target(format, available);
        double[] widths = FromGrid(table, count);

        Apply(table, resolver, widths);

        double stated = widths.Sum();
        if (stated <= 0)
        {
            widths = content.Count == count ? FromContent(content, target) : Even(count, target);
            stated = widths.Sum();
        }

        if (stated <= 0)
            return Even(count, target);

        // The grid is a shape, not a measurement: it is scaled to the room the table actually has.
        double scale = target / stated;
        for (int i = 0; i < widths.Length; i++)
            widths[i] *= scale;

        return widths;
    }

    /// <summary>How wide the table wants to be, never wider than the room it has.</summary>
    private static double Target(TableFormat format, double available)
    {
        TableWidth? width = format.Width;
        double indent = format.Indent is { Unit: WidthUnit.Twips } inset ? inset.Length.Points : 0;
        double room = Math.Max(1, available - Math.Max(0, indent));

        double wanted = width switch
        {
            { Unit: WidthUnit.Twips } absolute => absolute.Length.Points,
            { Unit: WidthUnit.Percent } relative => room * relative.Percent / 100,
            _ => room,
        };

        return Math.Clamp(wanted, 1, room);
    }

    private static double[] FromGrid(Table table, int count)
    {
        double[] widths = new double[count];
        for (int i = 0; i < count; i++)
            widths[i] = i < table.Grid.Count ? Math.Max(0, table.Grid[i].Points) : 0;

        return widths;
    }

    /// <summary>
    /// Lets a cell that states an absolute width of its own correct the grid. Only a cell that
    /// covers one column can speak for that column on its own.
    /// </summary>
    private static void Apply(Table table, StyleResolver resolver, double[] widths)
    {
        foreach (TableRow row in table.Rows)
        {
            int column = resolver.ResolveTableRowFormat(row).GridBefore ?? 0;
            foreach (TableCell cell in row.Cells)
            {
                TableCellFormat cellFormat = resolver.ResolveTableCellFormat(cell);
                int span = Math.Max(1, cellFormat.GridSpan ?? 1);
                if (span == 1 && column < widths.Length && cellFormat.Width is { Unit: WidthUnit.Twips } stated)
                    widths[column] = Math.Max(widths[column], stated.Length.Points);

                column += span;
            }
        }
    }

    /// <summary>
    /// Distributes the room across columns that have no grid to go by, using what the content
    /// wants. A column gets at least the widest word it holds, and shares out what is left in
    /// proportion to how much more it would like — the ordinary automatic table layout.
    /// </summary>
    private static double[] FromContent(IReadOnlyList<ContentWidth> content, double target)
    {
        double[] widths = new double[content.Count];
        double minimum = content.Sum(static column => column.Minimum);
        double maximum = content.Sum(static column => column.Maximum);

        if (maximum <= target)
        {
            for (int i = 0; i < widths.Length; i++)
                widths[i] = Math.Max(1, content[i].Maximum);

            return widths;
        }

        double slack = Math.Max(0, target - minimum);
        double spread = Math.Max(1e-6, maximum - minimum);

        for (int i = 0; i < widths.Length; i++)
            widths[i] = content[i].Minimum + (slack * (content[i].Maximum - content[i].Minimum) / spread);

        return widths;
    }

    private static double[] Even(int count, double target)
    {
        double[] widths = new double[count];
        Array.Fill(widths, target / count);
        return widths;
    }
}

/// <summary>How much room a column's content wants.</summary>
/// <param name="Minimum">The width below which the content would have to break a word.</param>
/// <param name="Maximum">The width at which the content would not wrap at all.</param>
internal readonly record struct ContentWidth(double Minimum, double Maximum);
