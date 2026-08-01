using System.Buffers.Binary;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Builds a <c>grpprl</c> — the packed list of property modifiers that carries all
/// formatting in a legacy Word file ([MS-DOC] 2.2.2).
/// </summary>
/// <remarks>
/// Each modifier is a two-byte opcode followed by an operand whose length is encoded in the
/// top three bits of the opcode. The writer derives the operand length from the opcode
/// exactly as <see cref="SprmReader"/> does when reading, so a modifier can never be written
/// in a shape the reader would step over incorrectly.
/// </remarks>
internal sealed class GrpprlWriter
{
    private readonly List<byte> _bytes = [];

    /// <summary>Number of bytes written so far.</summary>
    public int Length => _bytes.Count;

    /// <summary>Returns <see langword="true"/> when no modifier has been written.</summary>
    public bool IsEmpty => _bytes.Count == 0;

    /// <summary>The finished property list.</summary>
    public byte[] ToArray() => [.. _bytes];

    /// <summary>Writes a modifier whose operand is one byte.</summary>
    public GrpprlWriter Byte(ushort opcode, byte value)
    {
        Opcode(opcode, expectedSize: 1);
        _bytes.Add(value);
        return this;
    }

    /// <summary>Writes an on/off modifier.</summary>
    public GrpprlWriter Toggle(ushort opcode, bool value) => Byte(opcode, value ? (byte)1 : (byte)0);

    /// <summary>Writes a modifier whose operand is two bytes.</summary>
    public GrpprlWriter Int16(ushort opcode, short value)
    {
        Opcode(opcode, expectedSize: 2);
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        _bytes.AddRange(buffer);
        return this;
    }

    /// <summary>Writes a modifier whose operand is two bytes.</summary>
    public GrpprlWriter UInt16(ushort opcode, ushort value) => Int16(opcode, unchecked((short)value));

    /// <summary>Writes a modifier whose operand is four bytes.</summary>
    public GrpprlWriter Int32(ushort opcode, int value)
    {
        Opcode(opcode, expectedSize: 4);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _bytes.AddRange(buffer);
        return this;
    }

    /// <summary>Writes a modifier whose operand is three bytes.</summary>
    public GrpprlWriter Int24(ushort opcode, int value)
    {
        Opcode(opcode, expectedSize: 3);
        _bytes.Add((byte)value);
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)(value >> 16));
        return this;
    }

    /// <summary>
    /// Writes a modifier whose operand is variable length, prefixed by its size. The
    /// table-definition modifier is the one case whose prefix is two bytes rather than one.
    /// </summary>
    public GrpprlWriter Variable(ushort opcode, scoped ReadOnlySpan<byte> operand)
    {
        if (opcode >> 13 != 6)
            throw new InvalidOperationException($"Sprm 0x{opcode:X4} does not take a variable-length operand.");

        Write(opcode);
        if (opcode == SprmCode.TableDefinition)
        {
            // This one counts the bytes that follow it plus one, unlike every other operand
            // prefix in the format.
            Span<byte> size = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(size, (ushort)(operand.Length + 1));
            _bytes.AddRange(size);
        }
        else
        {
            if (operand.Length > 254)
                throw new InvalidOperationException($"Sprm 0x{opcode:X4} cannot carry {operand.Length} bytes.");
            _bytes.Add((byte)operand.Length);
        }

        _bytes.AddRange(operand);
        return this;
    }

    /// <summary>Appends an already-built property list.</summary>
    public GrpprlWriter Append(scoped ReadOnlySpan<byte> properties)
    {
        _bytes.AddRange(properties);
        return this;
    }

    private void Opcode(ushort opcode, int expectedSize)
    {
        int actual = (opcode >> 13) switch
        {
            0 or 1 => 1,
            2 or 4 or 5 => 2,
            3 => 4,
            7 => 3,
            _ => -1,
        };

        if (actual != expectedSize)
            throw new InvalidOperationException($"Sprm 0x{opcode:X4} takes a {actual}-byte operand, not {expectedSize}.");

        Write(opcode);
    }

    private void Write(ushort opcode)
    {
        _bytes.Add((byte)opcode);
        _bytes.Add((byte)(opcode >> 8));
    }
}
