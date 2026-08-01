using System.Buffers.Binary;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Writes the stories that follow the main text — footnotes, headers and footers, comments
/// and endnotes — and the tables that locate them.
/// </summary>
/// <remarks>
/// <para>
/// These are not separate streams. They continue the same run of characters the main text
/// occupies, one after another in the order the header counts them, and each is found only
/// by the character position it starts at. A reader that gets the order wrong reads a
/// footnote as a header.
/// </para>
/// <para>
/// The header document is the strictest of them: it always holds six stories for note
/// separators followed by exactly six per section, whether or not a section has headers, and
/// every non-empty one ends with a paragraph mark that is not part of its content.
/// </para>
/// </remarks>
internal static partial class SubStoryWriter
{
    private const int SeparatorStories = 6;
    private const int StoriesPerSection = 6;

    /// <summary>Appends every story that follows the main text.</summary>
    public static void Write(DocWriteContext context, StoryAssembler story, WordDocument document)
    {
        int start = story.Position;

        int footnotes = Document(story, () => WriteNotes(story, document.Footnotes, context.Footnotes));
        int headers = Document(story, () => WriteHeaders(context, story, document));
        int comments = Document(story, () => WriteComments(context, story, document));
        int endnotes = Document(story, () => WriteNotes(story, document.Endnotes, context.Endnotes));

        // Beyond all of them the text ends with one more paragraph mark, which belongs to no
        // document at all and is counted by none of them.
        if (story.Position > start)
            story.WriteGuardMark();

        story.CloseStories(footnotes, headers, comments, endnotes);
    }

    /// <summary>
    /// Writes one sub-document and returns its length. A sub-document that has any content
    /// ends with a paragraph mark of its own, one past the last story it holds, which is
    /// what the tables that locate those stories are defined against.
    /// </summary>
    private static int Document(StoryAssembler story, Action write)
    {
        int start = story.Position;
        write();
        if (story.Position == start)
            return 0;

        story.WriteGuardMark();
        return story.Position - start;
    }

    /// <summary>Writes the tables that say where each story begins.</summary>
    public static void WriteTables(DocWriteContext context, StoryAssembler story, FibBuilder fib, Action<int, byte[]> add)
    {
        _ = fib;
        add(FibBuilder.Pair.FootnoteReferences, References(context.Footnotes, 2, i => NoteRecord(context.Footnotes, i)));
        add(FibBuilder.Pair.FootnoteText, Bodies(context.Footnotes, story.MainLength, story.FootnoteLength));

        add(FibBuilder.Pair.Headers, PlcBuilder.Positions(context.HeaderStories));

        add(FibBuilder.Pair.CommentReferences, References(context.Comments, 30, i => CommentRecord(context, i)));
        add(
            FibBuilder.Pair.CommentText,
            Bodies(context.Comments, story.MainLength + story.FootnoteLength + story.HeaderLength, story.CommentLength));
        add(FibBuilder.Pair.CommentAuthors, Authors(context));
        add(FibBuilder.Pair.CommentExtra, CommentExtras(context));
        WriteCommentBookmarks(context, add);

        add(FibBuilder.Pair.EndnoteReferences, References(context.Endnotes, 2, i => NoteRecord(context.Endnotes, i)));
        add(
            FibBuilder.Pair.EndnoteText,
            Bodies(
                context.Endnotes,
                story.MainLength + story.FootnoteLength + story.HeaderLength + story.CommentLength,
                story.EndnoteLength));
    }

    /// <summary>
    /// Builds one note's record, which says only whether the note is numbered for you. Zero
    /// means the author supplied the mark themselves.
    /// </summary>
    private static byte[] NoteRecord(List<NoteSpan> anchors, int index) =>
        anchors[index].CustomMark ? [0, 0] : [1, 0];

