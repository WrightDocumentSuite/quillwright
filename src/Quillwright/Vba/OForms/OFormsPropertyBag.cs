namespace Quillwright.Vba.OForms;

/// <summary>Where a property's value is written, which is what decides how to step over it.</summary>
internal enum OFormsSlot : byte
{
    /// <summary>The bit stands for a boolean the mask itself carries; nothing is stored.</summary>
    Flag,

    /// <summary>A value of one, two or four bytes in the data block.</summary>
    Data,

    /// <summary>A length and compression flag in the data block, the string in the extra data block.</summary>
    Text,

    /// <summary>An <c>fmSize</c> or <c>fmPosition</c>: two four-byte numbers in the extra data block.</summary>
    Pair,

    /// <summary>A size in the data block and that many opaque bytes in the extra data block.</summary>
    Array,

    /// <summary>A two-byte marker in the data block and a font in the stream data.</summary>
    Font,

    /// <summary>A two-byte marker in the data block and a picture in the stream data.</summary>
    Picture,
}

/// <summary>One property of a control, named by its position in a <see cref="OFormsSchema"/>.</summary>
/// <param name="Name">What the property is called; the empty string for a bit nothing reads.</param>
/// <param name="Slot">Where the value is written.</param>
/// <param name="Size">Size in the data block, for <see cref="OFormsSlot.Data"/>.</param>
internal readonly record struct OFormsProperty(string Name, OFormsSlot Slot, byte Size = 0)
{
    /// <summary>A bit of the mask that stands for a boolean, or for a property this reader ignores.</summary>
    public static OFormsProperty Flag(string name = "") => new(name, OFormsSlot.Flag);

    /// <summary>A value in the data block.</summary>
    public static OFormsProperty Data(string name, byte size) => new(name, OFormsSlot.Data, size);

    /// <summary>A string, split between the data block and the extra data block.</summary>
    public static OFormsProperty Text(string name) => new(name, OFormsSlot.Text);

    /// <summary>A size or a position in the extra data block.</summary>
    public static OFormsProperty Pair(string name) => new(name, OFormsSlot.Pair);

    /// <summary>An opaque run of bytes in the extra data block, sized from the data block.</summary>
    public static OFormsProperty Array(string name) => new(name, OFormsSlot.Array);
}

/// <summary>The layout of one control record: how wide its mask is and what each bit means.</summary>
/// <param name="Name">Name of the structure, for diagnostics.</param>
/// <param name="MaskBytes">Four, or eight for the one control whose mask outgrew a word.</param>
/// <param name="Properties">The properties, in mask-bit order starting at the lowest bit.</param>
internal sealed record OFormsSchema(string Name, int MaskBytes, OFormsProperty[] Properties);

/// <summary>
/// The values read out of one control record.
/// </summary>
/// <param name="Mask">The property mask, kept so a caller can ask about a bit with no value.</param>
/// <param name="End">Offset just past the extra data block, taken from the record's own size field.</param>
internal sealed record OFormsValues(ulong Mask, int End)
{
    /// <summary>Numbers read out of the data block, by property name.</summary>
    public Dictionary<string, uint> Numbers { get; } = new(StringComparer.Ordinal);

    /// <summary>Strings read out of the extra data block, by property name.</summary>
    public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

    /// <summary>Sizes and positions read out of the extra data block, by property name.</summary>
    public Dictionary<string, (int First, int Second)> Pairs { get; } = new(StringComparer.Ordinal);

    /// <summary>The value of a number, or <see langword="null"/> when it was not stored.</summary>
    /// <param name="name">Name of the property.</param>
    public uint? Number(string name) => Numbers.TryGetValue(name, out uint value) ? value : null;

    /// <summary>The value of a string, or <see langword="null"/> when it was not stored.</summary>
    /// <param name="name">Name of the property.</param>
    public string? Text(string name) => Texts.TryGetValue(name, out string? value) ? value : null;

    /// <summary>The value of a size or position, or <see langword="null"/> when it was not stored.</summary>
    /// <param name="name">Name of the property.</param>
    public (int First, int Second)? Pair(string name) => Pairs.TryGetValue(name, out (int, int) value) ? value : null;
}

/// <summary>
/// Reads a control record by walking its property mask ([MS-OFORMS] 2.1.1.2).
/// </summary>
/// <remarks>
/// <para>
/// Every structure in the format has the same shape: a version, a size, a bit per property,
/// and then the values of exactly those properties whose bit is set, in bit order, split
/// across three blocks by how big they are. Nothing names itself, so a reader that
/// miscounts one property reads every later one from the wrong place — which is why the
/// walk is driven by one table per control rather than by hand-written field lists.
/// </para>
/// <para>
/// The record's own <c>cb</c> covers the mask and the first two blocks, so it is used as the
/// truth about where they end. A table that gets a size wrong then spoils only that record's
/// later values instead of derailing everything that follows it in the stream.
/// </para>
/// </remarks>
internal static class OFormsPropertyBag
{
    /// <summary>The bit of a string's length field that says the high bytes were dropped.</summary>
    private const uint CompressedFlag = 0x8000_0000;

    /// <summary>The class identifier a font carries when it is a standard font ([MS-OFORMS] 2.4.6).</summary>
    private static readonly Guid StdFont = new("0BE35203-8F91-11CE-9DE3-00AA004BB851");

