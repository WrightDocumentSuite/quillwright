using System.Runtime.CompilerServices;
using System.Xml;
using Quillwright.Diagnostics;
using Quillwright.Formats;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Streaming;

/// <summary>
/// Reads a document one block at a time, without building a model of the whole thing.
/// </summary>
/// <remarks>
/// Extracting text from a folder of contracts, or feeding a search index, does not need the
/// document tree — it needs the blocks in order and then nothing. This reader pulls one
/// top-level block out of the package at a time, so memory tracks the size of the largest
/// paragraph or table rather than the size of the file. Each block is a fully modelled
/// <see cref="Paragraph"/> or <see cref="Table"/>, so everything the DOM can say about a
/// paragraph applies here too.
/// </remarks>
public sealed class DocxReader : IAsyncDisposable
{
    private readonly OpcPackage _package;
    private readonly Stream? _ownedStream;
    private readonly WordDocument _shell;
    private readonly LoadContext _context;
    private readonly string _mainPart;

    private DocxReader(OpcPackage package, Stream? ownedStream, WordDocument shell, LoadContext context, string mainPart)
    {
        _package = package;
        _ownedStream = ownedStream;
        _shell = shell;
        _context = context;
        _mainPart = mainPart;
    }

    /// <summary>Recoverable problems found so far.</summary>
    public IReadOnlyList<DocumentWarning> Diagnostics => _shell.LoadDiagnostics;

    /// <summary>Opens a document for streaming reads.</summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    public static async ValueTask<DocxReader> OpenAsync(string path, CancellationToken cancellationToken = default)
        => await OpenWithOptionsAsync(path, LoadOptions.Default, cancellationToken).ConfigureAwait(false);

    /// <summary>Opens a document for streaming reads with explicit resource limits.</summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="options">Package fidelity, password and resource limits.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    public static async ValueTask<DocxReader> OpenWithOptionsAsync(
        string path, LoadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

        try
        {
            return await OpenAsync(stream, ownsStream: true, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Opens a document for streaming reads. The stream is left open.</summary>
    /// <param name="stream">Stream positioned at the start of the package.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    public static ValueTask<DocxReader> OpenAsync(Stream stream, CancellationToken cancellationToken = default) =>
        OpenAsync(stream, ownsStream: false, LoadOptions.Default, cancellationToken);

    /// <summary>Opens a document for streaming reads with explicit resource limits. The stream is left open.</summary>
    /// <param name="stream">Stream positioned at the start of the package.</param>
    /// <param name="options">Package fidelity, password and resource limits.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    public static ValueTask<DocxReader> OpenWithOptionsAsync(
        Stream stream, LoadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return OpenAsync(stream, ownsStream: false, options, cancellationToken);
    }

    /// <summary>The blocks of the body, in order.</summary>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    public async IAsyncEnumerable<Block> ReadBlocksAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Stream part = await _package.OpenPartAsync(_mainPart, cancellationToken).ConfigureAwait(false);
        await using (part.ConfigureAwait(false))
        {
            // The package is opened asynchronously, but the blocks themselves are parsed
            // straight off the decompressed stream rather than being buffered one at a time:
            // buffering each block into a string and standing up a reader for it costs more
            // in allocation than loading the whole document does.
            using XmlReader xml = XmlReader.Create(part, XmlDefaults.ReaderSettings);
            if (!MoveToBody(xml))
                yield break;

            var body = new BodyReader(_context);
            xml.Read();
            while (xml.NodeType is not (XmlNodeType.None or XmlNodeType.EndElement))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (xml.NodeType != XmlNodeType.Element)
                {
                    xml.Read();
                    continue;
                }

                if (xml.LocalName == "sectPr")
                {
                    xml.Skip();
                    continue;
                }

                if (body.ReadBlockElement(xml, xml.LocalName) is { } block)
                    yield return block;
            }
        }
    }

    /// <summary>The text of the body, one string per block.</summary>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    public async IAsyncEnumerable<string> ReadTextAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (Block block in ReadBlocksAsync(cancellationToken).ConfigureAwait(false))
            yield return block.GetText();
    }

    /// <summary>Closes the package.</summary>
    public async ValueTask DisposeAsync()
    {
        await _package.DisposeAsync().ConfigureAwait(false);
        if (_ownedStream is not null)
            await _ownedStream.DisposeAsync().ConfigureAwait(false);
    }

    private static async ValueTask<DocxReader> OpenAsync(
        Stream stream, bool ownsStream, LoadOptions options, CancellationToken cancellationToken)
    {
        OpcPackage package = await OpcPackage.OpenReadAsync(
            stream, leaveOpen: true, cancellationToken, options.Password, options.Budget).ConfigureAwait(false);
        var preserved = new PreservedPackage
        {
            ContentTypes = await package.GetContentTypesAsync(cancellationToken).ConfigureAwait(false),
        };

        IReadOnlyList<OpcRelationship> root = await package.GetRelationshipsAsync("/", cancellationToken).ConfigureAwait(false);
        OpcRelationship main = root.FirstOrDefault(static r => r.Is(DocxSchema.RelDocument));
        string mainPart = main.Target is null ? DocxSchema.PartDocument : OpcPath.Resolve("/", main.Target);
        if (!package.PartExists(mainPart))
            throw new DocxFormatException("The package has no main document part.");

        preserved.MainPartPath = mainPart;
        preserved.Relationships[mainPart] = [.. await package.GetRelationshipsAsync(mainPart, cancellationToken).ConfigureAwait(false)];

        WordDocument shell = WordDocument.CreateEmpty();
        shell.Preserved = preserved;
        return new DocxReader(package, ownsStream ? stream : null, shell, new LoadContext(shell, options, preserved), mainPart);
    }

    private static bool MoveToBody(XmlReader xml)
    {
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "body")
                return !xml.IsEmptyElement;
        }

        return false;
    }
}
