using Quillwright.Markdown;

namespace Quillwright.Model;

public sealed partial class WordDocument
{
    /// <summary>
    /// Renders the main story of the document as Markdown and returns the images its links use.
    /// Nothing is written to disk and the document is not changed.
    /// </summary>
    /// <param name="options">Controls the Markdown dialect and deliberate fidelity choices.</param>
    public MarkdownDocument ToMarkdown(MarkdownExportOptions? options = null) =>
        MarkdownExporter.Export(this, options ?? MarkdownExportOptions.Default);

    /// <summary>
    /// Renders the document and writes <see cref="MarkdownDocument.DefaultFileName"/> plus any
    /// referenced images into <paramref name="directoryPath"/>.
    /// </summary>
    /// <param name="directoryPath">Directory that receives the document and its media.</param>
    /// <param name="options">Controls the Markdown dialect and deliberate fidelity choices.</param>
    /// <param name="cancellationToken">Cancels file writing.</param>
    public ValueTask ExportMarkdownAsync(
        string directoryPath,
        MarkdownExportOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ToMarkdown(options).SaveAsync(directoryPath, cancellationToken);
}
