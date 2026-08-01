using Quillwright.Primitives;

namespace Quillwright.Model;

/// <summary>
/// The colour scheme of a document's theme, and the mapping that decides which slot a
/// WordprocessingML colour means (ECMA-376 part 1 §20.1.6.2, ISO/IEC 29500-1 §17.15.1.20).
/// </summary>
/// <remarks>
/// <para>
/// A theme colour is stored as a name rather than a value, so that changing the theme
/// recolours the document. Two vocabularies meet here and do not quite line up: the theme
/// names its twelve colours in the drawing layer's terms — <c>dk1</c>, <c>lt1</c>,
/// <c>accent1</c> — while a run names them in the word processor's — <c>text1</c>,
/// <c>background1</c>. The settings part carries the map between them, and a document may
/// have swapped them round, which is what makes a "light" background come out dark.
/// </para>
/// <para>
/// Word does not rely on any of this at display time: it caches the computed value in the
/// same element, beside the name. That cache is what the resolution here is checked against.
/// </para>
/// </remarks>
public sealed class DocumentTheme
{
    private readonly Dictionary<ThemeColorSlot, uint> _scheme = [];
    private readonly Dictionary<ThemeColorSlot, ThemeColorSlot> _mapping = [];

    /// <summary>The name the theme gives itself.</summary>
    public string? Name { get; internal set; }

    /// <summary>The colours the theme defines, by the slot the drawing layer names them with.</summary>
    public IReadOnlyDictionary<ThemeColorSlot, uint> Scheme => _scheme;

    /// <summary>
    /// The value a theme colour resolves to, with any tint or shade applied, or
    /// <see langword="null"/> when the theme does not define the slot.
    /// </summary>
    /// <param name="color">The colour to resolve.</param>
    public uint? Resolve(WordColor color)
    {
        if (color.Kind != ColorKind.Theme || Slot(color.ThemeSlot) is not { } value)
            return null;

        if (color.ThemeShade != 0)
            return ThemeShading.Shade(value, color.ThemeShade);

        return color.ThemeTint != 0 ? ThemeShading.Tint(value, color.ThemeTint) : value;
    }

    /// <summary>The theme colour a slot names, before any tint or shade.</summary>
    /// <param name="slot">The slot, as a run names it.</param>
    public uint? Slot(ThemeColorSlot slot)
    {
        ThemeColorSlot mapped = _mapping.TryGetValue(slot, out ThemeColorSlot target) ? target : slot;
        return _scheme.TryGetValue(mapped, out uint value) ? value : null;
    }

    internal void Define(ThemeColorSlot slot, uint value) => _scheme[slot] = value;

    internal void Map(ThemeColorSlot from, ThemeColorSlot to) => _mapping[from] = to;
}

/// <summary>
/// Lightening and darkening a theme colour ([MS-OI29500] 2.1.87). Both work on the lightness
/// of the colour rather than on its channels, so a tinted red stays red instead of turning
/// pink through grey.
/// </summary>
internal static class ThemeShading
{
    /// <summary>Lightens a colour towards white.</summary>
    /// <param name="rgb">The packed colour.</param>
    /// <param name="tint">The raw attribute value, where <c>0xFF</c> is no change.</param>
    public static uint Tint(uint rgb, byte tint)
    {
        double amount = tint / 255d;
        (double hue, double saturation, double lightness) = ToHsl(rgb);
        return FromHsl(hue, saturation, (lightness * amount) + (1 - amount));
    }

    /// <summary>Darkens a colour towards black.</summary>
    /// <param name="rgb">The packed colour.</param>
    /// <param name="shade">The raw attribute value, where <c>0xFF</c> is no change.</param>
    public static uint Shade(uint rgb, byte shade)
    {
        (double hue, double saturation, double lightness) = ToHsl(rgb);
        return FromHsl(hue, saturation, lightness * (shade / 255d));
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(uint rgb)
    {
        double red = ((rgb >> 16) & 0xFF) / 255d;
        double green = ((rgb >> 8) & 0xFF) / 255d;
        double blue = (rgb & 0xFF) / 255d;

        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2;
        if (max - min < 1e-9)
            return (0, 0, lightness);

        double range = max - min;
        double saturation = lightness > 0.5 ? range / (2 - max - min) : range / (max + min);
        double hue =
            max == red ? ((green - blue) / range) + (green < blue ? 6 : 0) :
            max == green ? ((blue - red) / range) + 2 :
            ((red - green) / range) + 4;

        return (hue / 6, saturation, lightness);
    }

    private static uint FromHsl(double hue, double saturation, double lightness)
    {
        lightness = Math.Clamp(lightness, 0, 1);
        if (saturation < 1e-9)
        {
            byte grey = Channel(lightness);
            return ((uint)grey << 16) | ((uint)grey << 8) | grey;
        }

        double q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - (lightness * saturation);
        double p = (2 * lightness) - q;

        return ((uint)Channel(Component(p, q, hue + (1 / 3d))) << 16)
            | ((uint)Channel(Component(p, q, hue)) << 8)
            | Channel(Component(p, q, hue - (1 / 3d)));
    }

    private static double Component(double p, double q, double t)
    {
        if (t < 0)
            t += 1;
        if (t > 1)
            t -= 1;

        return t < 1 / 6d ? p + ((q - p) * 6 * t)
            : t < 1 / 2d ? q
            : t < 2 / 3d ? p + ((q - p) * ((2 / 3d) - t) * 6)
            : p;
    }

    /// <summary>
    /// One channel of the result. Word truncates rather than rounds here, and matching it
    /// matters: a colour that comes out one step off is one a caller cannot compare against
    /// the value the file itself cached.
    /// </summary>
    private static byte Channel(double value) => (byte)Math.Clamp(Math.Floor(value * 255), 0, 255);
}
