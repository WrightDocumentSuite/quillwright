using System.Text;
using Inkwright.Annotations;
using Inkwright.Cos;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class TabAndLinkTests
{
    private static Rendered WithTabs(string text, params TabStop[] stops)
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(text);
        paragraph.Format = paragraph.Format with { Tabs = new EquatableArray<TabStop>(stops) };
        return Rendered.Of(document);
    }

    /// <summary>Where the first glyph of a word starts, measured from the left of the page.</summary>
    private static double StartOf(Rendered rendered, string word) =>
        rendered.Letters().First(letter => letter.Text == word[..1] && letter.Origin.X > 80).Origin.X;

    [Fact]
    public void ATabJumpsToTheDefaultGrid()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("a\tb");

        using Rendered rendered = Rendered.Of(document);

        double margin = document.Sections[0].Properties.Margins.Left.Points;
        double b = rendered.Letters().First(letter => letter.Text == "b").Origin.X;

        // The default grid is half an inch, so the first stop past the margin is thirty-six points.
        Assert.Equal(margin + 36, b, 1);
    }

    [Fact]
    public void ALeftStopPutsTheTextAtTheStop()
    {
        using Rendered rendered = WithTabs("a\tb", new TabStop(Length.FromCentimeters(5)));

        double margin = 72;
        double b = rendered.Letters().First(letter => letter.Text == "b").Origin.X;
        Assert.Equal(margin + Length.FromCentimeters(5).Points, b, 1);
    }

    [Fact]
    public void ARightStopEndsTheTextAtTheStop()
    {
        using Rendered rendered = WithTabs(
            "a\tright", new TabStop(Length.FromCentimeters(8), TabAlignment.Right));

        double stop = 72 + Length.FromCentimeters(8).Points;
        Assert.Equal(stop, rendered.RightEdge(), 0.05);
    }

    [Fact]
    public void ACentreStopCentresTheTextOnTheStop()
    {
        using Rendered rendered = WithTabs(
            "a\tmiddle", new TabStop(Length.FromCentimeters(8), TabAlignment.Center));

        double stop = 72 + Length.FromCentimeters(8).Points;
        double left = StartOf(rendered, "m");
        double right = rendered.RightEdge();

        Assert.Equal(stop, (left + right) / 2, 0.05);
    }

    [Fact]
    public void ADecimalStopLinesNumbersUpOnTheirSeparator()
    {
        WordDocument document = WordDocument.Create();
        var stops = new EquatableArray<TabStop>([new TabStop(Length.FromCentimeters(8), TabAlignment.Decimal)]);

        foreach (string amount in new[] { "1.5", "22.25", "333.125" })
        {
            Paragraph paragraph = document.Sections[0].AddParagraph("Item\t" + amount);
            paragraph.Format = paragraph.Format with { Tabs = stops };
        }

        using Rendered rendered = Rendered.Of(document);

        double[] separators = [.. rendered.Letters()
            .Where(letter => letter.Text == ".")
            .Select(letter => Math.Round(letter.Origin.X, 1))
            .Distinct()];

        Assert.Single(separators);
        Assert.Equal(72 + Length.FromCentimeters(8).Points, separators[0], 1);
    }

    [Fact]
    public void ADotLeaderFillsTheJump()
    {
        using Rendered rendered = WithTabs(
            "Chapter\t7", new TabStop(Length.FromCentimeters(12), TabAlignment.Right, TabLeader.Dot));

        Assert.Contains("....", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void ABarStopDrawsARule()
    {
        using Rendered rendered = WithTabs("a\tb", new TabStop(Length.FromCentimeters(5), TabAlignment.Bar));

        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());
        Assert.Contains("\nS\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExternalLinkBecomesAnAnnotation()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Visit the site");
        paragraph.AddRange(new Hyperlink { Url = "https://example.org/" }, 6, 8);

        using Rendered rendered = Rendered.Of(document);

        PdfAnnotation annotation = Assert.Single(rendered.Document.Pages[0].Annotations);
        var link = Assert.IsType<PdfLinkAnnotation>(annotation);
        Assert.Equal("https://example.org/", link.Uri);
    }

    [Fact]
    public void TheLinkCoversOnlyTheTextItWraps()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Visit the site");
        paragraph.AddRange(new Hyperlink { Url = "https://example.org/" }, 6, 8);

        using Rendered rendered = Rendered.Of(document);

        var link = (PdfLinkAnnotation)rendered.Document.Pages[0].Annotations[0];
        double left = link.Dictionary.GetArray(PdfName.Get("Rect"))!.Get(0).AsNumber;

        Assert.True(left > 72 + 20, "The clickable area started at the margin instead of at the word.");
    }

    [Fact]
    public void AnInternalLinkPointsAtTheBookmarkedPage()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        Paragraph link = section.AddParagraph("Go to the end");
        link.AddRange(new Hyperlink { Anchor = "TheEnd" }, 0, link.TextLength);

        for (int i = 0; i < 60; i++)
            section.AddParagraph("Filler " + i);

        Paragraph target = section.AddParagraph("The end.");
        target.AddMark(new BookmarkStart { Id = 1, Name = "TheEnd" }, 0);
        target.AddMark(new BookmarkEnd { Id = 1 }, target.TextLength);

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 2);
        var annotation = (PdfLinkAnnotation)rendered.Document.Pages[0].Annotations[0];

        Assert.Null(annotation.Uri);
        Assert.Equal(rendered.PageCount - 1, annotation.DestinationPageIndex);
    }

    [Fact]
    public void ALinkToNowhereIsLeftInert()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Dangling");
        paragraph.AddRange(new Hyperlink { Anchor = "NotThere" }, 0, paragraph.TextLength);

        using Rendered rendered = Rendered.Of(document);

        var annotation = (PdfLinkAnnotation)rendered.Document.Pages[0].Annotations[0];
        Assert.Null(annotation.Uri);
        Assert.Null(annotation.DestinationPageIndex);
    }
}
