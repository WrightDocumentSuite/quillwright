using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The measurements a paragraph is laid out against: how wide each line may be, where it starts,
/// and where the tab stops are.
/// </summary>
internal sealed class ParagraphMetrics
{
    /// <summary>How wide the container is, before indents.</summary>
    public required double ContainerWidth { get; init; }

    /// <summary>The indent from the container's leading edge.</summary>
    public double IndentLeft { get; init; }

    /// <summary>The indent from the container's trailing edge.</summary>
    public double IndentRight { get; init; }

    /// <summary>
    /// Extra indent on the first line. Negative for a hanging indent, which is how a list puts its
    /// marker out to the left of its text.
    /// </summary>
    public double FirstLineIndent { get; init; }

    /// <summary>How the lines line up between the indents.</summary>
    public ParagraphAlignment Alignment { get; init; }

    /// <summary>
    /// The tab stops in force, in ascending order of position. A hanging indent adds an implicit
    /// stop at the left indent, which is what the tab after a list marker jumps to.
    /// </summary>
    public IReadOnlyList<TabStop> Tabs { get; init; } = [];

    /// <summary>The spacing of the implicit tab stops that fill the gaps between the declared ones.</summary>
    public double DefaultTabStop { get; init; } = 36;

    /// <summary>Where a line starts, measured from the container's leading edge.</summary>
    /// <param name="lineIndex">The line's position in the paragraph, counted from zero.</param>
    public double IndentOf(int lineIndex) =>
        lineIndex == 0 ? Math.Max(0, IndentLeft + FirstLineIndent) : IndentLeft;

    /// <summary>How wide a line may be.</summary>
    /// <param name="lineIndex">The line's position in the paragraph, counted from zero.</param>
    public double WidthOf(int lineIndex) => Math.Max(1, ContainerWidth - IndentOf(lineIndex) - IndentRight);

    /// <summary>
    /// The next tab stop after a position, measured from the container's leading edge, and how the
    /// text after it lines up.
    /// </summary>
    /// <param name="position">Where the pen is, measured from the container's leading edge.</param>
    public TabStop NextStop(double position)
    {
        const double Epsilon = 0.01;

        foreach (TabStop stop in Tabs)
        {
            if (stop.Alignment == TabAlignment.Clear)
                continue;

            double at = stop.Position.Points;
            if (at > position + Epsilon)
                return stop with { Position = Primitives.Length.FromPoints(at) };
        }

        // Past the last declared stop the implicit grid takes over, counted from the left margin.
        double spacing = DefaultTabStop > 0 ? DefaultTabStop : 36;
        double next = (Math.Floor((position + Epsilon) / spacing) + 1) * spacing;
        return new TabStop(Primitives.Length.FromPoints(next));
    }
}
