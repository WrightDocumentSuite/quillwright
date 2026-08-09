using System.Buffers;
using System.Buffers.Binary;
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
    private readonly DocumentLoadBudgetState? _loadBudget;
    private readonly HashSet<string> _validatedXmlEntries = new(StringComparer.OrdinalIgnoreCase);
    private ContentTypeMap? _parsedContentTypes;
    private bool _disposed;

    private OpcPackage(ZipArchive archive, bool writeMode, DocumentLoadBudgetState? loadBudget = null)
    {
        _archive = archive;
        _writeMode = writeMode;
        _loadBudget = loadBudget;
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
    /// <param name="budget">Resource limits for the compressed package and parsed XML.</param>
    /// <exception cref="EncryptedDocumentException">The document is encrypted and the password was missing or wrong.</exception>
    /// <exception cref="DocxFormatException">The file is not a package.</exception>
    public static async ValueTask<OpcPackage> OpenReadAsync(
        Stream stream,
        bool leaveOpen,
        CancellationToken cancellationToken,
        string? password = null,
        DocumentLoadBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var loadBudget = new DocumentLoadBudgetState(budget ?? DocumentLoadBudget.Default);
        Stream packageStream = await PrepareInputAsync(stream, loadBudget.Budget, cancellationToken).ConfigureAwait(false);
        bool packageLeaveOpen = ReferenceEquals(packageStream, stream) && leaveOpen;

        if (await UnlockAsync(packageStream, password, loadBudget.Budget, cancellationToken).ConfigureAwait(false) is { } decrypted)
        {
            if (!ReferenceEquals(packageStream, stream))
                await packageStream.DisposeAsync().ConfigureAwait(false);
            packageStream = decrypted;
            packageLeaveOpen = false;
        }

        ZipArchive archive;
        try
        {
            ValidateZipEntryCount(packageStream, loadBudget.Budget);
            archive = await ZipArchive.CreateAsync(
                    packageStream, ZipArchiveMode.Read, packageLeaveOpen, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            if (!packageLeaveOpen)
                await packageStream.DisposeAsync().ConfigureAwait(false);
            throw new DocxFormatException("The file is not a valid Open XML package (zip archive).", exception);
        }

        try
        {
            var package = new OpcPackage(archive, writeMode: false, loadBudget);
            await ValidateEntriesAsync(
                    archive, loadBudget, package._validatedXmlEntries, cancellationToken)
                .ConfigureAwait(false);
            return package;
        }
        catch
        {
            await archive.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask ValidateEntriesAsync(
        ZipArchive archive,
        DocumentLoadBudgetState loadBudget,
        HashSet<string> validatedXmlEntries,
        CancellationToken cancellationToken)
    {
        DocumentLoadBudget budget = loadBudget.Budget;
        DocumentLoadBudgetState.Ensure(
            nameof(DocumentLoadBudget.MaxPackageParts), budget.MaxPackageParts, archive.Entries.Count);

        long inflated = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxPartBytes), budget.MaxPartBytes, entry.Length);

            inflated = AddWithoutOverflow(inflated, entry.Length);
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxInflatedBytes), budget.MaxInflatedBytes, inflated);
        }

        (HashSet<ZipArchiveEntry> mediaParts, HashSet<ZipArchiveEntry> embeddedParts) =
            await ClassifyResourcePartsAsync(
                    archive, loadBudget, validatedXmlEntries, cancellationToken)
                .ConfigureAwait(false);

        long media = 0;
        foreach (ZipArchiveEntry entry in mediaParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxMediaBytes), budget.MaxMediaBytes, entry.Length);
            media = AddWithoutOverflow(media, entry.Length);
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxTotalMediaBytes), budget.MaxTotalMediaBytes, media);
        }

        int embedded = 0;
        foreach (ZipArchiveEntry entry in embeddedParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxEmbeddedObjectBytes),
                budget.MaxEmbeddedObjectBytes,
                entry.Length);
            embedded++;
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxEmbeddedObjects), budget.MaxEmbeddedObjects, embedded);
        }
    }

    /// <summary>
    /// Finds media and embedded objects by their OPC role. Conventional directories remain a
    /// defensive fallback, but a producer is free to put a part anywhere its relationship points.
    /// </summary>
    private static async ValueTask<(HashSet<ZipArchiveEntry> Media, HashSet<ZipArchiveEntry> Embedded)>
        ClassifyResourcePartsAsync(
            ZipArchive archive,
            DocumentLoadBudgetState loadBudget,
            HashSet<string> validatedXmlEntries,
            CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = OpcPath.ToPartPath(entry.FullName);
            entries[path] = entry;

            string logicalPath = OpcPath.Unescape(path);
            if (!logicalPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                entries.TryAdd(logicalPath, entry);
        }

        ContentTypeMap? contentTypes = await TryReadContentTypesAsync(
                entries, loadBudget, validatedXmlEntries, cancellationToken)
            .ConfigureAwait(false);
        var media = new HashSet<ZipArchiveEntry>();
        var embedded = new HashSet<ZipArchiveEntry>();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rawPath = OpcPath.ToPartPath(entry.FullName);
            string path = OpcPath.Unescape(rawPath);
            string? contentType = contentTypes?.GetContentType(rawPath) ??
                                  (path.Equals(rawPath, StringComparison.OrdinalIgnoreCase)
                                      ? null
                                      : contentTypes?.GetContentType(path));

            if (path.Contains("/media/", StringComparison.OrdinalIgnoreCase) ||
                IsMediaContentType(contentType))
            {
                media.Add(entry);
            }

            if (path.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase))
                embedded.Add(entry);
        }

        // The lookup above contains raw and unescaped aliases for the same physical part.
        // Walk the archive itself so an escaped relationships entry is parsed exactly once.
        foreach (ZipArchiveEntry relationshipsEntry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = OpcPath.Unescape(OpcPath.ToPartPath(relationshipsEntry.FullName));
            if (!OpcPath.IsRelsPath(path))
                continue;

            IReadOnlyList<OpcRelationship> relationships;
            try
            {
                relationships = await ReadRelationshipsForClassificationAsync(
                        relationshipsEntry, loadBudget, validatedXmlEntries, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (XmlException)
            {
                // The format reader reports malformed relationship XML later. It cannot
                // provide trustworthy semantic classification here, so directory and content
                // type fallbacks above still apply.
                continue;
            }

            string sourcePart = OpcPath.GetSourcePart(path);
            foreach (OpcRelationship relationship in relationships)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (relationship.IsExternal ||
                    !TryLocateRelationshipTarget(entries, sourcePart, relationship.Target, out ZipArchiveEntry target))
                {
                    continue;
                }

                if (IsMediaRelationship(relationship.CanonicalType))
                    media.Add(target);
                if (relationship.Is(DocxSchema.RelOleObject) || relationship.Is(DocxSchema.RelPackage))
                    embedded.Add(target);
            }
        }

        return (media, embedded);
    }

    private static async ValueTask<ContentTypeMap?> TryReadContentTypesAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        DocumentLoadBudgetState loadBudget,
        HashSet<string> validatedXmlEntries,
        CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue("/[Content_Types].xml", out ZipArchiveEntry? entry))
            return null;

        try
        {
            var map = new ContentTypeMap();
            await ScanResourceMetadataAsync(
                    entry,
                    loadBudget,
                    validatedXmlEntries,
                    cancellationToken,
                    reader =>
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                            return;

                        if (reader.LocalName == "Default")
                        {
                            string? extension = reader.GetAttribute("Extension");
                            string? contentType = reader.GetAttribute("ContentType");
                            if (extension is not null && contentType is not null)
                                map.AddDefault(extension, contentType);
                        }
                        else if (reader.LocalName == "Override")
                        {
                            string? partName = reader.GetAttribute("PartName");
                            string? contentType = reader.GetAttribute("ContentType");
                            if (partName is not null && contentType is not null)
                                map.AddOverride(partName, contentType);
                        }
                    })
                .ConfigureAwait(false);
            return map;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static async ValueTask<IReadOnlyList<OpcRelationship>> ReadRelationshipsForClassificationAsync(
        ZipArchiveEntry entry,
        DocumentLoadBudgetState loadBudget,
        HashSet<string> validatedXmlEntries,
        CancellationToken cancellationToken)
    {
        var relationships = new List<OpcRelationship>();
        await ScanResourceMetadataAsync(
                entry,
                loadBudget,
                validatedXmlEntries,
                cancellationToken,
                reader =>
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship")
                        return;

                    string? id = reader.GetAttribute("Id");
                    string? type = reader.GetAttribute("Type");
                    string? target = reader.GetAttribute("Target");
                    if (id is null || type is null || target is null)
                        return;

                    bool isExternal = string.Equals(
                        reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
                    relationships.Add(new OpcRelationship(id, type, target, isExternal));
                })
            .ConfigureAwait(false);
        return relationships;
    }

    private static async ValueTask ScanResourceMetadataAsync(
        ZipArchiveEntry entry,
        DocumentLoadBudgetState loadBudget,
        HashSet<string> validatedXmlEntries,
        CancellationToken cancellationToken,
        Action<XmlReader> visit)
    {
        bool chargeBudget = validatedXmlEntries.Add(entry.FullName);

        try
        {
            Stream rawContent = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            var content = new CancellationAwareReadStream(rawContent, cancellationToken);
            await using (content.ConfigureAwait(false))
            {
                XmlReaderSettings settings = XmlDefaults.ForBudget(loadBudget.Budget);
                settings.Async = true;
                using XmlReader reader = XmlReader.Create(content, settings);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await reader.ReadAsync().ConfigureAwait(false))
                        break;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (chargeBudget)
                        loadBudget.AddXmlNode(reader.Depth + 1);
                    visit(reader);
                }
            }
        }
        catch (XmlException exception) when (IsXmlCharacterLimit(exception))
        {
            long limit = loadBudget.Budget.MaxXmlCharactersPerPart;
            throw new DocumentLoadLimitException(
                nameof(DocumentLoadBudget.MaxXmlCharactersPerPart),
                limit,
                limit == long.MaxValue ? long.MaxValue : limit + 1);
        }
    }

    private static bool TryLocateRelationshipTarget(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string sourcePart,
        string target,
        out ZipArchiveEntry entry)
    {
        string partPath;
        try
        {
            partPath = OpcPath.Resolve(sourcePart, target);
        }
        catch (UriFormatException)
        {
            entry = null!;
            return false;
        }

        return entries.TryGetValue(partPath, out entry!);
    }

    private static bool IsMediaContentType(string? contentType) =>
        contentType is not null &&
        (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
         contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
         contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase));

    private static bool IsMediaRelationship(string canonicalType) => canonicalType switch
    {
        DocxSchema.RelImage => true,
        DocxSchema.NsRelationships + "/audio" => true,
        DocxSchema.NsRelationships + "/video" => true,
        DocxSchema.NsRelationships + "/media" => true,
        _ => false,
    };

    private static long AddWithoutOverflow(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;

    /// <summary>Rejects an ordinary ZIP bomb before ZipArchive materializes its central directory.</summary>
    private static void ValidateZipEntryCount(Stream stream, DocumentLoadBudget budget)
    {
        if (!stream.CanSeek)
            return;

        long original = stream.Position;
        try
        {
            long available = Math.Max(0, stream.Length - original);
            int tailLength = (int)Math.Min(available, ushort.MaxValue + 22L);
            if (tailLength < 22)
                return;

            byte[] tail = ArrayPool<byte>.Shared.Rent(tailLength);
            try
            {
                long tailStart = stream.Length - tailLength;
                stream.Position = tailStart;
                stream.ReadExactly(tail.AsSpan(0, tailLength));

                int eocd = -1;
                for (int index = tailLength - 22; index >= 0; index--)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index, 4)) != 0x06054B50)
                        continue;

                    int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(index + 20, 2));
                    if (index + 22 + commentLength <= tailLength)
                    {
                        eocd = index;
                        break;
                    }
                }

                if (eocd < 0)
                    return;

                ushort count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 10, 2));
                if (count != ushort.MaxValue)
                {
                    DocumentLoadBudgetState.Ensure(
                        nameof(DocumentLoadBudget.MaxPackageParts), budget.MaxPackageParts, count);
                    return;
                }

                long eocdPosition = tailStart + eocd;
                if (eocdPosition - 20 < original)
                    return;

                Span<byte> locator = stackalloc byte[20];
                stream.Position = eocdPosition - locator.Length;
                stream.ReadExactly(locator);
                if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != 0x07064B50)
                    return;

                ulong relativeOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
                long maximumOffset = stream.Length - original - 56;
                if (maximumOffset < 0 || relativeOffset > (ulong)maximumOffset)
                    return;

                Span<byte> zip64 = stackalloc byte[56];
                stream.Position = original + (long)relativeOffset;
                stream.ReadExactly(zip64);
                if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != 0x06064B50)
                    return;

                ulong total = BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..]);
                long observed = total > long.MaxValue ? long.MaxValue : (long)total;
                DocumentLoadBudgetState.Ensure(
                    nameof(DocumentLoadBudget.MaxPackageParts), budget.MaxPackageParts, observed);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tail);
            }
        }
        finally
        {
            stream.Position = original;
        }
    }

    private static async ValueTask<Stream> PrepareInputAsync(
        Stream stream, DocumentLoadBudget budget, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            long remaining = Math.Max(0, stream.Length - stream.Position);
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxInputBytes), budget.MaxInputBytes, remaining);
            return stream;
        }

        byte[] content = await ReadLimitedAsync(stream, budget.MaxInputBytes, cancellationToken).ConfigureAwait(false);
        return new MemoryStream(content, writable: false);
    }

    private static async ValueTask<byte[]> ReadLimitedAsync(
        Stream stream, long maximum, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream((int)Math.Min(maximum, 81920));
        await CopyLimitedAsync(
            stream, content, maximum, nameof(DocumentLoadBudget.MaxInputBytes), cancellationToken).ConfigureAwait(false);
        return content.ToArray();
    }

    private static async ValueTask CopyLimitedAsync(
        Stream source, Stream destination, long maximum, string limitName, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return;

                long observed = AddWithoutOverflow(total, read);
                DocumentLoadBudgetState.Ensure(limitName, maximum, observed);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total = observed;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
    private static async ValueTask<Stream?> UnlockAsync(
        Stream stream, string? password, DocumentLoadBudget budget, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || await PeekAsync(stream, budget, cancellationToken).ConfigureAwait(false) is not { } data)
            return null;

        if (Open(data, budget) is not { } container)
            return null;

        if (OfficeCrypto.IsEncryptedPackage(container))
        {
            byte[] content = OfficeCrypto.DecryptPackage(container, password);
            DocumentLoadBudgetState.Ensure(
                nameof(DocumentLoadBudget.MaxInputBytes), budget.MaxInputBytes, content.LongLength);
            return new MemoryStream(content, writable: false);
        }

        if (container.ChildrenOf(string.Empty).Contains("WordDocument"))
            throw new DocxFormatException(
                "The file is a Word 97-2003 document, not an Open XML package. Read it with Quillwright.Doc.");

        return null;
    }

    /// <summary>The whole file when it is a compound file, otherwise nothing; the stream is left where it was.</summary>
    private static async ValueTask<byte[]?> PeekAsync(
        Stream stream, DocumentLoadBudget budget, CancellationToken cancellationToken)
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
            return await ReadLimitedAsync(stream, budget.MaxInputBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static CompoundFile? Open(byte[] data, DocumentLoadBudget budget)
    {
        try
        {
            return CompoundFile.Open(data, budget);
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

        if (_loadBudget is not null && LooksLikeXml(entry.FullName) &&
            !_validatedXmlEntries.Contains(entry.FullName))
        {
            Stream validation = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (validation.ConfigureAwait(false))
                ValidateXml(entry, validation);
        }

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

    private async ValueTask<MemoryStream> BufferAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        DocumentLoadBudget budget = _loadBudget?.Budget ?? DocumentLoadBudget.Default;
        DocumentLoadBudgetState.Ensure(nameof(DocumentLoadBudget.MaxPartBytes), budget.MaxPartBytes, entry.Length);

        if (_loadBudget is not null && LooksLikeXml(entry.FullName) &&
            !_validatedXmlEntries.Contains(entry.FullName))
        {
            Stream validation = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (validation.ConfigureAwait(false))
                ValidateXml(entry, validation);
        }

        var buffered = new MemoryStream(checked((int)Math.Max(entry.Length, 256)));
        Stream source = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            await CopyLimitedAsync(
                source,
                buffered,
                budget.MaxPartBytes,
                nameof(DocumentLoadBudget.MaxPartBytes),
                cancellationToken).ConfigureAwait(false);
        }

        buffered.Position = 0;
        return buffered;
    }

    private void ValidateXml(ZipArchiveEntry entry, Stream content)
    {
        if (_loadBudget is null || !LooksLikeXml(entry.FullName) ||
            !_validatedXmlEntries.Add(entry.FullName))
        {
            return;
        }

        try
        {
            using XmlReader reader = XmlReader.Create(content, XmlDefaults.ForBudget(_loadBudget.Budget));
            while (reader.Read())
                _loadBudget.AddXmlNode(reader.Depth + 1);
        }
        catch (XmlException exception) when (IsXmlCharacterLimit(exception))
        {
            long limit = _loadBudget.Budget.MaxXmlCharactersPerPart;
            throw new DocumentLoadLimitException(
                nameof(DocumentLoadBudget.MaxXmlCharactersPerPart),
                limit,
                limit == long.MaxValue ? long.MaxValue : limit + 1);
        }
        catch (XmlException)
        {
            // The format-specific reader reports malformed XML with its existing diagnostics.
            // Counters already charged for the prefix parsed before the malformed token.
            return;
        }
        finally
        {
            if (content.CanSeek)
                content.Position = 0;
        }

    }

    private bool LooksLikeXml(string entryName)
    {
        if (entryName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            entryName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) ||
            entryName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? contentType = _parsedContentTypes?.GetContentType(OpcPath.ToPartPath(entryName));
        return contentType is not null &&
               (contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase) ||
                contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsXmlCharacterLimit(XmlException exception) =>
        exception.Message.Contains(nameof(XmlReaderSettings.MaxCharactersInDocument), StringComparison.Ordinal);

    /// <summary>
    /// Supplies the caller's token to reads initiated by <see cref="XmlReader.ReadAsync()"/>.
    /// XmlReader has no token-bearing ReadAsync overload, so checking only between XML nodes
    /// would leave a single very large text node or attribute effectively non-cancellable.
    /// </summary>
    private sealed class CancellationAwareReadStream(Stream inner, CancellationToken cancellationToken) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = inner.Read(buffer, offset, count);
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = inner.Read(buffer);
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }

        public override int ReadByte()
        {
            cancellationToken.ThrowIfCancellationRequested();
            int value = inner.ReadByte();
            cancellationToken.ThrowIfCancellationRequested();
            return value;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ignored)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken ignored = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
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
