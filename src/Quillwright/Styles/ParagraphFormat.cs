using Quillwright.Primitives;

namespace Quillwright.Styles;

/// <summary>Vertical placement of characters on a line (<c>w:textAlignment</c>).</summary>
public enum LineTextAlignment : byte
{
    /// <summary>Chosen by the consumer.</summary>
    Auto = 0,

    /// <summary>On the baseline.</summary>
    Baseline,

    /// <summary>At the bottom of the line.</summary>
    Bottom,

    /// <summary>Centred on the line.</summary>
    Center,

    /// <summary>At the top of the line.</summary>
    Top,
}

/// <summary>
/// Paragraph formatting (<c>w:pPr</c>). Every property is optional: <see langword="null"/>
/// means "inherit from the style chain", a value means "override it here".
/// </summary>
public sealed record ParagraphFormat
{
    /// <summary>A format that overrides nothing.</summary>
    public static ParagraphFormat Default { get; } = new();

    /// <summary>Identifier of the paragraph style (<c>w:pStyle</c>).</summary>
    public string? StyleId { get; init; }

    /// <summary>Keeps this paragraph on the same page as the next one (<c>w:keepNext</c>).</summary>
    public bool? KeepWithNext { get; init; }

    /// <summary>Keeps all lines of this paragraph on one page (<c>w:keepLines</c>).</summary>
    public bool? KeepLinesTogether { get; init; }

    /// <summary>Starts this paragraph on a new page (<c>w:pageBreakBefore</c>).</summary>
    public bool? PageBreakBefore { get; init; }

    /// <summary>The text-frame element, kept verbatim (<c>w:framePr</c>).</summary>
    public string? FrameXml { get; init; }

    /// <summary>Prevents single lines at the top or bottom of a page (<c>w:widowControl</c>).</summary>
    public bool? WidowControl { get; init; }

    /// <summary>Numbering definition instance this paragraph belongs to (<c>w:numPr/w:numId</c>).</summary>
    public int? NumberingId { get; init; }

    /// <summary>Zero-based level within the numbering definition (<c>w:numPr/w:ilvl</c>).</summary>
    public int? NumberingLevel { get; init; }

    /// <summary>Excludes the paragraph from line numbering (<c>w:suppressLineNumbers</c>).</summary>
    public bool? SuppressLineNumbers { get; init; }

    /// <summary>Borders drawn around the paragraph (<c>w:pBdr</c>).</summary>
    public BorderSet? Borders { get; init; }

    /// <summary>Background fill (<c>w:shd</c>).</summary>
    public Shading? Shading { get; init; }

    /// <summary>Custom tab stops (<c>w:tabs</c>).</summary>
    public EquatableArray<TabStop> Tabs { get; init; }

    /// <summary>Turns off automatic hyphenation (<c>w:suppressAutoHyphens</c>).</summary>
    public bool? SuppressAutoHyphens { get; init; }

    /// <summary>Applies East Asian line-breaking rules (<c>w:kinsoku</c>).</summary>
    public bool? Kinsoku { get; init; }

    /// <summary>Breaks lines between words rather than inside them (<c>w:wordWrap</c>).</summary>
    public bool? WordWrap { get; init; }

    /// <summary>Lets punctuation hang past the margin (<c>w:overflowPunct</c>).</summary>
    public bool? OverflowPunctuation { get; init; }

    /// <summary>Compresses punctuation at the start of a line (<c>w:topLinePunct</c>).</summary>
    public bool? TopLinePunctuation { get; init; }

    /// <summary>Adds space between East Asian and Latin text (<c>w:autoSpaceDE</c>).</summary>
    public bool? AutoSpaceEastAsianLatin { get; init; }

    /// <summary>Adds space between East Asian text and numbers (<c>w:autoSpaceDN</c>).</summary>
    public bool? AutoSpaceEastAsianNumbers { get; init; }

    /// <summary>Right-to-left paragraph (<c>w:bidi</c>).</summary>
    public bool? RightToLeft { get; init; }

    /// <summary>Automatically adjusts the right indent when a grid is in use (<c>w:adjustRightInd</c>).</summary>
    public bool? AdjustRightIndent { get; init; }

    /// <summary>Aligns lines to the document grid (<c>w:snapToGrid</c>).</summary>
    public bool? SnapToGrid { get; init; }

    /// <summary>Space above the paragraph (<c>w:spacing/@w:before</c>).</summary>
    public Length? SpacingBefore { get; init; }

    /// <summary>Space below the paragraph (<c>w:spacing/@w:after</c>).</summary>
    public Length? SpacingAfter { get; init; }

    /// <summary>Lets the consumer choose the space above (<c>w:spacing/@w:beforeAutospacing</c>).</summary>
    public bool? SpacingBeforeAuto { get; init; }

    /// <summary>Lets the consumer choose the space below (<c>w:spacing/@w:afterAutospacing</c>).</summary>
    public bool? SpacingAfterAuto { get; init; }

