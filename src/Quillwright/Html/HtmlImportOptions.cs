using System.Collections;
using Quillwright.Diagnostics;
using Quillwright.Model;

namespace Quillwright.Html;

/// <summary>Controls how HTML becomes a document.</summary>
public sealed record HtmlImportOptions
{
    /// <summary>Resource limits for source markup, the parsed tree and imported images.</summary>
    public DocumentLoadBudget Budget { get; init; } = DocumentLoadBudget.Default;

    /// <summary>
    /// Where a relative image path resolves. When unset, images that name a file cannot be
    /// loaded and come through as their alternative text, each named in the diagnostics. Only
    /// portable relative paths are followed; rooted paths, dot segments and filesystem links
    /// below this caller-trusted directory are rejected.
    /// </summary>
    public string? MediaDirectory { get; init; }

    /// <summary>Whether images are loaded and embedded at all.</summary>
    public bool ImportImages { get; init; } = true;
}

/// <summary>What kind of compromise the importer had to make.</summary>
public enum HtmlImportWarningKind : byte
{
    /// <summary>An element with no Word counterpart was unwrapped or approximated.</summary>
    UnsupportedElement = 0,

    /// <summary>An image could not be loaded, so its alternative text stands in for it.</summary>
    ImageSkipped,

    /// <summary>Content that cannot be carried — a script, an embedded frame — was left out.</summary>
    ContentSkipped,

    /// <summary>A generated note definition, reference or reciprocal link was malformed or duplicated.</summary>
    NoteMalformed,

    /// <summary>A generated note reference or definition has no matching counterpart.</summary>
    NoteDangling,
}

/// <summary>One compromise made while importing, with the line it was made on.</summary>
/// <param name="Kind">What kind of compromise it was.</param>
/// <param name="Message">What happened, in one sentence.</param>
/// <param name="Subject">The element, path or fragment involved.</param>
/// <param name="Line">The 1-based source line, or zero when it has none.</param>
public readonly record struct HtmlImportWarning(
    HtmlImportWarningKind Kind, string Message, string? Subject = null, int Line = 0)
{
    /// <inheritdoc />
    public override string ToString() =>
        (Line > 0 ? $"line {Line}: " : string.Empty) + Message + (Subject is null ? string.Empty : $" ({Subject})");
}

/// <summary>
/// Everything the importer had to work around. An import never throws for markup it cannot
/// honour: it imports what it can and says here what it approximated. Repeated occurrences of
/// the same kind and subject are reported once.
/// </summary>
public sealed class HtmlImportDiagnostics : IReadOnlyList<HtmlImportWarning>
{
    private readonly List<HtmlImportWarning> _warnings = [];
    private readonly HashSet<(HtmlImportWarningKind Kind, string? Subject)> _seen = [];

    /// <inheritdoc />
    public int Count => _warnings.Count;

    /// <inheritdoc />
    public HtmlImportWarning this[int index] => _warnings[index];

    /// <summary>Whether anything at all had to be worked around.</summary>
    public bool IsEmpty => _warnings.Count == 0;

    /// <inheritdoc />
    public IEnumerator<HtmlImportWarning> GetEnumerator() => _warnings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => string.Join(Environment.NewLine, _warnings);

    internal void Add(HtmlImportWarningKind kind, string message, string? subject = null, int line = 0)
    {
        if (_seen.Add((kind, subject)))
            _warnings.Add(new HtmlImportWarning(kind, message, subject, line));
    }
}

/// <summary>What an import produced: the document, and what was approximated on the way.</summary>
public sealed class HtmlImportResult
{
    internal HtmlImportResult(WordDocument document, HtmlImportDiagnostics diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }

    /// <summary>The document the HTML became.</summary>
    public WordDocument Document { get; }

    /// <summary>Every approximation the import had to make.</summary>
    public HtmlImportDiagnostics Diagnostics { get; }
}
