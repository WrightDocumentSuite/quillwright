using Quillwright.Html;

namespace Quillwright.Model;

public sealed partial class WordDocument
{
    /// <summary>
    /// Renders the main story of the document as HTML — the web preview — and returns any
    /// sidecar images it references. Nothing is written to disk and the document is not
    /// changed; with the default options the page is one self-contained file.
    /// </summary>
    /// <param name="options">Controls the revision view, image carriage and page framing.</param>
    public Html.HtmlDocument ToHtml(HtmlExportOptions? options = null) =>
        HtmlExporter.Export(this, options ?? HtmlExportOptions.Default);

    /// <summary>
    /// Renders the document and writes <see cref="Html.HtmlDocument.DefaultFileName"/> plus any
    /// sidecar images into <paramref name="directoryPath"/>.
    /// </summary>
    /// <param name="directoryPath">Directory that receives the page and its media.</param>
    /// <param name="options">Controls the revision view, image carriage and page framing.</param>
    /// <param name="cancellationToken">Cancels file writing.</param>
    public ValueTask ExportHtmlAsync(
        string directoryPath,
        HtmlExportOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ToHtml(options).SaveAsync(directoryPath, cancellationToken);
}
