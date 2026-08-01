using Quillwright.Primitives;

namespace Quillwright.Styles;

/// <summary>
/// One edge of a border box. Immutable, so identical borders share a single instance and
/// comparing two of them is a value comparison.
/// </summary>
public sealed record BorderLine
{
    /// <summary>A border that draws nothing and reserves no space.</summary>
    public static BorderLine None { get; } = new();

    /// <summary>The line style.</summary>
    public BorderStyle Style { get; init; } = BorderStyle.Nil;

    /// <summary>
    /// The OOXML name of an art border or of a style this version does not know. Only set
    /// when <see cref="Style"/> is <see cref="BorderStyle.Custom"/>.
    /// </summary>
    public string? CustomStyle { get; init; }

    /// <summary>Line thickness. OOXML stores it in eighths of a point.</summary>
    public Length Width { get; init; }

    /// <summary>Distance between the line and the content it surrounds. OOXML stores it in points.</summary>
    public Length Space { get; init; }

    /// <summary>Line colour.</summary>
    public WordColor Color { get; init; }

    /// <summary>Whether the line is drawn with a shadow.</summary>
    public bool Shadow { get; init; }

    /// <summary>Whether the line uses the frame (reversed) appearance.</summary>
    public bool Frame { get; init; }

    /// <summary>Returns <see langword="true"/> when this edge draws nothing.</summary>
    public bool IsEmpty => Style is BorderStyle.Nil or BorderStyle.None;

    /// <summary>Creates a single line of the given thickness and colour.</summary>
    public static BorderLine Single(Length width, WordColor color) =>
        new() { Style = BorderStyle.Single, Width = width, Color = color };
}

/// <summary>
/// The border box of a paragraph, table or cell. Every edge is optional; a <see langword="null"/>
/// edge inherits from the style chain, while <see cref="BorderLine.None"/> explicitly removes it.
/// </summary>
public sealed record BorderSet
{
    /// <summary>A border box with nothing specified.</summary>
    public static BorderSet Empty { get; } = new();

    /// <summary>The top edge.</summary>
    public BorderLine? Top { get; init; }

    /// <summary>The leading edge.</summary>
    public BorderLine? Left { get; init; }

    /// <summary>The bottom edge.</summary>
    public BorderLine? Bottom { get; init; }

    /// <summary>The trailing edge.</summary>
    public BorderLine? Right { get; init; }

    /// <summary>Horizontal lines between the rows of a table, or between paragraphs sharing a border.</summary>
    public BorderLine? InsideHorizontal { get; init; }

    /// <summary>Vertical lines between the cells of a table row.</summary>
    public BorderLine? InsideVertical { get; init; }

    /// <summary>The diagonal from the top-left to the bottom-right of a cell.</summary>
    public BorderLine? DiagonalDown { get; init; }

    /// <summary>The diagonal from the bottom-left to the top-right of a cell.</summary>
    public BorderLine? DiagonalUp { get; init; }

    /// <summary>The vertical bar drawn beside a paragraph (<c>w:bar</c>).</summary>
    public BorderLine? Bar { get; init; }

    /// <summary>Returns <see langword="true"/> when no edge is specified.</summary>
    public bool IsEmpty =>
        Top is null && Left is null && Bottom is null && Right is null &&
        InsideHorizontal is null && InsideVertical is null &&
        DiagonalDown is null && DiagonalUp is null && Bar is null;

    /// <summary>Creates a box with the same line on all four outer edges.</summary>
    public static BorderSet All(BorderLine line) =>
        new() { Top = line, Left = line, Bottom = line, Right = line };

    /// <summary>Creates a box with the same line on all four outer edges and between cells.</summary>
    public static BorderSet AllWithInside(BorderLine line) =>
        new() { Top = line, Left = line, Bottom = line, Right = line, InsideHorizontal = line, InsideVertical = line };
}

/// <summary>
/// A background fill: a pattern drawn in <see cref="Color"/> over <see cref="Fill"/>.
/// The common case is <see cref="ShadingPattern.Clear"/> with only <see cref="Fill"/> set.
/// </summary>
public sealed record Shading
{
    /// <summary>No shading.</summary>
    public static Shading None { get; } = new();

    /// <summary>The pattern drawn over the fill.</summary>
    public ShadingPattern Pattern { get; init; } = ShadingPattern.Nil;

    /// <summary>
    /// The OOXML name of a percentage pattern or of a value this version does not know.
    /// Only set when <see cref="Pattern"/> is <see cref="ShadingPattern.Custom"/>.
    /// </summary>
    public string? CustomPattern { get; init; }

    /// <summary>The background colour.</summary>
    public WordColor Fill { get; init; }

    /// <summary>The colour the pattern is drawn in.</summary>
    public WordColor Color { get; init; }

    /// <summary>Returns <see langword="true"/> when this shading paints nothing.</summary>
    public bool IsEmpty => Pattern == ShadingPattern.Nil && Fill.IsAuto && Color.IsAuto;

    /// <summary>Creates a plain background fill.</summary>
    public static Shading Solid(WordColor fill) => new() { Pattern = ShadingPattern.Clear, Fill = fill };
}

/// <summary>A single tab stop of a paragraph.</summary>
/// <param name="Position">Distance from the leading margin.</param>
/// <param name="Alignment">How text lines up on the stop.</param>
/// <param name="Leader">The filler drawn in front of the stop.</param>
public readonly record struct TabStop(Length Position, TabAlignment Alignment = TabAlignment.Left, TabLeader Leader = TabLeader.None);

/// <summary>
/// A width that can be absolute, relative or unspecified — the <c>CT_TblWidth</c> pattern
/// used for table, cell and margin widths.
/// </summary>
/// <param name="Unit">How <paramref name="Value"/> is interpreted.</param>
/// <param name="Value">Twips, or fiftieths of a percent when <paramref name="Unit"/> is <see cref="WidthUnit.Percent"/>.</param>
public readonly record struct TableWidth(WidthUnit Unit, int Value)
{
    /// <summary>An unspecified width.</summary>
    public static TableWidth Auto => new(WidthUnit.Auto, 0);

    /// <summary>Creates an absolute width.</summary>
    public static TableWidth FromLength(Length length) => new(WidthUnit.Twips, length.Twips);

    /// <summary>Creates a relative width from a percentage.</summary>
    public static TableWidth FromPercent(double percent) => new(WidthUnit.Percent, (int)Math.Round(percent * 50));

    /// <summary>The absolute width, meaningful when <see cref="Unit"/> is <see cref="WidthUnit.Twips"/>.</summary>
    public Length Length => Primitives.Length.FromTwips(Value);

    /// <summary>The relative width, meaningful when <see cref="Unit"/> is <see cref="WidthUnit.Percent"/>.</summary>
    public double Percent => Value / 50.0;
}
