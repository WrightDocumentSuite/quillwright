using System.Buffers.Binary;
using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>
/// Reads the list definitions and overrides ([MS-DOC] 2.9.150 <c>PlfLst</c> and 2.9.131
/// <c>PlfLfo</c>).
/// </summary>
/// <remarks>
/// A numbered paragraph names an override, an override names a definition, and the
/// definition holds the nine levels. Reading only one of the three leaves the numbering
/// pointing at nothing, which is why they are read together into the model's own two-tiered
/// numbering.
/// </remarks>
internal static class DocListTable
{
    private const int Levels = 9;
    private const int DefinitionBytes = 28;
    private const int LevelHeaderBytes = 28;
    private const int OverrideBytes = 16;

    /// <summary>Reads the numbering of a document.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="definitions">Where the list definitions live.</param>
    /// <param name="overrides">Where the list overrides live.</param>
    /// <param name="numbering">The document's numbering, filled in place.</param>
    public static void Read(
        byte[] table,
        (int Offset, int Length) definitions,
        (int Offset, int Length) overrides,
        NumberingDefinitions numbering)
    {
        Dictionary<int, AbstractNumbering> byIdentifier = ReadDefinitions(table, definitions, numbering);
        if (byIdentifier.Count == 0)
            return;

        ReadOverrides(table, overrides, byIdentifier, numbering);
    }

    private static Dictionary<int, AbstractNumbering> ReadDefinitions(
        byte[] table,
        (int Offset, int Length) region,
        NumberingDefinitions numbering)
    {
        var byIdentifier = new Dictionary<int, AbstractNumbering>();
        if (region.Length < 2 + DefinitionBytes || region.Offset + region.Length > table.Length)
            return byIdentifier;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(region.Offset));
        int limit = region.Offset + region.Length;
        if (count <= 0 || region.Offset + 2 + (count * DefinitionBytes) > limit)
            return byIdentifier;

        var simple = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int at = region.Offset + 2 + (i * DefinitionBytes);
            int identifier = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at));
            simple[i] = (table[at + 26] & 0x01) != 0;

            var definition = new AbstractNumbering { Id = numbering.Definitions.Count };
            numbering.Definitions.Add(definition);
            byIdentifier[identifier] = definition;
        }

        // The levels of every definition follow the whole array of definitions, in order.
        int position = region.Offset + 2 + (count * DefinitionBytes);
        int index = 0;
        foreach (AbstractNumbering definition in byIdentifier.Values)
        {
            int levels = simple[index++] ? 1 : Levels;
            for (int level = 0; level < levels && position < limit; level++)
            {
                NumberingLevel? read = ReadLevel(table, ref position, limit, level);
                if (read is null)
                    return byIdentifier;
                definition.Levels.Add(read);
            }
        }

        return byIdentifier;
    }

    private static NumberingLevel? ReadLevel(byte[] table, ref int position, int limit, int level)
    {
        if (position + LevelHeaderBytes > limit)
            return null;

        ReadOnlySpan<byte> header = table.AsSpan(position, LevelHeaderBytes);
        int characterBytes = header[24];
        int paragraphBytes = header[25];
        int at = position + LevelHeaderBytes + characterBytes + paragraphBytes;
        if (at + 2 > limit)
            return null;

        int characters = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at));
        if (characters < 0 || at + 2 + (characters * 2) > limit)
            return null;

        string label = Encoding.Unicode.GetString(table, at + 2, characters * 2);
        position = at + 2 + (characters * 2);

        return new NumberingLevel
        {
            Level = level,
            Start = BinaryPrimitives.ReadInt32LittleEndian(header),
            Format = DocNumberFormat.Of(header[4]),
            Alignment = TranslateAlignment(header[5] & 0x03),
            IsLegal = (header[5] & 0x04) != 0,
            Suffix = TranslateSuffix(header[15]),
            Text = Pattern(label),
        };
    }

    private static void ReadOverrides(
        byte[] table,
        (int Offset, int Length) region,
        Dictionary<int, AbstractNumbering> byIdentifier,
        NumberingDefinitions numbering)
    {
        if (region.Length < 4 + OverrideBytes || region.Offset + region.Length > table.Length)
            return;

        int count = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(region.Offset));
        if (count <= 0 || region.Offset + 4 + (count * OverrideBytes) > region.Offset + region.Length)
            return;

        for (int i = 0; i < count; i++)
        {
            int identifier = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(region.Offset + 4 + (i * OverrideBytes)));
            if (!byIdentifier.TryGetValue(identifier, out AbstractNumbering? definition))
                continue;

            // A paragraph names its list by the one-based position of the override.
            numbering.Instances.Add(new NumberingInstance { Id = i + 1, AbstractId = definition.Id });
        }
    }

    /// <summary>Turns the byte placeholders of a label back into the model's <c>%1</c> form.</summary>
    private static string Pattern(string label)
    {
        var builder = new StringBuilder(label.Length + 4);
        foreach (char c in label)
        {
            if (c < Levels)
                builder.Append('%').Append((char)('1' + c));
            else
                builder.Append(c);
        }

        return builder.ToString();
    }

    private static ParagraphAlignment TranslateAlignment(int value) => value switch
    {
        1 => ParagraphAlignment.Center,
        2 => ParagraphAlignment.Right,
        _ => ParagraphAlignment.Left,
    };

    private static ListLevelSuffix TranslateSuffix(byte value) => value switch
    {
        1 => ListLevelSuffix.Space,
        2 => ListLevelSuffix.Nothing,
        _ => ListLevelSuffix.Tab,
    };
}
