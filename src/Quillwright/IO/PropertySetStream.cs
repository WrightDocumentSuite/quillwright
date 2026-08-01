using System.Buffers.Binary;
using System.Text;
using Quillwright.Model;

namespace Quillwright.IO;

/// <summary>
/// One property set: its format identifier, the values it holds, and the names they go by
/// ([MS-OLEPS] 2.20).
/// </summary>
internal sealed class PropertySetSection
{
    /// <summary>Creates a set of a given format.</summary>
    /// <param name="formatId">The sixteen bytes that identify what the set is for.</param>
    public PropertySetSection(ReadOnlySpan<byte> formatId) => FormatId = formatId.ToArray();

    /// <summary>The sixteen bytes that identify what the set is for.</summary>
    public byte[] FormatId { get; }

    /// <summary>The values, keyed by property identifier.</summary>
    public Dictionary<int, PropertyValue> Values { get; } = [];

    /// <summary>
    /// The names of the properties that have one ([MS-OLEPS] 2.17). A set whose properties are
    /// named — the user-defined half of a document summary — carries this mapping as a
    /// property of its own at identifier zero.
    /// </summary>
    public Dictionary<int, string> Names { get; } = [];

    /// <summary>Whether the set is worth writing.</summary>
    public bool IsEmpty => Values.Count == 0;
}

/// <summary>
/// Reads and writes an OLE property set stream ([MS-OLEPS] 2.21) — the little key-value store
/// a compound file keeps a document's title, author and custom properties in, outside the
/// document format entirely.
/// </summary>
/// <remarks>
/// A set is a table of identifiers and offsets followed by the values, each announcing its own
/// type. Everything is aligned to four bytes and the offsets are measured from the start of
/// the set rather than of the stream, which is the part that is easy to get wrong. One stream
/// usually holds one set; the document summary is the historical exception and holds two, the
/// second being the user-defined properties.
/// </remarks>
internal static class PropertySetStream
{
    /// <summary>Name of the stream holding the summary.</summary>
    public const string SummaryStream = "\u0005SummaryInformation";

    /// <summary>Name of the stream holding the document summary and the user-defined properties.</summary>
    public const string DocumentSummaryStream = "\u0005DocumentSummaryInformation";

    /// <summary>Identifier of the property naming the code page its strings are written in.</summary>
    public const int CodePageId = 1;

    /// <summary>Identifier of the property holding the names of the others.</summary>
    public const int DictionaryId = 0;

    private const int UnicodeCodePage = 0x04B0;
    private const int SetHeaderBytes = 28;

    /// <summary>Identifier of the summary property set.</summary>
    public static ReadOnlySpan<byte> SummaryFormat =>
    [
        0xE0, 0x85, 0x9F, 0xF2, 0xF9, 0x4F, 0x68, 0x10,
        0xAB, 0x91, 0x08, 0x00, 0x2B, 0x27, 0xB3, 0xD9,
    ];

    /// <summary>Identifier of the document summary property set.</summary>
    public static ReadOnlySpan<byte> DocumentSummaryFormat =>
    [
        0x02, 0xD5, 0xCD, 0xD5, 0x9C, 0x2E, 0x1B, 0x10,
        0x93, 0x97, 0x08, 0x00, 0x2B, 0x2C, 0xF9, 0xAE,
    ];

    /// <summary>Identifier of the user-defined property set, the one custom properties live in.</summary>
    public static ReadOnlySpan<byte> UserDefinedFormat =>
    [
        0x05, 0xD5, 0xCD, 0xD5, 0x9C, 0x2E, 0x1B, 0x10,
        0x93, 0x97, 0x08, 0x00, 0x2B, 0x2C, 0xF9, 0xAE,
    ];

