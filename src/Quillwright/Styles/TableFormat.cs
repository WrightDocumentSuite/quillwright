using Quillwright.Primitives;

namespace Quillwright.Styles;

/// <summary>How a table's column widths are decided (<c>ST_TblLayoutType</c>).</summary>
public enum TableLayout : byte
{
    /// <summary>Widths follow the content (<c>autofit</c>).</summary>
    AutoFit = 0,

    /// <summary>Widths come from the grid and the cell definitions.</summary>
    Fixed,
}

/// <summary>Horizontal placement of a table on the page (<c>w:jc</c> on <c>w:tblPr</c>).</summary>
public enum TableAlignment : byte
{
    /// <summary>Against the leading margin.</summary>
    Left = 0,

    /// <summary>Centred between the margins.</summary>
    Center,

    /// <summary>Against the trailing margin.</summary>
    Right,
}

/// <summary>
/// Which conditional parts of a table style apply (<c>w:tblLook</c>). Without these flags a
/// table style's banding and header formatting are defined but never drawn.
/// </summary>
[Flags]
public enum TableStyleOptions
{
    /// <summary>No conditional formatting.</summary>
    None = 0,

    /// <summary>Apply the first-row formatting.</summary>
    FirstRow = 1 << 0,

    /// <summary>Apply the last-row formatting.</summary>
    LastRow = 1 << 1,

    /// <summary>Apply the first-column formatting.</summary>
    FirstColumn = 1 << 2,

    /// <summary>Apply the last-column formatting.</summary>
    LastColumn = 1 << 3,

    /// <summary>Suppress row banding.</summary>
    NoHorizontalBanding = 1 << 4,

    /// <summary>Suppress column banding.</summary>
    NoVerticalBanding = 1 << 5,

    /// <summary>What Word applies to a freshly inserted table.</summary>
    Default = FirstRow | LastRow | FirstColumn | LastColumn | NoHorizontalBanding | NoVerticalBanding,
}

/// <summary>The padding inside table cells.</summary>
public sealed record CellMargins
{
    /// <summary>No margins specified.</summary>
    public static CellMargins Empty { get; } = new();

    /// <summary>Space above the content.</summary>
    public TableWidth? Top { get; init; }

    /// <summary>Space before the content.</summary>
    public TableWidth? Left { get; init; }

    /// <summary>Space below the content.</summary>
    public TableWidth? Bottom { get; init; }

    /// <summary>Space after the content.</summary>
    public TableWidth? Right { get; init; }

    /// <summary>Returns <see langword="true"/> when nothing is specified.</summary>
    public bool IsEmpty => Top is null && Left is null && Bottom is null && Right is null;

    /// <summary>Creates uniform margins.</summary>
    public static CellMargins Uniform(Length value)
    {
        TableWidth width = TableWidth.FromLength(value);
        return new CellMargins { Top = width, Left = width, Bottom = width, Right = width };
    }
}

/// <summary>Table-level formatting (<c>w:tblPr</c>).</summary>
public sealed record TableFormat
{
    /// <summary>A format that overrides nothing.</summary>
    public static TableFormat Default { get; } = new();

    /// <summary>Identifier of the table style (<c>w:tblStyle</c>).</summary>
    public string? StyleId { get; init; }

    /// <summary>The floating-position element, kept verbatim (<c>w:tblpPr</c>).</summary>
    public string? FloatingPositionXml { get; init; }

    /// <summary>The overlap element, kept verbatim (<c>w:tblOverlap</c>).</summary>
    public string? OverlapXml { get; init; }

    /// <summary>Lays the table out right to left (<c>w:bidiVisual</c>).</summary>
    public bool? RightToLeft { get; init; }

    /// <summary>Rows per band for row banding (<c>w:tblStyleRowBandSize</c>).</summary>
    public int? RowBandSize { get; init; }

    /// <summary>Columns per band for column banding (<c>w:tblStyleColBandSize</c>).</summary>
    public int? ColumnBandSize { get; init; }

    /// <summary>Preferred total width (<c>w:tblW</c>).</summary>
    public TableWidth? Width { get; init; }

    /// <summary>Horizontal placement on the page (<c>w:jc</c>).</summary>
    public TableAlignment? Alignment { get; init; }

    /// <summary>Space between cells (<c>w:tblCellSpacing</c>).</summary>
    public TableWidth? CellSpacing { get; init; }

    /// <summary>Indent of the whole table from the leading margin (<c>w:tblInd</c>).</summary>
    public TableWidth? Indent { get; init; }

    /// <summary>Borders of the table and between its cells (<c>w:tblBorders</c>).</summary>
    public BorderSet? Borders { get; init; }

    /// <summary>Background fill (<c>w:shd</c>).</summary>
    public Shading? Shading { get; init; }

    /// <summary>How column widths are decided (<c>w:tblLayout</c>).</summary>
    public TableLayout? Layout { get; init; }

    /// <summary>Default padding inside cells (<c>w:tblCellMar</c>).</summary>
    public CellMargins? CellMargins { get; init; }

    /// <summary>Which conditional parts of the table style apply (<c>w:tblLook</c>).</summary>
    public TableStyleOptions? StyleOptions { get; init; }

    /// <summary>Accessible caption (<c>w:tblCaption</c>).</summary>
    public string? Caption { get; init; }

    /// <summary>Accessible description (<c>w:tblDescription</c>).</summary>
    public string? Description { get; init; }

    /// <summary>The revision record of a formatting change, kept verbatim (<c>w:tblPrChange</c>).</summary>
    public string? ChangeXml { get; init; }

    /// <summary>Children of <c>w:tblPr</c> this version does not model.</summary>
    public string? Extensions { get; init; }

    /// <summary>Returns <see langword="true"/> when the format overrides nothing.</summary>
    public bool IsEmpty => Equals(Default);
}

