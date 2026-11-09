using Quillwright.Model;

namespace Quillwright.Sanitization;

/// <summary>Saves a copy of a document with chosen categories of content taken out.</summary>
public static class WordDocumentSanitizeExtensions
{
    /// <summary>Writes a copy without the given categories. The document itself is not changed.</summary>
    /// <param name="document">The document to copy.</param>
    /// <param name="stream">Where the copy goes; it is left open.</param>
    /// <param name="remove">What to leave out.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>What was left out and what was kept.</returns>
    public static async ValueTask<SanitizeReport> SaveSanitizedAsync(
        this WordDocument document,
        Stream stream,
        ContentCategories remove,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: cancellationToken).ConfigureAwait(false);

        (byte[] package, SanitizeReport report) = new DocxContentSanitizer(remove).Run(buffer.ToArray());
        await stream.WriteAsync(package, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>Writes a copy without the given categories to a file, replacing it if it exists.</summary>
    /// <param name="document">The document to copy.</param>
    /// <param name="path">Destination path. A copy without macros should not keep a <c>.docm</c> name.</param>
    /// <param name="remove">What to leave out.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>What was left out and what was kept.</returns>
    public static async ValueTask<SanitizeReport> SaveSanitizedAsync(
        this WordDocument document,
        string path,
        ContentCategories remove,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var buffer = new MemoryStream();
        SanitizeReport report = await document.SaveSanitizedAsync(buffer, remove, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        return report;
    }
}
