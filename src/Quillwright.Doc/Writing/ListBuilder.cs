using System.Buffers.Binary;
using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Builds the list definitions ([MS-DOC] 2.9.150 <c>PlfLst</c>) and overrides (2.9.131
/// <c>PlfLfo</c>) that numbered paragraphs refer to.
/// </summary>
/// <remarks>
/// <para>
/// Numbering is two-tiered here as it is in the model: a definition describes nine levels,
/// and an override is what a paragraph actually points at, so two lists can share formatting
/// but count separately. A paragraph names its override by a one-based index, not by any
/// identifier the model would recognise, so the builder hands out those indexes as it goes
/// and rewrites the paragraph's properties to match.
/// </para>
/// <para>
/// The label pattern uses byte placeholders rather than the <c>%1</c> text of the newer
/// format: the character 0x0000 stands for the first level's counter, and a separate array
/// records where in the string each placeholder sits.
/// </para>
/// </remarks>
internal sealed class ListBuilder
{
    private const int Levels = 9;
    private const int NoStyle = 0x0FFF;

    private readonly DocWriteContext _context;
    private readonly List<AbstractNumbering> _definitions = [];
    private readonly Dictionary<int, int> _references = [];

    public ListBuilder(DocWriteContext context) => _context = context;

    /// <summary>Returns <see langword="true"/> when no paragraph used a list.</summary>
    public bool IsEmpty => _references.Count == 0;

    /// <summary>
    /// Points a paragraph at its list, replacing the identifier the model uses with the
    /// index of the override the file will hold.
    /// </summary>
    /// <param name="paragraph">The paragraph being written.</param>
    /// <param name="properties">Its property list, rewritten in place.</param>
    public void Apply(Paragraph paragraph, ref byte[] properties)
    {
        if (paragraph.Format.NumberingId is not { } numberingId)
            return;

        int reference = Register(numberingId);
        if (reference <= 0)
            return;

        var writer = new GrpprlWriter();
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
        {
            if (sprm.Opcode == SprmCode.NumberingId)
                writer.UInt16(SprmCode.NumberingId, (ushort)reference);
            else
                writer.Append(Rebuild(sprm));
        }

        properties = writer.ToArray();
    }

    /// <summary>Writes the list definitions, each followed by its nine levels.</summary>
    public byte[] BuildDefinitions()
    {
        var bytes = new List<byte>(256);
        Append16(bytes, (ushort)_definitions.Count);
        foreach (AbstractNumbering definition in _definitions)
            bytes.AddRange(Definition(definition));

        // The levels follow the whole array of definitions rather than each one.
        foreach (AbstractNumbering definition in _definitions)
        {
            foreach (NumberingLevel level in Nine(definition))
                bytes.AddRange(Level(level));
        }

        return [.. bytes];
    }

    /// <summary>Writes the list overrides, which are what paragraphs point at.</summary>
    public byte[] BuildOverrides()
    {
        var bytes = new List<byte>(64);
        Append32(bytes, _references.Count);

        foreach (int instance in _references.OrderBy(pair => pair.Value).Select(static pair => pair.Key))
        {
            var entry = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(entry, ListIdentifier(instance));
            entry[14] = 0;  // No level of this override differs from the definition.
            bytes.AddRange(entry);
        }

        // Each override is followed by data of its own: with no level overrides, that is one
        // unused position apiece.
        foreach (int _ in _references.Values)
            Append32(bytes, 0);

        return [.. bytes];
    }

    private int Register(int numberingId)
    {
        if (_references.TryGetValue(numberingId, out int existing))
            return existing;

        AbstractNumbering? definition = Definition(numberingId);
        if (definition is null)
            return 0;

        if (!_definitions.Contains(definition))
            _definitions.Add(definition);

        int reference = _references.Count + 1;
        _references[numberingId] = reference;
        return reference;
    }

    private AbstractNumbering? Definition(int numberingId)
    {
        NumberingInstance? instance = _context.Document.Numbering.Instances.FirstOrDefault(i => i.Id == numberingId);
        return instance is null
            ? null
            : _context.Document.Numbering.Definitions.FirstOrDefault(d => d.Id == instance.AbstractId);
    }

    private int ListIdentifier(int numberingId) => (Definition(numberingId)?.Id ?? 0) + 1;

    private static IEnumerable<NumberingLevel> Nine(AbstractNumbering definition)
    {
        for (int i = 0; i < Levels; i++)
            yield return definition.Levels.FirstOrDefault(l => l.Level == i) ?? new NumberingLevel { Level = i };
    }

    private static byte[] Definition(AbstractNumbering definition)
    {
        var entry = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(entry, definition.Id + 1);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), definition.Id + 1);
        for (int i = 0; i < Levels; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(8 + (i * 2)), NoStyle);

        entry[26] = 0x04;  // The list numbers itself; it is not a simple one-level list.
        return entry;
    }

    private byte[] Level(NumberingLevel level)
    {
        byte[] paragraph = SprmBuilder.BuildParagraph(level.ParagraphFormat);
        byte[] character = _context.BuildRun(level.RunFormat);
        (string label, byte[] placeholders) = Label(level);

        var bytes = new List<byte>(64);
        var header = new byte[28];
        BinaryPrimitives.WriteInt32LittleEndian(header, Math.Clamp(level.Start, 0, 0x7FFF));
        header[4] = DocNumberFormat.Code(level.Format);
        header[5] = (byte)(AlignmentCode(level.Alignment) | (level.IsLegal ? 1 << 2 : 0));
        placeholders.CopyTo(header, 6);
        header[15] = SuffixCode(level.Suffix);
        header[24] = (byte)Math.Min(character.Length, byte.MaxValue);
        header[25] = (byte)Math.Min(paragraph.Length, byte.MaxValue);
        header[26] = (byte)Math.Clamp(level.RestartAfter ?? 0, 0, Levels);

        bytes.AddRange(header);
        bytes.AddRange(paragraph);
        bytes.AddRange(character);
        Append16(bytes, (ushort)label.Length);
        bytes.AddRange(Encoding.Unicode.GetBytes(label));
        return [.. bytes];
    }

    /// <summary>
    /// The label with the model's <c>%1</c> placeholders turned into the single characters
    /// the binary format uses, and the one-based offsets at which they ended up.
    /// </summary>
    private static (string Label, byte[] Placeholders) Label(NumberingLevel level)
    {
        var builder = new StringBuilder();
        var placeholders = new byte[Levels];
        int found = 0;

        ReadOnlySpan<char> pattern = level.Text;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '%' && i + 1 < pattern.Length && char.IsAsciiDigit(pattern[i + 1]) && found < Levels)
            {
                placeholders[found++] = (byte)(builder.Length + 1);
                builder.Append((char)(pattern[i + 1] - '1'));
                i++;
                continue;
            }

            builder.Append(pattern[i]);
        }

        return (builder.ToString(), placeholders);
    }

    private static int AlignmentCode(ParagraphAlignment alignment) => alignment switch
    {
        ParagraphAlignment.Center => 1,
        ParagraphAlignment.Right => 2,
        _ => 0,
    };

    private static byte SuffixCode(ListLevelSuffix suffix) => suffix switch
    {
        ListLevelSuffix.Space => 1,
        ListLevelSuffix.Nothing => 2,
        _ => 0,
    };

    private static byte[] Rebuild(Sprm sprm)
    {
        var bytes = new byte[2 + sprm.Operand.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, sprm.Opcode);
        sprm.Operand.CopyTo(bytes.AsSpan(2));
        return bytes;
    }

    private static void Append16(List<byte> bytes, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static void Append32(List<byte> bytes, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }
}
