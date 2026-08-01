using Inkwright;
using Inkwright.Fonts;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The appearance of one stretch of text once every question has been answered: which font file,
/// what size in points, what colour, how far off the baseline.
/// </summary>
/// <remarks>
/// Resolved formatting still speaks WordprocessingML — half-points, twips, toggles that may be
/// <see langword="null"/>. Layout and rendering want points and plain values, and want them
/// without asking twice, so the translation happens once here and both phases read the result.
/// </remarks>
internal sealed class CharacterStyle
{
    /// <summary>The font to draw with.</summary>
    public required PdfFont Font { get; init; }

    /// <summary>The size in points, already reduced for a superscript or a subscript.</summary>
    public required double FontSize { get; init; }

    /// <summary>The size in points before any superscript reduction, which sizes the line.</summary>
    public required double LineFontSize { get; init; }

    /// <summary>The glyph colour.</summary>
    public required PdfColor Color { get; init; }

    /// <summary>Height above the baseline in points.</summary>
    public required double Ascent { get; init; }

    /// <summary>Depth below the baseline in points, as a positive number.</summary>
    public required double Descent { get; init; }

    /// <summary>The distance between baselines of consecutive single-spaced lines, in points.</summary>
    public required double LineHeight { get; init; }

    /// <summary>How far the baseline is raised, in points; negative lowers it.</summary>
    public double Rise { get; init; }

    /// <summary>Extra space after every glyph, in points.</summary>
    public double CharacterSpacing { get; init; }

    /// <summary>Horizontal glyph scaling, where 1 is unscaled.</summary>
    public double HorizontalScale { get; init; } = 1;

    /// <summary>The underline decoration.</summary>
    public UnderlineStyle Underline { get; init; }

    /// <summary>The colour of the underline.</summary>
    public PdfColor UnderlineColor { get; init; }

    /// <summary>Whether a line is struck through the text.</summary>
    public bool Strike { get; init; }

    /// <summary>Whether two lines are struck through the text.</summary>
    public bool DoubleStrike { get; init; }

    /// <summary>The highlighter colour behind the text, or <see langword="null"/>.</summary>
    public PdfColor? Highlight { get; init; }

    /// <summary>The character shading behind the text, or <see langword="null"/>.</summary>
    public PdfColor? Shading { get; init; }

    /// <summary>Whether the text is drawn in capitals.</summary>
    public bool Caps { get; init; }

    /// <summary>Whether lower-case letters are drawn as small capitals.</summary>
    public bool SmallCaps { get; init; }

    /// <summary>Whether the text is hidden and normally not printed.</summary>
    public bool Hidden { get; init; }

    /// <summary>The language of the text as a BCP 47 tag, which picks its hyphenation patterns.</summary>
    public string? Language { get; init; }

    /// <summary>The width of a string in points, spacing and scaling included.</summary>
    /// <param name="text">The text to measure.</param>
    public double Measure(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0;

        // Small capitals draw lower-case letters as capitals at a reduced size, so their advances
        // come from a different size than the rest of the run and cannot be measured in one go.
        if (SmallCaps)
            return MeasureSmallCaps(text);

        string shaped = Caps ? text.ToString().ToUpperInvariant() : text.ToString();
        double width = Font.Measure(shaped, FontSize) + (CharacterSpacing * CountGlyphs(shaped));
        return width * HorizontalScale;
    }

    /// <summary>The size small capitals are drawn at, which Word sets to four fifths of the text.</summary>
    public double SmallCapsSize => FontSize * 0.8;

    private double MeasureSmallCaps(ReadOnlySpan<char> text)
    {
        double width = 0;
        foreach (char c in text)
        {
            char upper = char.ToUpperInvariant(c);
            double size = char.IsLower(c) ? SmallCapsSize : FontSize;
            width += Font.Measure(upper.ToString(), size) + CharacterSpacing;
        }

        return width * HorizontalScale;
    }

    private static int CountGlyphs(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsLowSurrogate(text[i]))
                count++;
        }

        return count;
    }
}
