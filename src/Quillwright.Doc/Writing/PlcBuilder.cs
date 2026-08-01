using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Builds a <c>PLC</c> ([MS-DOC] 2.2.2) — the format's one general-purpose way of attaching
/// data to positions in the text.
/// </summary>
/// <remarks>
/// A PLC is two arrays in one block: ascending positions first, then one fixed-size record
/// per gap between them. There is always exactly one more position than record, which is how
/// a reader recovers the record size from the block's length alone. Almost everything the
/// table stream holds — sections, headers, notes, comments, fields, bookmarks — is a PLC.
/// </remarks>
internal sealed class PlcBuilder(int recordBytes)
{
    private readonly List<int> _positions = [];
    private readonly List<byte[]> _records = [];

    /// <summary>Number of records added.</summary>
    public int Count => _records.Count;

    /// <summary>Returns <see langword="true"/> when nothing was added.</summary>
    public bool IsEmpty => _records.Count == 0;

    /// <summary>Adds a record covering the span between two positions.</summary>
    /// <param name="start">Where the record's span begins.</param>
    /// <param name="end">Where it ends; the next record continues from here.</param>
    /// <param name="record">The record's bytes, exactly the declared size.</param>
    public void Add(int start, int end, scoped ReadOnlySpan<byte> record)
    {
        if (record.Length != recordBytes)
            throw new ArgumentException($"A record of this list must be {recordBytes} bytes, not {record.Length}.", nameof(record));

        if (_positions.Count == 0)
            _positions.Add(start);

        _positions.Add(end);
        _records.Add(record.ToArray());
    }

    /// <summary>Adds a record with no bytes of its own, used by lists that are pure positions.</summary>
    /// <param name="start">Where the record's span begins.</param>
    /// <param name="end">Where it ends.</param>
    public void Add(int start, int end) => Add(start, end, []);

    /// <summary>Writes the list, or nothing at all when it is empty.</summary>
    public byte[] ToArray()
    {
        if (_records.Count == 0)
            return [];

        var bytes = new byte[(_positions.Count * 4) + (_records.Count * recordBytes)];
        for (int i = 0; i < _positions.Count; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), _positions[i]);

        int cursor = _positions.Count * 4;
        foreach (byte[] record in _records)
        {
            record.CopyTo(bytes.AsSpan(cursor));
            cursor += recordBytes;
        }

        return bytes;
    }

    /// <summary>
    /// Writes a list that is nothing but positions. The header document's story boundaries
    /// are stored this way: a list whose records are zero bytes long.
    /// </summary>
    /// <param name="positions">The positions, in ascending order.</param>
    public static byte[] Positions(IReadOnlyList<int> positions)
    {
        if (positions.Count < 2)
            return [];

        var bytes = new byte[positions.Count * 4];
        for (int i = 0; i < positions.Count; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), positions[i]);
        return bytes;
    }

    /// <summary>
    /// Builds the table that maps stretches of the document stream to the pages holding
    /// their formatting ([MS-DOC] 2.8.6).
    /// </summary>
    /// <param name="boundaries">The file offsets each page begins at, and the final end.</param>
    /// <param name="firstPage">Page number of the first page.</param>
    public static byte[] BinTable(IReadOnlyList<int> boundaries, int firstPage)
    {
        if (boundaries.Count < 2)
            return [];

        var builder = new PlcBuilder(4);
        Span<byte> record = stackalloc byte[4];
        for (int i = 0; i + 1 < boundaries.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(record, firstPage + i);
            builder.Add(boundaries[i], boundaries[i + 1], record);
        }

        return builder.ToArray();
    }

    /// <summary>
    /// Builds a string table ([MS-DOC] 2.2.4) — a counted list of strings, which is how the
    /// format stores names that positions elsewhere refer to by index.
    /// </summary>
    /// <param name="values">The strings, in index order.</param>
    /// <param name="extraBytes">Size of the record that follows each string.</param>
    public static byte[] StringTable(IReadOnlyList<string> values, int extraBytes = 0) =>
        StringTable([.. values.Select(value => (value, new byte[extraBytes]))]);

    /// <summary>
    /// Builds a string table whose entries carry a fixed block of data apiece. The annotation
    /// bookmarks are stored this way: every string is empty and everything that matters is in
    /// the block that follows it.
    /// </summary>
    /// <param name="entries">The strings and the data that follows each of them.</param>
    public static byte[] StringTable(IReadOnlyList<(string Value, byte[] Extra)> entries)
    {
        int extraBytes = entries.Count == 0 ? 0 : entries[0].Extra.Length;
        var bytes = new List<byte>(16 + (entries.Count * 24));
        Append16(bytes, 0xFFFF);
        Append16(bytes, (ushort)entries.Count);
        Append16(bytes, (ushort)extraBytes);

        foreach ((string value, byte[] extra) in entries)
        {
            Append16(bytes, (ushort)value.Length);
            bytes.AddRange(Encoding.Unicode.GetBytes(value));
            bytes.AddRange(extra);
        }

        return [.. bytes];
    }

    private static void Append16(List<byte> bytes, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }
}
