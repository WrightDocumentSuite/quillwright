using Quillwright.Primitives;

namespace Quillwright.Styles;

/// <summary>
/// Character formatting (<c>w:rPr</c>). Every property is optional: <see langword="null"/>
/// means "inherit from the style chain", a value means "override it here".
/// </summary>
/// <remarks>
/// Immutable, so equal formats can share one instance. A document with a hundred thousand
/// runs almost always has fewer than a hundred distinct run formats, and the reader interns
/// them, so the storage cost of formatting is a pointer per run.
/// </remarks>
public sealed record RunFormat
{
    /// <summary>A format that overrides nothing.</summary>
    public static RunFormat Default { get; } = new();

    /// <summary>
    /// Revision markers on a paragraph mark (<c>w:ins</c>, <c>w:del</c>, <c>w:moveFrom</c>,
    /// <c>w:moveTo</c>), kept verbatim. They are only legal inside <c>w:pPr/w:rPr</c>, where
    /// the schema puts them before everything else.
    /// </summary>
    public string? MarkRevisionXml { get; init; }

    /// <summary>Identifier of the character style this run is based on (<c>w:rStyle</c>).</summary>
    public string? StyleId { get; init; }

    /// <summary>Font for Latin text (<c>w:rFonts/@w:ascii</c>).</summary>
    public string? FontAscii { get; init; }

    /// <summary>Font for high-ANSI text (<c>w:rFonts/@w:hAnsi</c>).</summary>
    public string? FontHighAnsi { get; init; }

    /// <summary>Font for East Asian text (<c>w:rFonts/@w:eastAsia</c>).</summary>
    public string? FontEastAsia { get; init; }

    /// <summary>Font for complex-script text (<c>w:rFonts/@w:cs</c>).</summary>
    public string? FontComplexScript { get; init; }

    /// <summary>How a consumer picks between the font slots (<c>w:rFonts/@w:hint</c>).</summary>
    public string? FontHint { get; init; }

    /// <summary>Theme slot supplying the Latin font (<c>w:rFonts/@w:asciiTheme</c>).</summary>
    public string? FontAsciiTheme { get; init; }

    /// <summary>Theme slot supplying the high-ANSI font (<c>w:rFonts/@w:hAnsiTheme</c>).</summary>
    public string? FontHighAnsiTheme { get; init; }

    /// <summary>Theme slot supplying the East Asian font (<c>w:rFonts/@w:eastAsiaTheme</c>).</summary>
    public string? FontEastAsiaTheme { get; init; }

    /// <summary>Theme slot supplying the complex-script font (<c>w:rFonts/@w:cstheme</c>).</summary>
    public string? FontComplexScriptTheme { get; init; }

    /// <summary>Bold (<c>w:b</c>). A toggle property: it exclusive-ors down the style chain.</summary>
    public bool? Bold { get; init; }

    /// <summary>Bold for complex scripts (<c>w:bCs</c>).</summary>
    public bool? BoldComplexScript { get; init; }

    /// <summary>Italic (<c>w:i</c>). A toggle property.</summary>
    public bool? Italic { get; init; }

    /// <summary>Italic for complex scripts (<c>w:iCs</c>).</summary>
    public bool? ItalicComplexScript { get; init; }

    /// <summary>All capitals (<c>w:caps</c>). A toggle property.</summary>
    public bool? Caps { get; init; }

    /// <summary>Small capitals (<c>w:smallCaps</c>). A toggle property.</summary>
    public bool? SmallCaps { get; init; }

    /// <summary>Single strikethrough (<c>w:strike</c>). A toggle property.</summary>
    public bool? Strike { get; init; }

    /// <summary>Double strikethrough (<c>w:dstrike</c>). A toggle property.</summary>
    public bool? DoubleStrike { get; init; }

    /// <summary>Outlined glyphs (<c>w:outline</c>). A toggle property.</summary>
    public bool? Outline { get; init; }

    /// <summary>Shadowed glyphs (<c>w:shadow</c>). A toggle property.</summary>
    public bool? Shadow { get; init; }

    /// <summary>Embossed glyphs (<c>w:emboss</c>). A toggle property.</summary>
    public bool? Emboss { get; init; }

    /// <summary>Engraved glyphs (<c>w:imprint</c>). A toggle property.</summary>
    public bool? Imprint { get; init; }

    /// <summary>Excludes the run from proofing (<c>w:noProof</c>).</summary>
    public bool? NoProof { get; init; }

    /// <summary>Aligns the run to the document grid (<c>w:snapToGrid</c>).</summary>
    public bool? SnapToGrid { get; init; }

    /// <summary>Hidden text (<c>w:vanish</c>). A toggle property.</summary>
    public bool? Hidden { get; init; }

    /// <summary>Hidden when the document is shown as a web page (<c>w:webHidden</c>).</summary>
    public bool? WebHidden { get; init; }

    /// <summary>Glyph colour (<c>w:color</c>).</summary>
    public WordColor? Color { get; init; }

