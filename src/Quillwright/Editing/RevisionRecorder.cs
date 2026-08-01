using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Quillwright.Formats;
using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Xml;

namespace Quillwright.Editing;

/// <summary>
/// Turns an edit into the mark that records it, while a
/// <see cref="RevisionTracking"/> session is open (ISO/IEC 29500-1 §17.13.5.
/// </summary>
/// <remarks>
/// <para>
/// Recording an insertion is easy: the text goes in and a <c>w:ins</c> covers it. Recording a
/// deletion is the interesting half, because nothing is deleted — the text stays exactly where
/// it is, its runs are retagged so they write as <c>w:delText</c>, and a <c>w:del</c> is laid
/// over them. A reader that has never heard of tracked changes therefore still sees the text
/// in the right place, and accepting the change later is what actually removes it.
/// </para>
/// <para>
/// One case is not a deletion at all: text this same session inserted and the caller has now
/// changed their mind about. Marking it deleted would leave an insertion and a deletion of the
/// same characters, which is noise; it is removed outright instead.
/// </para>
/// </remarks>
internal static class RevisionRecorder
{
    /// <summary>Records a replacement: what goes away is marked, what arrives is wrapped.</summary>
    /// <param name="paragraph">The paragraph being edited.</param>
    /// <param name="tracking">The open session.</param>
    /// <param name="start">Offset of the first character being replaced.</param>
    /// <param name="count">How many characters are being replaced.</param>
    /// <param name="text">The replacement.</param>
    /// <param name="format">Formatting of the replacement.</param>
    public static void Replace(
        Paragraph paragraph,
        RevisionTracking tracking,
        int start,
        int count,
        scoped ReadOnlySpan<char> text,
        RunFormat? format)
    {
        // Word writes the deletion first and the insertion after it, which is what a reader
        // sees as "this became that".
        int retained = count > 0 ? Remove(paragraph, tracking, start, count) : 0;
        if (text.IsEmpty)
            return;

        int at = start + retained;
        paragraph.ReplaceTextCore(at, 0, text, format);

        // The text lands next to the stretch just marked deleted, so it would otherwise
        // inherit that run's tag and write itself out as removed.
        paragraph.SetDeletedRuns(at, text.Length, deleted: false);
        Inserted(paragraph, tracking, at, text.Length);
    }

    /// <summary>Records that a stretch of text was added.</summary>
    public static void Inserted(Paragraph paragraph, RevisionTracking tracking, int start, int length)
    {
        if (length <= 0)
            return;

        Uncover(paragraph, tracking, start, length);
        if (!Extend(paragraph, RevisionKind.Inserted, tracking, start, length))
            paragraph.AddRange(tracking.Create(RevisionKind.Inserted), start, length);
    }

