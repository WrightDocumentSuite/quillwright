using Quillwright.Doc.Writing;
using Quillwright.Model;

namespace Quillwright.Doc;

/// <summary>
/// Writes a document in the Word 97-2003 binary format ([MS-DOC]).
/// </summary>
/// <remarks>
/// <para>
/// The binary format predates the model this library is built on, and is narrower in almost
/// every direction: it has no revisions, no content controls, and no way to hold markup it
/// does not understand. Content that has no equivalent is written as the nearest thing the
/// format does have, and every such substitution raises a warning through
/// <see cref="DocWriteOptions.OnWarning"/>.
/// </para>
/// <para>
/// The whole file is built in memory before a byte is written. A legacy document is measured
/// in megabytes, and the format's cross-references — the header records where the text
/// ended, the formatting records where each paragraph began — cannot be resolved in one
/// forward pass.
/// </para>
/// </remarks>
public static class DocWriter
{
    /// <summary>Writes a document to a file.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="path">Where to write it.</param>
    /// <param name="options">Controls the conversion and receives its warnings.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async ValueTask SaveAsync(
        WordDocument document,
        string path,
        DocWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(path);

        byte[] content = Build(document, options);
        await File.WriteAllBytesAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a document to a stream.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="stream">Where to write it; the stream is left open.</param>
    /// <param name="options">Controls the conversion and receives its warnings.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async ValueTask SaveAsync(
        WordDocument document,
        Stream stream,
        DocWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        byte[] content = Build(document, options);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds the file in memory.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="options">Controls the conversion and receives its warnings.</param>
    public static byte[] Save(WordDocument document, DocWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(document, options);
    }

    private static byte[] Build(WordDocument document, DocWriteOptions? options) =>
        new DocSaver(document, options ?? DocWriteOptions.Default).Build();
}
