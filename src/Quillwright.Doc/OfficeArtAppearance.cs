using Quillwright.Primitives;

namespace Quillwright.Doc;

/// <summary>
/// What a drawing looks like, so far as the corpus says a legacy document ever states it.
/// </summary>
/// <remarks>
/// <para>
/// [MS-ODRAW] describes over a thousand shape properties. Counting them across the 247
/// documents of the reference corpus — the sweep is in <c>OfficeArtInventoryTests</c> — leaves a
/// much shorter list: of 554 shapes, all but the group roots are rectangles, picture frames or
/// lettering, and the only decoration any of them states is a fill colour, a line, and a
/// rotation. There is no custom geometry in the corpus at all, and no shadow.
/// </para>
/// <para>
/// So this reads those, and nothing is built for the parts of the specification no document
/// produces. What is here is what a converted text box needs to keep the frame and the
/// background it was drawn with, which it used to lose.
/// </para>
/// </remarks>
/// <param name="Fill">The background, or <see langword="null"/> when the shape states none.</param>
/// <param name="LineColor">The colour of the outline, or <see langword="null"/> for none.</param>
/// <param name="LineWidth">Thickness of the outline, when the shape states one.</param>
/// <param name="Rotation">How far the shape is turned, in degrees clockwise.</param>
/// <param name="GeometryText">The lettering of a WordArt shape, which holds its words here.</param>
internal readonly record struct OfficeArtAppearance(
    WordColor? Fill,
    WordColor? LineColor,
    Length? LineWidth,
    double Rotation,
    string? GeometryText)
{
    /// <summary>A shape that says nothing about how it looks.</summary>
    public static OfficeArtAppearance Plain => default;

    /// <summary>Whether the shape states anything at all worth carrying across.</summary>
    public bool IsPlain => Fill is null && LineColor is null && LineWidth is null && Rotation == 0;

    /// <summary>
    /// A colour as the drawing layer stores one ([MS-ODRAW] 2.2.2): blue, green and red in the
    /// low three bytes, and a fourth byte saying what kind of colour it is. Only a plain
    /// colour is read; a scheme index or a system colour would need a palette this reader does
    /// not carry, and comes back as nothing rather than as the wrong colour.
    /// </summary>
    /// <param name="value">The property value.</param>
    public static WordColor? Color(int value)
    {
        const int PaletteIndex = 0x01;
        const int SchemeIndex = 0x08;
        const int SystemColor = 0x10;

        int kind = (value >> 24) & 0xFF;
        if ((kind & (PaletteIndex | SchemeIndex | SystemColor)) != 0)
            return null;

        // Stored blue first, which is the opposite way round from every other colour here.
        int blue = (value >> 16) & 0xFF;
        int green = (value >> 8) & 0xFF;
        int red = value & 0xFF;
        return WordColor.FromRgb((uint)((red << 16) | (green << 8) | blue));
    }
}
