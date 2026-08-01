using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>
/// Rebuilds the stories that follow the main text: notes, comments, headers and footers.
/// </summary>
internal static partial class DocConverter
{
    private const int SeparatorStories = 6;
    private const int StoriesPerSection = 6;

    /// <summary>Rebuilds the footnotes or endnotes and their bodies.</summary>
    private static void ReadNotes(WordDocument document, DocReadContext context, bool isEndnote)
    {
        DocStoryReader anchors = isEndnote ? context.Endnotes : context.Footnotes;
        if (anchors.Bodies.Count == 0)
            return;

        int storyStart = isEndnote
            ? context.Fib.MainTextLength + context.Fib.FootnoteTextLength + context.Fib.HeaderTextLength + context.Fib.CommentTextLength
            : context.Fib.MainTextLength;

        List<Note> target = isEndnote ? document.EndnoteList : document.FootnoteList;
        for (int i = 0; i < anchors.Bodies.Count; i++)
        {
            (int from, int to) = anchors.Bodies[i];
            var note = new Note(document, isEndnote) { Id = i + 1 };
            foreach (DocParagraph entry in ReadParagraphs(context, storyStart + from, storyStart + to))
                note.Blocks.Add(entry.Paragraph);

            if (note.Blocks.Count == 0)
                note.AddParagraph();
            target.Add(note);
        }
    }

    /// <summary>Rebuilds the comments and their bodies.</summary>
    private static void ReadComments(WordDocument document, DocReadContext context)
    {
        if (context.Comments.Bodies.Count == 0)
            return;

        int storyStart = context.Fib.MainTextLength + context.Fib.FootnoteTextLength + context.Fib.HeaderTextLength;
        for (int i = 0; i < context.Comments.Bodies.Count; i++)
        {
            (int from, int to) = context.Comments.Bodies[i];
            var comment = new Comment(document)
            {
                Id = i + 1,
                Author = context.CommentAuthors.ElementAtOrDefault(Author(context, i)),
                Initials = Initials(context, i),
                Date = Extra(context, i)?.Date,
                ParentId = Parent(context, i),
            };

            foreach (DocParagraph entry in ReadParagraphs(context, storyStart + from, storyStart + to))
                comment.Blocks.Add(entry.Paragraph);

            if (comment.Blocks.Count == 0)
                comment.AddParagraph();
            document.CommentList.Add(comment);
        }
    }

    /// <summary>
    /// Appends the text of any text box whose anchor was never found, as ordinary paragraphs
    /// at the end of the document.
    /// </summary>
    /// <remarks>
    /// A box reached through its anchor keeps its place in the text and is not touched here.
    /// This is for the ones nothing points at — a chained box, or a file whose shape table
    /// does not line up. The alternative for those is losing the words entirely: they would
    /// not appear in <c>GetText</c>, would not be found by a search, and would not be in the
    /// saved package. Flattened, they are in the document and the warning says they moved.
    /// </remarks>
    private static void ReadStrandedTextboxes(WordDocument document, DocReadContext context)
    {
        FileInformationBlock fib = context.Fib;
        int textbox = fib.MainTextLength + fib.FootnoteTextLength + fib.HeaderTextLength +
            fib.CommentTextLength + fib.EndnoteTextLength;

        List<Paragraph> stranded =
        [
            .. Stranded(context, context.Textboxes, textbox),
            .. Stranded(context, context.HeaderTextboxes, textbox + fib.TextboxTextLength),
        ];

        if (stranded.Count == 0)
            return;

        context.Warn(
            WarningCode.PreservedVerbatim,
            "Text-box content nothing in the text points at was flattened to paragraphs at the end of the document.");

        Section last = document.Sections[^1];
        foreach (Paragraph paragraph in stranded)
            last.Blocks.Add(paragraph);
    }

    private static IEnumerable<Paragraph> Stranded(DocReadContext context, DocTextboxTable boxes, int storyStart)
    {
        foreach ((int shapeId, int start, int end) in boxes.Entries)
        {
            if (context.HasTextbox(shapeId))
                continue;

            foreach (DocParagraph entry in ReadParagraphs(context, storyStart + start, storyStart + end))
            {
                if (entry.Paragraph.TextLength > 0)
                    yield return entry.Paragraph;
            }
        }
    }

    /// <summary>The date and threading record of a comment, when the file carries one.</summary>
    private static DocCommentExtra? Extra(DocReadContext context, int index) =>
        index < context.CommentExtras.Count ? context.CommentExtras[index] : null;

    /// <summary>
    /// Which comment a comment answers. The record names its parent by how many records away
    /// it is rather than by an identifier, and comments are numbered by their position, so the
    /// two amount to the same arithmetic.
    /// </summary>
    private static int? Parent(DocReadContext context, int index)
    {
        if (Extra(context, index) is not { Depth: > 0 } extra || extra.ParentDelta == 0)
            return null;

        int parent = index + extra.ParentDelta;
        return parent >= 0 && parent < context.Comments.Bodies.Count && parent != index ? parent + 1 : null;
    }

