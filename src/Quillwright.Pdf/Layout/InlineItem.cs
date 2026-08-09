using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>What one piece of a paragraph's content is.</summary>
internal enum InlineKind
{
    /// <summary>Characters to draw.</summary>
    Text,

    /// <summary>A jump to the next tab stop.</summary>
    Tab,

    /// <summary>The end of a line inside the paragraph.</summary>
    LineBreak,

    /// <summary>The end of the page.</summary>
    PageBreak,

    /// <summary>The end of the column.</summary>
    ColumnBreak,

    /// <summary>A picture drawn in the flow.</summary>
    Picture,

    /// <summary>A picture that does not flow, anchored somewhere on the page.</summary>
    FloatingPicture,

    /// <summary>A text box drawn in the flow.</summary>
    Shape,

    /// <summary>A text box that does not flow, anchored somewhere on the page.</summary>
    FloatingShape,

    /// <summary>An equation laid out in two dimensions.</summary>
    Equation,

    /// <summary>A chart drawn from the numbers the document cached for it.</summary>
    Chart,

    /// <summary>A number that is only known once pagination has settled.</summary>
    PageField,

    /// <summary>The mark that stands for a note, and the note it owes the page.</summary>
    NoteReference,

    /// <summary>The invisible endpoint at which an interactive PDF comment is anchored.</summary>
    CommentReference,
}

/// <summary>
/// One piece of a paragraph's content, already resolved: the characters and how they look, or the
/// object that stands in their place.
/// </summary>
/// <remarks>
/// Walking a paragraph and breaking it into lines are separate jobs, and the seam between them is
/// this: the walker knows about runs, fields and tracked changes, and the line breaker knows about
/// widths. Neither has to know the other's rules.
/// </remarks>
internal readonly struct InlineItem
{
    /// <summary>What this piece is.</summary>
    public required InlineKind Kind { get; init; }

    /// <summary>How the piece looks; every kind but a floating picture has an appearance.</summary>
    public required CharacterStyle Style { get; init; }

    /// <summary>The characters, for a text piece.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The picture, for a picture piece.</summary>
    public Picture? Picture { get; init; }

    /// <summary>The text box, for a shape piece.</summary>
    public Shape? Shape { get; init; }

    /// <summary>The equation, already laid out, for an equation piece.</summary>
    public EquationLayout? Equation { get; init; }

    /// <summary>The chart, already laid out, for a chart piece.</summary>
    public ChartLayout? Chart { get; init; }

    /// <summary>The link this piece sits inside, or <see langword="null"/>.</summary>
    public Hyperlink? Link { get; init; }

    /// <summary>Which quantity a page field prints.</summary>
    public PageFieldKind Field { get; init; }

    /// <summary>The numbering scheme a page field prints in.</summary>
    public ListNumberFormat FieldFormat { get; init; }

    /// <summary>Whether the field's own instruction named that scheme.</summary>
    public bool FieldFormatStated { get; init; }

    /// <summary>The bookmark a <c>PAGEREF</c> field points at; nothing for the other kinds.</summary>
    public string? FieldBookmark { get; init; }

    /// <summary>The note a reference stands for, and the number it prints.</summary>
    public NoteMark? Note { get; init; }

    /// <summary>The Word comment an invisible comment reference points at.</summary>
    public Comment? Comment { get; init; }

    /// <summary>Whether a text piece reads right-to-left.</summary>
    public bool RightToLeft { get; init; }

    /// <summary>Creates a piece.</summary>
    public InlineItem()
    {
    }

    /// <summary>A stretch of text.</summary>
    public static InlineItem OfText(string text, CharacterStyle style, Hyperlink? link, bool rightToLeft = false) =>
        new() { Kind = InlineKind.Text, Text = text, Style = style, Link = link, RightToLeft = rightToLeft };

    /// <summary>A control piece that carries no characters.</summary>
    public static InlineItem Control(InlineKind kind, CharacterStyle style, Hyperlink? link = null) =>
        new() { Kind = kind, Style = style, Link = link };
}
