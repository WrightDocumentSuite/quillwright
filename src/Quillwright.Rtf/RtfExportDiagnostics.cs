using System.Collections;

namespace Quillwright.Rtf;

/// <summary>The kind of approximation made while exporting RTF.</summary>
public enum RtfExportWarningKind : byte
{
    /// <summary>A block has no representation in the current exporter.</summary>
    UnsupportedBlock = 0,

    /// <summary>An inline object has no representation in the current exporter.</summary>
    UnsupportedInline,

    /// <summary>Formatting was omitted while its text was retained.</summary>
    FormattingDropped,

    /// <summary>Model content has no representation in the current exporter.</summary>
    ContentSkipped,
}

/// <summary>One deliberate compromise made while exporting RTF.</summary>
/// <param name="Kind">What kind of compromise was made.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Subject">Model type or feature involved.</param>
public readonly record struct RtfExportWarning(
    RtfExportWarningKind Kind,
    string Message,
    string? Subject = null)
{
    /// <inheritdoc />
    public override string ToString() => Message + (Subject is null ? string.Empty : $" ({Subject})");
}

/// <summary>Losses and approximations made while exporting a document to RTF.</summary>
public sealed class RtfExportDiagnostics : IReadOnlyList<RtfExportWarning>
{
    private readonly List<RtfExportWarning> _warnings = [];
    private readonly HashSet<(RtfExportWarningKind Kind, string? Subject)> _seen = [];

    /// <inheritdoc />
    public int Count => _warnings.Count;

    /// <inheritdoc />
    public RtfExportWarning this[int index] => _warnings[index];

    /// <summary>Whether the export needed no workaround.</summary>
    public bool IsEmpty => _warnings.Count == 0;

    /// <inheritdoc />
    public IEnumerator<RtfExportWarning> GetEnumerator() => _warnings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => string.Join(Environment.NewLine, _warnings);

    internal void Add(RtfExportWarningKind kind, string message, string? subject)
    {
        if (_seen.Add((kind, subject)))
            _warnings.Add(new RtfExportWarning(kind, message, subject));
    }
}
