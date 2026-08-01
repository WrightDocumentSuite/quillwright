using Quillwright.Primitives;

namespace Quillwright.Styles;

/// <summary>
/// Definitions of the styles Word ships with. They are materialised only when a document
/// asks for one, so a file never carries a definition it does not use.
/// </summary>
internal static class BuiltInStyles
{
    private static readonly WordColor HeadingColor = WordColor.FromTheme(ThemeColorSlot.Accent1, shade: 0xBF);
    private static readonly WordColor SubtleColor = WordColor.FromTheme(ThemeColorSlot.Text1, tint: 0xA6);

    /// <summary>Creates the built-in style with the given identifier, or <see langword="null"/> when unknown.</summary>
    public static Style? Create(string id) => id switch
    {
        "Normal" => new Style(id, StyleKind.Paragraph) { Name = "Normal", IsDefault = true, QuickFormat = true },
        "DefaultParagraphFont" => new Style(id, StyleKind.Character)
        {
            Name = "Default Paragraph Font", IsDefault = true, SemiHidden = true, UnhideWhenUsed = true, Priority = 1,
        },
        "TableNormal" => new Style(id, StyleKind.Table)
        {
            Name = "Normal Table", IsDefault = true, SemiHidden = true, UnhideWhenUsed = true, Priority = 99,
            TableFormat = TableFormat.Default with { CellMargins = DefaultCellMargins },
        },
        "NoList" => new Style(id, StyleKind.Numbering)
        {
            Name = "No List", IsDefault = true, SemiHidden = true, UnhideWhenUsed = true, Priority = 99,
        },
        "Title" => Title(),
        "Subtitle" => Subtitle(),
        "Quote" => Quote(),
        "IntenseQuote" => IntenseQuote(),
        "ListParagraph" => ListParagraph(),
        "NoSpacing" => new Style(id, StyleKind.Paragraph)
        {
            Name = "No Spacing", BasedOn = null, QuickFormat = true, Priority = 1,
            ParagraphFormat = ParagraphFormat.Default with { SpacingAfter = Length.Zero, LineSpacing = Length.FromTwips(240), LineSpacingRule = LineSpacingRule.Auto },
        },
        "Caption" => new Style(id, StyleKind.Paragraph)
        {
            Name = "caption", BasedOn = "Normal", NextStyle = "Normal", SemiHidden = true, UnhideWhenUsed = true, QuickFormat = true, Priority = 35,
            ParagraphFormat = ParagraphFormat.Default with { SpacingAfter = Length.FromTwips(200), LineSpacing = Length.FromTwips(240), LineSpacingRule = LineSpacingRule.Auto },
            RunFormat = RunFormat.Default with { Italic = true, Size = Length.FromHalfPoints(18), Color = HeadingColor },
        },
        "Header" => TabbedStyle(id, "header", Length.FromInches(3.25), Length.FromInches(6.5)),
        "Footer" => TabbedStyle(id, "footer", Length.FromInches(3.25), Length.FromInches(6.5)),
        "Hyperlink" => new Style(id, StyleKind.Character)
        {
            Name = "Hyperlink", BasedOn = "DefaultParagraphFont", UnhideWhenUsed = true, Priority = 99,
            RunFormat = RunFormat.Default with { Color = WordColor.FromTheme(ThemeColorSlot.Hyperlink), Underline = UnderlineStyle.Single },
        },
        "FollowedHyperlink" => new Style(id, StyleKind.Character)
        {
            Name = "FollowedHyperlink", BasedOn = "DefaultParagraphFont", SemiHidden = true, UnhideWhenUsed = true, Priority = 99,
            RunFormat = RunFormat.Default with { Color = WordColor.FromTheme(ThemeColorSlot.FollowedHyperlink), Underline = UnderlineStyle.Single },
        },
        "Strong" => new Style(id, StyleKind.Character)
        {
            Name = "Strong", BasedOn = "DefaultParagraphFont", QuickFormat = true, Priority = 22,
            RunFormat = RunFormat.Default with { Bold = true, BoldComplexScript = true },
        },
        "Emphasis" => new Style(id, StyleKind.Character)
        {
            Name = "Emphasis", BasedOn = "DefaultParagraphFont", QuickFormat = true, Priority = 20,
            RunFormat = RunFormat.Default with { Italic = true, ItalicComplexScript = true },
        },
        "FootnoteText" => NoteText(id, "footnote text"),
        "EndnoteText" => NoteText(id, "endnote text"),
        "CommentText" => NoteText(id, "annotation text"),
        "FootnoteReference" => NoteReferenceStyle(id, "footnote reference"),
        "EndnoteReference" => NoteReferenceStyle(id, "endnote reference"),
        "CommentReference" => new Style(id, StyleKind.Character)
        {
            Name = "annotation reference", BasedOn = "DefaultParagraphFont", SemiHidden = true, UnhideWhenUsed = true, Priority = 99,
            RunFormat = RunFormat.Default with { Size = Length.FromHalfPoints(16), SizeComplexScript = Length.FromHalfPoints(16) },
        },
        "CommentSubject" => new Style(id, StyleKind.Paragraph)
        {
            Name = "annotation subject", BasedOn = "CommentText", NextStyle = "CommentText", SemiHidden = true, UnhideWhenUsed = true, Priority = 99,
            RunFormat = RunFormat.Default with { Bold = true, BoldComplexScript = true },
        },
        "TableGrid" => TableGrid(),
        "TOCHeading" => new Style(id, StyleKind.Paragraph)
        {
            Name = "TOC Heading", BasedOn = "Heading1", NextStyle = "Normal", SemiHidden = true, UnhideWhenUsed = true, QuickFormat = true, Priority = 39,
            ParagraphFormat = ParagraphFormat.Default with { OutlineLevel = null },
        },
        _ when TryHeadingLevel(id, out int level) => Heading(id, level),
        _ when TryContentsLevel(id, out int level) => Contents(id, level),
        _ => null,
    };