    /// <summary>Reads every property set a stream holds.</summary>
    /// <param name="stream">The whole stream, or <see langword="null"/> when there is none.</param>
    public static List<PropertySetSection> Read(byte[]? stream)
    {
        var sections = new List<PropertySetSection>();
        if (stream is null || stream.Length < SetHeaderBytes + 20)
            return sections;

        int count = Math.Min(BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(24)), 2);
        for (int i = 0; i < count; i++)
        {
            int entry = SetHeaderBytes + (i * 20);
            if (entry + 20 > stream.Length)
                break;

            int start = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(entry + 16));
            if (ReadSection(stream, start, stream.AsSpan(entry, 16)) is { } section)
                sections.Add(section);
        }

        return sections;
    }

    /// <summary>Builds a stream holding one or two property sets.</summary>
    /// <param name="sections">The sets, in the order they are to appear.</param>
    public static byte[] Build(params ReadOnlySpan<PropertySetSection> sections)
    {
        int header = SetHeaderBytes + (sections.Length * 20);
        var bodies = new byte[sections.Length][];
        int total = header;
        for (int i = 0; i < sections.Length; i++)
        {
            bodies[i] = BuildSection(sections[i]);
            total += bodies[i].Length;
        }

        var stream = new byte[total];
        BinaryPrimitives.WriteUInt16LittleEndian(stream, 0xFFFE);
        BinaryPrimitives.WriteUInt32LittleEndian(stream.AsSpan(4), 0x00020105);
        BinaryPrimitives.WriteInt32LittleEndian(stream.AsSpan(24), sections.Length);

        int at = header;
        for (int i = 0; i < sections.Length; i++)
        {
            int entry = SetHeaderBytes + (i * 20);
            sections[i].FormatId.CopyTo(stream.AsSpan(entry, 16));
            BinaryPrimitives.WriteInt32LittleEndian(stream.AsSpan(entry + 16), at);
            bodies[i].CopyTo(stream.AsSpan(at));
            at += bodies[i].Length;
        }

        return stream;
    }

    private static PropertySetSection? ReadSection(byte[] stream, int start, ReadOnlySpan<byte> formatId)
    {
        if (start < 0 || start + 8 > stream.Length)
            return null;

        int count = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(start + 4));
        if (count is < 0 or > 0x1000 || start + 8 + (count * 8) > stream.Length)
            return null;

        // Every string in the set is read through its code page, so that property has to be
        // found before any of the others is decoded.
        int codePage = ReadCodePage(stream, start, count);
        var section = new PropertySetSection(formatId);
        for (int i = 0; i < count; i++)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(start + 8 + (i * 8)));
            int at = start + BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(start + 12 + (i * 8)));
            if (at < 0 || at + 4 > stream.Length || id == CodePageId)
                continue;

            if (id == DictionaryId)
                ReadDictionary(stream, at, codePage, section.Names);
            else if (PropertyValues.Decode(stream, at, codePage) is { IsEmpty: false } value)
                section.Values[id] = value;
        }

        return section;
    }

    private static int ReadCodePage(byte[] stream, int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(start + 8 + (i * 8))) != CodePageId)
                continue;

            int at = start + BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(start + 12 + (i * 8)));
            if (at >= 0 && at + 6 <= stream.Length)
                return (ushort)BinaryPrimitives.ReadInt16LittleEndian(stream.AsSpan(at + 4));
        }

        return UnicodeCodePage;
    }

    /// <summary>Reads the mapping from property identifiers to names ([MS-OLEPS] 2.16-2.17).</summary>
    /// <remarks>
    /// A name is counted in characters rather than bytes, and whether a character is one byte
    /// or two depends on the set's code page — reading a single-byte name as Unicode yields
    /// text riddled with nulls, which is not text at all.
    /// </remarks>
    private static void ReadDictionary(byte[] stream, int at, int codePage, Dictionary<int, string> names)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(at));
        if (count is < 0 or > 0x1000)
            return;

        bool unicode = codePage == UnicodeCodePage;
        int cursor = at + 4;
        for (int i = 0; i < count && cursor + 8 <= stream.Length; i++)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(cursor));
            int characters = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(cursor + 4));
            int bytes = unicode ? characters * 2 : characters;
            if (characters is < 0 or > 0x8000 || cursor + 8 + bytes > stream.Length)
                return;

            // The stored count includes the terminator; a Unicode entry is padded to four bytes.
            names[id] = PropertyValues.ReadString(stream, cursor + 8, bytes, unicode, codePage);
            cursor += 8 + (unicode ? (bytes + 3) & ~3 : bytes);
        }
    }

    private static byte[] BuildSection(PropertySetSection section)
    {
        List<(int Id, byte[] Bytes)> encoded = [(CodePageId, PropertyValues.CodePage(UnicodeCodePage))];
        if (section.Names.Count > 0)
            encoded.Add((DictionaryId, BuildDictionary(section.Names)));

        foreach ((int id, PropertyValue value) in section.Values.OrderBy(static entry => entry.Key))
        {
            if (id is CodePageId or DictionaryId)
                continue;
            if (PropertyValues.Encode(value) is { Length: > 0 } bytes)
                encoded.Add((id, bytes));
        }

        int table = 8 + (encoded.Count * 8);
        var body = new byte[table + encoded.Sum(static entry => entry.Bytes.Length)];
        int at = table;
        for (int i = 0; i < encoded.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8 + (i * 8)), encoded[i].Id);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12 + (i * 8)), at);
            encoded[i].Bytes.CopyTo(body.AsSpan(at));
            at += encoded[i].Bytes.Length;
        }

        BinaryPrimitives.WriteInt32LittleEndian(body, body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), encoded.Count);
        return body;
    }

    private static byte[] BuildDictionary(Dictionary<int, string> names)
    {
        int size = 4;
        foreach (string name in names.Values)
            size += 8 + ((((name.Length + 1) * 2) + 3) & ~3);

        var bytes = new byte[size];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, names.Count);
        int at = 4;
        foreach ((int id, string name) in names)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), id);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 4), name.Length + 1);
            Encoding.Unicode.GetBytes(name, bytes.AsSpan(at + 8));
            at += 8 + ((((name.Length + 1) * 2) + 3) & ~3);
        }

        return bytes;
    }
}
