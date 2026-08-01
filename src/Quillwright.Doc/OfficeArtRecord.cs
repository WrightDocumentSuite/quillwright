using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>
/// One record of the drawing layer ([MS-ODRAW] 2.2.1, <c>OfficeArtRecordHeader</c>): eight
/// bytes saying what the record is and how long it is, followed by that many bytes of body.
/// </summary>
/// <remarks>
/// The whole drawing layer — the store of images, the shapes that display them, the property
/// tables that connect the two — is one tree of these records, so walking them is written
/// once here rather than in each reader that needs a different part of the tree.
/// </remarks>
/// <param name="Version">Low nibble of the header; <c>0xF</c> means the body is other records.</param>
/// <param name="Instance">High twelve bits of the header, whose meaning depends on the type.</param>
/// <param name="Type">What the record is.</param>
/// <param name="Start">Offset of the header within the stream it was read from.</param>
/// <param name="Length">Bytes of body following the header.</param>
internal readonly record struct OfficeArtRecord(int Version, int Instance, ushort Type, int Start, int Length)
{
    /// <summary>Bytes of the header every record begins with.</summary>
    public const int HeaderBytes = 8;

    /// <summary>Whether the body is made of other records rather than of fields.</summary>
    public bool IsContainer => Version == 0xF;

    /// <summary>Offset of the first byte of the body.</summary>
    public int Body => Start + HeaderBytes;

    /// <summary>Offset one past the last byte of the record.</summary>
    public int End => Body + Length;

    /// <summary>Reads the record beginning at an offset, when a whole one fits there.</summary>
    /// <param name="data">The stream the records live in.</param>
    /// <param name="start">Offset of the header.</param>
    /// <param name="end">Offset the enclosing record ends at.</param>
    /// <param name="record">The record that was read.</param>
    public static bool TryRead(byte[] data, int start, int end, out OfficeArtRecord record)
    {
        record = default;
        if (start < 0 || end > data.Length || start + HeaderBytes > end)
            return false;

        ushort header = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start + 2));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start + 4));
        if (length > (uint)(end - start - HeaderBytes))
            return false;

        record = new OfficeArtRecord(header & 0xF, header >> 4, type, start, (int)length);
        return true;
    }

    /// <summary>The records that sit side by side between two offsets.</summary>
    /// <param name="data">The stream the records live in.</param>
    /// <param name="start">Offset of the first header.</param>
    /// <param name="end">Offset to stop at.</param>
    public static IEnumerable<OfficeArtRecord> Walk(byte[] data, int start, int end)
    {
        int position = start;
        while (TryRead(data, position, end, out OfficeArtRecord record))
        {
            yield return record;

            // A record claiming no body would leave the position where it was; stopping is
            // the only way a malformed file cannot make this loop run forever.
            if (record.End <= position)
                yield break;

            position = record.End;
        }
    }

    /// <summary>The records inside this container.</summary>
    /// <param name="data">The stream the records live in.</param>
    public IEnumerable<OfficeArtRecord> Children(byte[] data) => Walk(data, Body, End);

    /// <summary>The first record of a type sitting directly between two offsets.</summary>
    /// <param name="data">The stream the records live in.</param>
    /// <param name="start">Offset of the first header.</param>
    /// <param name="end">Offset to stop at.</param>
    /// <param name="type">The record type to look for.</param>
    public static OfficeArtRecord? Find(byte[] data, int start, int end, ushort type)
    {
        foreach (OfficeArtRecord record in Walk(data, start, end))
        {
            if (record.Type == type)
                return record;
        }

        return null;
    }
}
