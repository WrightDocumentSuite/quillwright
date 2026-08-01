using Quillwright.Primitives;

namespace Quillwright.Model;

/// <summary>What a floating object's position is measured from (<c>ST_RelFromH</c>, <c>ST_RelFromV</c>).</summary>
public enum AnchorOrigin : byte
{
    /// <summary>The margin of the page.</summary>
    Margin = 0,

    /// <summary>The edge of the page.</summary>
    Page,

    /// <summary>The edge of the column, horizontally.</summary>
    Column,

    /// <summary>The top of the paragraph the object is anchored in, vertically.</summary>
    Paragraph,

    /// <summary>The character the object is anchored to, horizontally.</summary>
    Character,

    /// <summary>The line the object is anchored in, vertically.</summary>
    Line,
}

/// <summary>
/// How a floating object is placed against its origin: at a distance from it, or lined up
/// with one of its edges (<c>ST_AlignH</c>, <c>ST_AlignV</c>).
/// </summary>
public enum AnchorAlignment : byte
{
    /// <summary>At the offset the anchor gives.</summary>
    Offset = 0,

    /// <summary>At the leading edge: the left horizontally, the top vertically.</summary>
    Start,

    /// <summary>In the middle.</summary>
    Center,

    /// <summary>At the trailing edge: the right horizontally, the bottom vertically.</summary>
    End,

    /// <summary>At the edge nearer the spine, which swaps on facing pages.</summary>
    Inside,

    /// <summary>At the edge further from the spine, which swaps on facing pages.</summary>
    Outside,
}

/// <summary>How the text flows round a floating object (ISO/IEC 29500-1 §17.3.3.35 and after).</summary>
public enum TextWrapping : byte
{
    /// <summary>Text keeps clear of the object's bounding rectangle.</summary>
    Square = 0,

    /// <summary>Text stops above the object and continues below it.</summary>
    TopAndBottom,

    /// <summary>Text follows the object's outline down its left and right sides.</summary>
    Tight,

    /// <summary>Text follows the object's outline on every side, including through it.</summary>
    Through,

    /// <summary>Text ignores the object, which sits over or behind it.</summary>
    None,
}

/// <summary>
/// One corner of a wrapping polygon (<c>wp:start</c>, <c>wp:lineTo</c>). The coordinates count
/// in 21600ths of the object's own size, measured from its upper-left corner — the fixed space
/// this corner of the format inherited from the drawing layer before it.
/// </summary>
/// <param name="X">How far right the corner sits, in 21600ths of the object's width.</param>
/// <param name="Y">How far down it sits, in 21600ths of the object's height.</param>
public readonly record struct PolygonPoint(long X, long Y);

/// <summary>Which sides of a floating object text may flow down (<c>ST_WrapText</c>).</summary>
public enum WrapSides : byte
{
    /// <summary>Both.</summary>
    Both = 0,

    /// <summary>Only the leading side.</summary>
    Left,

    /// <summary>Only the trailing side.</summary>
    Right,

    /// <summary>Whichever side has more room.</summary>
    Largest,
}

/// <summary>
/// Where a floating picture sits on the page and how the text behaves around it
/// (<c>wp:anchor</c>, ISO/IEC 29500-1 §20.4.2.3).
/// </summary>
/// <remarks>
/// A picture in the text flow needs none of this: it sits where its character sits. A floating
/// one is placed against something — a margin, a page edge, the paragraph it is anchored in —
/// and the text has to be told what to do about it. Both formats say the same things here,
/// which is what lets a floating picture survive the trip from a <c>.doc</c>.
/// </remarks>
public sealed class PictureAnchor
{
    /// <summary>
    /// How far from the horizontal origin the picture's left edge sits. Ignored when
    /// <see cref="HorizontalAlignment"/> lines the picture up with an edge instead.
    /// </summary>
    public Length OffsetX { get; init; }

    /// <summary>How far from the vertical origin its top edge sits.</summary>
    public Length OffsetY { get; init; }

    /// <summary>What the horizontal offset is measured from.</summary>
    public AnchorOrigin HorizontalFrom { get; init; } = AnchorOrigin.Column;

    /// <summary>What the vertical offset is measured from.</summary>
    public AnchorOrigin VerticalFrom { get; init; } = AnchorOrigin.Paragraph;

    /// <summary>Which edge of the horizontal origin the picture lines up with, if any.</summary>
    public AnchorAlignment HorizontalAlignment { get; init; }

    /// <summary>Which edge of the vertical origin the picture lines up with, if any.</summary>
    public AnchorAlignment VerticalAlignment { get; init; }

    /// <summary>How the text flows round the picture.</summary>
    public TextWrapping Wrapping { get; init; } = TextWrapping.Square;

    /// <summary>Which sides of the picture the text may flow down.</summary>
    public WrapSides Sides { get; init; }

    /// <summary>
    /// The polygon the text keeps out of when the wrapping follows an outline, or an empty list
    /// when the markup stated none — in which case the object's rectangle stands in for it.
    /// </summary>
    public IReadOnlyList<PolygonPoint> Polygon { get; init; } = [];

    /// <summary>Whether the picture sits behind the text rather than over it.</summary>
    public bool BehindText { get; init; }

    /// <summary>How close the text above may come (<c>distT</c>).</summary>
    public Length DistanceTop { get; init; }

    /// <summary>How close the text below may come (<c>distB</c>).</summary>
    public Length DistanceBottom { get; init; }

    /// <summary>
    /// How close the text on the left may come (<c>distL</c>). An eighth of an inch, which is
    /// what Word puts in every anchor it writes, unless the anchor says otherwise.
    /// </summary>
    public Length DistanceLeft { get; init; } = Length.FromEmu(DefaultSideDistance);

    /// <summary>How close the text on the right may come (<c>distR</c>).</summary>
    public Length DistanceRight { get; init; } = Length.FromEmu(DefaultSideDistance);

    /// <summary>An eighth of an inch in English Metric Units.</summary>
    internal const long DefaultSideDistance = 114300;
}
