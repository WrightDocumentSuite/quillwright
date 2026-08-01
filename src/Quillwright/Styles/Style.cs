namespace Quillwright.Styles;

/// <summary>What a style can be applied to (<c>ST_StyleType</c>).</summary>
public enum StyleKind : byte
{
    /// <summary>Applies to whole paragraphs.</summary>
    Paragraph = 0,

    /// <summary>Applies to runs.</summary>
    Character,

    /// <summary>Applies to tables.</summary>
    Table,

    /// <summary>Supplies numbering to a list.</summary>
    Numbering,
}

/// <summary>Which part of a table a conditional style applies to (<c>ST_TblStyleOverrideType</c>).</summary>
public enum TableStyleRegion : byte
{
    /// <summary>Every cell.</summary>
    WholeTable = 0,

    /// <summary>The header row.</summary>
    FirstRow,

    /// <summary>The total row.</summary>
    LastRow,

    /// <summary>The leading column.</summary>
    FirstColumn,

    /// <summary>The trailing column.</summary>
    LastColumn,

    /// <summary>Odd row bands.</summary>
    Band1Horizontal,

    /// <summary>Even row bands.</summary>
    Band2Horizontal,

    /// <summary>Odd column bands.</summary>
    Band1Vertical,

    /// <summary>Even column bands.</summary>
    Band2Vertical,

    /// <summary>The top-leading corner cell.</summary>
    NorthWestCell,

    /// <summary>The top-trailing corner cell.</summary>
    NorthEastCell,

    /// <summary>The bottom-leading corner cell.</summary>
    SouthWestCell,

    /// <summary>The bottom-trailing corner cell.</summary>
    SouthEastCell,
}

/// <summary>Formatting a table style applies to one region of the table (<c>w:tblStylePr</c>).</summary>
public sealed class ConditionalTableStyle
{
    /// <summary>Which part of the table this applies to.</summary>
    public TableStyleRegion Region { get; set; }

    /// <summary>Paragraph formatting for the region.</summary>
    public ParagraphFormat ParagraphFormat { get; set; } = ParagraphFormat.Default;

    /// <summary>Character formatting for the region.</summary>
    public RunFormat RunFormat { get; set; } = RunFormat.Default;

    /// <summary>Table formatting for the region.</summary>
    public TableFormat TableFormat { get; set; } = TableFormat.Default;

    /// <summary>Row formatting for the region.</summary>
    public TableRowFormat RowFormat { get; set; } = TableRowFormat.Default;

    /// <summary>Cell formatting for the region.</summary>
    public TableCellFormat CellFormat { get; set; } = TableCellFormat.Default;
}

/// <summary>
/// A named bundle of formatting. Styles form chains through <see cref="BasedOn"/>, and the
/// resolver walks a chain from its root down so that overrides land in the right order.
/// </summary>
public sealed class Style
{
    /// <summary>Creates a style.</summary>
    /// <param name="id">Identifier used by <c>w:pStyle</c>, <c>w:rStyle</c> and <c>w:tblStyle</c>.</param>
    /// <param name="kind">What the style applies to.</param>
    public Style(string id, StyleKind kind)
    {
        Id = id;
        Kind = kind;
    }

    /// <summary>Identifier referenced from paragraph, run and table properties.</summary>
    public string Id { get; set; }

    /// <summary>What the style applies to.</summary>
    public StyleKind Kind { get; set; }

    /// <summary>The name shown in the user interface (<c>w:name</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Alternative names the style answers to (<c>w:aliases</c>).</summary>
    public string? Aliases { get; set; }

    /// <summary>Identifier of the style this one extends (<c>w:basedOn</c>).</summary>
    public string? BasedOn { get; set; }

    /// <summary>Style applied to the paragraph that follows one in this style (<c>w:next</c>).</summary>
    public string? NextStyle { get; set; }

    /// <summary>The character style paired with this paragraph style (<c>w:link</c>).</summary>
    public string? LinkedStyle { get; set; }

    /// <summary>Whether this is the default style of its kind (<c>w:default</c>).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Whether the style was created by the author rather than shipped with Word (<c>w:customStyle</c>).</summary>
    public bool IsCustom { get; set; }

    /// <summary>Sort order in the styles gallery (<c>w:uiPriority</c>).</summary>
    public int? Priority { get; set; }

    /// <summary>Hides the style until it is used (<c>w:semiHidden</c>).</summary>
    public bool SemiHidden { get; set; }

    /// <summary>Shows the style once it has been used (<c>w:unhideWhenUsed</c>).</summary>
    public bool UnhideWhenUsed { get; set; }

    /// <summary>Puts the style on the quick-access gallery (<c>w:qFormat</c>).</summary>
    public bool QuickFormat { get; set; }

    /// <summary>Prevents the style from being applied (<c>w:locked</c>).</summary>
    public bool Locked { get; set; }

    /// <summary>Redefines the style from the formatting the author applies (<c>w:autoRedefine</c>).</summary>
    public bool AutoRedefine { get; set; }

    /// <summary>Hides the style from the user interface entirely (<c>w:hidden</c>).</summary>
    public bool Hidden { get; set; }

    /// <summary>Marks the style as belonging to an e-mail personality (<c>w:personal</c>).</summary>
    public bool Personal { get; set; }

    /// <summary>Marks the style as used when composing e-mail (<c>w:personalCompose</c>).</summary>
    public bool PersonalCompose { get; set; }

    /// <summary>Marks the style as used when replying to e-mail (<c>w:personalReply</c>).</summary>
    public bool PersonalReply { get; set; }

    /// <summary>The revision-save identifier element, kept verbatim (<c>w:rsid</c>).</summary>
    public string? RsidXml { get; set; }

    /// <summary>Paragraph formatting the style contributes.</summary>
    public ParagraphFormat ParagraphFormat { get; set; } = ParagraphFormat.Default;

    /// <summary>Character formatting the style contributes.</summary>
    public RunFormat RunFormat { get; set; } = RunFormat.Default;

    /// <summary>Table formatting the style contributes.</summary>
    public TableFormat TableFormat { get; set; } = TableFormat.Default;

    /// <summary>Row formatting the style contributes.</summary>
    public TableRowFormat RowFormat { get; set; } = TableRowFormat.Default;

    /// <summary>Cell formatting the style contributes.</summary>
    public TableCellFormat CellFormat { get; set; } = TableCellFormat.Default;

    /// <summary>Formatting applied to specific regions of a table (<c>w:tblStylePr</c>).</summary>
    public List<ConditionalTableStyle> ConditionalFormats { get; } = [];

    /// <summary>Numbering definition a numbering style points at.</summary>
    public int? NumberingId { get; set; }

    /// <summary>Attributes of <c>w:style</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>Children of <c>w:style</c> this version does not model, kept verbatim.</summary>
    public string? Extensions { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind} style '{Name ?? Id}'";
}
