using Quillwright.Primitives;

namespace Quillwright.Styles;

/// <summary>
/// The style catalogue of a document (<c>styles.xml</c>): the document defaults plus every
/// named style.
/// </summary>
/// <remarks>
/// Built-in styles are created on demand rather than up front. Word does the same: a fresh
/// document declares only the handful of styles it actually uses, and asking for
/// <c>Heading1</c> is what brings its definition into the file.
/// </remarks>
public sealed class StyleSheet
{
    private readonly Dictionary<string, Style> _styles = new(StringComparer.OrdinalIgnoreCase);
    private int _version;

    /// <summary>Character formatting every run starts from (<c>w:docDefaults/w:rPrDefault</c>).</summary>
    public RunFormat DefaultRunFormat { get; set; } = new()
    {
        FontAsciiTheme = "minorHAnsi",
        FontHighAnsiTheme = "minorHAnsi",
        FontEastAsiaTheme = "minorEastAsia",
        FontComplexScriptTheme = "minorBidi",
        Size = Length.FromHalfPoints(22),
        SizeComplexScript = Length.FromHalfPoints(22),
        Language = "en-US",
        LanguageEastAsia = "en-US",
        LanguageComplexScript = "ar-SA",
    };

    /// <summary>Paragraph formatting every paragraph starts from (<c>w:docDefaults/w:pPrDefault</c>).</summary>
    public ParagraphFormat DefaultParagraphFormat { get; set; } = ParagraphFormat.Default;

    /// <summary>The latent-style declarations, kept verbatim (<c>w:latentStyles</c>).</summary>
    public string? LatentStylesXml { get; set; }

    /// <summary>Attributes of <c>w:styles</c> this version does not model, kept verbatim.</summary>
    public string? Attributes { get; set; }

    /// <summary>Number of defined styles.</summary>
    public int Count => _styles.Count;

    /// <summary>Every defined style.</summary>
    public IReadOnlyCollection<Style> All => _styles.Values;

    /// <summary>
    /// Increments whenever a style changes, so caches built on the resolved chains know when
    /// to drop what they computed.
    /// </summary>
    internal int Version => _version;

    /// <summary>Returns the style with the given identifier, or <see langword="null"/>.</summary>
    /// <param name="id">Style identifier.</param>
    public Style? Find(string? id) => id is not null && _styles.TryGetValue(id, out Style? style) ? style : null;

    /// <summary>Returns the default style of a kind, or <see langword="null"/> when none is marked.</summary>
    /// <param name="kind">Which kind of style.</param>
    public Style? FindDefault(StyleKind kind) => _styles.Values.FirstOrDefault(s => s.IsDefault && s.Kind == kind);

    /// <summary>Adds or replaces a style.</summary>
    /// <param name="style">The style to add.</param>
    public Style Add(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _styles[style.Id] = style;
        _version++;
        return style;
    }

    /// <summary>
    /// Returns a style, creating it from the built-in catalogue when the identifier names a
    /// style Word knows and it is not defined yet.
    /// </summary>
    /// <param name="id">Style identifier.</param>
    /// <param name="kind">Kind to use when the style has to be created and is not built in.</param>
    public Style GetOrAdd(string id, StyleKind kind = StyleKind.Paragraph)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (_styles.TryGetValue(id, out Style? existing))
            return existing;

        Style created = BuiltInStyles.Create(id) ?? new Style(id, kind) { Name = id, IsCustom = true };
        foreach (string dependency in BuiltInStyles.Dependencies(created))
            _ = GetOrAdd(dependency);

        return Add(created);
    }

    /// <summary>Removes a style.</summary>
    /// <param name="id">Style identifier.</param>
    public bool Remove(string id)
    {
        if (!_styles.Remove(id))
            return false;
        _version++;
        return true;
    }

    /// <summary>Notifies caches that a style definition was edited in place.</summary>
    public void Invalidate() => _version++;

    /// <summary>Creates the minimum set of styles a new document needs.</summary>
    internal static StyleSheet CreateDefault()
    {
        var sheet = new StyleSheet();
        sheet.GetOrAdd("Normal");
        sheet.GetOrAdd("DefaultParagraphFont", StyleKind.Character);
        sheet.GetOrAdd("TableNormal", StyleKind.Table);
        sheet.GetOrAdd("NoList", StyleKind.Numbering);
        return sheet;
    }

    /// <summary>
    /// Walks a style chain from its root down to the given style, so a caller can apply the
    /// contributions in inheritance order. Cycles are cut, which keeps a corrupt file from
    /// hanging the resolver.
    /// </summary>
    /// <param name="id">Identifier of the most derived style.</param>
    internal List<Style> Chain(string? id)
    {
        var chain = new List<Style>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (Style? style = Find(id); style is not null && seen.Add(style.Id); style = Find(style.BasedOn))
            chain.Add(style);

        chain.Reverse();
        return chain;
    }
}
