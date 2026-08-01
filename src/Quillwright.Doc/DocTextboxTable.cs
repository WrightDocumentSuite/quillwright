using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>
/// Which stretch of a text-box story belongs to which shape ([MS-DOC] 2.8.34
/// <c>PlcftxbxTxt</c>, 2.9.106 <c>FTXBXS</c>).
/// </summary>
/// <remarks>
/// The words of every text box in a document are laid end to end in a story of their own, and
/// nothing in that story says where one box ends and the next begins. This list does: each
/// entry bounds one box's text and names the shape it is drawn in. Entries marked reusable are
/// spares Word keeps for the next text box somebody draws, and hold no text.
/// </remarks>
internal sealed class DocTextboxTable
{
    /// <summary>Bytes of one <c>FTXBXS</c>.</summary>
    private const int RecordBytes = 22;

    private readonly Dictionary<int, (int Start, int End)> _byShape;

    private DocTextboxTable(Dictionary<int, (int Start, int End)> byShape) => _byShape = byShape;

    /// <summary>A story with no text boxes in it.</summary>
    public static DocTextboxTable Empty { get; } = new([]);

    /// <summary>Every text box of the story: which shape draws it, and what it bounds.</summary>
    public IEnumerable<(int ShapeId, int Start, int End)> Entries =>
        _byShape.Select(static entry => (entry.Key, entry.Value.Start, entry.Value.End));

    /// <summary>The stretch of the story a shape's text occupies, or nothing when it has none.</summary>
    /// <param name="shapeId">Identifier of the shape.</param>
    public (int Start, int End)? For(int shapeId) =>
        _byShape.TryGetValue(shapeId, out (int Start, int End) range) ? range : null;

    /// <summary>Reads the text-box list of one story.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="region">Where the list lives, and how long it is.</param>
    public static DocTextboxTable Read(byte[] table, (int Offset, int Length) region)
    {
        (int offset, int length) = region;
        if (length < 4 + RecordBytes || offset < 0 || offset + length > table.Length)
            return Empty;

        int count = (length - 4) / (4 + RecordBytes);
        var boxes = new Dictionary<int, (int Start, int End)>(count);
        int records = offset + ((count + 1) * 4);
        for (int i = 0; i < count; i++)
        {
            int at = records + (i * RecordBytes);
            if (at + RecordBytes > table.Length)
                break;

            // The last entry is always a spare, whatever its flag says, and a spare bounds no
            // text worth reading.
            bool reusable = i == count - 1 || (BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at + 8)) & 1) != 0;
            int shapeId = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(at + 14));
            int start = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + (i * 4)));
            int end = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + ((i + 1) * 4)));
            if (!reusable && shapeId != 0 && end > start)
                boxes[shapeId] = (start, end);
        }

        return new DocTextboxTable(boxes);
    }
}