    /// <summary>
    /// Reads a record, leaving the cursor at the start of the stream data that follows it.
    /// </summary>
    /// <param name="reader">Cursor positioned on the record's version number.</param>
    /// <param name="schema">The layout of the record.</param>
    public static OFormsValues Read(OFormsReader reader, OFormsSchema schema)
    {
        int start = reader.Position;
        reader.Skip(2);
        int declared = reader.UInt16();
        int end = Math.Min(start + 4 + declared, reader.End);

        ulong mask = schema.MaskBytes == 8 ? reader.UInt64() : reader.UInt32();
        var values = new OFormsValues(mask, end);

        var lengths = new Dictionary<string, (int Bytes, bool Compressed)>(StringComparer.Ordinal);
        var arrays = new Dictionary<string, int>(StringComparer.Ordinal);
        ReadDataBlock(reader, schema, mask, values, lengths, arrays);

        reader.Align(4);
        ReadExtraDataBlock(reader, schema, mask, values, lengths, arrays);

        // The record said how far its blocks reach; trust that over the walk above.
        reader.Position = end;
        return values;
    }

    /// <summary>Steps over the font and picture data that follows a record ([MS-OFORMS] 2.1.1.2.2).</summary>
    /// <param name="reader">Cursor positioned at the start of the stream data.</param>
    /// <param name="schema">The layout of the record.</param>
    /// <param name="mask">The record's property mask.</param>
    public static void SkipStreamData(OFormsReader reader, OFormsSchema schema, ulong mask)
    {
        for (int bit = 0; bit < schema.Properties.Length; bit++)
        {
            if ((mask & (1UL << bit)) == 0)
                continue;

            switch (schema.Properties[bit].Slot)
            {
                case OFormsSlot.Font:
                    SkipFont(reader);
                    break;
                case OFormsSlot.Picture:
                    SkipPicture(reader);
                    break;
                default:
                    break;
            }
        }
    }

    private static void ReadDataBlock(
        OFormsReader reader,
        OFormsSchema schema,
        ulong mask,
        OFormsValues values,
        Dictionary<string, (int Bytes, bool Compressed)> lengths,
        Dictionary<string, int> arrays)
    {
        for (int bit = 0; bit < schema.Properties.Length; bit++)
        {
            if ((mask & (1UL << bit)) == 0)
                continue;

            OFormsProperty property = schema.Properties[bit];
            switch (property.Slot)
            {
                case OFormsSlot.Data:
                    reader.Align(property.Size);
                    uint number = reader.Unsigned(property.Size);
                    if (property.Name.Length > 0)
                        values.Numbers[property.Name] = number;
                    break;

                case OFormsSlot.Text:
                    reader.Align(4);
                    uint size = reader.UInt32();
                    lengths[property.Name] = ((int)(size & ~CompressedFlag), (size & CompressedFlag) != 0);
                    break;

                case OFormsSlot.Array:
                    reader.Align(4);
                    arrays[property.Name] = (int)reader.UInt32();
                    break;

                case OFormsSlot.Font:
                case OFormsSlot.Picture:
                    // A marker of 0xFFFF stands in for the value, which lives in the stream data.
                    reader.Align(2);
                    reader.Skip(2);
                    break;

                default:
                    break;
            }
        }
    }

    private static void ReadExtraDataBlock(
        OFormsReader reader,
        OFormsSchema schema,
        ulong mask,
        OFormsValues values,
        Dictionary<string, (int Bytes, bool Compressed)> lengths,
        Dictionary<string, int> arrays)
    {
        for (int bit = 0; bit < schema.Properties.Length; bit++)
        {
            if ((mask & (1UL << bit)) == 0)
                continue;

            OFormsProperty property = schema.Properties[bit];
            switch (property.Slot)
            {
                case OFormsSlot.Text when lengths.TryGetValue(property.Name, out (int Bytes, bool Compressed) length):
                    values.Texts[property.Name] = reader.Text(length.Bytes, length.Compressed);
                    reader.Align(4);
                    break;

                case OFormsSlot.Pair:
                    reader.Align(4);
                    values.Pairs[property.Name] = (reader.Int32(), reader.Int32());
                    break;

                case OFormsSlot.Array when arrays.TryGetValue(property.Name, out int size):
                    reader.Skip(size);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Steps over a font, which is a class identifier that says whether what follows is a
    /// standard font record or a whole text-properties record ([MS-OFORMS] 2.4.7).
    /// </summary>
    private static void SkipFont(OFormsReader reader)
    {
        if (reader.ReadGuid() == StdFont)
        {
            // Version, charset, flags, weight and height, then a counted face name.
            reader.Skip(1 + 2 + 1 + 2 + 4);
            reader.Skip(reader.Byte());
            return;
        }

        // Otherwise a TextProps, which declares its own size after the two version bytes.
        reader.Skip(2);
        reader.Skip(reader.UInt16());
    }

    /// <summary>Steps over a picture, which is a class identifier and a sized blob ([MS-OFORMS] 2.4.8).</summary>
    private static void SkipPicture(OFormsReader reader)
    {
        reader.ReadGuid();
        reader.Skip(4);
        reader.Skip((int)reader.UInt32());
    }
}
