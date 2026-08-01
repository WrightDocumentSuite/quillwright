using System.Globalization;

namespace Quillwright.Primitives;

/// <summary>The slots of a document theme a colour can point at (<c>ST_ThemeColor</c>).</summary>
public enum ThemeColorSlot : byte
{
    /// <summary>No theme slot; the colour is literal.</summary>
    None = 0,

    /// <summary>Main dark colour (<c>dark1</c>).</summary>
    Dark1,

    /// <summary>Main light colour (<c>light1</c>).</summary>
    Light1,

    /// <summary>Secondary dark colour (<c>dark2</c>).</summary>
    Dark2,

    /// <summary>Secondary light colour (<c>light2</c>).</summary>
    Light2,

    /// <summary>Accent 1.</summary>
    Accent1,

    /// <summary>Accent 2.</summary>
    Accent2,

    /// <summary>Accent 3.</summary>
    Accent3,

    /// <summary>Accent 4.</summary>
    Accent4,

    /// <summary>Accent 5.</summary>
    Accent5,

    /// <summary>Accent 6.</summary>
    Accent6,

    /// <summary>Hyperlink colour.</summary>
    Hyperlink,

    /// <summary>Followed hyperlink colour.</summary>
    FollowedHyperlink,

    /// <summary>First background colour.</summary>
    Background1,

    /// <summary>First text colour.</summary>
    Text1,

    /// <summary>Second background colour.</summary>
    Background2,

    /// <summary>Second text colour.</summary>
    Text2,
}

/// <summary>How a <see cref="WordColor"/> gets its value.</summary>
public enum ColorKind : byte
{
    /// <summary>Determined by the consumer from context, usually black on white (<c>auto</c>).</summary>
    Auto = 0,

    /// <summary>A literal sRGB value.</summary>
    Rgb,

    /// <summary>A theme slot, optionally lightened or darkened.</summary>
    Theme,
}

