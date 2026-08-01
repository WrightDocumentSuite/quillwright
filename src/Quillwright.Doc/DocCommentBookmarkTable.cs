using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>
/// Reads the bookmarks that record what each comment applies to ([MS-DOC] 2.9.281
/// <c>SttbfAtnBkmk</c> with <c>PlcfAtnBkf</c> and <c>PlcfAtnBkl</c>).
/// </summary>
/// <remarks>
/// A comment reference says where a comment is anchored, not what it is about. The extent is
/// a separate bookmark whose tag the comment's own record names, so recovering the commented
/// range means joining three structures to a fourth.
/// </remarks>
internal static class DocCommentBookmarkTable
{
    private const int StartRecordBytes = 4;
    private const int IdentityBytes = 10;

    /// <summary>Reads the commented ranges, keyed by the tag a comment's record refers to.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="identities">Where the bookmark identities live.</param>
    /// <param name="starts">Where the opening positions live.</param>
    /// <param name="ends">Where the closing positions live.</param>
    public static Dictionary<int, (int Start, int End)> Read(
        byte[] table,
        (int Offset, int Length) identities,
        (int Offset, int Length) starts,
        (int Offset, int Length) ends)
    {
        var ranges = new Dictionary<int, (int Start, int End)>();
        List<int> tags = Tags(table, identities);
        if (tags.Count == 0 || starts.Length < 4 + StartRecordBytes)
            return ranges;

        List<int> closing = DocStoryReader.ReadPositions(table, ends.Offset, ends.Length);
        int count = (starts.Length - 4) / (4 + StartRecordBytes);

        for (int i = 0; i < count && i < tags.Count; i++)
        {
            int from = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(starts.Offset + (i * 4)));
            int record = starts.Offset + ((count + 1) * 4) + (i * StartRecordBytes);
            int index = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(record));
            int to = index >= 0 && index < closing.Count ? closing[index] : from;

            ranges[tags[i]] = (from, Math.Max(from, to));
        }

        return ranges;
    }

    /// <summary>
    /// The tag of each bookmark. The strings of this table are all empty; the tags are in the
    /// block of data that follows each of them.
    /// </summary>
    private static List<int> Tags(byte[] table, (int Offset, int Length) region)
    {
        var tags = new List<int>();
        if (region.Length < 6 || region.Offset + region.Length > table.Length)
            return tags;

        int position = region.Offset;
        bool unicode = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position)) == 0xFFFF;
        if (unicode)
            position += 2;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position));
        int extra = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position + 2));
        position += 4;
        if (extra < IdentityBytes)
            return tags;

        int limit = region.Offset + region.Length;
        for (int i = 0; i < count; i++)
        {
            int characters = unicode
                ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position))
                : table[position];
            position += unicode ? 2 : 1;

            int bytes = unicode ? characters * 2 : characters;
            if (position + bytes + extra > limit)
                break;

            tags.Add(BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(position + bytes + 2)));
            position += bytes + extra;
        }

        return tags;
    }
}
