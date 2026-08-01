namespace Quillwright.Pdf.Layout;

/// <summary>
/// Answers where a word may break, for the line breaker: finds the letters inside a segment,
/// picks the pattern set for the run's language and applies the document's own exemptions.
/// </summary>
/// <remarks>
/// One of these exists per export, and only when the document asks for automatic hyphenation
/// (<c>w:autoHyphenation</c>). A language with no pattern set supplied wraps whole, once per
/// language with a diagnostic naming it — silence here would look like a layout bug.
/// </remarks>
internal sealed class Hyphenator
{
    private readonly PdfExportOptions _options;
    private readonly PdfExportDiagnostics _diagnostics;
    private readonly bool _skipCapitals;
    private readonly string? _defaultLanguage;

    private Hyphenator(
        PdfExportOptions options, PdfExportDiagnostics diagnostics, bool skipCapitals, string? defaultLanguage)
    {
        _options = options;
        _diagnostics = diagnostics;
        _skipCapitals = skipCapitals;
        _defaultLanguage = defaultLanguage;
    }

    /// <summary>The service, or <see langword="null"/> when the document does not hyphenate.</summary>
    /// <param name="source">The document being rendered.</param>
    /// <param name="options">How the caller wants it rendered.</param>
    /// <param name="diagnostics">Where a missing pattern set is reported.</param>
    public static Hyphenator? Create(
        Model.WordDocument source, PdfExportOptions options, PdfExportDiagnostics diagnostics)
    {
        if (!source.Settings.AutoHyphenation)
            return null;

        return new Hyphenator(options, diagnostics, source.Settings.DoNotHyphenateCaps, options.Language);
    }

    /// <summary>
    /// Where a segment may break: for each entry, how many of the segment's characters stay on
    /// the line, a hyphen after them. Empty when it may not break at all.
    /// </summary>
    /// <param name="segment">The unbreakable piece the line breaker is placing.</param>
    /// <param name="language">The language of the run, as a BCP 47 tag.</param>
    public int[] Opportunities(string segment, string? language)
    {
        // The word is the letters; leading punctuation shifts the breaks, anything else — a
        // digit, an apostrophe, a second word — makes the segment no word to hyphenate.
        int first = 0;
        while (first < segment.Length && !char.IsLetter(segment[first]))
            first++;

        int last = segment.Length;
        while (last > first && !char.IsLetter(segment[last - 1]))
            last--;

        if (last - first < 4)
            return [];

        ReadOnlySpan<char> word = segment.AsSpan(first, last - first);
        bool allCapitals = true;
        foreach (char c in word)
        {
            if (!char.IsLetter(c))
                return [];

            if (!char.IsUpper(c))
                allCapitals = false;
        }

        if (allCapitals && _skipCapitals)
            return [];

        if (PatternsFor(language ?? _defaultLanguage) is not { } patterns)
            return [];

        int[] breaks = patterns.Opportunities(word);
        if (breaks.Length == 0 || first == 0)
            return breaks;

        var shifted = new int[breaks.Length];
        for (int i = 0; i < breaks.Length; i++)
            shifted[i] = breaks[i] + first;

        return shifted;
    }

    /// <summary>The pattern set for a language: the whole tag, or its primary subtag.</summary>
    private HyphenationPatterns? PatternsFor(string? language)
    {
        if (language is { Length: > 0 })
        {
            if (_options.HyphenationPatterns.TryGetValue(language, out HyphenationPatterns? exact))
                return exact;

            int dash = language.IndexOf('-');
            if (dash > 0 && _options.HyphenationPatterns.TryGetValue(language[..dash], out HyphenationPatterns? primary))
                return primary;
        }

        _diagnostics.Add(
            PdfExportWarningKind.LayoutApproximated,
            "The document hyphenates automatically, but no pattern set was supplied for this language, so its words wrapped whole",
            language is { Length: > 0 } ? language : "language unstated");
        return null;
    }
}