/// <summary>Row-level formatting (<c>w:trPr</c>).</summary>
public sealed record TableRowFormat
{
    /// <summary>A format that overrides nothing.</summary>
    public static TableRowFormat Default { get; } = new();

    /// <summary>The conditional-formatting element, kept verbatim (<c>w:cnfStyle</c>).</summary>
    public string? ConditionalFormattingXml { get; init; }

    /// <summary>The HTML div association, kept verbatim (<c>w:divId</c>).</summary>
    public string? DivIdXml { get; init; }

    /// <summary>Grid columns skipped before the first cell (<c>w:gridBefore</c>).</summary>
    public int? GridBefore { get; init; }

    /// <summary>Grid columns skipped after the last cell (<c>w:gridAfter</c>).</summary>
    public int? GridAfter { get; init; }

    /// <summary>Width of the skipped columns before the row (<c>w:wBefore</c>).</summary>
    public TableWidth? WidthBefore { get; init; }

    /// <summary>Width of the skipped columns after the row (<c>w:wAfter</c>).</summary>
    public TableWidth? WidthAfter { get; init; }

    /// <summary>Prevents the row from being split across pages (<c>w:cantSplit</c>).</summary>
    public bool? CannotSplit { get; init; }

    /// <summary>Row height (<c>w:trHeight/@w:val</c>).</summary>
    public Length? Height { get; init; }

    /// <summary>How the height is applied (<c>w:trHeight/@w:hRule</c>).</summary>
    public HeightRule? HeightRule { get; init; }

    /// <summary>Repeats the row at the top of every page (<c>w:tblHeader</c>).</summary>
    public bool? IsHeader { get; init; }

    /// <summary>Space between the cells of this row (<c>w:tblCellSpacing</c>).</summary>
    public TableWidth? CellSpacing { get; init; }

    /// <summary>Horizontal placement of the row (<c>w:jc</c>).</summary>
    public TableAlignment? Alignment { get; init; }

    /// <summary>Hides the row (<c>w:hidden</c>).</summary>
    public bool? Hidden { get; init; }

    /// <summary>The revision record marking the row as inserted, kept verbatim (<c>w:ins</c>).</summary>
    public string? InsertedXml { get; init; }

    /// <summary>The revision record marking the row as deleted, kept verbatim (<c>w:del</c>).</summary>
    public string? DeletedXml { get; init; }

    /// <summary>The revision record of a formatting change, kept verbatim (<c>w:trPrChange</c>).</summary>
    public string? ChangeXml { get; init; }

    /// <summary>Children of <c>w:trPr</c> this version does not model.</summary>
    public string? Extensions { get; init; }

    /// <summary>Returns <see langword="true"/> when the format overrides nothing.</summary>
    public bool IsEmpty => Equals(Default);
}

/// <summary>How a cell takes part in a vertical merge (<c>w:vMerge</c>).</summary>
public enum VerticalMerge : byte
{
    /// <summary>Not merged.</summary>
    None = 0,

    /// <summary>Starts a merged region (<c>restart</c>).</summary>
    Restart,

    /// <summary>Continues the merged region above (<c>continue</c>).</summary>
    Continue,
}

/// <summary>Cell-level formatting (<c>w:tcPr</c>).</summary>
public sealed record TableCellFormat
{
    /// <summary>A format that overrides nothing.</summary>
    public static TableCellFormat Default { get; } = new();

    /// <summary>The conditional-formatting element, kept verbatim (<c>w:cnfStyle</c>).</summary>
    public string? ConditionalFormattingXml { get; init; }

    /// <summary>Preferred width (<c>w:tcW</c>).</summary>
    public TableWidth? Width { get; init; }

    /// <summary>Number of grid columns this cell spans (<c>w:gridSpan</c>).</summary>
    public int? GridSpan { get; init; }

    /// <summary>Legacy horizontal merge, kept verbatim (<c>w:hMerge</c>).</summary>
    public string? HorizontalMergeXml { get; init; }

    /// <summary>Participation in a vertical merge (<c>w:vMerge</c>).</summary>
    public VerticalMerge? VerticalMerge { get; init; }

    /// <summary>Cell borders (<c>w:tcBorders</c>).</summary>
    public BorderSet? Borders { get; init; }

    /// <summary>Background fill (<c>w:shd</c>).</summary>
    public Shading? Shading { get; init; }

    /// <summary>Keeps the content on one line (<c>w:noWrap</c>).</summary>
    public bool? NoWrap { get; init; }

    /// <summary>Padding inside this cell (<c>w:tcMar</c>).</summary>
    public CellMargins? Margins { get; init; }

    /// <summary>Flow direction of the content (<c>w:textDirection</c>).</summary>
    public TextDirection? TextDirection { get; init; }

    /// <summary>Shrinks the text to fit the cell (<c>w:tcFitText</c>).</summary>
    public bool? FitText { get; init; }

    /// <summary>Vertical alignment of the content (<c>w:vAlign</c>).</summary>
    public VerticalCellAlignment? VerticalAlignment { get; init; }

    /// <summary>Ignores the end-of-cell mark when measuring the row (<c>w:hideMark</c>).</summary>
    public bool? HideMark { get; init; }

    /// <summary>Accessibility header association, kept verbatim (<c>w:headers</c>).</summary>
    public string? HeadersXml { get; init; }

    /// <summary>The revision records of cell insertion, deletion or merge, kept verbatim.</summary>
    public string? RevisionXml { get; init; }

    /// <summary>Children of <c>w:tcPr</c> this version does not model.</summary>
    public string? Extensions { get; init; }

    /// <summary>Returns <see langword="true"/> when the format overrides nothing.</summary>
    public bool IsEmpty => Equals(Default);
}
