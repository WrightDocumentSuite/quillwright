using System.Buffers.Binary;
using System.Text;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Builds the stylesheet ([MS-DOC] 2.9.270, <c>STSH</c>), which every paragraph and run
/// refers to by index rather than by name.
/// </summary>
/// <remarks>
/// The first fifteen slots are reserved for the styles the application defines itself, and
/// the format insists on that count whether or not they are used. Normal and the nine
/// heading levels take their conventional places there, so a converted document keeps the
/// built-in styles Word recognises rather than nine look-alikes; anything else is appended
/// as a user-defined style.
/// </remarks>
internal sealed class StyleSheetBuilder
{
    private const int FixedSlots = 15;
    private const ushort UserDefinedIdentifier = 0x0FFE;
    private const ushort NoParent = 0x0FFF;

    private static readonly string[] FixedNames =
    [
        "Normal", "heading 1", "heading 2", "heading 3", "heading 4", "heading 5",
        "heading 6", "heading 7", "heading 8", "heading 9",
    ];

    private readonly List<Entry?> _slots = [];
    private readonly Dictionary<string, int> _byIdentifier = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a stylesheet with the fixed slots in place and Normal defined.</summary>
    public StyleSheetBuilder()
    {
        for (int i = 0; i < FixedSlots; i++)
            _slots.Add(null);

        Define(0, FixedNames[0], StyleKind.Paragraph, sti: 0);
    }

    /// <summary>
    /// The index of a style, defining it when it is new. Returns <c>0</c> for the default
    /// paragraph style, which is what an unstyled paragraph refers to.
    /// </summary>
    /// <param name="styleId">Style identifier from the model.</param>
    /// <param name="styles">The document's style catalogue, consulted for the definition.</param>
    public int IndexOf(string? styleId, StyleSheet? styles = null)
    {
        if (string.IsNullOrEmpty(styleId))
            return 0;

        if (_byIdentifier.TryGetValue(styleId, out int existing))
            return existing;

        Style? definition = styles?.Find(styleId);
        StyleKind kind = definition?.Kind ?? StyleKind.Paragraph;

        int fixedSlot = FixedSlot(styleId);
        int index = fixedSlot >= 0 ? fixedSlot : _slots.Count;
        if (fixedSlot < 0)
            _slots.Add(null);

        Define(index, definition?.Name ?? styleId, kind, sti: fixedSlot >= 0 ? (ushort)fixedSlot : UserDefinedIdentifier);
        _byIdentifier[styleId] = index;

        if (definition is not null)
            Apply(index, definition, styles);

        return index;
    }

    /// <summary>Writes the stylesheet.</summary>
    /// <param name="fonts">Resolves a font name to its index, for the properties of a style.</param>
    public byte[] ToArray(FontTableBuilder fonts)
    {
        var bytes = new List<byte>(512);

        // The stylesheet opens with its own header, length-prefixed so that a reader can skip
        // whatever version of it it does not understand.
        Span<byte> header = stackalloc byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)_slots.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 0x000A);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], FixedSlots);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], FixedSlots);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)fonts.IndexOf("Times New Roman"));
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)fonts.IndexOf("Times New Roman"));
        BinaryPrimitives.WriteUInt16LittleEndian(header[16..], (ushort)fonts.IndexOf("Times New Roman"));

        Append16(bytes, (ushort)header.Length);
        bytes.AddRange(header);

        foreach (Entry? slot in _slots)
        {
            if (slot is null)
            {
                Append16(bytes, 0);
                continue;
            }

            byte[] definition = slot.Serialise(fonts);
            Append16(bytes, (ushort)definition.Length);
            bytes.AddRange(definition);
        }

        return [.. bytes];
    }

    private void Define(int index, string name, StyleKind kind, ushort sti)
    {
        _slots[index] = new Entry(name, kind, sti);
        _byIdentifier.TryAdd(name, index);
    }

    private void Apply(int index, Style definition, StyleSheet? styles)
    {
        Entry entry = _slots[index]!;
        entry.ParagraphFormat = definition.ParagraphFormat;
        entry.RunFormat = definition.RunFormat;

        // The parent has to exist before this style can point at it, and resolving it here
        // pulls the whole basedOn chain into the stylesheet.
        if (definition.BasedOn is { } parent && !string.Equals(parent, definition.Id, StringComparison.OrdinalIgnoreCase))
            entry.Parent = (ushort)IndexOf(parent, styles);
        if (definition.NextStyle is { } next && !string.Equals(next, definition.Id, StringComparison.OrdinalIgnoreCase))
            entry.Next = (ushort)IndexOf(next, styles);
    }

    private static int FixedSlot(string styleId)
    {
        if (string.Equals(styleId, "Normal", StringComparison.OrdinalIgnoreCase))
            return 0;

        return styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(styleId.AsSpan("Heading".Length), out int level) &&
               level is >= 1 and <= 9
            ? level
            : -1;
    }

    private static void Append16(List<byte> bytes, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private sealed class Entry(string name, StyleKind kind, ushort identifier)
    {
        public ParagraphFormat ParagraphFormat { get; set; } = ParagraphFormat.Default;

        public RunFormat RunFormat { get; set; } = RunFormat.Default;

        public ushort Parent { get; set; } = NoParent;

        public ushort Next { get; set; } = NoParent;

        public byte[] Serialise(FontTableBuilder fonts)
        {
            bool isParagraph = kind is not (StyleKind.Character);
            byte kindCode = kind switch
            {
                StyleKind.Character => 2,
                StyleKind.Table => 3,
                StyleKind.Numbering => 4,
                _ => 1,
            };

            var bytes = new List<byte>(96);
            Span<byte> stdf = stackalloc byte[10];
            BinaryPrimitives.WriteUInt16LittleEndian(stdf, (ushort)(identifier & 0x0FFF));
            BinaryPrimitives.WriteUInt16LittleEndian(stdf[2..], (ushort)(kindCode | (Parent << 4)));
            BinaryPrimitives.WriteUInt16LittleEndian(stdf[4..], (ushort)((isParagraph ? 2 : 1) | (Next << 4)));
            bytes.AddRange(stdf);

            byte[] text = Encoding.Unicode.GetBytes(name);
            Append16(bytes, (ushort)name.Length);
            bytes.AddRange(text);
            bytes.AddRange(new byte[2]);

            if (isParagraph)
                AppendUpx(bytes, [.. BitConverter.GetBytes((ushort)0), .. SprmBuilder.BuildParagraph(ParagraphFormat)]);
            AppendUpx(bytes, SprmBuilder.BuildRun(RunFormat, fonts.IndexOf));

            if (bytes.Count % 2 != 0)
                bytes.Add(0);

            // The size is recorded twice: once by the entry that contains this definition and
            // once inside it, and the two MUST agree.
            BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshalSpan(bytes)[6..], (ushort)bytes.Count);
            return [.. bytes];
        }

        private static Span<byte> CollectionsMarshalSpan(List<byte> bytes) =>
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes);

        private static void AppendUpx(List<byte> bytes, byte[] properties)
        {
            Append16(bytes, (ushort)properties.Length);
            bytes.AddRange(properties);
            if (properties.Length % 2 != 0)
                bytes.Add(0);
        }
    }
}
