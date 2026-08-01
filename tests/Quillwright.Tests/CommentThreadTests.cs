using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Replies and resolved state live in a part of their own that names comments by the
/// paragraph identifier of their last paragraph rather than by comment id, so writing it
/// means giving those paragraphs identifiers that agree with the ones already there.
/// </summary>
public class CommentThreadTests
{
    [Fact]
    public async Task AReply_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Some reviewed text");
        document.Sections[0].Blocks.Add(paragraph);

        Comment first = document.AddComment(paragraph, 0, 4, "what about this?", "Ada");
        Comment reply = document.AddComment(paragraph, 5, 8, "fixed", "Grace");
        reply.ParentId = first.Id;

        WordDocument reopened = await ReloadAsync(document);

        Assert.Equal(2, reopened.Comments.Count);
        Assert.Null(reopened.Comments[0].ParentId);
        Assert.Equal(reopened.Comments[0].Id, reopened.Comments[1].ParentId);
    }

    /// <summary>
    /// A reply answers a comment about particular words, so it is about the same words. Word
    /// gives it a range of its own covering the same text rather than sharing the parent's.
    /// </summary>
    [Fact]
    public async Task AReplyAddedThroughTheApi_CoversTheSameWordsAsWhatItAnswers()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        document.Sections[0].Blocks.Add(paragraph);

        Comment question = document.AddComment(paragraph, 4, 5, "which one?", "Ada");
        Comment answer = document.AddReply(question, "that one", "Grace", "G");

        WordDocument reopened = await ReloadAsync(document);
        Paragraph result = reopened.Sections[0].Blocks.OfType<Paragraph>().First();

        Assert.Equal(question.Id, answer.ParentId);
        Assert.Equal("quick", Commented(result, question.Id));

        // The reply's range reaches one character further, over the reference of the comment
        // it answers, which is where Word puts it too.
        Assert.Equal("quick", Commented(result, answer.Id).TrimEnd(InlineObject.Placeholder));
        Assert.Equal(answer.Id, reopened.Comments[1].Id);
        Assert.Equal(question.Id, reopened.Comments[1].ParentId);
    }

    [Fact]
    public async Task AReplyToAReply_KeepsTheChain()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        document.Sections[0].Blocks.Add(paragraph);

        Comment first = document.AddComment(paragraph, 0, 8, "a question", "Ada");
        Comment second = document.AddReply(first, "an answer", "Grace");
        Comment third = document.AddReply(second, "a follow-up", "Alan");

        WordDocument reopened = await ReloadAsync(document);

        Assert.Null(reopened.Comments[0].ParentId);
        Assert.Equal(first.Id, reopened.Comments[1].ParentId);
        Assert.Equal(second.Id, reopened.Comments[2].ParentId);
        Assert.Equal(3, third.Id);
    }

    /// <summary>A comment with no range is anchored at its reference, and a reply joins it there.</summary>
    [Fact]
    public async Task AReplyToACommentWithNoRange_AnchorsWhereTheReferenceIs()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        document.Sections[0].Blocks.Add(paragraph);

        Comment first = document.AddComment(paragraph, 0, 8, "a question", "Ada");
        foreach (InlineMark mark in paragraph.Marks.Select(static m => m.Mark).ToArray())
        {
            if (mark is CommentRangeStart or CommentRangeEnd)
                paragraph.RemoveMark(mark);
        }

        document.AddReply(first, "an answer", "Grace");
        WordDocument reopened = await ReloadAsync(document);

        Assert.Equal(2, reopened.Comments.Count);
        Assert.Equal(reopened.Comments[0].Id, reopened.Comments[1].ParentId);
    }

    [Fact]
    public void AReplyToACommentThatIsNotAnchored_IsRefused()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        document.Sections[0].Blocks.Add(paragraph);

        Comment first = document.AddComment(paragraph, 0, 8, "a question", "Ada");
        foreach (InlineMark mark in paragraph.Marks.Select(static m => m.Mark).ToArray())
        {
            if (mark is CommentRangeStart or CommentRangeEnd)
                paragraph.RemoveMark(mark);
        }

        // Deleting the reference character is what leaves the comment with nothing to point at.
        paragraph.RemoveText(paragraph.Objects.Single(static o => o.Object is CommentReference).Offset, 1);

        Assert.Throws<InvalidOperationException>(() => document.AddReply(first, "an answer", "Grace"));
    }

    [Fact]
    public async Task AResolvedComment_StaysResolved()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 8, "done with this", "Ada").IsResolved = true;

        Assert.True((await ReloadAsync(document)).Comments.Single().IsResolved);
    }

    [Fact]
    public async Task ADocumentWithNoThreads_DoesNotGetTheExtraPart()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 8, "a plain comment", "Ada");

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "comments without threads");

        Assert.DoesNotContain("commentsExtended", Names(buffer.ToArray()));
    }

    [Fact]
    public async Task ThreadedComments_ProduceAValidPackage()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        document.Sections[0].Blocks.Add(paragraph);
        Comment first = document.AddComment(paragraph, 0, 8, "a question", "Ada");
        document.AddComment(paragraph, 9, 4, "an answer", "Grace").ParentId = first.Id;

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "threaded comments");

        Assert.Contains("word/commentsExtended.xml", Names(buffer.ToArray()));
    }

    [Fact]
    public async Task EachCommentGetsItsOwnParagraphIdentifier()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("one two three");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 3, "first", "Ada").IsResolved = true;
        document.AddComment(paragraph, 5, 3, "second", "Grace").IsResolved = true;

        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        string part = OpenXmlAssert.ReadPart(buffer, "commentsExtended.xml");

        List<string> ids =
        [
            .. System.Text.RegularExpressions.Regex.Matches(part, "w15:paraId=\"([^\"]+)\"")
                .Select(static m => m.Groups[1].Value),
        ];

        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>The text a comment's range covers.</summary>
    /// <param name="paragraph">Paragraph holding the marks.</param>
    /// <param name="id">Identifier of the comment.</param>
    private static string Commented(Paragraph paragraph, int id)
    {
        int start = paragraph.Marks.Single(m => m.Mark is CommentRangeStart s && s.Id == id).Offset;
        int end = paragraph.Marks.Single(m => m.Mark is CommentRangeEnd e && e.Id == id).Offset;
        return paragraph.Text[start..end];
    }

    private static IEnumerable<string> Names(byte[] package)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(package), System.IO.Compression.ZipArchiveMode.Read);
        return [.. archive.Entries.Select(static entry => entry.FullName)];
    }

    private static async Task<WordDocument> ReloadAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "comment threads");
        buffer.Position = 0;
        return await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
    }
}
