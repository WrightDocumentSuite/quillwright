using Inkwright;

namespace Quillwright.Pdf;

/// <summary>What a render produced: the document, and everything it had to work around.</summary>
public sealed class PdfExportResult
{
    internal PdfExportResult(PdfDocument document, PdfExportDiagnostics diagnostics, int pageCount)
    {
        Document = document;
        Diagnostics = diagnostics;
        PageCount = pageCount;
    }

    /// <summary>
    /// The rendered document. It is not saved yet, so it can still be signed, encrypted or made
    /// archival before it goes to disk.
    /// </summary>
    public PdfDocument Document { get; }

    /// <summary>The compromises the render had to make; empty when it made none.</summary>
    public PdfExportDiagnostics Diagnostics { get; }

    /// <summary>How many pages the document came to.</summary>
    public int PageCount { get; }
}
