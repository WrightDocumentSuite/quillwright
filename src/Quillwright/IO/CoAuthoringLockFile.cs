using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace Quillwright.IO;

/// <summary>
/// Reads the lock file that says who is editing which part of a shared document
/// ([MS-WORDLFF]).
/// </summary>
/// <remarks>
/// <para>
/// The file is not part of a document and is not stored in one: it travels beside the document
/// on the server that hosts it, over a protocol this library has nothing to do with. This is
/// therefore a standalone reader — hand it the bytes and it says who holds what, which is
/// enough to tell a user why a paragraph will not open for editing.
/// </para>
/// <para>
/// A lock file is deflated XML behind an eight-byte signature, with the uncompressed length at
/// the end (2.3).
/// </para>
/// <para>
/// Only the root element is in the co-authoring namespace. The schema declares
/// <c>elementFormDefault="unqualified"</c> (5.1) and the example in section 3 writes
/// <c>&lt;Lock xmlns=""&gt;</c>, so everything below the root is in no namespace at all. Files
/// that qualify those children anyway are still read, but a name in any third namespace is
/// somebody else's element and is ignored.
/// </para>
/// </remarks>
public static class CoAuthoringLockFile
{
    private const string Namespace = "http://schemas.microsoft.com/word/2009/7/coauthoring";

    /// <summary>Bytes of the trailer: four reserved, then the uncompressed length.</summary>
    private const int TrailerBytes = 8;

    /// <summary>
    /// The largest expansion attempted. The declared length bounds the allocation, and this
    /// bounds the declared length, so a hostile file cannot ask for an arbitrary buffer.
    /// </summary>
    private const uint ExpansionLimit = 64 * 1024 * 1024;

    private static ReadOnlySpan<byte> Signature => [0x1A, 0x5A, 0x3A, 0x30, 0x00, 0x00, 0x00, 0x00];

