using System.Collections;
using Quillwright.Model;

namespace Quillwright.Markdown;

/// <summary>Controls how Markdown becomes a document.</summary>
public sealed record MarkdownImportOptions
{
    /// <summary>
    /// Where a relative image path resolves. When unset, images that name a file cannot be
    /// loaded and come through as their alternative text, each named in the diagnostics.
    /// </summary>
    public string? MediaDirectory { get; init; }

    /// <summary>Whether images are loaded and embedded at all.</summary>
    public bool ImportImages { get; init; } = true;
}

/// <summary>What kind of compromise the importer had to make.</summary>
public enum MarkdownImportWarningKind : byte
{
    /// <summary>Syntax with no Word counterpart was carried as ordinary text.</summary>
    UnsupportedSyntax,

    /// <summary>An image could not be loaded, so its alternative text stands in for it.</summary>
    ImageSkipped,

    /// <summary>Raw HTML has no interpreter here and was kept as the text it is.</summary>
    HtmlKeptAsText,
}

/// <summary>One compromise made while importing, with the line it was made on.</summary>
/// <param name="Kind">What kind of compromise it was.</param>
/// <param name="Message">What happened, in one sentence.</param>
/// <param name="Subject">The construct, path or fragment involved.</param>
/// <param name="Line">The 1-based source line, or zero when it has none.</param>
public readonly record struct MarkdownImportWarning(
    MarkdownImportWarningKind Kind, string Message, string? Subject = null, int Line = 0)
{
    /// <inheritdoc />
    public override string ToString() =>
        (Line > 0 ? $"line {Line}: " : string.Empty) + Message + (Subject is null ? string.Empty : $" ({Subject})");
}

/// <summary>
/// Everything the importer had to work around. An import never throws for syntax it cannot
/// honour: it imports what it can and says here what it approximated.
/// </summary>
public sealed class MarkdownImportDiagnostics : IReadOnlyList<MarkdownImportWarning>
{
    private readonly List<MarkdownImportWarning> _warnings = [];

    /// <inheritdoc />
    public int Count => _warnings.Count;

    /// <inheritdoc />
    public MarkdownImportWarning this[int index] => _warnings[index];

    /// <summary>Whether anything at all had to be worked around.</summary>
    public bool IsEmpty => _warnings.Count == 0;

    /// <inheritdoc />
    public IEnumerator<MarkdownImportWarning> GetEnumerator() => _warnings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => string.Join(Environment.NewLine, _warnings);

    internal void Add(MarkdownImportWarningKind kind, string message, string? subject = null, int line = 0) =>
        _warnings.Add(new MarkdownImportWarning(kind, message, subject, line));
}

/// <summary>What an import produced: the document, and what was approximated on the way.</summary>
public sealed class MarkdownImportResult
{
    internal MarkdownImportResult(WordDocument document, MarkdownImportDiagnostics diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }

    /// <summary>The document the Markdown became.</summary>
    public WordDocument Document { get; }

    /// <summary>Every approximation the import had to make.</summary>
    public MarkdownImportDiagnostics Diagnostics { get; }
}
