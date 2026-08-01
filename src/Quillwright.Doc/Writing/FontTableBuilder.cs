using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Collects the fonts a document uses and writes them as the table every character property
/// refers to by index ([MS-DOC] 2.9.79, <c>SttbfFfn</c>).
/// </summary>
/// <remarks>
/// A legacy file never names a font inside a run: it names an index into this table. The
/// builder therefore has to be filled before any character properties are encoded, which is
/// why it hands out indexes as it is asked for them.
/// </remarks>
internal sealed class FontTableBuilder
{
    private const int FixedFieldBytes = 40;

    private readonly List<string> _names = [];
    private readonly Dictionary<string, int> _indexes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a table that already contains the default font.</summary>
    public FontTableBuilder() => IndexOf("Times New Roman");

    /// <summary>The index of a font, adding it to the table when it is new.</summary>
    /// <param name="name">Font name.</param>
    public int IndexOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1;

        if (_indexes.TryGetValue(name, out int existing))
            return existing;

        int index = _names.Count;
        _names.Add(name);
        _indexes[name] = index;
        return index;
    }

    /// <summary>Writes the table.</summary>
    public byte[] ToArray() => Build(_names);

    /// <summary>Writes a table from a fixed list of names.</summary>
    /// <param name="names">Font names, in index order.</param>
    public static byte[] Build(IReadOnlyList<string> names)
    {
        // The header is a count and an extra-data size. This is the one string table in the
        // format that has no extended marker in front of them, and a reader that expects one
        // is left two bytes out for the whole table.
        var bytes = new List<byte>(4 + (names.Count * 64));
        Append16(bytes, (ushort)names.Count);
        Append16(bytes, 0);

        foreach (string name in names)
        {
            byte[] text = Encoding.Unicode.GetBytes(name);
            int total = FixedFieldBytes + text.Length + 2;

            bytes.Add((byte)(total - 1));
            bytes.Add(0x04);              // A TrueType font of unspecified family.
            Append16(bytes, 400);         // Normal weight.
            bytes.Add(0);                 // ANSI character set.
            bytes.Add(0);                 // No alternate name follows.
            bytes.AddRange(new byte[10]); // PANOSE classification, left unstated.
            bytes.AddRange(new byte[24]); // Font signature, left unstated.
            bytes.AddRange(text);
            bytes.AddRange(new byte[2]);  // The name is null-terminated.
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