    /// <summary>
    /// Rebuilds the headers and footers. The header document is a fixed sequence — six note
    /// separators, then six per section — so a story's meaning comes from its position in
    /// the list and nothing else.
    /// </summary>
    private static void ReadHeaders(WordDocument document, DocReadContext context)
    {
        IReadOnlyList<int> boundaries = context.HeaderStories;
        if (boundaries.Count <= SeparatorStories + StoriesPerSection)
            return;

        int storyStart = context.Fib.MainTextLength + context.Fib.FootnoteTextLength;
        for (int index = SeparatorStories; index + 1 < boundaries.Count - 1; index++)
        {
            int section = (index - SeparatorStories) / StoriesPerSection;
            if (section >= document.Sections.Count)
                break;

            int from = boundaries[index];
            int to = boundaries[index + 1];
            if (to <= from)
                continue;

            // The last mark of a story is a guard that separates it from the next, and is
            // not part of the content.
            List<DocParagraph> body = ReadParagraphs(context, storyStart + from, storyStart + to - 1);
            if (body.Count == 0)
                continue;

            HeaderFooter part = Slot(document.Sections[section], (index - SeparatorStories) % StoriesPerSection);
            part.Blocks.Clear();
            foreach (DocParagraph entry in body)
                part.Blocks.Add(entry.Paragraph);
        }
    }

    /// <summary>Which of the author names a comment's record points at.</summary>
    private static int Author(DocReadContext context, int index) =>
        context.Comments.Records.ElementAtOrDefault(index) is { Length: >= 22 } record
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(20))
            : index;

    /// <summary>The initials a comment's record carries, shown in the margin beside it.</summary>
    private static string? Initials(DocReadContext context, int index)
    {
        if (context.Comments.Records.ElementAtOrDefault(index) is not { Length: >= 20 } record)
            return null;

        int characters = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record);
        return characters is <= 0 or > 9
            ? null
            : System.Text.Encoding.Unicode.GetString(record, 2, characters * 2);
    }

    /// <summary>
    /// Puts back the marks that say what each comment applies to. The comment's own record
    /// names a bookmark, and that bookmark is the commented range.
    /// </summary>
    private static void ReadCommentRanges(DocReadContext context, List<DocParagraph> paragraphs)
    {
        if (context.CommentBookmarks.Count == 0 || paragraphs.Count == 0)
            return;

        for (int i = 0; i < context.Comments.Records.Count; i++)
        {
            byte[] record = context.Comments.Records[i];
            if (record.Length < 30)
                continue;

            int tag = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(26));
            if (!context.CommentBookmarks.TryGetValue(tag, out (int Start, int End) range))
                continue;

            Place(paragraphs, range.Start, new CommentRangeStart { Id = i + 1 });
            Place(paragraphs, range.End, new CommentRangeEnd { Id = i + 1 });
        }
    }

    /// <summary>
    /// Puts the bookmarks back as the pair of zero-width marks the model represents them
    /// with, at the paragraphs whose text they fall inside.
    /// </summary>
    private static void ReadBookmarks(WordDocument document, DocReadContext context, List<DocParagraph> paragraphs)
    {
        IReadOnlyList<DocBookmark> bookmarks = context.Bookmarks;
        if (bookmarks.Count == 0 || paragraphs.Count == 0)
            return;

        for (int i = 0; i < bookmarks.Count; i++)
        {
            Place(paragraphs, bookmarks[i].StartPosition, new BookmarkStart { Id = i, Name = bookmarks[i].Name });
            Place(paragraphs, bookmarks[i].EndPosition, new BookmarkEnd { Id = i });
        }

        _ = document;
    }

    /// <summary>Adds a mark at a character position, in whichever paragraph contains it.</summary>
    private static void Place(List<DocParagraph> paragraphs, int position, InlineMark mark)
    {
        int start = 0;
        foreach (DocParagraph entry in paragraphs)
        {
            if (position < entry.EndPosition)
            {
                // The stored position counts the characters the model drops, so it is
                // clamped rather than trusted.
                entry.Paragraph.AddMark(mark, Math.Clamp(position - start, 0, entry.Paragraph.TextLength));
                return;
            }

            start = entry.EndPosition;
        }

        paragraphs[^1].Paragraph.AddMark(mark, paragraphs[^1].Paragraph.TextLength);
    }

    private static HeaderFooter Slot(Section section, int position) => position switch
    {
        0 => section.Headers.GetOrCreate(HeaderFooterKind.Even),
        1 => section.Headers.GetOrCreate(),
        2 => section.Footers.GetOrCreate(HeaderFooterKind.Even),
        3 => section.Footers.GetOrCreate(),
        4 => section.Headers.GetOrCreate(HeaderFooterKind.First),
        _ => section.Footers.GetOrCreate(HeaderFooterKind.First),
    };
}