    /// <summary>Extra space between characters (<c>w:spacing</c>).</summary>
    public Length? CharacterSpacing { get; init; }

    /// <summary>Horizontal glyph scaling as a percentage (<c>w:w</c>).</summary>
    public int? Scale { get; init; }

    /// <summary>Smallest size at which kerning applies (<c>w:kern</c>).</summary>
    public Length? Kerning { get; init; }

    /// <summary>Vertical offset from the baseline (<c>w:position</c>).</summary>
    public Length? Position { get; init; }

    /// <summary>Font size (<c>w:sz</c>, stored in half-points).</summary>
    public Length? Size { get; init; }

    /// <summary>Font size for complex scripts (<c>w:szCs</c>).</summary>
    public Length? SizeComplexScript { get; init; }

    /// <summary>Highlighter colour (<c>w:highlight</c>).</summary>
    public HighlightColor? Highlight { get; init; }

    /// <summary>Underline decoration (<c>w:u</c>).</summary>
    public UnderlineStyle? Underline { get; init; }

    /// <summary>Underline colour (<c>w:u/@w:color</c>).</summary>
    public WordColor? UnderlineColor { get; init; }

    /// <summary>The animated text effect element, kept verbatim (<c>w:effect</c>).</summary>
    public string? EffectXml { get; init; }

    /// <summary>Border drawn around the run (<c>w:bdr</c>).</summary>
    public BorderLine? Border { get; init; }

    /// <summary>Background fill (<c>w:shd</c>).</summary>
    public Shading? Shading { get; init; }

    /// <summary>The fit-text element, kept verbatim (<c>w:fitText</c>).</summary>
    public string? FitTextXml { get; init; }

    /// <summary>Superscript or subscript placement (<c>w:vertAlign</c>).</summary>
    public VerticalTextAlignment? VerticalAlignment { get; init; }

    /// <summary>Right-to-left text (<c>w:rtl</c>). A toggle property.</summary>
    public bool? RightToLeft { get; init; }

    /// <summary>Treats the run as complex script (<c>w:cs</c>). A toggle property.</summary>
    public bool? ComplexScript { get; init; }

    /// <summary>The emphasis-mark element, kept verbatim (<c>w:em</c>).</summary>
    public string? EmphasisXml { get; init; }

    /// <summary>Language of Latin text (<c>w:lang/@w:val</c>).</summary>
    public string? Language { get; init; }

    /// <summary>Language of East Asian text (<c>w:lang/@w:eastAsia</c>).</summary>
    public string? LanguageEastAsia { get; init; }

    /// <summary>Language of complex-script text (<c>w:lang/@w:bidi</c>).</summary>
    public string? LanguageComplexScript { get; init; }

    /// <summary>The East Asian layout element, kept verbatim (<c>w:eastAsianLayout</c>).</summary>
    public string? EastAsianLayoutXml { get; init; }

    /// <summary>Hides the paragraph mark of an otherwise empty numbered paragraph (<c>w:specVanish</c>).</summary>
    public bool? SpecialHidden { get; init; }

    /// <summary>Marks the run as part of an equation (<c>w:oMath</c>).</summary>
    public bool? OfficeMath { get; init; }

    /// <summary>
    /// Children of <c>w:rPr</c> this version does not model, concatenated in document order
    /// and re-emitted after the modelled ones. Extension elements sort last in the schema,
    /// which is exactly where they land.
    /// </summary>
    public string? Extensions { get; init; }

    /// <summary>The revision record of a formatting change, kept verbatim (<c>w:rPrChange</c>).</summary>
    public string? ChangeXml { get; init; }

    /// <summary>Returns <see langword="true"/> when the format overrides nothing.</summary>
    public bool IsEmpty => Equals(Default);

