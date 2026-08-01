using System.IO.Compression;
using System.Xml;
using Quillwright.Diagnostics;
using Quillwright.Formats;
using Quillwright.Xml;

namespace Quillwright.IO;

/// <summary>
/// A focused Open Packaging Conventions wrapper over <see cref="ZipArchive"/> using the
/// .NET 10 asynchronous zip APIs. Read mode navigates parts and relationships; write mode
/// accumulates relationships and content types and emits them on dispose.
/// </summary>
internal sealed class OpcPackage : IAsyncDisposable
{
    private readonly ZipArchive _archive;
    private readonly bool _writeMode;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Dictionary<string, List<OpcRelationship>> _writeRelationships = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _writtenParts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentTypeMap _contentTypes = new();
    private ContentTypeMap? _parsedContentTypes;
    private bool _disposed;

    private OpcPackage(ZipArchive archive, bool writeMode)
    {
        _archive = archive;
        _writeMode = writeMode;
        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        if (!writeMode)
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
                _entries[OpcPath.ToPartPath(entry.FullName)] = entry;
        }
    }

    /// <summary>Opens an existing package for reading, decrypting it first when it is locked.</summary>
    /// <param name="stream">The file.</param>
    /// <param name="leaveOpen">Whether the caller keeps ownership of the stream.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <param name="password">Password of an encrypted document, when there is one.</param>
    /// <exception cref="EncryptedDocumentException">The document is encrypted and the password was missing or wrong.</exception>
    /// <exception cref="DocxFormatException">The file is not a package.</exception>
    public static async ValueTask<OpcPackage> OpenReadAsync(
        Stream stream, bool leaveOpen, CancellationToken cancellationToken, string? password = null)
    {
        if (await UnlockAsync(stream, password, cancellationToken).ConfigureAwait(false) is { } decrypted)
        {
            stream = decrypted;
            leaveOpen = false;
        }

        ZipArchive archive;
        try
        {
            archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new DocxFormatException("The file is not a valid Open XML package (zip archive).", exception);
        }

        return new OpcPackage(archive, writeMode: false);
    }

    /// <summary>
    /// Turns an encrypted document back into the package it was, and refuses anything else
    /// that is a compound file rather than letting the zip reader call it a damaged archive.
    /// </summary>
    /// <remarks>
    /// An encrypted OOXML document is a compound file holding the package as an
    /// <c>EncryptedPackage</c> stream beside the <c>EncryptionInfo</c> that unlocks it
    /// ([MS-OFFCRYPTO] 2.3.4.4 and 2.3.4.5). A legacy <c>.doc</c> is a compound file too.
    /// Neither is a zip, and "not a valid Open XML package" tells the caller nothing about
    /// which one they have or what to do next.
    /// </remarks>
    private static async ValueTask<Stream?> UnlockAsync(Stream stream, string? password, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || await PeekAsync(stream, cancellationToken).ConfigureAwait(false) is not { } data)
            return null;

        if (Open(data) is not { } container)
            return null;

        if (OfficeCrypto.IsEncryptedPackage(container))
            return new MemoryStream(OfficeCrypto.DecryptPackage(container, password), writable: false);

        if (container.ChildrenOf(string.Empty).Contains("WordDocument"))
            throw new DocxFormatException(
                "The file is a Word 97-2003 document, not an Open XML package. Read it with Quillwright.Doc.");

        return null;
    }

    /// <summary>The whole file when it is a compound file, otherwise nothing; the stream is left where it was.</summary>
    private static async ValueTask<byte[]?> PeekAsync(Stream stream, CancellationToken cancellationToken)
    {
        long start = stream.Position;
        try
        {
            byte[] signature = new byte[8];
            int read = await stream.ReadAtLeastAsync(signature, signature.Length, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
            if (read < signature.Length || !CompoundFile.HasSignature(signature))
                return null;

            stream.Position = start;
            var whole = new MemoryStream();
            await stream.CopyToAsync(whole, cancellationToken).ConfigureAwait(false);
            return whole.ToArray();
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static CompoundFile? Open(byte[] data)
    {
        try
        {
            return CompoundFile.Open(data);
        }
        catch (CompoundFileException)
        {
            // A container too damaged to read is a damaged file, which is what the zip
            // reader is about to say anyway.
            return null;
        }
    }

    /// <summary>Creates a new package for forward-only writing.</summary>
    public static async ValueTask<OpcPackage> CreateAsync(Stream stream, bool leaveOpen, CancellationToken cancellationToken)
    {
        ZipArchive archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Create, leaveOpen, entryNameEncoding: null, cancellationToken)
            .ConfigureAwait(false);
        var package = new OpcPackage(archive, writeMode: true);
        package._contentTypes.AddDefault("rels", DocxSchema.ContentTypeRelationships);
        package._contentTypes.AddDefault("xml", "application/xml");
        return package;
    }

    /// <summary>Returns <see langword="true"/> when a part with the given name exists (read mode).</summary>
    public bool PartExists(string partPath) => Locate(partPath) is not null;

    /// <summary>
    /// The archive entry holding a part, under either spelling of its name.
    /// </summary>
    /// <remarks>
    /// A part name and the ZIP item name that carries it are not the same string when the
    /// name has a character outside ASCII: ECMA-376 part 2 §7.3.4 percent-encodes those on the
    /// way into the archive, and §7.3.5 decodes them on the way out. Producers disagree about
    /// whether they bother, so a part is looked for as named and then as encoded.
    /// </remarks>
    private ZipArchiveEntry? Locate(string partPath)
    {
        if (_entries.TryGetValue(partPath, out ZipArchiveEntry? entry))
            return entry;

        string escaped = OpcPath.ToPartPath(OpcPath.ToEscapedEntryName(partPath));
        return escaped != partPath && _entries.TryGetValue(escaped, out entry) ? entry : null;
    }

    /// <summary>Every part name in the package, in archive order (read mode).</summary>
    public IEnumerable<string> PartPaths => _entries.Keys;

    /// <summary>Reads a whole part into memory (read mode).</summary>
    public async ValueTask<byte[]> ReadPartBytesAsync(string partPath, CancellationToken cancellationToken)
    {
        if (Locate(partPath) is not { } entry)
            throw new DocxFormatException($"The package part '{partPath}' is missing.");

        using MemoryStream buffered = await BufferAsync(entry, cancellationToken).ConfigureAwait(false);
        return buffered.ToArray();
    }

    /// <summary>Reads a whole part into a rewound, seekable stream (read mode).</summary>
    public async ValueTask<MemoryStream> ReadPartAsync(string partPath, CancellationToken cancellationToken)
    {
        if (Locate(partPath) is not { } entry)
            throw new DocxFormatException($"The package part '{partPath}' is missing.");

        return await BufferAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a part for streaming reads.</summary>
    /// <exception cref="DocxFormatException">The part does not exist.</exception>
    public async ValueTask<Stream> OpenPartAsync(string partPath, CancellationToken cancellationToken)
    {
        if (Locate(partPath) is not { } entry)
            throw new DocxFormatException($"The package part '{partPath}' is missing.");
        return await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads and resolves the relationships of a part. Returns an empty list when the part has none.</summary>
    public async ValueTask<IReadOnlyList<OpcRelationship>> GetRelationshipsAsync(string partPath, CancellationToken cancellationToken)
    {
        string relsPath = OpcPath.GetRelsPath(partPath);
        if (Locate(relsPath) is not { } entry)
            return [];

        using MemoryStream buffered = await BufferAsync(entry, cancellationToken).ConfigureAwait(false);
        return ParseRelationships(buffered);
    }

    /// <summary>Returns the content-type map of the package (read mode; parsed once).</summary>
    public async ValueTask<ContentTypeMap> GetContentTypesAsync(CancellationToken cancellationToken)
    {
        if (_parsedContentTypes is not null)
            return _parsedContentTypes;

        if (_entries.TryGetValue("/[Content_Types].xml", out ZipArchiveEntry? entry))
        {
            using MemoryStream buffered = await BufferAsync(entry, cancellationToken).ConfigureAwait(false);
            return _parsedContentTypes = ContentTypeMap.Parse(buffered);
        }

        return _parsedContentTypes = _contentTypes;
    }

    /// <summary>Creates a new part and registers its content type override (write mode).</summary>
    public async ValueTask<Stream> CreatePartAsync(string partPath, string contentType, CancellationToken cancellationToken)
    {
        RegisterContentType(partPath, contentType);
        _writtenParts.Add(partPath);
        ZipArchiveEntry entry = _archive.CreateEntry(OpcPath.ToEntryName(partPath), CompressionLevel.Optimal);
        return await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the package being written is Strict, which every part writer needs to know
    /// because the two vocabularies spell some names and values differently.
    /// </summary>
    public bool Strict { get; set; }

    /// <summary>Creates a new part and a writer over it (write mode).</summary>
    public async ValueTask<Utf8XmlWriter> CreateXmlPartAsync(string partPath, string contentType, CancellationToken cancellationToken)
    {
        Stream stream = await CreatePartAsync(partPath, contentType, cancellationToken).ConfigureAwait(false);
        return new Utf8XmlWriter(stream) { Strict = Strict };
    }

    /// <summary>
    /// Copies a part into the package verbatim (write mode). The content type is expected to
    /// come from the preserved map or from an explicit <see cref="RegisterContentType"/> call.
    /// </summary>
    public async ValueTask WriteRawPartAsync(string partPath, byte[] content, CancellationToken cancellationToken)
    {
        _writtenParts.Add(partPath);
        ZipArchiveEntry entry = _archive.CreateEntry(OpcPath.ToEntryName(partPath), CompressionLevel.Optimal);
        Stream stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Returns <see langword="true"/> when the part has already been emitted (write mode).</summary>
    public bool WasWritten(string partPath) => _writtenParts.Contains(partPath);

    /// <summary>
    /// Declares the content type of a part. Only needed when the extension default does not
    /// already cover it; <see cref="CreatePartAsync"/> does this on its own.
    /// </summary>
    public void RegisterContentType(string partPath, string contentType) =>
        _contentTypes.AddOverride(partPath, contentType);

    /// <summary>Declares the content type of every part with a given extension.</summary>
    public void RegisterDefaultContentType(string extension, string contentType) =>
        _contentTypes.AddDefault(extension, contentType);

    /// <summary>Seeds the content-type map with entries carried over from a loaded package (write mode).</summary>
    public void MergeContentTypes(ContentTypeMap source)
    {
        foreach ((string extension, string contentType) in source.Defaults)
            _contentTypes.AddDefault(extension, contentType);
        foreach ((string partPath, string contentType) in source.Overrides)
            _contentTypes.AddOverride(partPath, contentType);
    }

    /// <summary>Buffers a relationship to be written on dispose (write mode). Targets are written as given.</summary>
    public void AddRelationship(string sourcePartPath, OpcRelationship relationship)
    {
        if (!_writeRelationships.TryGetValue(sourcePartPath, out List<OpcRelationship>? list))
            _writeRelationships[sourcePartPath] = list = [];
        list.Add(relationship);
    }

    /// <summary>In write mode emits pending relationship parts and the content-types part, then closes the archive.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_writeMode)
        {
            foreach ((string sourcePart, List<OpcRelationship> relationships) in _writeRelationships)
                await WriteRelationshipsPartAsync(sourcePart, relationships).ConfigureAwait(false);

            // A content type for a part that never made it into the package makes the file
            // unopenable, so overrides carried over from a source package are pruned here.
            foreach (string partPath in _contentTypes.Overrides.Keys.ToArray())
            {
                if (!_writtenParts.Contains(partPath))
                    _contentTypes.RemoveOverride(partPath);
            }

            ZipArchiveEntry entry = _archive.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
            Stream stream = await entry.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            var writer = new Utf8XmlWriter(stream, bufferSize: 4 * 1024);
            await using (writer.ConfigureAwait(false))
            {
                _contentTypes.Write(writer);
            }
        }

        await _archive.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask WriteRelationshipsPartAsync(string sourcePart, List<OpcRelationship> relationships)
    {
        string relsPath = OpcPath.GetRelsPath(sourcePart);
        ZipArchiveEntry entry = _archive.CreateEntry(OpcPath.ToEntryName(relsPath), CompressionLevel.Optimal);
        Stream stream = await entry.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        var writer = new Utf8XmlWriter(stream, bufferSize: 4 * 1024);
        await using (writer.ConfigureAwait(false))
        {
            writer.WriteDeclaration();
            writer.WriteRaw("<Relationships xmlns=\""u8);
            writer.WriteRawXml(DocxSchema.NsPackageRelationships);
            writer.WriteRaw("\">"u8);
            foreach (OpcRelationship relationship in relationships)
            {
                writer.WriteRaw("<Relationship Id=\""u8);
                writer.WriteAttributeText(relationship.Id);
                writer.WriteRaw("\" Type=\""u8);
                writer.WriteAttributeText(relationship.Type);
                writer.WriteRaw("\" Target=\""u8);
                writer.WriteAttributeText(relationship.Target);
                if (relationship.IsExternal)
                    writer.WriteRaw("\" TargetMode=\"External"u8);
                writer.WriteRaw("\"/>"u8);
            }

            writer.WriteRaw("</Relationships>"u8);
        }
    }

    private static async ValueTask<MemoryStream> BufferAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var buffered = new MemoryStream(checked((int)Math.Max(entry.Length, 256)));
        Stream source = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            await source.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        }

        buffered.Position = 0;
        return buffered;
    }

    private static List<OpcRelationship> ParseRelationships(Stream stream)
    {
        var relationships = new List<OpcRelationship>();
        using var reader = XmlReader.Create(stream, XmlDefaults.ReaderSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship")
                continue;

            string? id = reader.GetAttribute("Id");
            string? type = reader.GetAttribute("Type");
            string? target = reader.GetAttribute("Target");
            if (id is null || type is null || target is null)
                continue;

            bool isExternal = string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
            relationships.Add(new OpcRelationship(id, type, target, isExternal));
        }

        return relationships;
    }
}
