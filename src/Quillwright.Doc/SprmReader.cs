using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>One property modifier from a packed property list.</summary>
/// <param name="opcode">The two-byte identifier.</param>
/// <param name="operand">The bytes of the value.</param>
internal readonly ref struct Sprm(ushort opcode, ReadOnlySpan<byte> operand)
{
    /// <summary>The two-byte identifier.</summary>
    public ushort Opcode { get; } = opcode;

    /// <summary>The bytes of the value.</summary>
    public ReadOnlySpan<byte> Operand { get; } = operand;

    /// <summary>The value read as a single byte.</summary>
    public byte Byte => Operand.Length > 0 ? Operand[0] : (byte)0;

    /// <summary>The value read as a signed 16-bit number.</summary>
    public short Int16 => Operand.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(Operand) : (short)0;

    /// <summary>The value read as an unsigned 16-bit number.</summary>
    public ushort UInt16 => Operand.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(Operand) : (ushort)0;

    /// <summary>The value read as a signed 32-bit number.</summary>
    public int Int32 => Operand.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(Operand) : 0;

    /// <summary>
    /// A toggle value as Word stores it: <c>0</c> off, <c>1</c> on, <c>128</c> inherit and
    /// <c>129</c> invert what was inherited.
    /// </summary>
    /// <param name="inherited">The value in force before this modifier.</param>
    public bool? Toggle(bool? inherited) => Byte switch
    {
        0 => false,
        1 => true,
        129 => !(inherited ?? false),
        _ => inherited,
    };
}

/// <summary>
/// Walks a packed list of property modifiers ([MS-DOC] 2.2.2).
/// </summary>
/// <remarks>
/// Each modifier is a two-byte opcode followed by an operand whose length is encoded in the
/// top three bits of the opcode, so a reader can step over modifiers it does not know
/// without losing its place. That is what makes it safe to understand only the properties
/// that matter and skip the rest of a twenty-year-old file.
/// </remarks>
internal ref struct SprmReader(ReadOnlySpan<byte> properties)
{
    private readonly ReadOnlySpan<byte> _properties = properties;
    private int _position;

    /// <summary>The modifier at the current position, or <see langword="false"/> at the end.</summary>
    public bool TryRead(out Sprm sprm)
    {
        sprm = default;
        if (_position + 2 > _properties.Length)
            return false;

        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(_properties[_position..]);
        int operandStart = _position + 2;
        int size = OperandSize(opcode, _properties, operandStart);
        if (size < 0 || operandStart + size > _properties.Length)
            return false;

        sprm = new Sprm(opcode, _properties.Slice(operandStart, size));
        _position = operandStart + size;
        return true;
    }

    private static int OperandSize(ushort opcode, ReadOnlySpan<byte> properties, int operandStart)
    {
        int lengthCode = opcode >> 13;
        switch (lengthCode)
        {
            case 0:
            case 1:
                return 1;
            case 2:
            case 4:
            case 5:
                return 2;
            case 3:
                return 4;
            case 7:
                return 3;
            default:
                if (operandStart >= properties.Length)
                    return -1;

                // The table-definition modifier is the one variable-length case whose length
                // prefix is two bytes rather than one, and it counts the bytes that follow it
                // plus one, so the whole operand is that value plus the prefix, less the one.
                return opcode == SprmCode.TableDefinition
                    ? operandStart + 2 <= properties.Length
                        ? BinaryPrimitives.ReadUInt16LittleEndian(properties[operandStart..]) + 1
                        : -1
                    : properties[operandStart] + 1;
        }
    }
}