    /// <summary>
    /// Takes new text out from under a deletion that grew over it. Splicing inside a wrapper
    /// widens the wrapper, which is what a hyperlink wants and a deletion does not: a
    /// <c>w:del</c> holding text that reads as present is a file Word refuses.
    /// </summary>
    private static void Uncover(Paragraph paragraph, RevisionTracking tracking, int start, int length)
    {
        if (paragraph.RangeList is not { } ranges)
            return;

        var split = new List<(int Start, int Length, Revision Source)>();
        Span<AnchoredRange> span = CollectionsMarshal.AsSpan(ranges);
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].Range is not Revision revision ||
                revision.Kind is not (RevisionKind.Deleted or RevisionKind.MovedFrom) ||
                span[i].Start > start || span[i].End < start + length)
            {
                continue;
            }

            int head = start - span[i].Start;
            int tail = span[i].End - (start + length);
            if (head == 0)
            {
                span[i].Start = start + length;
                span[i].Length = tail;
                continue;
            }

            span[i].Length = head;
            if (tail > 0)
                split.Add((start + length, tail, revision));
        }

        foreach ((int from, int count, Revision source) in split)
        {
            // The far half is the same change by the same author, but a second element in the
            // file, so it needs an identifier of its own.
            paragraph.AddRange(
                new Revision
                {
                    Kind = source.Kind,
                    Id = tracking.NextId(),
                    Author = source.Author,
                    Date = source.Date,
                    MoveName = source.MoveName,
                },
                from,
                count);
        }
    }

    /// <summary>Records that a stretch of text was removed, leaving it in place.</summary>
    public static void Deleted(Paragraph paragraph, RevisionTracking tracking, int start, int length)
    {
        if (length <= 0)
            return;

        paragraph.SetDeletedRuns(start, length, deleted: true);
        if (!Extend(paragraph, RevisionKind.Deleted, tracking, start, length))
            paragraph.AddRange(tracking.Create(RevisionKind.Deleted), start, length);
    }

    /// <summary>Records that a paragraph was added, by marking the paragraph mark itself.</summary>
    public static void Added(Paragraph paragraph, RevisionTracking tracking)
    {
        tracking.Added(paragraph);
        paragraph.MarkFormat = paragraph.MarkFormat with { MarkRevisionXml = Mark(RevisionKind.Inserted, tracking) };
    }

    /// <summary>The mark that records a paragraph as added or removed (<c>w:pPr/w:rPr/w:ins</c>).</summary>
    public static string Mark(RevisionKind kind, RevisionTracking tracking) =>
        $"<w:{(kind == RevisionKind.Deleted ? "del" : "ins")}{Stamp(tracking)}/>";

    /// <summary>
    /// The record of what a run's formatting was before it was changed (<c>w:rPrChange</c>).
    /// The old formatting is written out as it stood, minus the two things a
    /// <c>w:rPrChange</c> may not carry: another change record, and a paragraph mark's own
    /// revision.
    /// </summary>
    public static string FormatChange(RunFormat original, RevisionTracking tracking)
    {
        RunFormat previous = original with { ChangeXml = null, MarkRevisionXml = null };
        string body = Utf8XmlWriter.Render(writer => RunFormatWriter.Write(writer, previous));
        return $"<w:rPrChange{Stamp(tracking)}>{(body.Length == 0 ? "<w:rPr/>" : body)}</w:rPrChange>";
    }

    /// <summary>
    /// Marks a stretch as removed and returns how many of its characters stayed in place. The
    /// stretch is taken apart from the end, so that removing part of it leaves the offsets of
    /// the rest where they were.
    /// </summary>
    private static int Remove(Paragraph paragraph, RevisionTracking tracking, int start, int count)
    {
        Span<Fate> fates = count <= 256 ? stackalloc Fate[count] : new Fate[count];
        Classify(paragraph, tracking, start, fates);

        int retained = 0;
        int cursor = start + count;
        while (cursor > start)
        {
            Fate fate = fates[cursor - 1 - start];
            int from = cursor;
            while (from > start && fates[from - 1 - start] == fate)
                from--;

            switch (fate)
            {
                case Fate.Undo:
                    paragraph.ReplaceTextCore(from, cursor - from, default, null);
                    Prune(paragraph, tracking);
                    break;
                case Fate.Gone:
                    retained += cursor - from;
                    break;
                default:
                    Deleted(paragraph, tracking, from, cursor - from);
                    retained += cursor - from;
                    break;
            }

            cursor = from;
        }

        return retained;
    }

    /// <summary>
    /// Drops the marks this session made that no longer cover anything. Taking back an
    /// insertion leaves the wrapper behind at zero width, and an insertion of nothing is a
    /// claim about the document that is not true.
    /// </summary>
    private static void Prune(Paragraph paragraph, RevisionTracking tracking)
    {
        if (paragraph.RangeList is not { } ranges)
            return;

        for (int i = ranges.Count - 1; i >= 0; i--)
        {
            if (ranges[i].Length == 0 && ranges[i].Range is Revision revision && tracking.Recorded(revision))
                ranges.RemoveAt(i);
        }
    }

    /// <summary>Decides, character by character, what removing the stretch should do to it.</summary>
    private static void Classify(Paragraph paragraph, RevisionTracking tracking, int start, Span<Fate> fates)
    {
        for (int i = 0; i < fates.Length; i++)
            fates[i] = paragraph.IsDeletedAt(start + i) ? Fate.Gone : Fate.Mark;

        if (paragraph.RangeList is not { } ranges)
            return;

        foreach (AnchoredRange anchored in ranges)
        {
            if (anchored.Range is not Revision revision ||
                revision.Kind != RevisionKind.Inserted ||
                !tracking.Recorded(revision))
            {
                continue;
            }

            int from = Math.Max(anchored.Start, start) - start;
            int to = Math.Min(anchored.End, start + fates.Length) - start;
            for (int i = from; i < to; i++)
            {
                if (fates[i] == Fate.Mark)
                    fates[i] = Fate.Undo;
            }
        }
    }

    /// <summary>
    /// Grows a revision this session already made rather than adding a second one beside it,
    /// so that typing a word letter by letter is one insertion and not eight.
    /// </summary>
    private static bool Extend(Paragraph paragraph, RevisionKind kind, RevisionTracking tracking, int start, int length)
    {
        if (paragraph.RangeList is not { } ranges)
            return false;

        Span<AnchoredRange> span = CollectionsMarshal.AsSpan(ranges);
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].Range is not Revision revision || revision.Kind != kind || !tracking.Recorded(revision))
                continue;

            // Splicing inside a range already widened it, so covering means there is nothing
            // left to do rather than something to add.
            if (span[i].Start <= start && span[i].End >= start + length)
                return true;

            if (span[i].End == start)
            {
                span[i].Length += length;
                return true;
            }

            if (span[i].Start == start + length)
            {
                span[i].Start = start;
                span[i].Length += length;
                return true;
            }
        }

        return false;
    }

    /// <summary>The identity every recorded change carries: who, when, and which change.</summary>
    private static string Stamp(RevisionTracking tracking)
    {
        var builder = new StringBuilder(64);
        builder.Append(" w:id=\"").Append(tracking.NextId().ToString(CultureInfo.InvariantCulture)).Append('"');
        builder.Append(" w:author=\"").Append(SecurityElement.Escape(tracking.Author)).Append('"');
        if (tracking.Date is { } date)
        {
            builder.Append(" w:date=\"")
                .Append(date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                .Append('"');
        }

        return builder.ToString();
    }

    /// <summary>What removing one character of a stretch should do to it.</summary>
    private enum Fate : byte
    {
        /// <summary>Leave it where it is and record that it went.</summary>
        Mark = 0,

        /// <summary>It is already recorded as gone; leave it alone.</summary>
        Gone,

        /// <summary>This session put it there, so take it away for real.</summary>
        Undo,
    }
}