    /// <summary>Space above expressed in hundredths of a line (<c>w:spacing/@w:beforeLines</c>).</summary>
    public int? SpacingBeforeLines { get; init; }

    /// <summary>Space below expressed in hundredths of a line (<c>w:spacing/@w:afterLines</c>).</summary>
    public int? SpacingAfterLines { get; init; }

    /// <summary>
    /// Line height (<c>w:spacing/@w:line</c>). With <see cref="LineSpacingRule.Auto"/> the
    /// value is in 240ths of a line, otherwise in twips.
    /// </summary>
    public Length? LineSpacing { get; init; }

    /// <summary>How <see cref="LineSpacing"/> is applied (<c>w:spacing/@w:lineRule</c>).</summary>
    public LineSpacingRule? LineSpacingRule { get; init; }

    /// <summary>Indent from the leading margin (<c>w:ind/@w:left</c>).</summary>
    public Length? IndentLeft { get; init; }

    /// <summary>Indent from the trailing margin (<c>w:ind/@w:right</c>).</summary>
    public Length? IndentRight { get; init; }

    /// <summary>Extra indent applied to the first line (<c>w:ind/@w:firstLine</c>).</summary>
    public Length? IndentFirstLine { get; init; }

    /// <summary>Negative first-line indent (<c>w:ind/@w:hanging</c>).</summary>
    public Length? IndentHanging { get; init; }

    /// <summary>
    /// Indent from the leading margin in hundredths of a character
    /// (<c>w:ind/@w:startChars</c> or Transitional <c>w:leftChars</c>).
    /// When present, Word uses it instead of <see cref="IndentLeft"/>.
    /// </summary>
    public int? IndentLeftCharacters { get; init; }

    /// <summary>
    /// Indent from the trailing margin in hundredths of a character
    /// (<c>w:ind/@w:endChars</c> or Transitional <c>w:rightChars</c>).
    /// When present, Word uses it instead of <see cref="IndentRight"/>.
    /// </summary>
    public int? IndentRightCharacters { get; init; }

    /// <summary>
    /// Additional first-line indent in hundredths of a character
    /// (<c>w:ind/@w:firstLineChars</c>). It supersedes <see cref="IndentFirstLine"/>.
    /// </summary>
    public int? IndentFirstLineCharacters { get; init; }

    /// <summary>
    /// Hanging indent in hundredths of a character (<c>w:ind/@w:hangingChars</c>).
    /// It supersedes <see cref="IndentHanging"/>.
    /// </summary>
    public int? IndentHangingCharacters { get; init; }

    /// <summary>Drops the space between paragraphs of the same style (<c>w:contextualSpacing</c>).</summary>
    public bool? ContextualSpacing { get; init; }

    /// <summary>Swaps the indents on facing pages (<c>w:mirrorIndents</c>).</summary>
    public bool? MirrorIndents { get; init; }

    /// <summary>Prevents floating objects from overlapping this paragraph (<c>w:suppressOverlap</c>).</summary>
    public bool? SuppressOverlap { get; init; }

    /// <summary>Horizontal alignment (<c>w:jc</c>).</summary>
    public ParagraphAlignment? Alignment { get; init; }

    /// <summary>Flow direction (<c>w:textDirection</c>).</summary>
    public TextDirection? TextDirection { get; init; }

    /// <summary>Vertical placement of characters on the line (<c>w:textAlignment</c>).</summary>
    public LineTextAlignment? LineTextAlignment { get; init; }

    /// <summary>The tight-wrap element, kept verbatim (<c>w:textboxTightWrap</c>).</summary>
    public string? TextboxTightWrapXml { get; init; }

    /// <summary>Heading level in the document outline, zero-based (<c>w:outlineLvl</c>).</summary>
    public int? OutlineLevel { get; init; }

    /// <summary>The HTML div association, kept verbatim (<c>w:divId</c>).</summary>
    public string? DivIdXml { get; init; }

    /// <summary>The conditional-formatting element of a table paragraph, kept verbatim (<c>w:cnfStyle</c>).</summary>
    public string? ConditionalFormattingXml { get; init; }

    /// <summary>The revision record of a formatting change, kept verbatim (<c>w:pPrChange</c>).</summary>
    public string? ChangeXml { get; init; }

    /// <summary>Children of <c>w:pPr</c> this version does not model, re-emitted after the modelled ones.</summary>
    public string? Extensions { get; init; }

    /// <summary>Returns <see langword="true"/> when the format overrides nothing.</summary>
    public bool IsEmpty => Equals(Default);

