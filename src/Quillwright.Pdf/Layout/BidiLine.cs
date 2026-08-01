using Inkwright.Text;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Puts the fragments of a laid-out line into the order they are painted.
/// </summary>
/// <remarks>
/// The algorithm itself lives in Inkwright, which both this and the spreadsheet renderer share.
/// What belongs here is the part that knows about lines: which fragments are text, which are
/// tabs, and what happens to their positions once they have moved.
/// </remarks>
internal static class BidiLine
{
    /// <summary>
    /// Rearranges a line's fragments into visual order. Tabs pin their surroundings: only the
    /// stretches between them are rearranged, which is how a word processor treats them.
    /// </summary>
    /// <param name="line">The line to rearrange, fragments in logical order.</param>
    /// <param name="baseRightToLeft">Whether the paragraph reads right-to-left.</param>
    public static void Reorder(LineBox line, bool baseRightToLeft)
    {
        ArgumentNullException.ThrowIfNull(line);

        bool any = baseRightToLeft;
        foreach (InlineFragment fragment in line.Fragments)
            any |= fragment is TextFragment { RightToLeft: true };

        if (!any)
            return;

        int start = 0;
        for (int i = 0; i <= line.Fragments.Count; i++)
        {
            if (i == line.Fragments.Count || line.Fragments[i] is TabFragment)
            {
                BidiLayout.ReorderRuns(line.Fragments, start, i, IsRightToLeft, baseRightToLeft);
                start = i + 1;
            }
        }

        foreach (InlineFragment fragment in line.Fragments)
        {
            if (fragment is TextFragment { RightToLeft: true } text)
                text.Visual = BidiLayout.ToVisual(text.Text);
        }

        // The fragments moved; their positions walk the new order, starting where the old did.
        double x = line.Fragments.Count > 0 ? line.Fragments.Min(static fragment => fragment.X) : 0;
        foreach (InlineFragment fragment in line.Fragments)
        {
            fragment.X = x;
            x += fragment.Width;
        }
    }

    private static bool IsRightToLeft(InlineFragment fragment) => fragment is TextFragment { RightToLeft: true };
}
