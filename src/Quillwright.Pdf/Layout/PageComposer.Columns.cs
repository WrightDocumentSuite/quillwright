using Inkwright;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The column cursor: which column of the page is being filled, where it is, and what happens
/// when it runs out.
/// </summary>
/// <remarks>
/// A page with one column is the ordinary case and costs nothing extra: the band has one entry
/// that is the body itself. With more, overflow walks the columns before it walks the pages, and
/// everything above the composer — paragraphs, tables, notes — is none the wiser.
/// </remarks>
internal sealed partial class PageComposer
{
    private ColumnBand _columns = null!;
    private double[] _columnDepth = [];
    private int _column;

    /// <summary>
    /// Where the current band of columns begins, in points down from the top of the page. The
    /// first band of a page begins at the content top; a continuous section that changes the
    /// columns starts a new band below whatever the old one reached.
    /// </summary>
    private double _bandTop;

    /// <summary>Whether anything at all has landed on the current page, in any column.</summary>
    private bool _pageHasContent;

    /// <summary>The left edge of the column being filled.</summary>
    private double CurrentLeft
    {
        get
        {
            _ = Current;
            return _columns.LeftOf(_column);
        }
    }

    /// <summary>How wide the column being filled is.</summary>
    private double CurrentWidth
    {
        get
        {
            _ = Current;
            return _columns.WidthOf(_column);
        }
    }

    /// <summary>Notes that something landed in the current column.</summary>
    private void MarkFilled()
    {
        _hasContent = true;
        _pageHasContent = true;
    }

    /// <summary>
    /// Moves to the next column, or to the next page when this was the last one. The cursor goes
    /// back to the top of the band either way, which is the whole idea of a column.
    /// </summary>
    private void NewColumn()
    {
        _columnDepth[_column] = Math.Max(_columnDepth[_column], _cursor);

        if (_column + 1 >= _columns.Count)
        {
            NewPage();
            return;
        }

        _column++;
        _cursor = _bandTop;
        _hasContent = false;
    }

    /// <summary>Prepares the band of a page that has just been opened.</summary>
    private void ResetColumns(ColumnBand columns, double top)
    {
        _columns = columns;
        _columnDepth = new double[columns.Count];
        _column = 0;
        _bandTop = top;
        _pageHasContent = false;
    }

    /// <summary>
    /// Closes the band that is filling and opens another below it, which is what a continuous
    /// section that changes the columns does: the page carries on, restacked.
    /// </summary>
    private void StartBandBelow(ColumnBand next)
    {
        _columnDepth[_column] = Math.Max(_columnDepth[_column], _cursor);
        double depth = Math.Max(_bandTop, _columnDepth.Max());
        CloseColumns();

        // A band that would begin at the very bottom has nowhere to go but the next page,
        // which opens under the new section's own setup and marks its own band start.
        if (depth > Current.Geometry.ContentBottom - 20)
        {
            NewPage();
            return;
        }

        _columns = next;
        _columnDepth = new double[next.Count];
        _column = 0;
        _bandTop = depth;
        _cursor = depth;
        _hasContent = false;
        MarkBandStart();
    }

    /// <summary>
    /// Draws the rules between the columns of the band that is closing, for the height the
    /// content actually reached. Only now is that height known.
    /// </summary>
    private void CloseColumns()
    {
        if (_page is null || _columns.Count <= 1)
            return;

        _columnDepth[_column] = Math.Max(_columnDepth[_column], _cursor);

        if (!_columns.Separator)
            return;

        PageGeometry geometry = Current.Geometry;

        for (int i = 0; i < _columns.Count - 1; i++)
        {
            double depth = Math.Min(
                Math.Max(_columnDepth[i], _columnDepth[i + 1]),
                geometry.ContentBottom);

            if (depth <= _bandTop + 0.5)
                continue;

            double x = _columns.GapCenter(i);
            Current.Items.Add(new StrokeItem
            {
                X = x,
                Y = _bandTop,
                X2 = x,
                Y2 = depth,
                Thickness = 0.5,
                Color = PdfColor.Black,
            });
        }
    }
}
