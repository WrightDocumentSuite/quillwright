using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Decides which border wins where two of them meet.
/// </summary>
/// <remarks>
/// Every edge inside a table is claimed by up to three parties: the cell on each side and the
/// table itself. Word settles it by weight — the heavier line wins, and where the weight is equal
/// the more emphatic style does. A cell that states an edge always beats the table's own inside
/// border, which is what lets one cell in a plain grid draw a thick rule under itself.
/// </remarks>
internal static class TableBorders
{
    /// <summary>How emphatic each line style is, used only to break a tie on thickness.</summary>
    private static int Rank(BorderStyle style) => style switch
    {
        BorderStyle.Nil or BorderStyle.None => 0,
        BorderStyle.Dotted => 1,
        BorderStyle.Dashed or BorderStyle.DashSmallGap => 2,
        BorderStyle.DotDash or BorderStyle.DotDotDash => 3,
        BorderStyle.Single => 4,
        BorderStyle.Thick => 5,
        BorderStyle.Double or BorderStyle.Triple => 6,
        _ => 4,
    };

    /// <summary>The border to draw where several parties claim one edge.</summary>
    /// <param name="candidates">Every border claiming the edge, strongest claim first.</param>
    /// <returns>The winner, or <see langword="null"/> when nothing is to be drawn.</returns>
    public static BorderLine? Resolve(params ReadOnlySpan<BorderLine?> candidates)
    {
        BorderLine? best = null;
        bool stated = false;

        foreach (BorderLine? candidate in candidates)
        {
            if (candidate is null)
                continue;

            // An edge said to be absent is a statement, and it silences the weaker claims behind
            // it, but a heavier line still wins over it.
            stated = true;
            if (best is null || Beats(candidate, best))
                best = candidate;
        }

        return !stated || best is null || best.IsEmpty ? null : best;
    }

    private static bool Beats(BorderLine candidate, BorderLine current)
    {
        if (candidate.IsEmpty != current.IsEmpty)
            return current.IsEmpty;

        int byWidth = candidate.Width.Twips.CompareTo(current.Width.Twips);
        return byWidth != 0 ? byWidth > 0 : Rank(candidate.Style) > Rank(current.Style);
    }

    /// <summary>The four edges of one cell, after every claim on them has been settled.</summary>
    /// <param name="table">The table the cell belongs to.</param>
    /// <param name="cell">The cell.</param>
    /// <param name="above">The cell directly above, or <see langword="null"/> at the top.</param>
    /// <param name="before">The cell directly before, or <see langword="null"/> at the leading edge.</param>
    /// <param name="isFirstRow">Whether the cell is in the first row.</param>
    /// <param name="isLastRow">Whether the cell is in the last row.</param>
    /// <param name="isFirstColumn">Whether the cell starts at the leading edge.</param>
    /// <param name="isLastColumn">Whether the cell ends at the trailing edge.</param>
    public static CellEdges EdgesOf(
        Table table,
        TableCell cell,
        TableCell? above,
        TableCell? before,
        bool isFirstRow,
        bool isLastRow,
        bool isFirstColumn,
        bool isLastColumn)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(cell);

        BorderSet? own = cell.Format.Borders;
        BorderSet? outer = table.Format.Borders;

        BorderLine? top = Resolve(
            own?.Top,
            above?.Format.Borders?.Bottom,
            isFirstRow ? outer?.Top : outer?.InsideHorizontal);

        BorderLine? left = Resolve(
            own?.Left,
            before?.Format.Borders?.Right,
            isFirstColumn ? outer?.Left : outer?.InsideVertical);

        BorderLine? right = Resolve(own?.Right, isLastColumn ? outer?.Right : outer?.InsideVertical);
        BorderLine? bottom = Resolve(own?.Bottom, isLastRow ? outer?.Bottom : outer?.InsideHorizontal);

        return new CellEdges(left, top, right, bottom);
    }
}