    /// <summary>Styles a built-in style depends on and that therefore have to exist alongside it.</summary>
    public static IEnumerable<string> Dependencies(Style style)
    {
        if (style.BasedOn is { } basedOn)
            yield return basedOn;
        if (style.LinkedStyle is { } linked)
            yield return linked;
    }

    private static CellMargins DefaultCellMargins => new()
    {
        Top = new TableWidth(WidthUnit.Twips, 0),
        Left = TableWidth.FromLength(Length.FromTwips(108)),
        Bottom = new TableWidth(WidthUnit.Twips, 0),
        Right = TableWidth.FromLength(Length.FromTwips(108)),
    };

    private static bool TryHeadingLevel(string id, out int level)
    {
        level = 0;
        return id.StartsWith("Heading", StringComparison.Ordinal) &&
               int.TryParse(id.AsSpan("Heading".Length), out level) && level is >= 1 and <= 9;
    }

    private static bool TryContentsLevel(string id, out int level)
    {
        level = 0;
        return id.StartsWith("TOC", StringComparison.Ordinal) &&
               int.TryParse(id.AsSpan("TOC".Length), out level) && level is >= 1 and <= 9;
    }

    private static Style Heading(string id, int level) => new(id, StyleKind.Paragraph)
    {
        Name = $"heading {level}",
        BasedOn = "Normal",
        NextStyle = "Normal",
        QuickFormat = true,
        Priority = 9,
        UnhideWhenUsed = level > 2,
        SemiHidden = level > 2,
        ParagraphFormat = ParagraphFormat.Default with
        {
            KeepWithNext = true,
            KeepLinesTogether = true,
            SpacingBefore = Length.FromTwips(level == 1 ? 240 : 40),
            SpacingAfter = Length.Zero,
            OutlineLevel = level - 1,
        },
        RunFormat = RunFormat.Default with
        {
            FontAsciiTheme = "majorHAnsi",
            FontHighAnsiTheme = "majorHAnsi",
            FontEastAsiaTheme = "majorEastAsia",
            FontComplexScriptTheme = "majorBidi",
            Color = HeadingColor,
            Size = Length.FromHalfPoints(level switch { 1 => 32, 2 => 26, 3 => 24, _ => 22 }),
            SizeComplexScript = Length.FromHalfPoints(level switch { 1 => 32, 2 => 26, 3 => 24, _ => 22 }),
            Italic = level >= 4 ? true : null,
        },
    };

    private static Style Contents(string id, int level) => new(id, StyleKind.Paragraph)
    {
        Name = $"toc {level}",
        BasedOn = "Normal",
        NextStyle = "Normal",
        SemiHidden = true,
        UnhideWhenUsed = true,
        Priority = 39,
        ParagraphFormat = ParagraphFormat.Default with
        {
            SpacingAfter = Length.Zero,
            IndentLeft = level > 1 ? Length.FromTwips(220 * (level - 1)) : null,
        },
    };

    private static Style Title() => new("Title", StyleKind.Paragraph)
    {
        Name = "Title", BasedOn = "Normal", NextStyle = "Normal", QuickFormat = true, Priority = 10,
        ParagraphFormat = ParagraphFormat.Default with
        {
            SpacingAfter = Length.Zero,
            ContextualSpacing = true,
            LineSpacing = Length.FromTwips(240),
            LineSpacingRule = LineSpacingRule.Auto,
        },
        RunFormat = RunFormat.Default with
        {
            FontAsciiTheme = "majorHAnsi", FontHighAnsiTheme = "majorHAnsi",
            FontEastAsiaTheme = "majorEastAsia", FontComplexScriptTheme = "majorBidi",
            Size = Length.FromHalfPoints(56), SizeComplexScript = Length.FromHalfPoints(56),
            CharacterSpacing = Length.FromTwips(-10),
            Kerning = Length.FromHalfPoints(28),
        },
    };

