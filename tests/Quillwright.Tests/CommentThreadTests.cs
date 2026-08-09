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

    /// <summary>
    /// The loaded package still has a threading part after its last resolved flag is cleared.
    /// That part has to be regenerated with <c>done="0"</c>, rather than copied back with the
    /// old value.
    /// </summary>
    [Fact]
    public async Task ClearingTheOnlyResolvedState_RewritesTheExistingThreadPart()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 8, "done with this", "Ada").IsResolved = true;

        WordDocument reopened = await ReloadAsync(document);
        reopened.Comments.Single().IsResolved = false;

        MemoryStream saved = await SaveAsync(reopened);
        string part = OpenXmlAssert.ReadPart(saved, "commentsExtended.xml");

        Assert.Contains("w15:done=\"0\"", part, StringComparison.Ordinal);
        Assert.DoesNotContain("w15:done=\"1\"", part, StringComparison.Ordinal);
        AssertThreadPartWiring(saved);

        saved.Position = 0;
        WordDocument again = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(again.Comments.Single().IsResolved);
    }

    /// <summary>
    /// Clearing the last parent link is the other transition that makes
    /// <see cref="Formats.CommentThreadWriter.HasThreads"/> false. The existing part must
    /// still be rewritten so the removed link cannot return on reload.
    /// </summary>
    [Fact]
    public async Task ClearingTheLastReplyLink_RewritesTheExistingThreadPart()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text here");
        document.Sections[0].Blocks.Add(paragraph);
        Comment question = document.AddComment(paragraph, 0, 8, "a question", "Ada");
        document.AddReply(question, "an answer", "Grace");

        WordDocument reopened = await ReloadAsync(document);
        reopened.Comments[1].ParentId = null;

        MemoryStream saved = await SaveAsync(reopened);
        string part = OpenXmlAssert.ReadPart(saved, "commentsExtended.xml");

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(part, "<w15:commentEx ").Count);
        Assert.DoesNotContain("w15:paraIdParent", part, StringComparison.Ordinal);
        AssertThreadPartWiring(saved);

        saved.Position = 0;
        WordDocument again = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(again.Comments, static comment => Assert.Null(comment.ParentId));
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

    /// <summary>
    /// Comments are not reliably visible in headless rendering, so the part itself and the
    /// two pieces of OPC wiring that make it reachable are asserted directly.
    /// </summary>
    private static void AssertThreadPartWiring(MemoryStream package)
    {
        byte[] bytes = package.ToArray();
        Assert.Contains("word/commentsExtended.xml", Names(bytes));
        Assert.Contains(
            Formats.DocxSchema.RelCommentsExtended,
            Entry(bytes, "word/_rels/document.xml.rels"),
            StringComparison.Ordinal);
        Assert.Contains(
            Formats.DocxSchema.ContentTypeCommentsExtended,
            Entry(bytes, "[Content_Types].xml"),
            StringComparison.Ordinal);
    }

    private static string Entry(byte[] package, string name)
    {
        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(package), System.IO.Compression.ZipArchiveMode.Read);
        System.IO.Compression.ZipArchiveEntry? entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static async Task<MemoryStream> SaveAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "comment threads");
        return buffer;
    }

    private static async Task<WordDocument> ReloadAsync(WordDocument document)
    {
        MemoryStream buffer = await SaveAsync(document);
        buffer.Position = 0;
        return await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
    }
}