    /// <summary>
    /// Overlays <paramref name="over"/> on top of this format: every property the argument
    /// specifies wins, the rest are kept. Toggle properties are exclusive-ored, which is how
    /// WordprocessingML makes bold-inside-bold come out unbold.
    /// </summary>
    /// <remarks>
    /// ISO/IEC 29500-1 §17.7.3 names the toggles exhaustively, and there are twelve: <c>b</c>,
    /// <c>bCs</c>, <c>caps</c>, <c>emboss</c>, <c>i</c>, <c>iCs</c>, <c>imprint</c>,
    /// <c>outline</c>, <c>shadow</c>, <c>smallCaps</c>, <c>strike</c> and <c>vanish</c>. The
    /// neighbours that look like they belong are not on the list and must not be
    /// exclusive-ored: <c>dstrike</c> (§17.3.2.9) is not a toggle even though <c>strike</c> is,
    /// and neither are <c>rtl</c> (§17.3.2.30) or <c>cs</c> (§17.3.2.6) — treating those two as
    /// toggles turns a right-to-left run left-to-right whenever two layers of the hierarchy
    /// both ask for it.
    /// </remarks>
    public RunFormat Merge(RunFormat? over)
    {
        if (over is null)
            return this;

        return new RunFormat
        {
            MarkRevisionXml = over.MarkRevisionXml ?? MarkRevisionXml,
            ChangeXml = over.ChangeXml ?? ChangeXml,
            StyleId = over.StyleId ?? StyleId,
            FontAscii = over.FontAscii ?? FontAscii,
            FontHighAnsi = over.FontHighAnsi ?? FontHighAnsi,
            FontEastAsia = over.FontEastAsia ?? FontEastAsia,
            FontComplexScript = over.FontComplexScript ?? FontComplexScript,
            FontHint = over.FontHint ?? FontHint,
            FontAsciiTheme = over.FontAsciiTheme ?? FontAsciiTheme,
            FontHighAnsiTheme = over.FontHighAnsiTheme ?? FontHighAnsiTheme,
            FontEastAsiaTheme = over.FontEastAsiaTheme ?? FontEastAsiaTheme,
            FontComplexScriptTheme = over.FontComplexScriptTheme ?? FontComplexScriptTheme,
            Bold = Toggle(Bold, over.Bold),
            BoldComplexScript = Toggle(BoldComplexScript, over.BoldComplexScript),
            Italic = Toggle(Italic, over.Italic),
            ItalicComplexScript = Toggle(ItalicComplexScript, over.ItalicComplexScript),
            Caps = Toggle(Caps, over.Caps),
            SmallCaps = Toggle(SmallCaps, over.SmallCaps),
            Strike = Toggle(Strike, over.Strike),
            DoubleStrike = over.DoubleStrike ?? DoubleStrike,
            Outline = Toggle(Outline, over.Outline),
            Shadow = Toggle(Shadow, over.Shadow),
            Emboss = Toggle(Emboss, over.Emboss),
            Imprint = Toggle(Imprint, over.Imprint),
            NoProof = over.NoProof ?? NoProof,
            SnapToGrid = over.SnapToGrid ?? SnapToGrid,
            Hidden = Toggle(Hidden, over.Hidden),
            WebHidden = over.WebHidden ?? WebHidden,
            Color = over.Color ?? Color,
            CharacterSpacing = over.CharacterSpacing ?? CharacterSpacing,
            Scale = over.Scale ?? Scale,
            Kerning = over.Kerning ?? Kerning,
            Position = over.Position ?? Position,
            Size = over.Size ?? Size,
            SizeComplexScript = over.SizeComplexScript ?? SizeComplexScript,
            Highlight = over.Highlight ?? Highlight,
            Underline = over.Underline ?? Underline,
            UnderlineColor = over.UnderlineColor ?? UnderlineColor,
            EffectXml = over.EffectXml ?? EffectXml,
            Border = over.Border ?? Border,
            Shading = over.Shading ?? Shading,
            FitTextXml = over.FitTextXml ?? FitTextXml,
            VerticalAlignment = over.VerticalAlignment ?? VerticalAlignment,
            RightToLeft = over.RightToLeft ?? RightToLeft,
            ComplexScript = over.ComplexScript ?? ComplexScript,
            EmphasisXml = over.EmphasisXml ?? EmphasisXml,
            Language = over.Language ?? Language,
            LanguageEastAsia = over.LanguageEastAsia ?? LanguageEastAsia,
            LanguageComplexScript = over.LanguageComplexScript ?? LanguageComplexScript,
            EastAsianLayoutXml = over.EastAsianLayoutXml ?? EastAsianLayoutXml,
            SpecialHidden = over.SpecialHidden ?? SpecialHidden,
            OfficeMath = over.OfficeMath ?? OfficeMath,
            Extensions = over.Extensions ?? Extensions,
        };
    }

    /// <summary>
    /// Overlays <paramref name="over"/> without toggle semantics, so every property simply
    /// wins where it is stated.
    /// </summary>
    /// <remarks>
    /// This is how direct formatting behaves — <c>w:b</c> on the run means bold whatever the
    /// style said — and how a <c>basedOn</c> chain behaves, because the whole chain is one
    /// layer of the hierarchy and states each toggle once (ISO/IEC 29500-1 §17.7.3).
    /// </remarks>
    public RunFormat Apply(RunFormat? over)
    {
        if (over is null)
            return this;

        RunFormat merged = Merge(over);
        return merged with
        {
            Bold = over.Bold ?? Bold,
            BoldComplexScript = over.BoldComplexScript ?? BoldComplexScript,
            Italic = over.Italic ?? Italic,
            ItalicComplexScript = over.ItalicComplexScript ?? ItalicComplexScript,
            Caps = over.Caps ?? Caps,
            SmallCaps = over.SmallCaps ?? SmallCaps,
            Strike = over.Strike ?? Strike,
            Outline = over.Outline ?? Outline,
            Shadow = over.Shadow ?? Shadow,
            Emboss = over.Emboss ?? Emboss,
            Imprint = over.Imprint ?? Imprint,
            Hidden = over.Hidden ?? Hidden,
        };
    }

    private static bool? Toggle(bool? inherited, bool? applied) => (inherited, applied) switch
    {
        (null, null) => null,
        (null, _) => applied,
        (_, null) => inherited,
        _ => inherited != applied,
    };
}
