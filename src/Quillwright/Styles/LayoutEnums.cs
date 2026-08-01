namespace Quillwright.Styles;

/// <summary>Horizontal alignment of a paragraph (<c>ST_Jc</c>).</summary>
public enum ParagraphAlignment : byte
{
    /// <summary>Aligned to the leading edge.</summary>
    Left = 0,

    /// <summary>Centred between the indents.</summary>
    Center,

    /// <summary>Aligned to the trailing edge.</summary>
    Right,

    /// <summary>Stretched to both indents (<c>both</c>).</summary>
    Justify,

    /// <summary>Every character spread across the line.</summary>
    Distribute,

    /// <summary>Thai-style distribution.</summary>
    ThaiDistribute,

    /// <summary>Low kashida justification.</summary>
    LowKashida,

    /// <summary>Medium kashida justification.</summary>
    MediumKashida,

    /// <summary>High kashida justification.</summary>
    HighKashida,

    /// <summary>Aligned to the numbering tab.</summary>
    NumTab,
}

/// <summary>How the line-spacing value is interpreted (<c>ST_LineSpacingRule</c>).</summary>
public enum LineSpacingRule : byte
{
    /// <summary>A multiple of single spacing, in 240ths.</summary>
    Auto = 0,

    /// <summary>Exactly the given height; taller content is clipped.</summary>
    Exact,

    /// <summary>At least the given height, growing for taller content.</summary>
    AtLeast,
}

/// <summary>Vertical alignment of content inside a table cell (<c>ST_VerticalJc</c>).</summary>
public enum VerticalCellAlignment : byte
{
    /// <summary>At the top of the cell.</summary>
    Top = 0,

    /// <summary>Centred vertically.</summary>
    Center,

    /// <summary>Stretched to fill the cell.</summary>
    Both,

    /// <summary>At the bottom of the cell.</summary>
    Bottom,
}

/// <summary>How a row height is applied (<c>ST_HeightRule</c>).</summary>
public enum HeightRule : byte
{
    /// <summary>The row grows to fit its content.</summary>
    Auto = 0,

    /// <summary>Exactly the given height.</summary>
    Exact,

    /// <summary>At least the given height.</summary>
    AtLeast,
}

/// <summary>How a width value is interpreted (<c>ST_TblWidth</c>).</summary>
public enum WidthUnit : byte
{
    /// <summary>No width is specified.</summary>
    Auto = 0,

    /// <summary>An absolute width in twips.</summary>
    Twips,

    /// <summary>A percentage of the available width.</summary>
    Percent,

    /// <summary>No width, laid out by the consumer (<c>nil</c>).</summary>
    None,
}

/// <summary>Flow direction of text in a cell or frame (<c>ST_TextDirection</c>).</summary>
public enum TextDirection : byte
{
    /// <summary>Left to right, top to bottom.</summary>
    LeftToRightTopToBottom = 0,

    /// <summary>Top to bottom, right to left (vertical East Asian).</summary>
    TopToBottomRightToLeft,

    /// <summary>Top to bottom, left to right (vertical, rotated).</summary>
    TopToBottomLeftToRight,

    /// <summary>Bottom to top, left to right (rotated 90° counter-clockwise).</summary>
    BottomToTopLeftToRight,

    /// <summary>Left to right, top to bottom, rotated.</summary>
    LeftToRightTopToBottomRotated,

    /// <summary>Top to bottom, right to left, rotated.</summary>
    TopToBottomRightToLeftRotated,
}

/// <summary>Where a section begins (<c>ST_SectionMark</c>).</summary>
public enum SectionStart : byte
{
    /// <summary>On the next page.</summary>
    NextPage = 0,

    /// <summary>On the same page, immediately after the previous section.</summary>
    Continuous,

    /// <summary>In the next column.</summary>
    NextColumn,

    /// <summary>On the next even-numbered page.</summary>
    EvenPage,

    /// <summary>On the next odd-numbered page.</summary>
    OddPage,
}

/// <summary>Page orientation (<c>ST_PageOrientation</c>).</summary>
public enum PageOrientation : byte
{
    /// <summary>Taller than wide.</summary>
    Portrait = 0,

    /// <summary>Wider than tall.</summary>
    Landscape,
}

/// <summary>Which pages a header or footer applies to (<c>ST_HdrFtr</c>).</summary>
public enum HeaderFooterKind : byte
{
    /// <summary>Every page not covered by a more specific kind.</summary>
    Default = 0,

    /// <summary>The first page of the section.</summary>
    First,

    /// <summary>Even-numbered pages.</summary>
    Even,
}

/// <summary>The numbering scheme of a list level or a page number field (<c>ST_NumberFormat</c>).</summary>
public enum ListNumberFormat : byte
{
    /// <summary>1, 2, 3.</summary>
    Decimal = 0,

    /// <summary>i, ii, iii.</summary>
    LowerRoman,

    /// <summary>I, II, III.</summary>
    UpperRoman,

    /// <summary>a, b, c.</summary>
    LowerLetter,

    /// <summary>A, B, C.</summary>
    UpperLetter,

    /// <summary>1st, 2nd, 3rd.</summary>
    Ordinal,

    /// <summary>One, Two, Three.</summary>
    CardinalText,

    /// <summary>First, Second, Third.</summary>
    OrdinalText,

    /// <summary>A literal character from a symbol font.</summary>
    Bullet,

    /// <summary>01, 02, 03.</summary>
    DecimalZero,

    /// <summary>No number is displayed.</summary>
    None,

    /// <summary>Russian lower-case letters.</summary>
    RussianLower,

    /// <summary>Russian upper-case letters.</summary>
    RussianUpper,

    /// <summary>Numbers enclosed in a circle.</summary>
    DecimalEnclosedCircle,

    /// <summary>Full-width decimal numbers.</summary>
    DecimalFullWidth,

    /// <summary>A scheme this version does not know; the name is kept verbatim.</summary>
    Custom = 255,
}

/// <summary>What follows the number of a list level (<c>ST_LevelSuffix</c>).</summary>
public enum ListLevelSuffix : byte
{
    /// <summary>A tab.</summary>
    Tab = 0,

    /// <summary>A space.</summary>
    Space,

    /// <summary>Nothing.</summary>
    Nothing,
}

/// <summary>Which parts of a document are protected from editing (<c>ST_DocProtect</c>).</summary>
public enum DocumentProtection : byte
{
    /// <summary>No protection.</summary>
    None = 0,

    /// <summary>Only revision-tracked edits are allowed.</summary>
    TrackedChanges,

    /// <summary>Only comments may be added.</summary>
    Comments,

    /// <summary>Only form fields may be filled in.</summary>
    Forms,

    /// <summary>The document is read-only.</summary>
    ReadOnly,
}
