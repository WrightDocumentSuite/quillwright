using System.Collections;

namespace Quillwright.Markdown;

/// <summary>What kind of compromise a Markdown export made.</summary>
public enum MarkdownExportWarningKind : byte
{
    /// <summary>Visual formatting with no selected-dialect equivalent was omitted.</summary>
    FormattingDropped = 0,

    /// <summary>Content the model cannot safely project was omitted.</summary>
    ContentSkipped,

    /// <summary>Generated HTML was used for a construct Markdown cannot express.</summary>
    HtmlFallbackUsed,

    /// <summary>The content survived but its original structure was approximated.</summary>
    StructureApproximated,

    /// <summary>A potentially executable hyperlink target was not emitted.</summary>
    UnsafeLinkSkipped,

    /// <summary>An image was preserved, but common Markdown viewers may not display its format.</summary>
    MediaMayNotRender,
}

/// <summary>One deliberate compromise made by a Markdown export.</summary>
/// <param name="Kind">The kind of compromise.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="Subject">A stable feature, format, or source name involved.</param>
public readonly record struct MarkdownExportWarning(
    MarkdownExportWarningKind Kind,
    string Message,
    string? Subject = null)
{
    /// <inheritdoc />
    public override string ToString() => Subject is null ? Message : $"{Message} ({Subject})";
}

/// <summary>
/// The compromises an export had to make, in first-occurrence order. Repeated occurrences of the
/// same warning kind and subject are reported once.
/// </summary>
public sealed class MarkdownExportDiagnostics : IReadOnlyList<MarkdownExportWarning>
{
    private readonly List<MarkdownExportWarning> _warnings = [];
    private readonly HashSet<(MarkdownExportWarningKind Kind, string? Subject)> _seen = [];

    /// <inheritdoc />
    public int Count => _warnings.Count;

    /// <inheritdoc />
    public MarkdownExportWarning this[int index] => _warnings[index];

    /// <summary>Whether the export needed no workaround.</summary>
    public bool IsEmpty => _warnings.Count == 0;

    /// <inheritdoc />
    public IEnumerator<MarkdownExportWarning> GetEnumerator() => _warnings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => string.Join(Environment.NewLine, _warnings);

    internal void Add(MarkdownExportWarningKind kind, string message, string? subject = null)
    {
        if (_seen.Add((kind, subject)))
            _warnings.Add(new MarkdownExportWarning(kind, message, subject));
    }
}
