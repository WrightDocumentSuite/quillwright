using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The columns of one page: where each begins, how wide it is, and where the gaps between them
/// fall.
/// </summary>
/// <remarks>
/// A section states its columns one of two ways: a count with one shared gap, split evenly, or an
/// explicit list in which every column brings its own width and the gap that follows it. Either
/// way the band is fixed by the section before any content flows, which is what lets a paragraph
/// be measured against the column it starts in.
/// </remarks>
internal sealed class ColumnBand
{
    private readonly double[] _lefts;
    private readonly double[] _widths;

    private ColumnBand(double[] lefts, double[] widths, bool separator)
    {
        _lefts = lefts;
        _widths = widths;
        Separator = separator;

        IsUniform = true;
        for (int i = 1; i < widths.Length; i++)
            IsUniform &= Math.Abs(widths[i] - widths[0]) < 0.5;
    }

    /// <summary>How many columns the band has; one means the page is not columned at all.</summary>
    public int Count => _widths.Length;

    /// <summary>Whether a rule is drawn down the middle of each gap.</summary>
    public bool Separator { get; }

    /// <summary>
    /// Whether every column is the same width. Only then can a paragraph measured in one column
    /// be split into the next; between unequal columns a paragraph moves whole.
    /// </summary>
    public bool IsUniform { get; }

    /// <summary>The left edge of a column, in points from the left of the page.</summary>
    public double LeftOf(int column) => _lefts[Math.Clamp(column, 0, Count - 1)];

    /// <summary>How wide a column is.</summary>
    public double WidthOf(int column) => _widths[Math.Clamp(column, 0, Count - 1)];

    /// <summary>The middle of the gap between a column and the one after it, where a rule goes.</summary>
    public double GapCenter(int column)
    {
        double right = LeftOf(column) + WidthOf(column);
        return right + ((LeftOf(column + 1) - right) / 2);
    }

    /// <summary>Whether another band puts the same columns in the same places.</summary>
    public bool Matches(ColumnBand other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Count != other.Count)
            return false;

        for (int i = 0; i < Count; i++)
        {
            if (Math.Abs(_lefts[i] - other._lefts[i]) > 0.5 || Math.Abs(_widths[i] - other._widths[i]) > 0.5)
                return false;
        }

        return true;
    }

    /// <summary>Reads the band a section puts on a page.</summary>
    /// <param name="properties">The section's page setup.</param>
    /// <param name="geometry">The page the columns divide.</param>
    public static ColumnBand Of(SectionProperties properties, PageGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(properties);

        ColumnLayout layout = properties.Columns;
        int count = Math.Max(1, layout.Count);

        if (count == 1 && layout.Columns.Count == 0)
            return new ColumnBand([geometry.ContentLeft], [geometry.ContentWidth], separator: false);

        double[] lefts;
        double[] widths;

        if (!layout.EqualWidth && layout.Columns.Count > 0)
        {
            // Every column brings its own width and the gap that follows it.
            count = layout.Columns.Count;
            lefts = new double[count];
            widths = new double[count];
            double x = geometry.ContentLeft;

            for (int i = 0; i < count; i++)
            {
                TextColumn column = layout.Columns[i];
                lefts[i] = x;
                widths[i] = Math.Max(1, column.Width.Points);
                x += widths[i] + Math.Max(0, column.Space.Points);
            }
        }
        else
        {
            double gap = Math.Max(0, layout.Space.Points);
            double width = Math.Max(1, (geometry.ContentWidth - (gap * (count - 1))) / count);
            lefts = new double[count];
            widths = new double[count];

            for (int i = 0; i < count; i++)
            {
                lefts[i] = geometry.ContentLeft + (i * (width + gap));
                widths[i] = width;
            }
        }

        return new ColumnBand(lefts, widths, layout.Separator);
    }
}
