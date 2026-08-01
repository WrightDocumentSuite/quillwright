using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Doc;

/// <summary>
/// Reads a string table ([MS-DOC] 2.2.4, <c>STTB</c>) — a counted list of strings that other
/// structures refer to by index.
/// </summary>
/// <remarks>
/// A table may be Unicode or single-byte, and says which by opening with 0xFFFF. Each entry
/// may also be followed by a fixed block of extra data whose size the header declares, which
/// has to be stepped over even when it is not wanted.
/// </remarks>
internal static class DocStringTable
{
    /// <summary>
    /// Reads a bare array of counted strings, with no table header in front of it. The
    /// comment authors are stored this way rather than as a string table.
    /// </summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Where the array lives.</param>
    /// <param name="length">How long it is.</param>
    public static List<string> ReadCountedStrings(byte[] table, int offset, int length)
    {
        var values = new List<string>();
        int position = offset;
        int limit = Math.Min(offset + length, table.Length);

        while (position + 2 <= limit)
        {
            int characters = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position));
            if (characters is < 0 or > 55 || position + 2 + (characters * 2) > limit)
                break;

            values.Add(Encoding.Unicode.GetString(table, position + 2, characters * 2));
            position += 2 + (characters * 2);
        }

        return values;
    }

    /// <summary>Reads the strings of a table.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Where the string table lives.</param>
    /// <param name="length">How long it is.</param>
    public static List<string> Read(byte[] table, int offset, int length)
    {
        var values = new List<string>();
        if (length < 6 || offset + length > table.Length)
            return values;

        bool unicode = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset)) == 0xFFFF;
        int position = offset + (unicode ? 2 : 0);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position));
        int extra = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position + 2));
        position += 4;

        int limit = offset + length;
        for (int i = 0; i < count && position < limit; i++)
        {
            int characters = unicode
                ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position))
                : table[position];
            position += unicode ? 2 : 1;

            int bytes = unicode ? characters * 2 : characters;
            if (position + bytes > limit)
                break;

            values.Add(unicode
                ? Encoding.Unicode.GetString(table, position, bytes)
                : Encoding.GetEncoding(1252).GetString(table, position, bytes));
            position += bytes + extra;
        }

        return values;
    }
}
