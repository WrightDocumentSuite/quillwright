namespace Quillwright.Pdf;

/// <summary>
/// Controls how a Word document is turned into PDF.
/// </summary>
/// <remarks>
/// The defaults render what Word would print: fonts taken from the machine, pagination driven by
/// the document's own section properties, no tagging. Everything that costs something — embedding
/// a whole font, building a structure tree — is opt-in or opt-out here rather than guessed at.
/// </remarks>
public sealed class PdfExportOptions
{
    /// <summary>The options used when a caller passes none.</summary>
    public static PdfExportOptions Default { get; } = new();

    /// <summary>
    /// The family used when a run names a font this machine does not have and no substitute in
    /// the chain matches either.
    /// </summary>
    public string FallbackFontFamily { get; set; } = "Arial";

    /// <summary>
    /// Extra font files to draw with, keyed by family name. They are consulted before the
    /// machine's own fonts, which is how a server with no fonts installed still renders correctly.
    /// </summary>
    /// <remarks>
    /// The value is the path of a TrueType or OpenType file. Give one entry per face and name the
    /// key the way the document does, for example <c>Calibri Bold</c> beside <c>Calibri</c>.
    /// </remarks>
    public Dictionary<string, string> FontFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to ship only the glyphs the document actually uses. Turning it off embeds whole
    /// font programs, which is larger but survives editing by another tool.
    /// </summary>
    public bool SubsetFonts { get; set; } = true;

    /// <summary>Whether to build a structure tree so the result is a tagged PDF.</summary>
    public bool Tagged { get; set; }

    /// <summary>
    /// Whether Word comments become interactive PDF note annotations. Replies remain a thread,
    /// authors and dates are retained, and a resolved Word message receives an informational reply.
    /// Word records no resolver identity, so the exporter cannot write the user-specific PDF review
    /// state without inventing provenance. The default leaves comments out, matching an ordinary
    /// print.
    /// </summary>
    public bool IncludeComments { get; set; }

    /// <summary>
    /// Whether to recompute the result of <c>PAGE</c>, <c>NUMPAGES</c>, <c>SECTIONPAGES</c>
    /// and <c>PAGEREF</c> fields — the ones a table of contents prints its numbers with.
    /// Turning it off prints the result Word cached, which is what a viewer would show for a
    /// document that has not been repaginated.
    /// </summary>
    public bool UpdatePageFields { get; set; } = true;

    /// <summary>
    /// Whether <c>w:lastRenderedPageBreak</c> hints saved by Word are used to reproduce the
    /// source pagination.  These hints are especially useful for long fixed-layout tables;
    /// turn this off after substantial editing so stale hints do not force old page boundaries.
    /// </summary>
    public bool HonorLastRenderedPageBreaks { get; set; } = true;

    /// <summary>Whether hidden text (<c>w:vanish</c>) is printed.</summary>
    public bool IncludeHiddenText { get; set; }

    /// <summary>
    /// The title written into the document information dictionary. Falls back to the Word
    /// document's own title, and then to nothing.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The document language, as a BCP 47 tag. Falls back to the language of the default run
    /// formatting, which is what Word writes into <c>docDefaults</c>.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// How many pages may be produced before the export gives up. A document whose content cannot
    /// fit — a table row taller than the page, a frame that never advances — would otherwise
    /// paginate forever.
    /// </summary>
    public int MaxPages { get; set; } = 20_000;

    /// <summary>
    /// Pattern sets for automatic hyphenation, keyed by BCP 47 language tag. A run finds its set
    /// by its own tag first and by the primary subtag second, so one entry under <c>en</c>
    /// serves <c>en-US</c> and <c>en-GB</c> alike.
    /// </summary>
    /// <remarks>
    /// Hyphenation happens only when the document itself asks for it (<c>w:autoHyphenation</c>)
    /// and a set covers the run's language; a language left uncovered wraps whole and says so in
    /// the diagnostics. The library ships no patterns because the standard files have licences
    /// of their own — see <see cref="Pdf.HyphenationPatterns"/> for what to load.
    /// </remarks>
    public Dictionary<string, HyphenationPatterns> HyphenationPatterns { get; } = new(StringComparer.OrdinalIgnoreCase);
}
