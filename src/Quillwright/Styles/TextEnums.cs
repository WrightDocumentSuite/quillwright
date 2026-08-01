namespace Quillwright.Styles;

/// <summary>Underline decorations (<c>ST_Underline</c>).</summary>
public enum UnderlineStyle : byte
{
    /// <summary>No underline.</summary>
    None = 0,

    /// <summary>A single line.</summary>
    Single,

    /// <summary>A single line under words but not spaces.</summary>
    Words,

    /// <summary>Two lines.</summary>
    Double,

    /// <summary>A single thick line.</summary>
    Thick,

    /// <summary>A dotted line.</summary>
    Dotted,

    /// <summary>A thick dotted line.</summary>
    DottedHeavy,

    /// <summary>A dashed line.</summary>
    Dash,

    /// <summary>A thick dashed line.</summary>
    DashedHeavy,

    /// <summary>A line of long dashes.</summary>
    DashLong,

    /// <summary>A thick line of long dashes.</summary>
    DashLongHeavy,

    /// <summary>Alternating dots and dashes.</summary>
    DotDash,

    /// <summary>Thick alternating dots and dashes.</summary>
    DashDotHeavy,

    /// <summary>Two dots then a dash, repeating.</summary>
    DotDotDash,

    /// <summary>Thick two dots then a dash, repeating.</summary>
    DashDotDotHeavy,

    /// <summary>A wavy line.</summary>
    Wave,

    /// <summary>A thick wavy line.</summary>
    WavyHeavy,

    /// <summary>Two wavy lines.</summary>
    WavyDouble,
}

/// <summary>The fixed highlighter palette (<c>ST_HighlightColor</c>).</summary>
public enum HighlightColor : byte
{
    /// <summary>No highlighting.</summary>
    None = 0,

    /// <summary>Black.</summary>
    Black,

    /// <summary>Blue.</summary>
    Blue,

    /// <summary>Cyan.</summary>
    Cyan,

    /// <summary>Green.</summary>
    Green,

    /// <summary>Magenta.</summary>
    Magenta,

    /// <summary>Red.</summary>
    Red,

    /// <summary>Yellow.</summary>
    Yellow,

    /// <summary>White.</summary>
    White,

    /// <summary>Dark blue.</summary>
    DarkBlue,

    /// <summary>Dark cyan.</summary>
    DarkCyan,

    /// <summary>Dark green.</summary>
    DarkGreen,

    /// <summary>Dark magenta.</summary>
    DarkMagenta,

    /// <summary>Dark red.</summary>
    DarkRed,

    /// <summary>Dark yellow.</summary>
    DarkYellow,

    /// <summary>Dark grey.</summary>
    DarkGray,

    /// <summary>Light grey.</summary>
    LightGray,
}

/// <summary>Baseline placement of a run (<c>ST_VerticalAlignRun</c>).</summary>
public enum VerticalTextAlignment : byte
{
    /// <summary>On the baseline.</summary>
    Baseline = 0,

    /// <summary>Raised and reduced.</summary>
    Superscript,

    /// <summary>Lowered and reduced.</summary>
    Subscript,
}

/// <summary>Line styles usable on a border (<c>ST_Border</c>, excluding the art borders).</summary>
public enum BorderStyle : byte
{
    /// <summary>No border, and no space reserved for one (<c>nil</c>).</summary>
    Nil = 0,

    /// <summary>No border (<c>none</c>).</summary>
    None,

    /// <summary>A single line.</summary>
    Single,

    /// <summary>A single thick line.</summary>
    Thick,

    /// <summary>Two lines.</summary>
    Double,

    /// <summary>A dotted line.</summary>
    Dotted,

    /// <summary>A dashed line.</summary>
    Dashed,

    /// <summary>Alternating dots and dashes.</summary>
    DotDash,

    /// <summary>Two dots then a dash, repeating.</summary>
    DotDotDash,

    /// <summary>Three lines.</summary>
    Triple,

    /// <summary>Thin then thick, small gap.</summary>
    ThinThickSmallGap,

    /// <summary>Thick then thin, small gap.</summary>
    ThickThinSmallGap,

    /// <summary>Thin, thick then thin, small gap.</summary>
    ThinThickThinSmallGap,

    /// <summary>Thin then thick, medium gap.</summary>
    ThinThickMediumGap,

    /// <summary>Thick then thin, medium gap.</summary>
    ThickThinMediumGap,

    /// <summary>Thin, thick then thin, medium gap.</summary>
    ThinThickThinMediumGap,

    /// <summary>Thin then thick, large gap.</summary>
    ThinThickLargeGap,

    /// <summary>Thick then thin, large gap.</summary>
    ThickThinLargeGap,

    /// <summary>Thin, thick then thin, large gap.</summary>
    ThinThickThinLargeGap,

    /// <summary>A wavy line.</summary>
    Wave,

    /// <summary>Two wavy lines.</summary>
    DoubleWave,

    /// <summary>Dashes with small gaps.</summary>
    DashSmallGap,

    /// <summary>A stroked dash-dot line.</summary>
    DashDotStroked,

    /// <summary>An embossed 3D line.</summary>
    ThreeDEmboss,

    /// <summary>An engraved 3D line.</summary>
    ThreeDEngrave,

    /// <summary>An outset line.</summary>
    Outset,

    /// <summary>An inset line.</summary>
    Inset,

    /// <summary>An art border or a value this version does not know; the name is kept verbatim.</summary>
    Custom = 255,
}

/// <summary>Fill patterns of a shading (<c>ST_Shd</c>).</summary>
public enum ShadingPattern : byte
{
    /// <summary>No shading, and no space reserved for one.</summary>
    Nil = 0,

    /// <summary>No pattern; the background colour shows through.</summary>
    Clear,

    /// <summary>The foreground colour fills the area.</summary>
    Solid,

    /// <summary>Horizontal stripes.</summary>
    HorizontalStripe,

    /// <summary>Vertical stripes.</summary>
    VerticalStripe,

    /// <summary>Diagonal stripes rising to the right.</summary>
    DiagonalStripe,

    /// <summary>Diagonal stripes falling to the right.</summary>
    ReverseDiagonalStripe,

    /// <summary>A horizontal and vertical grid.</summary>
    HorizontalCross,

    /// <summary>A diagonal grid.</summary>
    DiagonalCross,

    /// <summary>A percentage fill or a value this version does not know; the name is kept verbatim.</summary>
    Custom = 255,
}

/// <summary>Alignment of a tab stop (<c>ST_TabJc</c>).</summary>
public enum TabAlignment : byte
{
    /// <summary>Text starts at the stop.</summary>
    Left = 0,

    /// <summary>Text is centred on the stop.</summary>
    Center,

    /// <summary>Text ends at the stop.</summary>
    Right,

    /// <summary>Numbers align on their decimal separator.</summary>
    Decimal,

    /// <summary>A vertical bar is drawn at the stop.</summary>
    Bar,

    /// <summary>The stop inherited from the style is removed.</summary>
    Clear,

    /// <summary>The stop is placed relative to the numbering indent.</summary>
    Number,
}

/// <summary>The filler drawn in front of a tab stop (<c>ST_TabTlc</c>).</summary>
public enum TabLeader : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>A row of dots.</summary>
    Dot,

    /// <summary>A row of hyphens.</summary>
    Hyphen,

    /// <summary>A continuous line.</summary>
    Underscore,

    /// <summary>A heavy continuous line.</summary>
    Heavy,

    /// <summary>A row of middle dots.</summary>
    MiddleDot,
}