/// <summary>
/// A colour as WordprocessingML expresses it: automatic, a literal sRGB triple, or a theme
/// slot with an optional tint or shade. Theme colours keep their slot rather than being
/// flattened, so a document that gets a new theme recolours the way Word intends.
/// </summary>
public readonly struct WordColor : IEquatable<WordColor>
{
    private readonly uint _rgb;
    private readonly ThemeColorSlot _slot;
    private readonly byte _tint;
    private readonly byte _shade;
    private readonly ColorKind _kind;

    private WordColor(ColorKind kind, uint rgb, ThemeColorSlot slot, byte tint, byte shade)
    {
        _kind = kind;
        _rgb = rgb;
        _slot = slot;
        _tint = tint;
        _shade = shade;
    }

    /// <summary>The automatic colour, chosen by the consumer from context.</summary>
    public static WordColor Auto => default;

    /// <summary>Opaque black.</summary>
    public static WordColor Black => FromRgb(0x000000);

    /// <summary>Opaque white.</summary>
    public static WordColor White => FromRgb(0xFFFFFF);

    /// <summary>Creates a colour from a packed <c>0xRRGGBB</c> value.</summary>
    public static WordColor FromRgb(uint rgb) => new(ColorKind.Rgb, rgb & 0xFFFFFFu, ThemeColorSlot.None, 0, 0);

    /// <summary>Creates a colour from its components.</summary>
    public static WordColor FromRgb(byte red, byte green, byte blue) =>
        FromRgb(((uint)red << 16) | ((uint)green << 8) | blue);

    /// <summary>
    /// Creates a theme colour. <paramref name="tint"/> lightens and <paramref name="shade"/>
    /// darkens the slot; both are the raw <c>00</c>–<c>FF</c> values of the OOXML attributes
    /// and <c>0</c> means the attribute is absent.
    /// </summary>
    public static WordColor FromTheme(ThemeColorSlot slot, byte tint = 0, byte shade = 0) =>
        new(ColorKind.Theme, 0, slot, tint, shade);

    /// <summary>How this colour gets its value.</summary>
    public ColorKind Kind => _kind;

    /// <summary>The packed <c>0xRRGGBB</c> value; meaningful when <see cref="Kind"/> is <see cref="ColorKind.Rgb"/>.</summary>
    public uint Rgb => _rgb;

    /// <summary>Red component of <see cref="Rgb"/>.</summary>
    public byte Red => (byte)(_rgb >> 16);

    /// <summary>Green component of <see cref="Rgb"/>.</summary>
    public byte Green => (byte)(_rgb >> 8);

    /// <summary>Blue component of <see cref="Rgb"/>.</summary>
    public byte Blue => (byte)_rgb;

    /// <summary>The theme slot; <see cref="ThemeColorSlot.None"/> unless <see cref="Kind"/> is <see cref="ColorKind.Theme"/>.</summary>
    public ThemeColorSlot ThemeSlot => _slot;

    /// <summary>Lightening applied to the theme slot; <c>0</c> when absent.</summary>
    public byte ThemeTint => _tint;

    /// <summary>Darkening applied to the theme slot; <c>0</c> when absent.</summary>
    public byte ThemeShade => _shade;

    /// <summary>Returns <see langword="true"/> for the automatic colour.</summary>
    public bool IsAuto => _kind == ColorKind.Auto;

    /// <summary>Formats the colour as the six hex digits OOXML expects, or <c>auto</c>.</summary>
    public string ToHex() => _kind == ColorKind.Auto ? "auto" : _rgb.ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>Parses the value of a <c>w:val</c> colour attribute.</summary>
    public static WordColor Parse(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return Auto;

        if (value[0] == '#')
            value = value[1..];

        return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)
            ? FromRgb(rgb)
            : Auto;
    }

    /// <summary>Parses the <c>w:themeColor</c> attribute value.</summary>
    public static ThemeColorSlot ParseThemeSlot(ReadOnlySpan<char> value) => value switch
    {
        "dark1" => ThemeColorSlot.Dark1,
        "light1" => ThemeColorSlot.Light1,
        "dark2" => ThemeColorSlot.Dark2,
        "light2" => ThemeColorSlot.Light2,
        "accent1" => ThemeColorSlot.Accent1,
        "accent2" => ThemeColorSlot.Accent2,
        "accent3" => ThemeColorSlot.Accent3,
        "accent4" => ThemeColorSlot.Accent4,
        "accent5" => ThemeColorSlot.Accent5,
        "accent6" => ThemeColorSlot.Accent6,
        "hyperlink" => ThemeColorSlot.Hyperlink,
        "followedHyperlink" => ThemeColorSlot.FollowedHyperlink,
        "background1" => ThemeColorSlot.Background1,
        "text1" => ThemeColorSlot.Text1,
        "background2" => ThemeColorSlot.Background2,
        "text2" => ThemeColorSlot.Text2,
        _ => ThemeColorSlot.None,
    };

    /// <summary>Formats a theme slot as the <c>w:themeColor</c> attribute value.</summary>
    public static string ThemeSlotToString(ThemeColorSlot slot) => slot switch
    {
        ThemeColorSlot.Dark1 => "dark1",
        ThemeColorSlot.Light1 => "light1",
        ThemeColorSlot.Dark2 => "dark2",
        ThemeColorSlot.Light2 => "light2",
        ThemeColorSlot.Accent1 => "accent1",
        ThemeColorSlot.Accent2 => "accent2",
        ThemeColorSlot.Accent3 => "accent3",
        ThemeColorSlot.Accent4 => "accent4",
        ThemeColorSlot.Accent5 => "accent5",
        ThemeColorSlot.Accent6 => "accent6",
        ThemeColorSlot.Hyperlink => "hyperlink",
        ThemeColorSlot.FollowedHyperlink => "followedHyperlink",
        ThemeColorSlot.Background1 => "background1",
        ThemeColorSlot.Text1 => "text1",
        ThemeColorSlot.Background2 => "background2",
        ThemeColorSlot.Text2 => "text2",
        _ => "none",
    };

    /// <summary>Converts a packed <c>0xRRGGBB</c> value to a literal colour.</summary>
    public static implicit operator WordColor(uint rgb) => FromRgb(rgb);

    /// <inheritdoc />
    public bool Equals(WordColor other) =>
        _kind == other._kind && _rgb == other._rgb && _slot == other._slot &&
        _tint == other._tint && _shade == other._shade;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is WordColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_kind, _rgb, _slot, _tint, _shade);

    /// <summary>Compares two colours for equality.</summary>
    public static bool operator ==(WordColor left, WordColor right) => left.Equals(right);

    /// <summary>Compares two colours for inequality.</summary>
    public static bool operator !=(WordColor left, WordColor right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => _kind switch
    {
        ColorKind.Auto => "auto",
        ColorKind.Theme => $"{ThemeSlotToString(_slot)}{(_tint != 0 ? $" tint {_tint:X2}" : "")}{(_shade != 0 ? $" shade {_shade:X2}" : "")}",
        _ => "#" + ToHex(),
    };
}
