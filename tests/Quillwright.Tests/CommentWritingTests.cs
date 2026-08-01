using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// A comment is three pieces of markup that have to agree: the marks around the commented
/// text, the reference the reader clicks, and the body in a part of its own.
/// </summary>
public class CommentWritingTests
{
    [Fact]
    public async Task TheReference_SitsAtTheEndOfTheCommentedRange()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 4, 5, "about this word", "Reviewer", "R");

        string xml = await MarkupAsync(document);
        int rangeEnd = xml.IndexOf("<w:commentRangeEnd", StringComparison.Ordinal);
        int reference = xml.IndexOf("<w:commentReference", StringComparison.Ordinal);
        int trailing = xml.IndexOf("fox", StringComparison.Ordinal);

        // Word puts the reference immediately after the closing mark, not at the end of the
        // paragraph: it is the anchor the comment bubble points at.
        Assert.True(rangeEnd < reference, "the reference must follow the closing mark");
        Assert.True(reference < trailing, "the reference must precede the rest of the paragraph");
    }

    /// <summary>
    /// Two comments on the same words each need a reference of their own, and Word pairs every
    /// closing mark with the reference of the same comment: end, reference, end, reference. The
    /// second comment's range therefore reaches past the first comment's reference character,
    /// which is the arrangement that makes the pairing possible at all.
    /// </summary>
    [Fact]
    public async Task TwoCommentsOnTheSameWords_ArePairedInTheOrderTheyWereMade()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 4, 5, "a question", "Ada");
        document.AddComment(paragraph, 4, 5, "an answer", "Grace");

        string xml = await MarkupAsync(document);
        int[] order =
        [
            xml.IndexOf("<w:commentRangeEnd w:id=\"1\"", StringComparison.Ordinal),
            xml.IndexOf("<w:commentReference w:id=\"1\"", StringComparison.Ordinal),
            xml.IndexOf("<w:commentRangeEnd w:id=\"2\"", StringComparison.Ordinal),
            xml.IndexOf("<w:commentReference w:id=\"2\"", StringComparison.Ordinal),
        ];

        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
    }

    [Fact]
    public async Task TheCommentedText_IsTheTextBetweenTheMarks()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("alpha beta gamma");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 6, 4, "on beta", "Reviewer");

        WordDocument reopened = await ReloadAsync(document);
        Paragraph result = reopened.Sections[0].Blocks.OfType<Paragraph>().First();

        int start = result.Marks.Single(static m => m.Mark is CommentRangeStart).Offset;
        int end = result.Marks.Single(static m => m.Mark is CommentRangeEnd).Offset;

        Assert.Equal("beta", result.Text[start..end]);
    }

    [Fact]
    public async Task ACommentsBodyAndAuthor_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Reviewed text");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 0, 8, "needs work", "Ada Lovelace", "AL");

        Comment reopened = (await ReloadAsync(document)).Comments.Single();

        Assert.Contains("needs work", reopened.GetText(), StringComparison.Ordinal);
        Assert.Equal("Ada Lovelace", reopened.Author);
        Assert.Equal("AL", reopened.Initials);
    }

    /// <summary>
    /// A comment about text that runs past a paragraph break opens in one paragraph and closes
    /// in another, with the reference beside the closing mark rather than the opening one.
    /// </summary>
    [Fact]
    public async Task ACommentAcrossParagraphs_OpensInOneAndClosesInTheOther()
    {
        WordDocument document = WordDocument.Create();
        var first = new Paragraph("first paragraph");
        var second = new Paragraph("second paragraph");
        document.Sections[0].Blocks.Add(first);
        document.Sections[0].Blocks.Add(second);

        document.AddComment(first, 6, second, 6, "over the break", "Ada", "A");

        List<Paragraph> reopened = [.. (await ReloadAsync(document)).Sections[0].Blocks.OfType<Paragraph>()];

        Assert.Equal(6, reopened[0].Marks.Single(static m => m.Mark is CommentRangeStart).Offset);
        Assert.DoesNotContain(reopened[0].Marks, static m => m.Mark is CommentRangeEnd);
        Assert.Equal(6, reopened[1].Marks.Single(static m => m.Mark is CommentRangeEnd).Offset);
        Assert.Equal(6, reopened[1].Objects.Single(static o => o.Object is CommentReference).Offset);
    }

    [Fact]
    public async Task SeveralCommentsOnOneParagraph_EachKeepTheirOwnRange()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("one two three four");
        document.Sections[0].Blocks.Add(paragraph);

        // Backwards, so that the reference each call inserts does not move the offsets the
        // next one is about to use.
        document.AddComment(paragraph, 8, 5, "on three", "Second");
        document.AddComment(paragraph, 0, 3, "on one", "First");

        WordDocument reopened = await ReloadAsync(document);
        Paragraph result = reopened.Sections[0].Blocks.OfType<Paragraph>().First();

        List<string> commented =
        [
            .. result.Marks.Where(static m => m.Mark is CommentRangeStart)
                .OrderBy(static m => m.Offset)
                .Select(m => result.Text[m.Offset..EndOf(result, ((CommentRangeStart)m.Mark).Id)]),
        ];

        Assert.Equal(["one", "three"], commented);
    }

    private static int EndOf(Paragraph paragraph, int id) =>
        paragraph.Marks.Single(m => m.Mark is CommentRangeEnd end && end.Id == id).Offset;

    private static async Task<string> MarkupAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "comment markup");
        return OpenXmlAssert.ReadPart(buffer, "document.xml");
    }

    private static async Task<WordDocument> ReloadAsync(WordDocument document)
    {
        var buffer = new MemoryStream();
        await document.SaveAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
        OpenXmlAssert.Valid(buffer, "comment round trip");
        buffer.Position = 0;
        return await WordDocument.LoadAsync(buffer, cancellationToken: TestContext.Current.CancellationToken);
    }
}
