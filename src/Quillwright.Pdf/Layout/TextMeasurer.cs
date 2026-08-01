using Inkwright;
using Inkwright.Fonts;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Turns resolved character formatting into a <see cref="CharacterStyle"/>, and caches the answer.
/// </summary>
/// <remarks>
/// A document of any size has a handful of distinct appearances and hundreds of thousands of runs
/// wearing them, so the translation — which embeds a font among other things — is done once per
/// distinct formatting. <see cref="RunFormat"/> is a record, so value equality makes the key.
/// </remarks>
internal sealed class TextMeasurer
{
    /// <summary>How much smaller a superscript or subscript is drawn, as Word reduces it.</summary>
    private const double ScriptScale = 0.65;

    /// <summary>How far a superscript is raised, as a fraction of the unreduced size.</summary>
    private const double SuperscriptRise = 0.33;

    /// <summary>How far a subscript is lowered, as a fraction of the unreduced size.</summary>
    private const double SubscriptDrop = 0.14;

    /// <summary>The size a run gets when nothing in the chain states one.</summary>
    private const double DefaultFontSize = 11;

    private readonly PdfExportContext _context;
    private readonly Dictionary<RunFormat, CharacterStyle> _cache = [];

    internal TextMeasurer(PdfExportContext context) => _context = context;

    /// <summary>The appearance of a run whose formatting has already been resolved.</summary>
    /// <param name="resolved">Formatting after the whole style chain has been applied.</param>
    public CharacterStyle Style(RunFormat resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        if (_cache.TryGetValue(resolved, out CharacterStyle? cached))
            return cached;

        CharacterStyle style = Build(resolved);
        _cache[resolved] = style;
        return style;
    }

    /// <summary>The appearance of the paragraph mark, which decides how tall an empty line is.</summary>
    /// <param name="paragraph">The paragraph whose mark to measure.</param>
    public CharacterStyle MarkStyle(Paragraph paragraph) => Style(_context.Resolver.ResolveMarkFormat(paragraph));

    private CharacterStyle Build(RunFormat format)
    {
        PdfFont font = _context.Fonts.Resolve(format);
        double size = format.RightToLeft == true
            ? (format.SizeComplexScript ?? format.Size)?.Points ?? DefaultFontSize
            : format.Size?.Points ?? DefaultFontSize;
        if (size <= 0)
            size = DefaultFontSize;

        VerticalTextAlignment placement = format.VerticalAlignment ?? VerticalTextAlignment.Baseline;
        double drawSize = placement == VerticalTextAlignment.Baseline ? size : size * ScriptScale;
        double rise = placement switch
        {
            VerticalTextAlignment.Superscript => size * SuperscriptRise,
            VerticalTextAlignment.Subscript => -size * SubscriptDrop,
            _ => 0,
        };

        // An explicit w:position offsets on top of whatever the vertical alignment already did.
        rise += format.Position?.Points ?? 0;

        (double ascent, double descent, double lineHeight) = Metrics(font, size);
        PdfColor color = _context.ColorOf(format.Color ?? Primitives.WordColor.Auto, PdfColor.Black);

        return new CharacterStyle
        {
            Font = font,
            FontSize = drawSize,
            LineFontSize = size,
            Color = color,
            Ascent = ascent + Math.Max(0, rise),
            Descent = descent + Math.Max(0, -rise),
            LineHeight = lineHeight,
            Rise = rise,
            CharacterSpacing = format.CharacterSpacing?.Points ?? 0,
            HorizontalScale = format.Scale is { } scale and > 0 ? scale / 100.0 : 1,
            Underline = format.Underline ?? UnderlineStyle.None,
            UnderlineColor = _context.ColorOf(format.UnderlineColor ?? Primitives.WordColor.Auto, color),
            Strike = format.Strike == true,
            DoubleStrike = format.DoubleStrike == true,
            Highlight = PdfExportContext.HighlightColorOf(format.Highlight ?? HighlightColor.None),
            Shading = ShadingOf(format.Shading),
            Caps = format.Caps == true,
            SmallCaps = format.SmallCaps == true,
            Hidden = format.Hidden == true,
            Language = format.RightToLeft == true ? format.LanguageComplexScript ?? format.Language : format.Language,
        };
    }

    private PdfColor? ShadingOf(Shading? shading)
    {
        if (shading is null || shading.IsEmpty || shading.Pattern == ShadingPattern.Nil)
            return null;

        // A solid pattern paints in the pattern colour; every other pattern shows the fill behind
        // it, and approximating the weave of a stripe with its background is closer than nothing.
        Primitives.WordColor value = shading.Pattern == ShadingPattern.Solid ? shading.Color : shading.Fill;
        return value.IsAuto ? null : _context.ColorOf(value, PdfColor.White);
    }

    /// <summary>
    /// The vertical metrics of a font at a size. Single line spacing is the font's own ascent,
    /// descent and line gap, which is how a word processor decides what "single" means; the
    /// built-in fonts carry no gap, so they get the conventional two tenths.
    /// </summary>
    private static (double Ascent, double Descent, double LineHeight) Metrics(PdfFont font, double size)
    {
        double ascent = font.Ascent * size / 1000;
        double descent = -font.Descent * size / 1000;

        double gap = font is EmbeddedTrueTypeFont embedded
            ? embedded.Program.ToPerMille(embedded.Program.LineGap) * size / 1000
            : (font.Ascent - font.Descent) * size / 1000 * 0.2;

        return (ascent, descent, ascent + descent + Math.Max(0, gap));
    }
}
