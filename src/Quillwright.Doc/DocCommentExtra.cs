using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>
/// What the binary format records about a comment beyond its author and its extent
/// (<c>ATRDPost10</c>, [MS-DOC] 2.9.6): when it was written, and where it sits in a thread.
/// </summary>
/// <param name="Date">When the comment was last written, or <see langword="null"/> when unset.</param>
/// <param name="Depth">How deep in the thread it is; zero for a comment that answers nothing.</param>
/// <param name="ParentDelta">
/// How many records away its parent is, counted from this one. Negative means earlier, and
/// zero means there is no parent.
/// </param>
internal readonly record struct DocCommentExtra(DateTimeOffset? Date, int Depth, int ParentDelta)
{
    /// <summary>Size of one record.</summary>
    public const int Size = 18;

    /// <summary>
    /// Reads the array that runs parallel to the comment records (<c>AtrdExtra</c>,
    /// [MS-DOC] 2.9.5). A file written before Word 2002 has none, and one written since may
    /// still be short, so the caller gets only as many records as are really there.
    /// </summary>
    /// <param name="table">The table stream.</param>
    /// <param name="region">Where the array lives, and how long it is.</param>
    /// <param name="count">How many comments the reference list holds.</param>
    public static IReadOnlyList<DocCommentExtra> Read(byte[] table, (int Offset, int Length) region, int count)
    {
        (int offset, int length) = region;
        if (count <= 0 || length < Size || offset < 0 || offset + length > table.Length)
            return [];

        var extras = new List<DocCommentExtra>(count);
        for (int i = 0; i < count && ((i + 1) * Size) <= length; i++)
        {
            ReadOnlySpan<byte> record = table.AsSpan(offset + (i * Size), Size);
            extras.Add(new DocCommentExtra(
                DocDateTime.Unpack(BinaryPrimitives.ReadUInt32LittleEndian(record)),
                BinaryPrimitives.ReadInt32LittleEndian(record[6..]),
                BinaryPrimitives.ReadInt32LittleEndian(record[10..])));
        }

        return extras;
    }
}
