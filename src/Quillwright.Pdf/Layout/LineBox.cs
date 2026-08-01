namespace Quillwright.Pdf.Layout;

/// <summary>
/// One laid-out line: the fragments on it, where the baseline sits inside it, and how tall the
/// line box is once the paragraph's spacing rule has had its say.
/// </summary>
internal sealed class LineBox
{
    /// <summary>The fragments of the line, in reading order.</summary>
    public List<InlineFragment> Fragments { get; } = [];

    /// <summary>How far the tallest fragment reaches above the baseline.</summary>
    public double Ascent { get; set; }

    /// <summary>How far the deepest fragment reaches below the baseline.</summary>
    public double Descent { get; set; }

    /// <summary>The total height of the line box, spacing rule included.</summary>
    public double Height { get; set; }

    /// <summary>Where the baseline sits, measured down from the top of the line box.</summary>
    public double BaselineFromTop { get; set; }

    /// <summary>The width the fragments actually occupy.</summary>
    public double Width { get; set; }

    /// <summary>How far the line starts from the left edge of the paragraph's text area.</summary>
    public double IndentLeft { get; set; }

    /// <summary>How much of the text area the line may use.</summary>
    public double AvailableWidth { get; set; }

    /// <summary>
    /// Empty room above the line, put there by a float the line had to move below. It belongs to
    /// the line the way its height does, but only while the line sits where the float was when
    /// it was measured; a line carried to another page leaves the gap behind.
    /// </summary>
    public double Lead { get; set; }

    /// <summary>
    /// Whether this box continues the line before it rather than starting one of its own: the
    /// far side of a float both sides of which may carry text. Boxes joined this way share one
    /// vertical position and move between pages together.
    /// </summary>
    public bool JoinsPrevious { get; set; }

    /// <summary>Whether this is the last line of its paragraph, which justification must not stretch.</summary>
    public bool IsLastLine { get; set; }

    /// <summary>Whether an explicit break inside the paragraph puts this line on a new page.</summary>
    public bool StartsNewPage { get; set; }

    /// <summary>
    /// Whether an explicit break puts this line at the top of the next column. In a section with
    /// one column that is the top of the next page, which is what Word does with it too.
    /// </summary>
    public bool StartsNewColumn { get; set; }

    /// <summary>How tall the line is when it carries nothing, taken from the formatting in force.</summary>
    public CharacterStyle? EmptyStyle { get; set; }

    /// <summary>
    /// The notes referenced from this line, which have to be printed on whatever page it lands on.
    /// A line owing a note needs room for the note as well as for itself.
    /// </summary>
    public List<NoteMark> Notes { get; } = [];

    /// <summary>
    /// Extra width added to each space by justification, in points. Applied to the spaces of every
    /// fragment rather than to the gaps between fragments, which is what a word processor does.
    /// </summary>
    public double ExtraSpaceWidth { get; set; }

    /// <summary>How many spaces the line holds, which is what justification is shared between.</summary>
    public int SpaceCount
    {
        get
        {
            int total = 0;
            foreach (InlineFragment fragment in Fragments)
            {
                if (fragment is TextFragment text)
                    total += text.SpaceCount;
            }

            return total;
        }
    }

    /// <summary>
    /// How many of the line's spaces sit past its last visible character. They are not stretched
    /// by justification, because the width they would gain hangs off the end of the line.
    /// </summary>
    public int TrailingSpaceCount
    {
        get
        {
            int count = 0;
            for (int i = Fragments.Count - 1; i >= 0; i--)
            {
                if (Fragments[i] is not TextFragment text)
                    break;

                string content = text.Text;
                int end = content.Length;
                while (end > 0 && content[end - 1] == ' ')
                {
                    end--;
                    count++;
                }

                if (end > 0)
                    break;
            }

            return count;
        }
    }

    /// <summary>Whether the line carries nothing that would print.</summary>
    public bool IsEmpty => Fragments.Count == 0;
}

/// <summary>
/// Walks the rows of a paragraph: a row is a line as the reader sees it — one box, or several
/// boxes joined across a float that carries text on both of its sides.
/// </summary>
internal static class LineRows
{
    /// <summary>The index just past the row that starts at <paramref name="index"/>.</summary>
    public static int End(IReadOnlyList<LineBox> lines, int index)
    {
        int end = index + 1;
        while (end < lines.Count && lines[end].JoinsPrevious)
            end++;

        return end;
    }

    /// <summary>The start of the row the line at <paramref name="index"/> belongs to.</summary>
    public static int Start(IReadOnlyList<LineBox> lines, int index)
    {
        while (index > 0 && lines[index].JoinsPrevious)
            index--;

        return index;
    }

    /// <summary>How tall the row is: the tallest of the boxes that share it.</summary>
    public static double Height(IReadOnlyList<LineBox> lines, int index, int end)
    {
        double tallest = 0;
        for (int i = index; i < end; i++)
            tallest = Math.Max(tallest, lines[i].Height);

        return tallest;
    }
}
