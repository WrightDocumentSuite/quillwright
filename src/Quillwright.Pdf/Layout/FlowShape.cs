using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// One thing the text has to keep out of: where it is, how close text may come, and which side
/// of it the text is allowed to pass on.
/// </summary>
/// <param name="Left">Its left edge with the clearance already added, in points from the page's left.</param>
/// <param name="Right">Its right edge, clearance included.</param>
/// <param name="Top">Its top edge, clearance included, in points down from the top of the page.</param>
/// <param name="Bottom">Its bottom edge, clearance included.</param>
/// <param name="Sides">Which sides text may flow down, when it may flow beside it at all.</param>
/// <param name="Blocking">Whether text may only pass above and below, never beside.</param>
internal readonly record struct WrapObstacle(
    double Left, double Right, double Top, double Bottom, WrapSides Sides, bool Blocking)
{
    /// <summary>
    /// The outline the text follows when the wrapping is tight, in page coordinates, or
    /// <see langword="null"/> when the rectangle is the outline.
    /// </summary>
    public IReadOnlyList<(double X, double Y)>? Outline { get; init; }

    /// <summary>How much clear room the text leaves at the outline's left.</summary>
    public double ClearLeft { get; init; }

    /// <summary>The same, at its right.</summary>
    public double ClearRight { get; init; }

    /// <summary>
    /// The stretch a horizontal strip may not use, or <see langword="null"/> when the strip
    /// misses the outline. For a rectangle that is the rectangle; for an outline it is the
    /// widest extent of the edges crossing the strip, which is what tight wrapping keeps out of
    /// (ISO/IEC 29500-1 §20.4.2.19: text does not enter the object's own extents).
    /// </summary>
    public (double Left, double Right)? SpanIn(double top, double bottom)
    {
        if (Outline is not { Count: >= 3 } outline)
            return (Left, Right);

        double min = double.MaxValue;
        double max = double.MinValue;

        for (int i = 0; i < outline.Count; i++)
        {
            (double x1, double y1) = outline[i];
            (double x2, double y2) = outline[(i + 1) % outline.Count];

            if (Math.Max(y1, y2) <= top || Math.Min(y1, y2) >= bottom)
                continue;

            if (Math.Abs(y2 - y1) < 0.0001)
            {
                // A level edge inside the strip contributes both of its ends.
                min = Math.Min(min, Math.Min(x1, x2));
                max = Math.Max(max, Math.Max(x1, x2));
                continue;
            }

            // The edge clipped to the strip: where it crosses each boundary, or its own end
            // when that end already sits inside.
            double tA = Math.Clamp((top - y1) / (y2 - y1), 0, 1);
            double tB = Math.Clamp((bottom - y1) / (y2 - y1), 0, 1);
            double xA = x1 + (tA * (x2 - x1));
            double xB = x1 + (tB * (x2 - x1));

            min = Math.Min(min, Math.Min(xA, xB));
            max = Math.Max(max, Math.Max(xA, xB));
        }

        if (min > max)
            return null;

        return (min - ClearLeft, max + ClearRight);
    }
}

/// <summary>One free stretch of a line: where it starts and how wide it may be.</summary>
internal readonly record struct FlowBand(double Left, double Width);

/// <summary>
/// Where a line may go: how far down it had to move, and the free stretches it may write in,
/// left to right. Text allowed on both sides of a float gets both stretches; anything else gets
/// exactly one.
/// </summary>
internal readonly record struct BandFit(double Lead, IReadOnlyList<FlowBand> Segments);

/// <summary>
/// The shape the text of one paragraph flows into: the column it sits in, minus whatever floats
/// over it.
/// </summary>
/// <remarks>
/// The line breaker asks for a band for every line it starts: given how far into the paragraph
/// the line begins and roughly how tall it will be, the shape answers where the line may sit.
/// A band beside an obstacle is the free room on the side the obstacle allows; when that is too
/// narrow to be worth writing in — or the obstacle wants no text beside it at all — the line is
/// pushed below it, and the push comes back as lead the composer has to account for.
/// </remarks>
internal sealed class FlowShape
{
    /// <summary>A side narrower than this is not worth writing in, so the line goes below instead.</summary>
    private const double MinimumWidth = 18;

    /// <summary>More rounds than obstacles means the loop is not getting anywhere; give up gracefully.</summary>
    private const int MostAttempts = 32;

    private readonly List<WrapObstacle> _obstacles;
    private readonly double _start;

    private FlowShape(List<WrapObstacle> obstacles, double start)
    {
        _obstacles = obstacles;
        _start = start;
    }