    /// <summary>
    /// Overlays <paramref name="over"/> on top of this format: every property the argument
    /// specifies wins, the rest are kept.
    /// </summary>
    public ParagraphFormat Merge(ParagraphFormat? over)
    {
        if (over is null)
            return this;

        return new ParagraphFormat
        {
            StyleId = over.StyleId ?? StyleId,
            KeepWithNext = over.KeepWithNext ?? KeepWithNext,
            KeepLinesTogether = over.KeepLinesTogether ?? KeepLinesTogether,
            PageBreakBefore = over.PageBreakBefore ?? PageBreakBefore,
            FrameXml = over.FrameXml ?? FrameXml,
            WidowControl = over.WidowControl ?? WidowControl,
            NumberingId = over.NumberingId ?? NumberingId,
            NumberingLevel = over.NumberingLevel ?? NumberingLevel,
            SuppressLineNumbers = over.SuppressLineNumbers ?? SuppressLineNumbers,
            Borders = over.Borders ?? Borders,
            Shading = over.Shading ?? Shading,
            Tabs = over.Tabs.IsEmpty ? Tabs : over.Tabs,
            SuppressAutoHyphens = over.SuppressAutoHyphens ?? SuppressAutoHyphens,
            Kinsoku = over.Kinsoku ?? Kinsoku,
            WordWrap = over.WordWrap ?? WordWrap,
            OverflowPunctuation = over.OverflowPunctuation ?? OverflowPunctuation,
            TopLinePunctuation = over.TopLinePunctuation ?? TopLinePunctuation,
            AutoSpaceEastAsianLatin = over.AutoSpaceEastAsianLatin ?? AutoSpaceEastAsianLatin,
            AutoSpaceEastAsianNumbers = over.AutoSpaceEastAsianNumbers ?? AutoSpaceEastAsianNumbers,
            RightToLeft = over.RightToLeft ?? RightToLeft,
            AdjustRightIndent = over.AdjustRightIndent ?? AdjustRightIndent,
            SnapToGrid = over.SnapToGrid ?? SnapToGrid,
            SpacingBefore = over.SpacingBefore ?? SpacingBefore,
            SpacingAfter = over.SpacingAfter ?? SpacingAfter,
            SpacingBeforeAuto = over.SpacingBeforeAuto ?? SpacingBeforeAuto,
            SpacingAfterAuto = over.SpacingAfterAuto ?? SpacingAfterAuto,
            SpacingBeforeLines = over.SpacingBeforeLines ?? SpacingBeforeLines,
            SpacingAfterLines = over.SpacingAfterLines ?? SpacingAfterLines,
            LineSpacing = over.LineSpacing ?? LineSpacing,
            LineSpacingRule = over.LineSpacingRule ?? LineSpacingRule,
            IndentLeft = over.IndentLeft ?? IndentLeft,
            IndentRight = over.IndentRight ?? IndentRight,
            IndentFirstLine = over.IndentFirstLine ?? IndentFirstLine,
            IndentHanging = over.IndentHanging ?? IndentHanging,
            IndentLeftCharacters = over.IndentLeftCharacters ?? IndentLeftCharacters,
            IndentRightCharacters = over.IndentRightCharacters ?? IndentRightCharacters,
            IndentFirstLineCharacters = over.IndentFirstLineCharacters ?? IndentFirstLineCharacters,
            IndentHangingCharacters = over.IndentHangingCharacters ?? IndentHangingCharacters,
            ContextualSpacing = over.ContextualSpacing ?? ContextualSpacing,
            MirrorIndents = over.MirrorIndents ?? MirrorIndents,
            SuppressOverlap = over.SuppressOverlap ?? SuppressOverlap,
            Alignment = over.Alignment ?? Alignment,
            TextDirection = over.TextDirection ?? TextDirection,
            LineTextAlignment = over.LineTextAlignment ?? LineTextAlignment,
            TextboxTightWrapXml = over.TextboxTightWrapXml ?? TextboxTightWrapXml,
            OutlineLevel = over.OutlineLevel ?? OutlineLevel,
            DivIdXml = over.DivIdXml ?? DivIdXml,
            ConditionalFormattingXml = over.ConditionalFormattingXml ?? ConditionalFormattingXml,
            ChangeXml = over.ChangeXml ?? ChangeXml,
            Extensions = over.Extensions ?? Extensions,
        };
    }

    /// <summary>
    /// Overlays one level of Word's style hierarchy, including its character-indent rule:
    /// a zero character-unit value clears the same value inherited from an earlier level.
    /// </summary>
    internal ParagraphFormat MergeForStyleHierarchy(ParagraphFormat? over)
    {
        ParagraphFormat result = Merge(over);
        if (over is null)
            return result;

        return result with
        {
            IndentLeftCharacters = over.IndentLeftCharacters == 0 ? null : result.IndentLeftCharacters,
            IndentRightCharacters = over.IndentRightCharacters == 0 ? null : result.IndentRightCharacters,
            IndentFirstLineCharacters = over.IndentFirstLineCharacters == 0 ? null : result.IndentFirstLineCharacters,
            IndentHangingCharacters = over.IndentHangingCharacters == 0 ? null : result.IndentHangingCharacters,
        };
    }
}