    private static Style Subtitle() => new("Subtitle", StyleKind.Paragraph)
    {
        Name = "Subtitle", BasedOn = "Normal", NextStyle = "Normal", QuickFormat = true, Priority = 11,
        ParagraphFormat = ParagraphFormat.Default with { SpacingAfter = Length.FromTwips(160) },
        RunFormat = RunFormat.Default with
        {
            Color = SubtleColor,
            Size = Length.FromHalfPoints(28), SizeComplexScript = Length.FromHalfPoints(28),
            CharacterSpacing = Length.FromTwips(15),
        },
    };

    private static Style Quote() => new("Quote", StyleKind.Paragraph)
    {
        Name = "Quote", BasedOn = "Normal", NextStyle = "Normal", QuickFormat = true, Priority = 29,
        ParagraphFormat = ParagraphFormat.Default with
        {
            SpacingBefore = Length.FromTwips(200), SpacingAfter = Length.FromTwips(200),
            IndentLeft = Length.FromTwips(864), IndentRight = Length.FromTwips(864),
            Alignment = ParagraphAlignment.Center,
        },
        RunFormat = RunFormat.Default with { Italic = true, Color = SubtleColor },
    };

    private static Style IntenseQuote() => new("IntenseQuote", StyleKind.Paragraph)
    {
        Name = "Intense Quote", BasedOn = "Normal", NextStyle = "Normal", QuickFormat = true, Priority = 30,
        ParagraphFormat = ParagraphFormat.Default with
        {
            SpacingBefore = Length.FromTwips(360), SpacingAfter = Length.FromTwips(360),
            IndentLeft = Length.FromTwips(864), IndentRight = Length.FromTwips(864),
            Alignment = ParagraphAlignment.Center,
            Borders = new BorderSet
            {
                Top = new BorderLine { Style = BorderStyle.Single, Width = Length.FromEighthPoints(4), Space = Length.FromPoints(10), Color = HeadingColor },
                Bottom = new BorderLine { Style = BorderStyle.Single, Width = Length.FromEighthPoints(4), Space = Length.FromPoints(10), Color = HeadingColor },
            },
        },
        RunFormat = RunFormat.Default with { Italic = true, Color = HeadingColor },
    };

    private static Style ListParagraph() => new("ListParagraph", StyleKind.Paragraph)
    {
        Name = "List Paragraph", BasedOn = "Normal", QuickFormat = true, UnhideWhenUsed = true, Priority = 34,
        ParagraphFormat = ParagraphFormat.Default with { IndentLeft = Length.FromTwips(720), ContextualSpacing = true },
    };

    private static Style TabbedStyle(string id, string name, Length center, Length right) => new(id, StyleKind.Paragraph)
    {
        Name = name, BasedOn = "Normal", LinkedStyle = null, UnhideWhenUsed = true, Priority = 99,
        ParagraphFormat = ParagraphFormat.Default with
        {
            SpacingAfter = Length.Zero,
            LineSpacing = Length.FromTwips(240),
            LineSpacingRule = LineSpacingRule.Auto,
            Tabs = new[]
            {
                new TabStop(center, TabAlignment.Center),
                new TabStop(right, TabAlignment.Right),
            },
        },
    };

    private static Style NoteText(string id, string name) => new(id, StyleKind.Paragraph)
    {
        Name = name, BasedOn = "Normal", SemiHidden = true, UnhideWhenUsed = true, Priority = 99,
        ParagraphFormat = ParagraphFormat.Default with
        {
            SpacingAfter = Length.Zero, LineSpacing = Length.FromTwips(240), LineSpacingRule = LineSpacingRule.Auto,
        },
        RunFormat = RunFormat.Default with { Size = Length.FromHalfPoints(20), SizeComplexScript = Length.FromHalfPoints(20) },
    };

    private static Style NoteReferenceStyle(string id, string name) => new(id, StyleKind.Character)
    {
        Name = name, BasedOn = "DefaultParagraphFont", SemiHidden = true, UnhideWhenUsed = true, Priority = 99,
        RunFormat = RunFormat.Default with { VerticalAlignment = VerticalTextAlignment.Superscript },
    };

    private static Style TableGrid()
    {
        BorderLine line = BorderLine.Single(Length.FromEighthPoints(4), WordColor.Auto);
        return new Style("TableGrid", StyleKind.Table)
        {
            Name = "Table Grid", BasedOn = "TableNormal", Priority = 39,
            TableFormat = TableFormat.Default with
            {
                Borders = BorderSet.AllWithInside(line),
                CellMargins = DefaultCellMargins,
            },
        };
    }
}
