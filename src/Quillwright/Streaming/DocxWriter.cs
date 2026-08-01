using Quillwright.Diagnostics;
using Quillwright.Formats;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Streaming;

/// <summary>
/// Writes a document straight to a package, one block at a time, without ever holding the
/// whole thing in memory.
/// </summary>
/// <remarks>
/// The document model is the right tool when a document is going to be read, changed and
/// looked at from several angles. Generating a hundred thousand rows of a report is the
/// other case: nothing is ever read back, and building the tree only to serialise it once
/// costs memory for no benefit. This writer produces the same markup the model does — it
/// shares the writing code — but keeps only the current block alive.
/// </remarks>
public sealed class DocxWriter : IAsyncDisposable
{
    private readonly OpcPackage _package;
    private readonly Utf8XmlWriter _writer;
    private readonly Stream? _ownedStream;
    private readonly RelationshipIdAllocator _ids = new();
    private readonly List<OpcRelationship> _relationships = [];
    private readonly Dictionary<ImageData, string> _images = [];
    private readonly List<ImageData> _pendingMedia = [];
    private readonly Dictionary<string, string> _hyperlinks = new(StringComparer.Ordinal);
    private readonly BodyWriteContext _context;
    private int _nextMedia = 1;
    private bool _closed;

    private DocxWriter(OpcPackage package, Utf8XmlWriter writer, Stream? ownedStream)
    {
        _package = package;
        _writer = writer;
        _ownedStream = ownedStream;
        _context = new BodyWriteContext
        {
            ResolvePicture = picture => _images.TryGetValue(picture.Image, out string? id) ? id : RegisterImage(picture.Image),
            ResolveHyperlink = RegisterHyperlink,
        };
    }

    /// <summary>The styles the document declares. Add to it before the first block is written.</summary>
    public StyleSheet Styles { get; } = StyleSheet.CreateDefault();

    /// <summary>The page setup of the single section the writer produces.</summary>
    public SectionProperties Section { get; } = new();

    /// <summary>The core properties written into the package.</summary>
    public DocumentProperties Properties { get; } = new();

