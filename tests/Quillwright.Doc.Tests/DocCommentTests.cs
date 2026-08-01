using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// A comment is three separate things in this format: a reference character in the text, a
/// body in a story of its own, and a bookmark saying which words it is about. These tests
/// check that all three come back joined up.
/// </summary>
public class DocCommentTests
{
    [Fact]
    public void ACommentedRange_KeepsItsExtent()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox jumps");
        Add(document, paragraph);
        document.AddComment(paragraph, 4, 11, "about these words", "Reviewer", "R");

        Paragraph reopened = First(RoundTrip(document));

        Assert.Equal(4, reopened.Marks.Single(static m => m.Mark is CommentRangeStart).Offset);
        Assert.Equal(15, reopened.Marks.Single(static m => m.Mark is CommentRangeEnd).Offset);
    }

    [Fact]
    public void TheRangeAndTheReference_NameTheSameComment()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Commented text here");
        Add(document, paragraph);
        document.AddComment(paragraph, 0, 9, "a note", "Reviewer");

        WordDocument reopened = RoundTrip(document);
        Paragraph body = First(reopened);

        int rangeId = ((CommentRangeStart)body.Marks.Single(static m => m.Mark is CommentRangeStart).Mark).Id;
        int referenceId = body.Objects.Select(static o => o.Object).OfType<CommentReference>().Single().Id;

        Assert.Equal(rangeId, referenceId);
        Assert.Equal(rangeId, reopened.Comments.Single().Id);
    }

    [Fact]
    public void SeveralCommentsOnOneParagraph_KeepTheirOwnExtents()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("alpha beta gamma delta");
        Add(document, paragraph);
        document.AddComment(paragraph, 0, 5, "on alpha", "First");
        document.AddComment(paragraph, 11, 5, "on gamma", "Second");

        Paragraph reopened = First(RoundTrip(document));
        List<int> starts = [.. reopened.Marks.Where(static m => m.Mark is CommentRangeStart).Select(static m => m.Offset).Order()];
        List<int> ends = [.. reopened.Marks.Where(static m => m.Mark is CommentRangeEnd).Select(static m => m.Offset).Order()];

        Assert.Equal([0, 11], starts);
        Assert.Equal([5, 16], ends);
    }

    [Fact]
    public void OverlappingComments_KeepBothExtents()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("one two three four five");
        Add(document, paragraph);
        document.AddComment(paragraph, 0, 13, "outer", "First");

        // The first comment's reference is a character of its own, so what was offset 4 is
        // still offset 4 but everything past 13 has moved along by one.
        document.AddComment(paragraph, 4, 3, "inner", "Second");

        Paragraph reopened = First(RoundTrip(document));

        Assert.Equal([0, 4], reopened.Marks.Where(static m => m.Mark is CommentRangeStart).Select(static m => m.Offset).Order());
        Assert.Equal([7, 14], reopened.Marks.Where(static m => m.Mark is CommentRangeEnd).Select(static m => m.Offset).Order());
    }

    [Fact]
    public void ACommentSpanningParagraphs_KeepsBothEnds()
    {
        // A comment that runs across a paragraph break has its reference in the paragraph
        // where it ends, which is what the two ends have to be moved to together.
        WordDocument document = WordDocument.Create();
        var first = new Paragraph("first paragraph");
        var second = new Paragraph("second paragraph");
        Add(document, first);
        Add(document, second);

        document.AddComment(first, 6, second, 6, "across", "Reviewer");

        List<Paragraph> reopened = [.. RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.Equal(6, reopened[0].Marks.Single(static m => m.Mark is CommentRangeStart).Offset);
        Assert.DoesNotContain(reopened[0].Marks, static m => m.Mark is CommentRangeEnd);
        Assert.Equal(6, reopened[1].Marks.Single(static m => m.Mark is CommentRangeEnd).Offset);
    }

    [Fact]
    public void ACommentWithNoRange_StillCarriesItsText()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Body");
        Add(document, paragraph);
        document.AddComment(paragraph, 0, 4, "a note", "Reviewer");

        foreach (InlineMark mark in paragraph.Marks.Select(static m => m.Mark).ToArray())
        {
            if (mark is CommentRangeStart or CommentRangeEnd)
                paragraph.RemoveMark(mark);
        }

        WordDocument reopened = RoundTrip(document);

        Assert.Contains("a note", reopened.Comments.Single().GetText(), StringComparison.Ordinal);
        Assert.DoesNotContain(First(reopened).Marks, static m => m.Mark is CommentRangeStart);
    }

    [Fact]
    public void TheAuthorAndInitials_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        Add(document, paragraph);
        document.AddComment(paragraph, 0, 8, "looks good", "Ada Lovelace", "AL");

        Comment reopened = RoundTrip(document).Comments.Single();

        Assert.Equal("Ada Lovelace", reopened.Author);
        Assert.Equal("AL", reopened.Initials);
    }

    [Fact]
    public void TwoAuthors_AreBothNamed()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("alpha beta");
        Add(document, paragraph);
        document.AddComment(paragraph, 0, 5, "first note", "Grace Hopper", "GH");
        document.AddComment(paragraph, 6, 4, "second note", "Alan Turing", "AT");

        List<Comment> reopened = [.. RoundTrip(document).Comments];

        Assert.Equal(["Grace Hopper", "Alan Turing"], reopened.Select(static c => c.Author));
        Assert.Equal(["GH", "AT"], reopened.Select(static c => c.Initials));
    }

    /// <summary>
    /// The comment tree of <c>AtrdExtra</c> ([MS-DOC] 2.9.5) says which comment answers which,
    /// naming the parent by how many records away it is rather than by an identifier.
    /// </summary>
    [Fact]
    public void AReply_StillAnswersItsParent()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        Add(document, paragraph);

        Comment question = document.AddComment(paragraph, 4, 5, "which one?", "Ada");
        document.AddReply(question, "that one", "Grace");

        List<Comment> reopened = [.. RoundTrip(document).Comments];

        Assert.Equal(2, reopened.Count);
        Assert.Null(reopened[0].ParentId);
        Assert.Equal(reopened[0].Id, reopened[1].ParentId);
    }

    [Fact]
    public void AReplyToAReply_KeepsTheWholeChain()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        Add(document, paragraph);

        Comment first = document.AddComment(paragraph, 0, 8, "a question", "Ada");
        Comment second = document.AddReply(first, "an answer", "Grace");
        document.AddReply(second, "a follow-up", "Alan");

        List<Comment> reopened = [.. RoundTrip(document).Comments];

        Assert.Equal(3, reopened.Count);
        Assert.Null(reopened[0].ParentId);
        Assert.Equal(reopened[0].Id, reopened[1].ParentId);
        Assert.Equal(reopened[1].Id, reopened[2].ParentId);
    }

    /// <summary>
    /// The date is packed into thirty-two bits, which leaves room for minutes but not seconds
    /// ([MS-DOC] 2.9.75), so what comes back is the same wall clock truncated to the minute.
    /// </summary>
    [Fact]
    public void ACommentsDate_SurvivesToTheMinute()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        Add(document, paragraph);

        var written = new DateTimeOffset(2024, 3, 17, 9, 41, 37, TimeSpan.Zero);
        document.AddComment(paragraph, 0, 8, "dated", "Ada").Date = written;

        Assert.Equal(
            new DateTimeOffset(2024, 3, 17, 9, 41, 0, TimeSpan.Zero),
            RoundTrip(document).Comments.Single().Date);
    }

    /// <summary>
    /// What the binary format really has no room for is the flag saying a thread is settled,
    /// so that is what the warning is about now that replies and dates survive.
    /// </summary>
    [Fact]
    public void SavingAResolvedComment_WarnsThatTheResolvedMarkIsLost()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        Add(document, paragraph);

        Comment question = document.AddComment(paragraph, 4, 5, "which one?", "Ada");
        document.AddReply(question, "that one", "Grace");
        question.IsResolved = true;

        var warnings = new List<DocumentWarning>();
        byte[] saved = DocWriter.Save(document, new DocWriteOptions { OnWarning = warnings.Add });
        WordDocument reopened = DocReader.Load(saved);

        Assert.Contains(warnings, static w => w.Message.Contains("resolved", StringComparison.Ordinal));
        Assert.All(reopened.Comments, static comment => Assert.False(comment.IsResolved));
    }

    [Fact]
    public void SavingARepliedToCommentThatIsNotResolved_SaysNothingAboutIt()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        Add(document, paragraph);
        document.AddReply(document.AddComment(paragraph, 4, 5, "a plain comment", "Ada"), "and a reply", "Grace");

        var warnings = new List<DocumentWarning>();
        DocWriter.Save(document, new DocWriteOptions { OnWarning = warnings.Add });

        Assert.DoesNotContain(warnings, static w => w.Message.Contains("comment", StringComparison.OrdinalIgnoreCase));
    }

    private static WordDocument RoundTrip(WordDocument document) => DocReader.Load(DocWriter.Save(document));

    private static void Add(WordDocument document, Block block) => document.Sections[0].Blocks.Add(block);

    private static Paragraph First(WordDocument document) =>
        document.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();
}