    /// <summary>Whether the bytes begin the way a lock file does.</summary>
    /// <param name="bytes">The whole file.</param>
    public static bool IsLockFile(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Signature.Length + TrailerBytes && bytes[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Reads the regions of a lock file that are actually held, or returns <see langword="null"/>
    /// when <see cref="ReadAll"/> would.
    /// </summary>
    /// <param name="bytes">The whole file.</param>
    /// <remarks>
    /// The committed locks, less any whose identifier has since been given up, which 2.4.3.9
    /// says MUST be ignored. Everything else the file records — the uncommitted and ephemeral
    /// locks, the identifier bookkeeping, the synchronisation request — is behind
    /// <see cref="ReadAll"/>.
    /// </remarks>
    public static IReadOnlyList<CoAuthoringLock>? Read(byte[] bytes) => ReadAll(bytes)?.Effective;

    /// <summary>
    /// Reads everything a lock file records, or returns <see langword="null"/> when the bytes
    /// are not a lock file, will not expand, or are not rooted in a <c>CoAuthoringLocks</c>
    /// element in the co-authoring namespace.
    /// </summary>
    /// <param name="bytes">The whole file.</param>
    /// <remarks>
    /// Reading is lenient below the root: a record whose required identifier is missing or is
    /// not a four-byte hexadecimal number is dropped rather than guessed at, and an element the
    /// specification does not define is stepped over along with everything inside it.
    /// </remarks>
    public static CoAuthoringLocks? ReadAll(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!IsLockFile(bytes) || Expand(bytes) is not { } markup)
            return null;

        try
        {
            using var xml = XmlReader.Create(new MemoryStream(markup), Xml.XmlDefaults.ReaderSettings);
            if (xml.MoveToContent() != XmlNodeType.Element ||
                xml.LocalName != "CoAuthoringLocks" || xml.NamespaceURI != Namespace)
            {
                return null;
            }

            var draft = new Draft();
            foreach (XmlReader child in Children(xml))
                draft.Take(child);

            return draft.Build();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Reads a <c>CT_Sync</c> (2.4.3.10), which is nothing but three attributes.</summary>
    private static CoAuthoringSync? ReadSync(XmlReader xml) =>
        Identifier(xml.GetAttribute("DocID")) is { } document && Identifier(xml.GetAttribute("NextID")) is { } next
            ? new CoAuthoringSync
            {
                DocumentId = document,
                NextId = next,
                RevisionId = xml.GetAttribute("RevisionID"),
            }
            : null;

    /// <summary>Reads a <c>CT_ParaLock</c> (2.4.3.7) and the paragraphs it covers.</summary>
    private static CoAuthoringLock? ReadLock(XmlReader xml)
    {
        if (Identifier(xml.GetAttribute("LockId")) is not { } id)
            return null;

        string? ownerId = xml.GetAttribute("OwnerID");
        string? ownerName = xml.GetAttribute("OwnerName");
        string? ownerUserName = xml.GetAttribute("OwnerUserName");
        string? ownerEmail = xml.GetAttribute("OwnerEmailAddress");
        string? ownerSip = xml.GetAttribute("OwnerSIPAddress");

        var paragraphs = new List<uint>();
        foreach (XmlReader child in Children(xml))
        {
            if (child.LocalName == "ParaId" && Identifier(child.GetAttribute("Val")) is { } paragraph)
                paragraphs.Add(paragraph);
        }

        return new CoAuthoringLock
        {
            Id = id,
            OwnerId = ownerId,
            OwnerName = ownerName,
            OwnerUserName = ownerUserName,
            OwnerEmailAddress = ownerEmail,
            OwnerSipAddress = ownerSip,
            Paragraphs = paragraphs,
        };
    }

    /// <summary>Reads a <c>CT_ReservedIDs</c> (2.4.3.9): identifiers with the time they were freed.</summary>
    private static void ReadDeletedLocks(XmlReader xml, List<CoAuthoringDeletedLock> into)
    {
        foreach (XmlReader child in Children(xml))
        {
            if (child.LocalName == "LockId" && Identifier(child.GetAttribute("Val")) is { } id)
                into.Add(new CoAuthoringDeletedLock(id, Moment(child.GetAttribute("TimeStamp"))));
        }
    }

    /// <summary>Reads a <c>CT_LockIDChange</c> (2.4.3.4): identifiers and nothing else.</summary>
    private static void ReadIdentifiers(XmlReader xml, List<uint> into)
    {
        foreach (XmlReader child in Children(xml))
        {
            if (child.LocalName == "LockId" && Identifier(child.GetAttribute("Val")) is { } id)
                into.Add(id);
        }
    }

    /// <summary>Reads a <c>CT_UserInfoChanges</c> (2.4.3.11): authors whose details moved on.</summary>
    private static void ReadUserInfoChanges(XmlReader xml, List<CoAuthoringLockOwner> into)
    {
        foreach (XmlReader child in Children(xml))
        {
            if (child.LocalName != "UserInfoChange")
                continue;

            into.Add(new CoAuthoringLockOwner
            {
                OwnerId = child.GetAttribute("OwnerID"),
                OwnerName = child.GetAttribute("OwnerName"),
                OwnerUserName = child.GetAttribute("OwnerUserName"),
                OwnerEmailAddress = child.GetAttribute("OwnerEmailAddress"),
                OwnerSipAddress = child.GetAttribute("OwnerSIPAddress"),
            });
        }
    }

    /// <summary>
    /// Walks the direct children of the element the reader is on, skipping any whose namespace
    /// is neither the unqualified one the schema asks for nor the root's as a concession.
    /// </summary>
    /// <remarks>
    /// Depth is what makes an element a child rather than a descendant, so an element nobody
    /// has defined yet is stepped over without its contents being mistaken for records. The
    /// reader is handed back as it stands, and a caller that walks a child's own children
    /// leaves it on that child's end tag, which is exactly where this loop resumes from.
    /// </remarks>
    private static IEnumerable<XmlReader> Children(XmlReader xml)
    {
        if (xml.IsEmptyElement)
            yield break;

        int depth = xml.Depth;
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == depth)
                yield break;

            if (xml.NodeType == XmlNodeType.Element && xml.Depth == depth + 1 &&
                (xml.NamespaceURI.Length == 0 || xml.NamespaceURI == Namespace))
            {
                yield return xml;
            }
        }
    }

    private static byte[]? Expand(byte[] bytes)
    {
        int length = bytes.Length - Signature.Length - TrailerBytes;
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4));
        if (length <= 0 || declared == 0 || declared > ExpansionLimit)
            return null;

