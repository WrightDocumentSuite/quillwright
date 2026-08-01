using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Quillwright.Model;

namespace Quillwright.IO;

/// <summary>
/// Encodes and decodes the typed values of an OLE property set ([MS-OLEPS] 2.15), the binary
/// twin of the variant types a package writes as XML.
/// </summary>
/// <remarks>
/// Every value announces its type in its first two bytes and is padded to a four-byte
/// boundary, so a reader can step over a type it does not know without losing its place. Only
/// the types that appear in a document's property sets are decoded; anything else is left out
/// rather than guessed at.
/// </remarks>
internal static class PropertyValues
{
    private const ushort TypeInt16 = 0x0002;
    private const ushort TypeInt32 = 0x0003;
    private const ushort TypeReal32 = 0x0004;
    private const ushort TypeReal64 = 0x0005;
    private const ushort TypeCurrency = 0x0006;
    private const ushort TypeDate = 0x0007;
    private const ushort TypeBoolean = 0x000B;
    private const ushort TypeInt8 = 0x0010;
    private const ushort TypeUInt8 = 0x0011;
    private const ushort TypeUInt16 = 0x0012;
    private const ushort TypeUInt32 = 0x0013;
    private const ushort TypeInt64 = 0x0014;
    private const ushort TypeUInt64 = 0x0015;
    private const ushort TypeAnsiString = 0x001E;
    private const ushort TypeUnicodeString = 0x001F;
    private const ushort TypeFileTime = 0x0040;
    private const ushort TypeClassId = 0x0048;