    /// <summary>
    /// Writes each note's body in the order its reference appeared. The order of the bodies
    /// has to match the order of the references, because the two lists are paired by index.
    /// </summary>
    private static void WriteNotes(StoryAssembler story, IReadOnlyList<Note> notes, List<NoteSpan> anchors)
    {
        for (int i = 0; i < anchors.Count; i++)
        {
            anchors[i] = anchors[i] with { BodyStart = story.Position };
            Note? note = notes.FirstOrDefault(n => n.Id == anchors[i].Id);
            if (note is { Blocks.Count: > 0 })
                story.WriteStory(note.Blocks);
            else
                story.WriteGuardMark();
        }
    }

    /// <summary>
    /// Writes the header document: six separator stories, then six per section. Stories with
    /// nothing in them are written as nothing at all, which is how the format says a section
    /// inherits the header of the one before it.
    /// </summary>
    private static void WriteHeaders(DocWriteContext context, StoryAssembler story, WordDocument document)
    {
        if (!document.Sections.Any(HasHeaders))
            return;

        int start = story.Position;
        for (int i = 0; i < SeparatorStories; i++)
            context.HeaderStories.Add(story.Position - start);

        foreach (Section section in document.Sections)
        {
            foreach (HeaderFooter? content in Slots(section))
            {
                context.HeaderStories.Add(story.Position - start);
                if (content is null || content.Blocks.Count == 0)
                    continue;

                story.WriteStory(content.Blocks);
                story.WriteGuardMark();
            }
        }

        // The list ends with the position that closes the last story — which is where the
        // document's own trailing mark will go — and one more the format requires but leaves
        // undefined.
        context.HeaderStories.Add(story.Position - start);
        context.HeaderStories.Add(story.Position - start + 1);
    }

    private static IEnumerable<HeaderFooter?> Slots(Section section)
    {
        yield return section.Headers.Even;
        yield return section.Headers.Default;
        yield return section.Footers.Even;
        yield return section.Footers.Default;
        yield return section.Headers.First;
        yield return section.Footers.First;
    }

    private static bool HasHeaders(Section section) =>
        Slots(section).Any(static slot => slot is { Blocks.Count: > 0 });

    private static void WriteComments(DocWriteContext context, StoryAssembler story, WordDocument document)
    {
        for (int i = 0; i < context.Comments.Count; i++)
        {
            context.Comments[i] = context.Comments[i] with { BodyStart = story.Position };

            // A comment's text has to open with the same reference character that anchors it
            // in the main story, whether or not the model bothered to keep one.
            story.WriteSpecial(DocChar.CommentReference, context.BuildSpecialRun(Styles.RunFormat.Default));

            Comment? comment = document.Comments.FirstOrDefault(c => c.Id == context.Comments[i].Id);
            if (comment is { Blocks.Count: > 0 })
                story.WriteStory(comment.Blocks);
            else
                story.WriteGuardMark();
        }
    }

    /// <summary>Builds the list of reference positions in the main story.</summary>
    private static byte[] References(List<NoteSpan> anchors, int recordBytes, Func<int, byte[]> record)
    {
        if (anchors.Count == 0)
            return [];

        var builder = new PlcBuilder(recordBytes);
        for (int i = 0; i < anchors.Count; i++)
        {
            int end = i + 1 < anchors.Count ? anchors[i + 1].ReferencePosition : anchors[i].ReferencePosition + 1;
            builder.Add(anchors[i].ReferencePosition, end, record(i));
        }

        return builder.ToArray();
    }

    /// <summary>
    /// Builds the list of body positions, which are measured from the start of the story
    /// they are in rather than from the start of the document.
    /// </summary>
    private static byte[] Bodies(List<NoteSpan> anchors, int storyStart, int storyLength)
    {
        if (anchors.Count == 0 || storyLength == 0)
            return [];

        var builder = new PlcBuilder(0);
        for (int i = 0; i < anchors.Count; i++)
        {
            int from = anchors[i].BodyStart - storyStart;
            int to = i + 1 < anchors.Count ? anchors[i + 1].BodyStart - storyStart : storyLength - 1;
            builder.Add(from, to);
        }

        // The final position closes the last body; one more follows that the format leaves
        // undefined and readers ignore.
        builder.Add(storyLength - 1, storyLength);
        return builder.ToArray();
    }
}