        try
        {
            using var source = new MemoryStream(bytes, Signature.Length, length, writable: false);
            using var expanding = new ZLibStream(source, CompressionMode.Decompress);

            // The trailer is the only bound on the expansion, so it is also the test of it:
            // the stream has to hold exactly what it said it holds, no more and no less.
            var markup = new byte[declared];
            int read = expanding.ReadAtLeast(markup, markup.Length, throwOnEndOfStream: false);
            return read == markup.Length && expanding.ReadByte() < 0 ? markup : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// An <c>ST_LongHexNumber</c> (2.4.4.2): four bytes written as eight hexadecimal digits,
    /// which the schema forbids from being zero. Anything else is not an identifier, and is
    /// reported as such rather than being rounded down to zero.
    /// </summary>
    private static uint? Identifier(string? value) =>
        value is { Length: 8 } &&
        uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint parsed) &&
        parsed != 0
            ? parsed
            : null;

    /// <summary>A schema <c>dateTime</c>, keeping the offset it was written with.</summary>
    private static DateTimeOffset? Moment(string? value)
    {
        if (value is null)
            return null;

        try
        {
            return XmlConvert.ToDateTimeOffset(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The nine child categories of <c>CT_CALocks</c> (2.4.3.1), while they are being read.</summary>
    private sealed class Draft
    {
        private CoAuthoringSync? _sync;
        private DateTimeOffset? _pruneTime;
        private readonly List<CoAuthoringLock> _committed = [];
        private readonly List<CoAuthoringLock> _uncommitted = [];
        private readonly List<CoAuthoringLock> _ephemeral = [];
        private readonly List<CoAuthoringDeletedLock> _deleted = [];
        private readonly List<uint> _autoDeletable = [];
        private readonly List<uint> _placeholder = [];
        private readonly List<CoAuthoringLockOwner> _userInfoChanges = [];

        /// <summary>Files one direct child of the root under the category it belongs to.</summary>
        public void Take(XmlReader child)
        {
            switch (child.LocalName)
            {
                case "Sync": _sync ??= ReadSync(child); break;
                case "Lock": Add(_committed, ReadLock(child)); break;
                case "UncommittedLock": Add(_uncommitted, ReadLock(child)); break;
                case "EphemeralLock": Add(_ephemeral, ReadLock(child)); break;
                case "DeletedLocks": ReadDeletedLocks(child, _deleted); break;
                case "IDPruneTime": _pruneTime ??= Moment(child.GetAttribute("TimeStamp")); break;
                case "AutoDeletableLocks": ReadIdentifiers(child, _autoDeletable); break;
                case "MakePlaceholder": ReadIdentifiers(child, _placeholder); break;
                case "UserInfoChanges": ReadUserInfoChanges(child, _userInfoChanges); break;
            }
        }

        public CoAuthoringLocks Build() => new()
        {
            Sync = _sync,
            Locks = _committed,
            UncommittedLocks = _uncommitted,
            EphemeralLocks = _ephemeral,
            DeletedLocks = _deleted,
            IdPruneTime = _pruneTime,
            AutoDeletableLocks = _autoDeletable,
            MakePlaceholder = _placeholder,
            UserInfoChanges = _userInfoChanges,
        };

        private static void Add(List<CoAuthoringLock> into, CoAuthoringLock? held)
        {
            if (held is not null)
                into.Add(held);
        }
    }
}