    /// <summary>
    /// Builds the shape a paragraph starting at <paramref name="startY"/> flows into, or
    /// <see langword="null"/> when nothing overlaps its column and the plain width will do.
    /// </summary>
    /// <param name="obstacles">Everything wrapping-relevant on the page, in page coordinates.</param>
    /// <param name="columnLeft">The left edge of the column, in points from the page's left.</param>
    /// <param name="columnWidth">How wide the column is.</param>
    /// <param name="startY">Where the paragraph begins, in points down from the top of the page.</param>
    public static FlowShape? For(
        IReadOnlyList<WrapObstacle> obstacles, double columnLeft, double columnWidth, double startY)
    {
        List<WrapObstacle>? kept = null;

        foreach (WrapObstacle obstacle in obstacles)
        {
            // Everything below the paragraph's start can still matter; everything above it ended.
            if (obstacle.Right <= columnLeft || obstacle.Left >= columnLeft + columnWidth)
                continue;

            if (obstacle.Bottom <= startY)
                continue;

            // The shape works in column coordinates, so a line's indents can be applied to it.
            kept ??= [];
            kept.Add(obstacle with
            {
                Left = obstacle.Left - columnLeft,
                Right = obstacle.Right - columnLeft,
                Outline = Shift(obstacle.Outline, -columnLeft),
            });
        }

        return kept is null ? null : new FlowShape(kept, startY);
    }

    private static IReadOnlyList<(double X, double Y)>? Shift(IReadOnlyList<(double X, double Y)>? outline, double dx)
    {
        if (outline is null)
            return null;

        var moved = new (double X, double Y)[outline.Count];
        for (int i = 0; i < outline.Count; i++)
            moved[i] = (outline[i].X + dx, outline[i].Y);

        return moved;
    }

    /// <summary>Finds the band a line may occupy.</summary>
    /// <param name="offset">How far into the paragraph the line starts, leads of earlier lines included.</param>
    /// <param name="height">How tall the line is expected to be.</param>
    /// <param name="left">Where the line's text area begins, from the paragraph's indents.</param>
    /// <param name="right">Where the line's text area ends.</param>
    public BandFit Fit(double offset, double height, double left, double right)
    {
        double lead = 0;

        for (int attempt = 0; attempt < MostAttempts; attempt++)
        {
            double top = _start + offset + lead;
            double bottom = top + Math.Max(1, height);
            double nextTop = double.MaxValue;
            bool blocked = false;
            List<(double Left, double Right, WrapSides Sides)>? hits = null;

            foreach (WrapObstacle obstacle in _obstacles)
            {
                if (obstacle.Top >= bottom || obstacle.Bottom <= top)
                    continue;

                if (obstacle.Right <= left || obstacle.Left >= right)
                    continue;

                // What the strip really may not use: for an outline that is narrower than the
                // box near its slanted edges, which is the whole point of tight wrapping.
                if (obstacle.SpanIn(top, bottom) is not { } span)
                    continue;

                if (span.Right <= left || span.Left >= right)
                    continue;

                blocked |= obstacle.Blocking;
                nextTop = Math.Min(nextTop, obstacle.Bottom);
                (hits ??= []).Add((span.Left, span.Right, obstacle.Sides));
            }

            if (hits is null)
                return new BandFit(lead, [new FlowBand(left, right - left)]);

            if (!blocked && Free(hits, left, right) is { Count: > 0 } segments)
                return new BandFit(lead, segments);

            // Nothing beside the obstacles is worth writing in, so the line moves below the
            // shallowest of them and looks again from there.
            lead += nextTop - top + 0.01;
        }

        // The shape never opens up, which a page-filling float can arrange. The line keeps the
        // plain width and overlaps, which loses nothing a reader needs.
        return new BandFit(0, [new FlowBand(left, right - left)]);
    }

    /// <summary>
    /// The stretches of a line not covered by anything, left to right, respecting the side each
    /// obstacle says text may pass on. Text that may pass both sides of a float gets both
    /// stretches; a float that asks for the largest side collapses the line to the widest one.
    /// </summary>
    private static List<FlowBand> Free(List<(double Left, double Right, WrapSides Sides)> hits, double left, double right)
    {
        List<(double Left, double Right)> free = [(left, right)];
        bool largestOnly = false;

        foreach ((double spanLeft, double spanRight, WrapSides sides) in hits)
        {
            largestOnly |= sides == WrapSides.Largest;
            List<(double Left, double Right)> next = [];

            foreach ((double from, double to) in free)
            {
                double cutLeft = Math.Max(from, Math.Min(to, spanLeft));
                double cutRight = Math.Max(from, Math.Min(to, spanRight));

                // The side rule drops the segments the obstacle refuses to share a line with.
                if (cutLeft > from && sides != WrapSides.Right)
                    next.Add((from, cutLeft));

                if (cutRight < to && sides != WrapSides.Left)
                    next.Add((cutRight, to));
            }

            free = next;
            if (free.Count == 0)
                return [];
        }

        List<FlowBand> segments = [];
        foreach ((double from, double to) in free)
        {
            if (to - from >= MinimumWidth)
                segments.Add(new FlowBand(from, to - from));
        }

        if (largestOnly && segments.Count > 1)
        {
            FlowBand widest = segments[0];
            foreach (FlowBand band in segments)
            {
                if (band.Width > widest.Width)
                    widest = band;
            }

            return [widest];
        }

        return segments;
    }
}
