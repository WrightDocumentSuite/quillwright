using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Doc;

/// <summary>
/// The font names a legacy document refers to by index ([MS-DOC] 2.9.79).
/// </summary>
internal sealed class DocFontTable
{
    private readonly List<string> _names = [];

    private DocFontTable()
    {
    }

    /// <summary>An empty table, used when the document does not declare one.</summary>
    public static DocFontTable Empty { get; } = new();

    /// <summary>Reads the table from the table stream.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Offset of the font table.</param>
    /// <param name="length">Its length in bytes.</param>
    public static DocFontTable Read(byte[] table, int offset, int length)
    {
        var result = new DocFontTable();
        if (length <= 2 || offset + length > table.Length)
            return result;

        // The header is a count and an extra-data size — this string table has no extended
        // marker, unlike most — then a sequence of self-sized entries whose name is a
        // null-terminated UTF-16 string 40 bytes in.
        int position = offset + 4;
        int limit = offset + length;
        while (position < limit)
        {
            int size = table[position];
            if (size < 40 || position + size + 1 > limit)
                break;

            int nameStart = position + 40;
            int nameEnd = nameStart;
            while (nameEnd + 1 < position + size + 1 && (table[nameEnd] != 0 || table[nameEnd + 1] != 0))
                nameEnd += 2;

            result._names.Add(Encoding.Unicode.GetString(table, nameStart, Math.Max(0, nameEnd - nameStart)));
            position += size + 1;
        }

        return result;
    }

    /// <summary>The name at an index, or <see langword="null"/> when the index is unknown.</summary>
    /// <param name="index">Font index used by a property modifier.</param>
    public string? Name(int index) => index >= 0 && index < _names.Count && _names[index].Length > 0 ? _names[index] : null;
}

/// <summary>
/// The style names a legacy document defines ([MS-DOC] 2.9.271), enough to map a paragraph's
/// style index onto a style identifier in the converted document.
/// </summary>
internal sealed class DocStyleSheet
{
    private readonly List<string?> _names = [];

    private DocStyleSheet()
    {
    }

    /// <summary>An empty sheet, used when the document does not declare one.</summary>
    public static DocStyleSheet Empty { get; } = new();

    /// <summary>Reads the sheet from the table stream.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Offset of the style sheet.</param>
    /// <param name="length">Its length in bytes.</param>
    public static DocStyleSheet Read(byte[] table, int offset, int length)
    {
        var result = new DocStyleSheet();
        if (length < 4 || offset + length > table.Length)
            return result;

        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset));
        int count = headerSize >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset + 2)) : 0;

        // The header says how wide the fixed part of each definition is. Word 2007 and later
        // write eight bytes more than Word 97 did, and reading a name at the older offset
        // picks up the extra fields as characters.
        int fixedFields = headerSize >= 6 ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset + 4)) : 10;
        if (fixedFields is not (10 or 18))
            fixedFields = 10;

        int position = offset + 2 + headerSize;
        int limit = offset + length;

        for (int i = 0; i < count && position + 2 <= limit; i++)
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position));
            if (size == 0)
            {
                result._names.Add(null);
                position += 2;
                continue;
            }

            result._names.Add(position + 2 + size <= limit ? ReadName(table, position + 2, size, fixedFields) : null);
            position += 2 + size + (size % 2);
        }

        return result;
    }

    /// <summary>The name at a style index, or <see langword="null"/> when the index is unknown.</summary>
    /// <param name="index">Style index from a paragraph's property list.</param>
    public string? Name(int index) => index >= 0 && index < _names.Count ? _names[index] : null;

    /// <summary>The style identifier at an index, or <see langword="null"/> when the index is unknown.</summary>
    /// <param name="index">Style index from a property list.</param>
    public string? Identifier(int index) => Name(index) is { Length: > 0 } name ? ToIdentifier(name) : null;

    /// <summary>
    /// Turns a legacy style name into the identifier the converted document uses. Word stores
    /// display names such as "heading 1"; a style identifier may not contain spaces.
    /// </summary>
    /// <param name="name">The display name.</param>
    public static string ToIdentifier(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        int written = 0;
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
                buffer[written++] = c;
        }

        return written == 0 ? "Normal" : new string(buffer[..written]);
    }

    private static string? ReadName(byte[] table, int position, int size, int fixedFields)
    {
        // The entry begins with fixed fields; the name follows as a counted UTF-16 string.
        if (size <= fixedFields + 2)
            return null;

        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position + fixedFields));
        int nameStart = position + fixedFields + 2;
        if (nameLength <= 0 || nameLength > 255 || nameStart + (nameLength * 2) > table.Length)
            return null;

        return Encoding.Unicode.GetString(table, nameStart, nameLength * 2);
    }
}