    /// <summary>Creates a writer over a new file, replacing it if it exists.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async ValueTask<DocxWriter> CreateAsync(string path, CancellationToken cancellationToken = default)
    {
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

        try
        {
            return await CreateAsync(stream, ownsStream: true, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Creates a writer over a stream. The stream is left open.</summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static ValueTask<DocxWriter> CreateAsync(Stream stream, CancellationToken cancellationToken = default) =>
        CreateAsync(stream, ownsStream: false, cancellationToken);

    /// <summary>Writes a paragraph of plain text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="format">Character formatting of the run.</param>
    /// <param name="styleId">Paragraph style to apply.</param>
    public void WriteParagraph(string text, RunFormat? format = null, string? styleId = null)
    {
        var paragraph = new Paragraph();
        if (!string.IsNullOrEmpty(text))
            paragraph.AppendText(text, format);
        if (styleId is not null)
        {
            paragraph.Format = paragraph.Format with { StyleId = styleId };
            Styles.GetOrAdd(styleId);
        }

        WriteParagraph(paragraph);
    }

    /// <summary>Writes a paragraph that was built up in memory.</summary>
    /// <param name="paragraph">The paragraph to write.</param>
    public void WriteParagraph(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ObjectDisposedException.ThrowIf(_closed, this);
        BodyWriter.WriteParagraph(_writer, paragraph, _context);
    }

    /// <summary>Writes a table that was built up in memory.</summary>
    /// <param name="table">The table to write.</param>
    public void WriteTable(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        ObjectDisposedException.ThrowIf(_closed, this);
        BodyWriter.WriteTable(_writer, table, _context);
    }

    /// <summary>Flushes buffered markup to the package once enough has accumulated.</summary>
    /// <param name="cancellationToken">Cancels the flush.</param>
    public ValueTask FlushIfNeededAsync(CancellationToken cancellationToken = default) =>
        _writer.FlushIfNeededAsync(cancellationToken);

    /// <summary>Closes the body, writes the remaining parts and finishes the package.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        _closed = true;

        SectionWriter.Write(_writer, Section, new SectionWriteContext());
        _writer.WriteRaw("</w:body></w:document>"u8);
        await _writer.DisposeAsync().ConfigureAwait(false);

        foreach (OpcRelationship relationship in _relationships)
            _package.AddRelationship(DocxSchema.PartDocument, relationship);

        await WriteStylesAsync().ConfigureAwait(false);
        await WriteSettingsAsync().ConfigureAwait(false);
        await WritePropertiesAsync().ConfigureAwait(false);
        await WriteMediaAsync().ConfigureAwait(false);
        await _package.DisposeAsync().ConfigureAwait(false);

        if (_ownedStream is not null)
            await _ownedStream.DisposeAsync().ConfigureAwait(false);
    }

    private static async ValueTask<DocxWriter> CreateAsync(Stream stream, bool ownsStream, CancellationToken cancellationToken)
    {
        OpcPackage package = await OpcPackage.CreateAsync(stream, leaveOpen: true, cancellationToken).ConfigureAwait(false);
        package.AddRelationship("/", new OpcRelationship("rId1", DocxSchema.RelDocument, "word/document.xml"));
        package.AddRelationship("/", new OpcRelationship("rId2", DocxSchema.RelCoreProperties, "docProps/core.xml"));

        Utf8XmlWriter writer = await package
            .CreateXmlPartAsync(DocxSchema.PartDocument, DocxSchema.ContentTypeDocument, cancellationToken)
            .ConfigureAwait(false);

        WordXml.OpenRoot(writer, "document"u8);
        writer.WriteRaw("<w:body>"u8);

        var result = new DocxWriter(package, writer, ownsStream ? stream : null);
        result._relationships.Add(new OpcRelationship(result._ids.Next(), DocxSchema.RelStyles, "styles.xml"));
        result._relationships.Add(new OpcRelationship(result._ids.Next(), DocxSchema.RelSettings, "settings.xml"));
        return result;
    }

    /// <summary>
    /// Reserves a part and a relationship for an image. The bytes are written when the
    /// package is closed: a zip written forward-only can only have one entry open at a time,
    /// and the one that is open is the document body.
    /// </summary>
    private string RegisterImage(ImageData image)
    {
        image.PartPath ??= $"/word/media/image{_nextMedia++}.{image.Extension}";
        string id = _ids.Next();
        image.RelationshipId = id;
        _images[image] = id;
        _pendingMedia.Add(image);
        _relationships.Add(new OpcRelationship(id, DocxSchema.RelImage, OpcPath.MakeRelative(DocxSchema.PartDocument, image.PartPath)));
        return id;
    }

    private async ValueTask WriteMediaAsync()
    {
        foreach (ImageData image in _pendingMedia)
        {
            _package.RegisterDefaultContentType(image.Extension, image.ContentType);
            await _package.WriteRawPartAsync(image.PartPath!, image.Bytes.ToArray(), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private string? RegisterHyperlink(Hyperlink link)
    {
        if (link.Url is null)
            return null;
        if (_hyperlinks.TryGetValue(link.Url, out string? existing))
            return existing;

        string id = _ids.Next();
        _hyperlinks[link.Url] = id;
        _relationships.Add(new OpcRelationship(id, DocxSchema.RelHyperlink, link.Url, IsExternal: true));
        return id;
    }

    private async ValueTask WriteStylesAsync()
    {
        Utf8XmlWriter writer = await _package
            .CreateXmlPartAsync(DocxSchema.PartStyles, DocxSchema.ContentTypeStyles, CancellationToken.None)
            .ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            StylesPartWriter.Write(writer, Styles);
    }

    private async ValueTask WriteSettingsAsync()
    {
        Utf8XmlWriter writer = await _package
            .CreateXmlPartAsync(DocxSchema.PartSettings, DocxSchema.ContentTypeSettings, CancellationToken.None)
            .ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
        {
            WordXml.OpenRoot(writer, "settings"u8);
            writer.WriteRaw("</w:settings>"u8);
        }
    }

    private async ValueTask WritePropertiesAsync()
    {
        Utf8XmlWriter writer = await _package
            .CreateXmlPartAsync(DocxSchema.PartCoreProperties, DocxSchema.ContentTypeCoreProperties, CancellationToken.None)
            .ConfigureAwait(false);
        await using (writer.ConfigureAwait(false))
            CorePropertiesWriter.Write(writer, Properties, SaveOptions.Default);
    }
}
