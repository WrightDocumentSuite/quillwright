using Inkwright;
using Quillwright.Model;

namespace Quillwright.Pdf;

/// <summary>Saves a Word document straight to PDF.</summary>
/// <remarks>
/// These are the short way round. A caller who wants to sign the result, encrypt it or claim
/// PDF/A conformance should render it with <see cref="PdfExporter.Render"/> and save the
/// <see cref="PdfDocument"/> itself.
/// </remarks>
public static class WordDocumentPdfExtensions
{
    /// <summary>Renders the document and writes it to a file, replacing it if it exists.</summary>
    /// <param name="document">The document to render.</param>
    /// <param name="path">Destination path.</param>
    /// <param name="options">How to render it, or <see langword="null"/> for the defaults.</param>
    /// <returns>The compromises the render had to make; empty when it made none.</returns>
    public static PdfExportDiagnostics SaveAsPdf(
        this WordDocument document, string path, PdfExportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        PdfExportResult result = PdfExporter.Render(document, options);
        using (result.Document)
        {
            result.Document.Save(path);
        }

        return result.Diagnostics;
    }

    /// <summary>Renders the document and writes it to a stream, which is left open.</summary>
    /// <param name="document">The document to render.</param>
    /// <param name="stream">Destination stream.</param>
    /// <param name="options">How to render it, or <see langword="null"/> for the defaults.</param>
    /// <returns>The compromises the render had to make; empty when it made none.</returns>
    public static PdfExportDiagnostics SaveAsPdf(
        this WordDocument document, Stream stream, PdfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        PdfExportResult result = PdfExporter.Render(document, options);
        using (result.Document)
        {
            result.Document.Save(stream);
        }

        return result.Diagnostics;
    }

    /// <summary>Renders the document and returns the whole file.</summary>
    /// <param name="document">The document to render.</param>
    /// <param name="options">How to render it, or <see langword="null"/> for the defaults.</param>
    public static byte[] ToPdf(this WordDocument document, PdfExportOptions? options = null)
    {
        PdfExportResult result = PdfExporter.Render(document, options);
        using (result.Document)
        {
            return result.Document.ToArray();
        }
    }
}
