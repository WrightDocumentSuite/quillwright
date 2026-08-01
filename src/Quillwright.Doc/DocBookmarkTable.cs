using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>A bookmark and the range of characters it covers.</summary>
/// <param name="Name">The bookmark name.</param>
/// <param name="StartPosition">Character position the bookmark opens at.</param>
/// <param name="EndPosition">Character position the bookmark closes at.</param>
internal readonly record struct DocBookmark(string Name, int StartPosition, int EndPosition);

/// <summary>
/// Reads the bookmarks ([MS-DOC] 2.8.10 <c>Plcfbkf</c>, 2.8.11 <c>Plcfbkl</c> and 2.9.284
/// <c>SttbfBkmk</c>).
/// </summary>
/// <remarks>
/// The three structures are read together because none of them is meaningful alone: the
/// names are in one, the opening positions in another, and the closing positions in a third
/// that the opening records index into rather than parallel — which is what allows bookmarks
/// to overlap.
/// </remarks>
internal static class DocBookmarkTable
{
    private const int StartRecordBytes = 4;

    /// <summary>Reads every bookmark of the main story.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="names">Where the name table lives.</param>
    /// <param name="starts">Where the opening positions live.</param>
    /// <param name="ends">Where the closing positions live.</param>
    public static List<DocBookmark> Read(
        byte[] table,
        (int Offset, int Length) names,
        (int Offset, int Length) starts,
        (int Offset, int Length) ends)
    {
        var bookmarks = new List<DocBookmark>();
        List<string> text = DocStringTable.Read(table, names.Offset, names.Length);
        if (text.Count == 0 || starts.Length < 4 + StartRecordBytes)
            return bookmarks;

        List<int> closing = DocStoryReader.ReadPositions(table, ends.Offset, ends.Length);
        int count = (starts.Length - 4) / (4 + StartRecordBytes);

        for (int i = 0; i < count && i < text.Count; i++)
        {
            int from = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(starts.Offset + (i * 4)));
            int record = starts.Offset + ((count + 1) * 4) + (i * StartRecordBytes);
            int index = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(record));
            int to = index >= 0 && index < closing.Count ? closing[index] : from;

            bookmarks.Add(new DocBookmark(text[i], from, Math.Max(from, to)));
        }

        return bookmarks;
    }
}
