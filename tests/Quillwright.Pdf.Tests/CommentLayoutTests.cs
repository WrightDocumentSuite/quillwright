using Quillwright.Model;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class CommentLayoutTests
{
    [Fact]
    public void PointCommentBeforeLongTokenDoesNotCreateMarkerOnlyLine()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(new string('W', 400));
        document.AddComment(paragraph, 0, 0, "Start of a long token.", "Ada", "A");

        AssertLayoutIsInvariant(document);
    }

    [Fact]
    public void PointCommentBeforePageBreakDoesNotCreateBlankPageLine()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendBreak(BreakKind.Page);
        paragraph.AppendText("Text after an opening page break.");
        document.AddComment(paragraph, 0, 0, "Before the break.", "Ada", "A");

        AssertLayoutIsInvariant(document);
    }

    [Fact]
    public void CommentOnlyParagraphKeepsDefaultEmptyParagraphHeight()
    {
        WordDocument document = WordDocument.Create();
        Paragraph empty = document.Sections[0].AddParagraph();
        document.AddComment(empty, 0, 0, "Point comment.", "Ada", "A");
        document.Sections[0].AddParagraph("Paragraph after the empty one.");

        AssertLayoutIsInvariant(document);
    }

    private static void AssertLayoutIsInvariant(WordDocument document)
    {
        using Rendered withoutComments = Rendered.Of(document);
        using Rendered withComments = Rendered.Of(
            document,
            new PdfExportOptions { IncludeComments = true });

        Assert.Equal(withoutComments.PageCount, withComments.PageCount);
        Assert.Equal(LayoutOf(withoutComments), LayoutOf(withComments));
        Assert.Equal(1, CommentCount(withComments));
    }

    private static (int Page, string Text, double X, double Y, double Width)[] LayoutOf(Rendered rendered)
    {
        List<(int Page, string Text, double X, double Y, double Width)> layout = [];
        for (int page = 0; page < rendered.PageCount; page++)
        {
            layout.AddRange(rendered.Letters(page).Select(letter => (
                page,
                letter.Text,
                Math.Round(letter.Origin.X, 4),
                Math.Round(letter.Origin.Y, 4),
                Math.Round(letter.Width, 4))));
        }

        return [.. layout];
    }

    private static int CommentCount(Rendered rendered)
    {
        int count = 0;
        for (int page = 0; page < rendered.PageCount; page++)
            count += rendered.Document.Pages[page].Annotations.Comments.Count();

        return count;
    }
}