    /// <summary>Reads the value at an offset, or nothing when its type is not one we decode.</summary>
    /// <param name="stream">The whole stream.</param>
    /// <param name="at">Offset of the typed value.</param>
    /// <param name="codePage">Code page the set's single-byte strings are written in.</param>
    public static PropertyValue Decode(byte[] stream, int at, int codePage)
    {
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(at));
        ReadOnlySpan<byte> payload = stream.AsSpan(at + 4);
        return type switch
        {
            TypeInt16 when payload.Length >= 2 => PropertyValue.FromInteger(BinaryPrimitives.ReadInt16LittleEndian(payload)),
            TypeUInt16 when payload.Length >= 2 => PropertyValue.FromInteger(BinaryPrimitives.ReadUInt16LittleEndian(payload)),
            TypeInt32 when payload.Length >= 4 => PropertyValue.FromInteger(BinaryPrimitives.ReadInt32LittleEndian(payload)),
            TypeUInt32 when payload.Length >= 4 => PropertyValue.FromInteger(BinaryPrimitives.ReadUInt32LittleEndian(payload)),
            TypeInt64 when payload.Length >= 8 => PropertyValue.FromInteger(BinaryPrimitives.ReadInt64LittleEndian(payload)),
            TypeUInt64 when payload.Length >= 8 => PropertyValue.FromInteger((long)BinaryPrimitives.ReadUInt64LittleEndian(payload)),
            TypeInt8 when payload.Length >= 1 => PropertyValue.FromInteger((sbyte)payload[0]),
            TypeUInt8 when payload.Length >= 1 => PropertyValue.FromInteger(payload[0]),
            TypeReal32 when payload.Length >= 4 => PropertyValue.FromReal(BinaryPrimitives.ReadSingleLittleEndian(payload)),
            TypeReal64 when payload.Length >= 8 => PropertyValue.FromReal(BinaryPrimitives.ReadDoubleLittleEndian(payload)),

            // Currency is a fixed-point integer of ten-thousandths ([MS-OLEPS] 2.15).
            TypeCurrency when payload.Length >= 8 => PropertyValue.FromReal(BinaryPrimitives.ReadInt64LittleEndian(payload) / 10000d),
            TypeDate when payload.Length >= 8 => Moment(DateTime.FromOADate(BinaryPrimitives.ReadDoubleLittleEndian(payload))),
            TypeBoolean when payload.Length >= 2 => PropertyValue.FromBoolean(BinaryPrimitives.ReadUInt16LittleEndian(payload) != 0),
            TypeFileTime when payload.Length >= 8 => FileTime(BinaryPrimitives.ReadInt64LittleEndian(payload)),
            TypeClassId when payload.Length >= 16 => PropertyValue.FromGuid(ClassId(payload)),
            TypeUnicodeString or TypeAnsiString => Text(stream, at, type == TypeUnicodeString, codePage),
            _ => default,
        };
    }

    /// <summary>Reads a string of a property set, in whichever encoding the set declares.</summary>
    /// <param name="stream">The whole stream.</param>
    /// <param name="at">Offset of the first byte of text.</param>
    /// <param name="bytes">Length of the text including its terminator.</param>
    /// <param name="unicode">Whether the text is two bytes per character.</param>
    /// <param name="codePage">Code page a single-byte string is written in.</param>
    public static string ReadString(byte[] stream, int at, int bytes, bool unicode, int codePage)
    {
        int content = Math.Max(0, bytes - (unicode ? 2 : 1));
        string text = unicode
            ? Encoding.Unicode.GetString(stream, at, content)
            : TextEncoding(codePage).GetString(stream, at, content);

        // Some writers store the length without the terminator, leaving one behind in the text.
        int terminator = text.IndexOf('\0');
        return terminator < 0 ? text : text[..terminator];
    }

    /// <summary>The encoding of a code page, falling back to Latin-1 when it is not available.</summary>
    private static Encoding TextEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }

    /// <summary>Writes a value as the type that carries it best.</summary>
    /// <param name="value">The value to write.</param>
    public static byte[] Encode(PropertyValue value) => value.Kind switch
    {
        PropertyValueKind.Text when value.AsText() is { Length: > 0 } text => StringValue(text),
        PropertyValueKind.Guid when value.AsText() is { Length: > 0 } identifier => StringValue(identifier),
        PropertyValueKind.Integer => Int32Value(value.AsInteger() ?? 0),
        PropertyValueKind.Real => Real64Value(value.AsReal() ?? 0),
        PropertyValueKind.Boolean => BooleanValue(value.AsBoolean() ?? false),
        PropertyValueKind.DateTime => FileTimeValue(value.AsDateTime() ?? default),
        _ => [],
    };

    /// <summary>Writes the code page a set's strings are in, which has to be its first property.</summary>
    /// <param name="codePage">The code page identifier.</param>
    public static byte[] CodePage(int codePage)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, TypeInt16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), unchecked((short)codePage));
        return bytes;
    }

    private static PropertyValue Text(byte[] stream, int at, bool unicode, int codePage)
    {
        if (at + 8 > stream.Length)
            return default;

        int characters = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(at + 4));
        int bytes = unicode ? characters * 2 : characters;
        return characters is <= 0 or > 0x8000 || at + 8 + bytes > stream.Length
            ? default
            : PropertyValue.FromText(ReadString(stream, at + 8, bytes, unicode, codePage));
    }

    private static PropertyValue FileTime(long ticks) =>
        ticks <= 0 ? default : Moment(DateTime.FromFileTimeUtc(ticks));

    private static PropertyValue Moment(DateTime value) =>
        PropertyValue.FromDateTime(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    /// <summary>A class identifier, whose first three groups are stored little-endian.</summary>
    private static string ClassId(ReadOnlySpan<byte> payload) => new Guid(payload[..16]).ToString("B").ToUpperInvariant();

    private static byte[] Int32Value(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, TypeInt32);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), (int)Math.Clamp(value, int.MinValue, int.MaxValue));
        return bytes;
    }

    private static byte[] Real64Value(double value)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, TypeReal64);
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(4), value);
        return bytes;
    }

    private static byte[] BooleanValue(bool value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, TypeBoolean);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), value ? (ushort)0xFFFF : (ushort)0);
        return bytes;
    }

    private static byte[] StringValue(string text)
    {
        // The stored length counts the terminator, and the value is padded to the next
        // four-byte boundary so the property that follows it starts aligned.
        int characters = text.Length + 1;
        int padded = ((characters * 2) + 3) & ~3;
        var bytes = new byte[8 + padded];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, TypeUnicodeString);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), characters);
        Encoding.Unicode.GetBytes(text, bytes.AsSpan(8));
        return bytes;
    }

    private static byte[] FileTimeValue(DateTimeOffset moment)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, TypeFileTime);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), moment.UtcDateTime.ToFileTimeUtc());
        return bytes;
    }

    /// <summary>Formats a whole number the way a property set writes it into text.</summary>
    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
