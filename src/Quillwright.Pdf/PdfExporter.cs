using Inkwright;
using Inkwright.Cos;
using Quillwright.Model;
using Quillwright.Pdf.Layout;
using Quillwright.Pdf.Render;

namespace Quillwright.Pdf;

/// <summary>
/// Renders a Word document to PDF.
/// </summary>
/// <remarks>
/// <para>
/// The render is two passes over a seam. Composition walks the model, measures it with real font
/// metrics and decides what lands on which page; rendering turns that decision into content
/// streams. Keeping them apart is what lets a page be thrown away and laid out again — which is
/// exactly what a document with a <c>NUMPAGES</c> field needs, because the count of pages is not
/// known until the pages exist.
/// </para>
/// <para>
/// The result is an ordinary <see cref="PdfDocument"/>, not a file, so the caller can go on to
/// sign it, encrypt it or run it through a PDF/A profile before saving.
/// </para>
/// </remarks>
public static class PdfExporter
{
    /// <summary>Renders a document.</summary>
    /// <param name="document">The document to render; it is not modified.</param>
    /// <param name="options">How to render it, or <see langword="null"/> for the defaults.</param>
    public static PdfExportResult Render(WordDocument document, PdfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        PdfExportOptions settings = options ?? PdfExportOptions.Default;
        var pdf = PdfDocument.Create();
        var diagnostics = new PdfExportDiagnostics();
        var context = new PdfExportContext(document, pdf, settings, diagnostics);
        var fields = new PageFieldResolver(diagnostics);

        (IReadOnlyList<ComposedPage> pages, PageComposer composer) = ComposeUntilStable(context, fields);

        var tagger = settings.Tagged ? new DocumentTagger(context) : null;
        new PageRenderer(context, fields).Render(pages, composer.Bookmarks, tagger);
        tagger?.Build();

        ApplyMetadata(context);
        return new PdfExportResult(pdf, diagnostics, pages.Count);
    }

    /// <summary>
    /// Lays the document out, and lays it out again if the page count it assumed while measuring
    /// <c>NUMPAGES</c> turned out to be wrong, or a <c>PAGEREF</c> was measured before the
    /// bookmarks had pages. One repeat is enough in practice and is capped at one on purpose: a
    /// document whose length oscillates would otherwise never settle.
    /// </summary>
    private static (IReadOnlyList<ComposedPage> Pages, PageComposer Composer) ComposeUntilStable(
        PdfExportContext context, PageFieldResolver fields)
    {
        var composer = new PageComposer(context, fields);
        IReadOnlyList<ComposedPage> pages = composer.Compose();
        int assumed = fields.TotalPages;
        bool blind = fields.EstimatedBlindPageRef;
        fields.Observe(pages, composer.Bookmarks);

        if (assumed == fields.TotalPages && !blind)
            return (pages, composer);

        composer = new PageComposer(context, fields);
        pages = composer.Compose();
        fields.Observe(pages, composer.Bookmarks);
        return (pages, composer);
    }

    private static void ApplyMetadata(PdfExportContext context)
    {
        DocumentProperties properties = context.Source.Properties;

        string? title = context.Options.Title ?? properties.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            context.Pdf.Info.Title = title;

            // A tagged document is meant to be read out as well as looked at, and a reader that
            // announces the file name instead of the title is the first thing to go wrong.
            if (context.Options.Tagged)
                DisplayTitle(context.Pdf);
        }

        if (!string.IsNullOrWhiteSpace(properties.Creator))
            context.Pdf.Info.Author = properties.Creator;

        if (!string.IsNullOrWhiteSpace(properties.Subject))
            context.Pdf.Info.Subject = properties.Subject;

        if (!string.IsNullOrWhiteSpace(properties.Keywords))
            context.Pdf.Info.Keywords = properties.Keywords;

        if (properties.Created is { } created)
            context.Pdf.Info.CreationDate = created;

        WriteXmp(context);
    }

    /// <summary>
    /// Writes the metadata a second time, as XMP. The information dictionary is what old readers
    /// look at and XMP is what everything since PDF 1.4 looks at, including every archival and
    /// accessibility profile, so a document that carries only the first is half described.
    /// </summary>
    private static void WriteXmp(PdfExportContext context)
    {
        var metadata = new Inkwright.Metadata.XmpMetadata();
        metadata.CopyFrom(context.Pdf.Info);
        metadata.Language = context.Options.Language
            ?? context.Source.Properties.Language
            ?? context.Source.Styles.DefaultRunFormat.Language;

        metadata.ApplyTo(context.Pdf);
    }

    /// <summary>Asks readers to show the document's title rather than its file name.</summary>
    private static void DisplayTitle(PdfDocument pdf)
    {
        PdfName key = PdfName.Get("ViewerPreferences");
        PdfDictionary preferences = pdf.Catalog.GetDictionary(key) ?? new PdfDictionary(1);

        preferences.Set(PdfName.Get("DisplayDocTitle"), PdfValue.True);
        pdf.Catalog.Set(key, PdfValue.Dictionary(preferences));
        pdf.MarkChanged(pdf.CatalogId);
    }
}
