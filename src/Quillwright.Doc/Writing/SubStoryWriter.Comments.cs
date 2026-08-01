using System.Buffers.Binary;
using Quillwright.Model;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Writes what a comment needs beyond its text: who left it, and which words it is about.
/// </summary>
internal static partial class SubStoryWriter
{
    /// <summary>
    /// Builds one comment's record: the author's initials, an index into the list of author
    /// names, and the bookmark that says what the comment applies to.
    /// </summary>
    private static byte[] CommentRecord(DocWriteContext context, int index)
    {
        var record = new byte[30];
        NoteSpan anchor = context.Comments[index];
        Comment? comment = context.Document.Comments.FirstOrDefault(c => c.Id == anchor.Id);

        string initials = comment?.Initials ?? string.Empty;
        if (initials.Length > 9)
            initials = initials[..9];
        BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)initials.Length);
        System.Text.Encoding.Unicode.GetBytes(initials, record.AsSpan(2, 18));

        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(20), (ushort)Author(context, comment));

        // A comment with no extent of its own is marked as having no bookmark; otherwise it
        // names the bookmark that covers the commented text.
        int tag = Ranges(context).FindIndex(range => range.Id == anchor.Id);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(26), tag);
        return record;
    }

    /// <summary>
    /// Builds the array that runs parallel to the comment records and holds what they have no
    /// room for: when each comment was written, and what it answers (<c>AtrdExtra</c>,
    /// [MS-DOC] 2.9.5).
    /// </summary>
    /// <remarks>
    /// The date is the one the comments part of a <c>.docx</c> carries rather than the UTC one
    /// beside it, because this field has no time zone and Word fills it — like <c>w:date</c> —
    /// with the wall clock the author saw.
    /// </remarks>
    private static byte[] CommentExtras(DocWriteContext context)
    {
        if (context.Comments.Count == 0)
            return [];

        var bytes = new byte[context.Comments.Count * DocCommentExtra.Size];
        for (int i = 0; i < context.Comments.Count; i++)
        {
            Comment? comment = context.Document.Comments.FirstOrDefault(c => c.Id == context.Comments[i].Id);
            Span<byte> record = bytes.AsSpan(i * DocCommentExtra.Size, DocCommentExtra.Size);

            BinaryPrimitives.WriteUInt32LittleEndian(record, DocDateTime.Pack(comment?.Date ?? comment?.DateUtc));
            (int depth, int parent) = Thread(context, comment, i);
            BinaryPrimitives.WriteInt32LittleEndian(record[6..], depth);
            BinaryPrimitives.WriteInt32LittleEndian(record[10..], parent);
        }

        return bytes;
    }

    /// <summary>
    /// How deep a comment sits in its thread and how far away its parent's record is, counted
    /// in records from its own.
    /// </summary>
    /// <remarks>
    /// The chain is followed only as far as comments that are actually being written: a depth
    /// of more than zero with no parent to point at is a contradiction the format forbids, so
    /// a comment whose parent was dropped is written as a comment of its own.
    /// </remarks>
    private static (int Depth, int ParentDelta) Thread(DocWriteContext context, Comment? comment, int index)
    {
        if (comment is null)
            return (0, 0);

        var seen = new HashSet<int> { comment.Id };
        Comment current = comment;
        int depth = 0;
        int parentIndex = -1;

        while (current.ParentId is { } parentId && seen.Add(parentId))
        {
            if (context.Document.Comments.FirstOrDefault(c => c.Id == parentId) is not { } parent)
                break;

            int at = context.Comments.FindIndex(span => span.Id == parentId);
            if (at < 0 || at == index)
                break;

            if (depth == 0)
                parentIndex = at;
            depth++;
            current = parent;
        }

        return depth == 0 ? (0, 0) : (depth, parentIndex - index);
    }

    /// <summary>
    /// The commented ranges that can be written, in the order their bookmarks are.
    /// </summary>
    /// <remarks>
    /// A commented range has to end where its reference character sits: that is how the
    /// format ties the two together, and Word rejects a file where they disagree. A range
    /// that does not reach its reference is stretched to it, and one with no reference at all
    /// is dropped.
    /// </remarks>
    private static List<CommentRangeSpan> Ranges(DocWriteContext context)
    {
        var ranges = new List<CommentRangeSpan>();
        foreach (CommentRangeSpan range in context.CommentRanges)
        {
            int reference = context.Comments.FirstOrDefault(c => c.Id == range.Id).ReferencePosition;
            if (reference <= range.StartPosition)
                continue;
            ranges.Add(range with { EndPosition = reference });
        }

        ranges.Sort(static (left, right) => left.StartPosition.CompareTo(right.StartPosition));
        return ranges;
    }

    /// <summary>
    /// Writes the bookmarks that record what each comment applies to. They are ordinary
    /// bookmarks with no names — the string table exists only to carry the tag that ties each
    /// one back to its comment.
    /// </summary>
    private static void WriteCommentBookmarks(DocWriteContext context, Action<int, byte[]> add)
    {
        List<CommentRangeSpan> ranges = Ranges(context);
        if (ranges.Count == 0)
            return;

        // The closing positions are a list of their own that the opening records index into,
        // which is what lets commented ranges overlap. Both lists must ascend.
        int[] byEnd = [.. Enumerable.Range(0, ranges.Count).OrderBy(i => ranges[i].EndPosition)];
        var order = new int[ranges.Count];
        for (int i = 0; i < byEnd.Length; i++)
            order[byEnd[i]] = i;

        // Both lists close with the same position, one past the furthest a bookmark of this
        // kind may reach. Its value is ignored, but it has to leave both lists ascending.
        int limit = ranges.Max(static range => Math.Max(range.StartPosition, range.EndPosition)) + 1;

        var names = new List<(string Value, byte[] Extra)>(ranges.Count);
        var starts = new PlcBuilder(4);
        Span<byte> record = stackalloc byte[4];

        for (int i = 0; i < ranges.Count; i++)
        {
            var identity = new byte[10];
            BinaryPrimitives.WriteUInt16LittleEndian(identity, 0x0100);
            BinaryPrimitives.WriteInt32LittleEndian(identity.AsSpan(2), i);
            BinaryPrimitives.WriteInt32LittleEndian(identity.AsSpan(6), -1);
            names.Add((string.Empty, identity));

            BinaryPrimitives.WriteUInt16LittleEndian(record, (ushort)order[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(record[2..], 0);
            int to = i + 1 < ranges.Count ? ranges[i + 1].StartPosition : limit;
            starts.Add(ranges[i].StartPosition, to, record);
        }

        List<int> ends = [.. byEnd.Select(i => ranges[i].EndPosition)];
        ends.Add(limit);

        add(FibBuilder.Pair.CommentBookmarks, PlcBuilder.StringTable(names));
        add(FibBuilder.Pair.CommentBookmarkStarts, starts.ToArray());
        add(FibBuilder.Pair.CommentBookmarkEnds, PlcBuilder.Positions(ends));
    }

    /// <summary>
    /// The author names, as the bare array of counted strings this one table is — not the
    /// string table with a header that nearly every other name list in the format uses.
    /// </summary>
    private static byte[] Authors(DocWriteContext context)
    {
        List<string> names = Names(context);
        if (names.Count == 0)
            return [];

        var bytes = new List<byte>(names.Count * 24);
        var count = new byte[2];
        foreach (string name in names)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(count, (ushort)name.Length);
            bytes.AddRange(count);
            bytes.AddRange(System.Text.Encoding.Unicode.GetBytes(name));
        }

        return [.. bytes];
    }

    private static int Author(DocWriteContext context, Comment? comment) =>
        comment is null ? 0 : Math.Max(0, Names(context).IndexOf(comment.Author ?? "Author"));

    /// <summary>The distinct author names, in the order they first appear.</summary>
    private static List<string> Names(DocWriteContext context) =>
        [.. context.Document.Comments.Select(static c => c.Author ?? "Author").Distinct(StringComparer.Ordinal)];
}
