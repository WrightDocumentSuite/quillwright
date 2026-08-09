using Quillwright.Model;

namespace Quillwright.Rtf;

/// <summary>The document produced by an RTF import and every approximation made along the way.</summary>
public sealed class RtfImportResult
{
    internal RtfImportResult(WordDocument document, RtfImportDiagnostics diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }

    /// <summary>The imported document.</summary>
    public WordDocument Document { get; }

    /// <summary>Every recoverable loss or approximation encountered during import.</summary>
    public RtfImportDiagnostics Diagnostics { get; }
}
