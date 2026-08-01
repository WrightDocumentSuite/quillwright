using Inkwright;
using Quillwright.Model;
using Quillwright.Pdf.Fonts;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Pdf;

/// <summary>
/// Everything one render needs to reach: where it reads from, where it writes to, and the
/// services — fonts, colours, diagnostics — that both halves share.
/// </summary>
internal sealed class PdfExportContext
{
    internal PdfExportContext(
        WordDocument source, PdfDocument pdf, PdfExportOptions options, PdfExportDiagnostics diagnostics)
    {
        Source = source;
        Pdf = pdf;
        Options = options;
        Diagnostics = diagnostics;
        Fonts = new FontMap(pdf, options, diagnostics);
        Hyphenation = Layout.Hyphenator.Create(source, options, diagnostics);
    }

    /// <summary>The document being rendered.</summary>
    public WordDocument Source { get; }

    /// <summary>The document being produced.</summary>
    public PdfDocument Pdf { get; }

    /// <summary>How the caller wants it rendered.</summary>
    public PdfExportOptions Options { get; }

    /// <summary>What had to be worked around.</summary>
    public PdfExportDiagnostics Diagnostics { get; }

    /// <summary>Fonts, already substituted and embedded.</summary>
    public FontMap Fonts { get; }

    /// <summary>Where words may break, or <see langword="null"/> when the document does not hyphenate.</summary>
    public Layout.Hyphenator? Hyphenation { get; }

    /// <summary>The formatting that actually applies, after the whole style chain.</summary>
    public StyleResolver Resolver => Source.Resolver;

    /// <summary>
    /// The colour to paint with. The automatic colour is not a colour but an instruction to pick
    /// one from context, so the caller says what context means here.
    /// </summary>
    /// <param name="color">The colour as the document states it.</param>
    /// <param name="fallback">What the automatic colour, or an unresolvable theme slot, means.</param>
    public PdfColor ColorOf(WordColor color, PdfColor fallback) =>
        Source.ResolveColor(color) is { } rgb ? PdfColor.FromRgb(rgb) : fallback;

    /// <summary>The colour of a highlighter, which comes from a fixed palette rather than a value.</summary>
    /// <param name="highlight">The palette entry.</param>
    /// <returns>The colour, or <see langword="null"/> when nothing is highlighted.</returns>
    public static PdfColor? HighlightColorOf(HighlightColor highlight) => highlight switch
    {
        HighlightColor.None => null,
        HighlightColor.Black => PdfColor.FromRgb(0x000000),
        HighlightColor.Blue => PdfColor.FromRgb(0x0000FF),
        HighlightColor.Cyan => PdfColor.FromRgb(0x00FFFF),
        HighlightColor.Green => PdfColor.FromRgb(0x00FF00),
        HighlightColor.Magenta => PdfColor.FromRgb(0xFF00FF),
        HighlightColor.Red => PdfColor.FromRgb(0xFF0000),
        HighlightColor.Yellow => PdfColor.FromRgb(0xFFFF00),
        HighlightColor.White => PdfColor.FromRgb(0xFFFFFF),
        HighlightColor.DarkBlue => PdfColor.FromRgb(0x000080),
        HighlightColor.DarkCyan => PdfColor.FromRgb(0x008080),
        HighlightColor.DarkGreen => PdfColor.FromRgb(0x008000),
        HighlightColor.DarkMagenta => PdfColor.FromRgb(0x800080),
        HighlightColor.DarkRed => PdfColor.FromRgb(0x800000),
        HighlightColor.DarkYellow => PdfColor.FromRgb(0x808000),
        HighlightColor.DarkGray => PdfColor.FromRgb(0x808080),
        HighlightColor.LightGray => PdfColor.FromRgb(0xC0C0C0),
        _ => null,
    };
}
