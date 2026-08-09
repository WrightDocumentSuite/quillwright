using System.Collections;

namespace Quillwright.Rtf;

/// <summary>The kind of approximation made while importing RTF.</summary>
public enum RtfImportWarningKind : byte
{
    /// <summary>A recognised destination is not represented by this importer yet.</summary>
    UnsupportedDestination = 0,

    /// <summary>Content was valid but deliberately omitted.</summary>
    ContentSkipped,

    /// <summary>Text used an unavailable or invalid code page and replacement characters were used.</summary>
    InvalidEncoding,

    /// <summary>An annotation was recovered despite inconsistent anchors or threading metadata.</summary>
    MalformedAnnotation,
}

/// <summary>One recoverable compromise made while importing RTF.</summary>
/// <param name="Kind">What kind of compromise was made.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Subject">Destination or feature involved.</param>
/// <param name="ByteOffset">Zero-based source byte offset.</param>
public readonly record struct RtfImportWarning(
    RtfImportWarningKind Kind,
    string Message,
    string? Subject = null,
    int ByteOffset = 0)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"byte {ByteOffset}: {Message}" + (Subject is null ? string.Empty : $" ({Subject})");
}

/// <summary>Recoverable losses and approximations encountered during an RTF import.</summary>
public sealed class RtfImportDiagnostics : IReadOnlyList<RtfImportWarning>
{
    private readonly List<RtfImportWarning> _warnings = [];
    private readonly HashSet<(RtfImportWarningKind Kind, string? Subject)> _seen = [];

    /// <inheritdoc />
    public int Count => _warnings.Count;

    /// <inheritdoc />
    public RtfImportWarning this[int index] => _warnings[index];

    /// <summary>Whether the import needed no workaround.</summary>
    public bool IsEmpty => _warnings.Count == 0;

    /// <inheritdoc />
    public IEnumerator<RtfImportWarning> GetEnumerator() => _warnings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => string.Join(Environment.NewLine, _warnings);

    internal void Add(RtfImportWarningKind kind, string message, string? subject, int byteOffset)
    {
        if (_seen.Add((kind, subject)))
            _warnings.Add(new RtfImportWarning(kind, message, subject, byteOffset));
    }
}
