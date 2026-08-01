using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Vba.OForms;

/// <summary>
/// A cursor over one control structure of an Office Forms stream ([MS-OFORMS] 2.1.1.2).
/// </summary>
/// <remarks>
/// <para>
/// Everything in the format is little-endian and aligned to its own size, counted from the
/// start of the structure rather than from the start of the stream — so the cursor remembers
/// where its structure began. A nested structure that carries a version number of its own
/// starts a new alignment frame, which is what <see cref="Nested"/> is for.
/// </para>
/// <para>
/// Every read is bounds-checked against the end of the structure and throws rather than
/// returning nonsense, because a control that will not parse is caught and skipped whole.
/// </para>
/// </remarks>
internal sealed class OFormsReader
{
    private readonly byte[] _data;
    private readonly int _origin;
    private readonly int _end;

    /// <summary>Opens a cursor over part of a stream.</summary>
    /// <param name="data">The stream.</param>
    /// <param name="start">Where the structure begins, which is also its alignment origin.</param>
    /// <param name="end">One past the last byte the structure may reach.</param>
    public OFormsReader(byte[] data, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _origin = start;
        _end = Math.Clamp(end, start, data.Length);
        Position = start;
    }

    /// <summary>Where the cursor is, as an offset into the whole stream.</summary>
    public int Position { get; set; }

    /// <summary>One past the last byte this cursor may read.</summary>
    public int End => _end;

    /// <summary>How many bytes are left before <see cref="End"/>.</summary>
    public int Remaining => Math.Max(0, _end - Position);

    /// <summary>A cursor over a structure nested in this one, with an alignment frame of its own.</summary>
    /// <param name="length">Size of the nested structure, clamped to what is left.</param>
    public OFormsReader Nested(int length) => new(_data, Position, Position + Math.Min(length, Remaining));

    /// <summary>Moves the cursor forward, without reading.</summary>
    /// <param name="count">How many bytes to step over.</param>
    public void Skip(int count) => Position = Math.Min(_end, Position + Math.Max(0, count));

    /// <summary>
    /// Steps over the padding before a value of a given size ([MS-OFORMS] 2.1.1.2.4).
    /// </summary>
    /// <param name="size">Size of the value about to be read.</param>
    public void Align(int size)
    {
        int misaligned = (Position - _origin) % size;
        if (misaligned != 0)
            Position += size - misaligned;
    }

    public byte Byte() => Take(1)[0];

    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    /// <summary>Reads a class identifier, which the format stores the way [MS-DTYP] does.</summary>
    public Guid ReadGuid() => new(Take(16));

    /// <summary>Reads an unsigned value of one, two or four bytes.</summary>
    /// <param name="size">How many bytes the value occupies.</param>
    public uint Unsigned(int size) => size switch
    {
        1 => Byte(),
        2 => UInt16(),
        4 => UInt32(),
        _ => throw new InvalidDataException($"A property of {size} bytes cannot be read from a data block."),
    };

    /// <summary>
    /// Reads a string ([MS-OFORMS] 2.4.14). Compression means the high byte of every character
    /// was zero and was dropped, so decompressing is widening each byte back to a character.
    /// </summary>
    /// <param name="bytes">Size of the stored string, after compression.</param>
    /// <param name="compressed">Whether the high bytes were dropped.</param>
    public string Text(int bytes, bool compressed)
    {
        if (bytes <= 0)
            return string.Empty;

        ReadOnlySpan<byte> text = Take(bytes);
        return compressed ? Encoding.Latin1.GetString(text) : Encoding.Unicode.GetString(text[..(text.Length & ~1)]);
    }

    /// <summary>
    /// Takes a run of bytes, filling in with zeroes past the end of the structure.
    /// </summary>
    /// <param name="count">How many bytes are wanted.</param>
    /// <remarks>
    /// Word writes forms whose last property runs a byte or two past the end of the stream it
    /// stored them in — the values there are zero, and every reader has to accept it. Because
    /// each record is bounded by the size it declares, a short read cannot run into the next
    /// one; it can only leave this record's last property at its default.
    /// </remarks>
    private ReadOnlySpan<byte> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        int available = Math.Clamp(_end - Position, 0, count);
        if (available == count)
        {
            ReadOnlySpan<byte> whole = _data.AsSpan(Position, count);
            Position += count;
            return whole;
        }

        var padded = new byte[count];
        _data.AsSpan(Position, available).CopyTo(padded);
        Position = _end;
        return padded;
    }
}
