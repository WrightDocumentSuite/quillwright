using Quillwright.Primitives;

namespace Quillwright.Model;

/// <summary>What a chart draws (ISO/IEC 29500-1 §21.2.2).</summary>
public enum ChartKind : byte
{
    /// <summary>Something this version does not name.</summary>
    Unknown = 0,

    /// <summary>Bars or columns.</summary>
    Bar,

    /// <summary>A line through the points.</summary>
    Line,

    /// <summary>A pie.</summary>
    Pie,

    /// <summary>A pie with a hole in it.</summary>
    Doughnut,

    /// <summary>Filled area beneath the line.</summary>
    Area,

    /// <summary>Points against two value axes.</summary>
    Scatter,

    /// <summary>Points sized by a third value.</summary>
    Bubble,

    /// <summary>Values around a circle.</summary>
    Radar,

    /// <summary>A three-dimensional surface.</summary>
    Surface,

    /// <summary>High, low, open and close.</summary>
    Stock,
}

/// <summary>One series of a chart, with the values the file cached for it.</summary>
public sealed class ChartSeries
{
    /// <summary>What the series is called, when the chart names it.</summary>
    public string? Name { get; init; }

    /// <summary>The category each value belongs to, in order.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The values, in order; an entry is missing where the source had a gap.</summary>
    public IReadOnlyList<double?> Values { get; init; } = [];

    /// <summary>
    /// How big each bubble is, for a chart that draws them — a third stream of numbers beside
    /// the categories and the values. Empty for every other kind.
    /// </summary>
    public IReadOnlyList<double?> BubbleSizes { get; init; } = [];

    /// <summary>
    /// What this series is drawn as, when the chart says so for each of them separately.
    /// </summary>
    /// <remarks>
    /// A chart can combine two kinds — bars with a line over them — by putting its series into
    /// groups and giving each group a kind of its own. <see cref="Chart.Kind"/> is the first
    /// group's, which is what the chart mostly is; this is what this series actually is.
    /// </remarks>
    public ChartKind Kind { get; init; }
}

/// <summary>
/// A chart the document draws, read from the numbers it caches rather than from the workbook
/// they came out of.
/// </summary>
/// <remarks>
/// <para>
/// Reading is one way, as it is for macros and embedded objects: the chart part is copied
/// through untouched on save, so what is read here is what a saved file draws.
/// </para>
/// <para>
/// A chart in a package keeps a copy of its own data — the categories and values it was last
/// drawn from — beside the reference to the workbook that supplied them. That cache is what is
/// read, because it is what the document actually shows. A chart in a legacy document is an
/// embedded object instead, and only says what it is.
/// </para>
/// </remarks>
public sealed class Chart
{
    /// <summary>Where the chart lives: the package part, or the storage inside a legacy file.</summary>
    public required string Location { get; init; }

    /// <summary>What the chart draws.</summary>
    public ChartKind Kind { get; init; }

    /// <summary>The chart's title, when it has one.</summary>
    public string? Title { get; init; }

    /// <summary>The series it draws, in order.</summary>
    public IReadOnlyList<ChartSeries> Series { get; init; } = [];
}

/// <summary>
/// Where a chart sits in the text and how big it is (<c>w:drawing</c> holding a
/// <c>c:chart</c> reference), with the markup kept as the bytes it arrived as.
/// </summary>
/// <remarks>
/// The chart itself is a part of its own, read into <see cref="WordDocument.Charts"/>; this is
/// only the frame the document reserves for it. The two are joined by <see cref="Location"/>
/// rather than by a reference, so neither has to be read before the other and neither keeps the
/// other alive. Nothing here is written: the markup goes back exactly as it came.
/// </remarks>
public sealed class ChartFrame : InlineObject
{
    /// <summary>Creates a frame round the markup that reserves it.</summary>
    /// <param name="xml">The verbatim markup.</param>
    /// <param name="location">The chart part the frame draws, when the relationship resolved.</param>
    public ChartFrame(string xml, string? location)
    {
        Xml = xml;
        Location = location;
    }

    /// <summary>The verbatim markup.</summary>
    public string Xml { get; }

    /// <summary>Absolute name of the chart part, matching <see cref="Chart.Location"/>.</summary>
    public string? Location { get; }

    /// <summary>How wide the frame is.</summary>
    public Length Width { get; init; }

    /// <summary>How tall it is.</summary>
    public Length Height { get; init; }

    /// <summary>Whether the frame flows with the text rather than floating.</summary>
    public bool IsInline { get; init; } = true;

    /// <summary>Where a floating frame sits, or <see langword="null"/> when it flows.</summary>
    public PictureAnchor? Anchor { get; init; }
}
