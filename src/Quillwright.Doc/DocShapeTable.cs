using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>Where one shape is anchored, and how large it is ([MS-DOC] 2.9.253 <c>Spa</c>).</summary>
/// <param name="ShapeId">Identifier of the drawing this anchor stands for.</param>
/// <param name="Left">Left edge in twips, measured from the origin the flags name.</param>
/// <param name="Top">Top edge in twips, measured from the origin the flags name.</param>
/// <param name="Right">Right edge in twips.</param>
/// <param name="Bottom">Bottom edge in twips.</param>
/// <param name="Flags">The word that says which origin the rectangle is measured from and how text wraps.</param>
internal readonly record struct DocShapeAnchor(int ShapeId, int Left, int Top, int Right, int Bottom, ushort Flags)
{
    /// <summary>Width in twips, never negative.</summary>
    public int Width => Math.Max(0, Right - Left);

    /// <summary>Height in twips, never negative.</summary>
    public int Height => Math.Max(0, Bottom - Top);

    /// <summary>What the horizontal position is measured from (<c>bx</c>): margin, page or column.</summary>
    public int HorizontalOrigin => (Flags >> 1) & 0x3;

    /// <summary>What the vertical position is measured from (<c>by</c>): margin, page or paragraph.</summary>
    public int VerticalOrigin => (Flags >> 3) & 0x3;

    /// <summary>How the text flows round the shape (<c>wr</c>).</summary>
    public int Wrapping => (Flags >> 5) & 0xF;

    /// <summary>Which sides of the shape the text may flow down (<c>wrk</c>).</summary>
    public int WrappingSides => (Flags >> 9) & 0xF;

    /// <summary>Whether the shape sits behind the text rather than over it (<c>fBelowText</c>).</summary>
    public bool BehindText => (Flags & 0x4000) != 0;
}

/// <summary>
/// Where the shapes of a story are anchored ([MS-DOC] 2.8.37 <c>PlcfSpa</c>).
/// </summary>
/// <remarks>
/// The text stream marks a floating shape with a single character and says nothing more about
/// it. This list is what turns that character into a drawing: it pairs the character position
/// with the identifier of a shape in the document's drawing, and with the rectangle the shape
/// occupies.
/// </remarks>
internal sealed class DocShapeTable
{
    /// <summary>Bytes of one <c>Spa</c>: the identifier, the rectangle, the flags, and a field to ignore.</summary>
    private const int RecordBytes = 26;

    private readonly Dictionary<int, DocShapeAnchor> _byPosition;

    private DocShapeTable(Dictionary<int, DocShapeAnchor> byPosition) => _byPosition = byPosition;

    /// <summary>A story with no shapes in it.</summary>
    public static DocShapeTable Empty { get; } = new([]);

    /// <summary>Whether the story has any shapes at all.</summary>
    public bool IsEmpty => _byPosition.Count == 0;

    /// <summary>The shape anchored at a character position, or nothing when none is.</summary>
    /// <param name="position">Character position relative to the start of the story.</param>
    public DocShapeAnchor? At(int position) =>
        _byPosition.TryGetValue(position, out DocShapeAnchor anchor) ? anchor : null;

    /// <summary>Reads the anchors of one story.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="region">Where the list lives, and how long it is.</param>
    public static DocShapeTable Read(byte[] table, (int Offset, int Length) region)
    {
        (int offset, int length) = region;
        if (length < 4 + RecordBytes || offset < 0 || offset + length > table.Length)
            return Empty;

        // A PLC is n+1 positions followed by n records, so its length settles the count.
        int count = (length - 4) / (4 + RecordBytes);
        var anchors = new Dictionary<int, DocShapeAnchor>(count);
        int records = offset + ((count + 1) * 4);
        for (int i = 0; i < count; i++)
        {
            int position = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + (i * 4)));
            int at = records + (i * RecordBytes);
            if (at + RecordBytes > table.Length)
                break;

            anchors[position] = new DocShapeAnchor(
                BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at)),
                BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at + 4)),
                BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at + 8)),
                BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at + 12)),
                BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at + 16)),
                BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at + 20)));
        }

        return new DocShapeTable(anchors);
    }
}
