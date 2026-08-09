using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class PaginationTests
{
    private const string Sentence =
        "The quick brown fox jumps over the lazy dog while the cooper mends the barrel by the river. ";

    /// <summary>A paragraph tall enough that five of them will not share an A4 page.</summary>
    private static Paragraph AddBlock(Section section, int sentences = 8)
    {
        Paragraph paragraph = section.AddParagraph(string.Concat(Enumerable.Repeat(Sentence, sentences)));
        return paragraph;
    }

    [Fact]
    public void ContentOverflowsOntoTheNextPage()
    {
        WordDocument document = WordDocument.Create();
        for (int i = 0; i < 20; i++)
            AddBlock(document.Sections[0]);

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 2);
        Assert.NotEmpty(rendered.Text(1));
    }

    [Fact]
    public void NothingIsDrawnBelowTheBottomMargin()
    {
        WordDocument document = WordDocument.Create();
        for (int i = 0; i < 20; i++)
            AddBlock(document.Sections[0]);

        using Rendered rendered = Rendered.Of(document);

        double bottom = document.Sections[0].Properties.Margins.Bottom.Points;
        for (int page = 0; page < rendered.PageCount; page++)
            Assert.True(rendered.Letters(page).Min(letter => letter.Origin.Y) >= bottom - 6);
    }

    [Fact]
    public void AnExplicitPageBreakStartsANewPage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Before.");
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendBreak(BreakKind.Page);
        paragraph.AppendText("After.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("Before.", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("After.", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void ALastRenderedPageBreakReproducesWordsSavedPagination()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Before");
        Paragraph after = document.Sections[0].AddParagraph();
        after.AppendObject(new RenderedPageBreak());
        after.AppendText("After");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("Before", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("After", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void LastRenderedPageBreakHintsCanBeIgnoredAfterEditing()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Before");
        Paragraph after = document.Sections[0].AddParagraph();
        after.AppendObject(new RenderedPageBreak());
        after.AppendText("After");

        using Rendered rendered = Rendered.Of(document, new PdfExportOptions
        {
            HonorLastRenderedPageBreaks = false,
        });

        Assert.Single(rendered.Document.Pages);
        Assert.Contains("Before", rendered.Text(), StringComparison.Ordinal);
        Assert.Contains("After", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void ALastRenderedPageBreakAtTheStartOfACellMovesTheWholeRow()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Before");
        Table table = document.Sections[0].AddTable(1, 1);
        Paragraph cell = table[0, 0].Blocks.Paragraphs.Single();
        cell.AppendObject(new RenderedPageBreak());
        cell.AppendText("After");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.DoesNotContain("After", rendered.Text(0), StringComparison.Ordinal);
        Assert.Equal(1, rendered.Letters(1).Count(letter => letter.Text == "A"));
    }

    [Fact]
    public void ARenderedHintAfterAnExplicitBreakDoesNotCreateABlankPage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Before");
        document.Sections[0].AddParagraph().AppendBreak(BreakKind.Page);
        Paragraph after = document.Sections[0].AddParagraph();
        after.AppendObject(new RenderedPageBreak());
        after.AppendText("After");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("After", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void PageBreakBeforeStartsANewPage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Before.");
        Paragraph second = document.Sections[0].AddParagraph("After.");
        second.Format = second.Format with { PageBreakBefore = true };

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("After.", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void PageBreakBeforeDoesNotWasteTheFirstPage()
    {
        WordDocument document = WordDocument.Create();
        Paragraph first = document.Sections[0].AddParagraph("Only.");
        first.Format = first.Format with { PageBreakBefore = true };

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(1, rendered.PageCount);
    }

    [Fact]
    public void KeepLinesTogetherMovesTheWholeParagraph()
    {
        WordDocument document = WordDocument.Create();
        for (int i = 0; i < 6; i++)
            AddBlock(document.Sections[0]);

        Paragraph last = AddBlock(document.Sections[0], sentences: 6);
        last.Format = last.Format with { KeepLinesTogether = true };
        last.Runs[0].SetText("KEPT " + last.Text);

        using Rendered rendered = Rendered.Of(document);

        // Every line of the kept paragraph is on one page, so the marker and the final word share it.
        int page = Enumerable.Range(0, rendered.PageCount)
            .First(index => rendered.Text(index).Contains("KEPT", StringComparison.Ordinal));

        Assert.Contains("river.", rendered.Text(page), StringComparison.Ordinal);
    }

    /// <summary>
    /// A section whose lines are exactly twenty points tall, so a test can say how many of them
    /// fit and mean it. A4 less the default margins leaves 697.9 points, which is thirty-four.
    /// </summary>
    private static ParagraphFormat FixedLines => ParagraphFormat.Default with
    {
        LineSpacingRule = LineSpacingRule.Exact,
        LineSpacing = Length.FromPoints(20),
    };

    private static void Fill(Section section, int lines)
    {
        for (int i = 0; i < lines; i++)
            section.AddParagraph("Filler line " + i).Format = FixedLines;
    }

    [Fact]
    public void WidowControlDoesNotStrandASingleLine()
    {
        // Thirty-three fillers leave room for exactly one more line, which is the line widow
        // control refuses to leave behind.
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        Fill(section, 33);

        Paragraph wrapped = section.AddParagraph(string.Concat(Enumerable.Repeat(Sentence, 3)));
        wrapped.Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.DoesNotContain("quick brown fox", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("quick brown fox", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutWidowControlTheLineStaysWhereItFalls()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        Fill(section, 33);

        Paragraph wrapped = section.AddParagraph(string.Concat(Enumerable.Repeat(Sentence, 3)));
        wrapped.Format = FixedLines with { WidowControl = false };

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("quick brown fox", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepWithNextHoldsAHeadingToItsBody()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        Fill(section, 33);

        Paragraph heading = section.AddParagraph("HEADING");
        heading.Format = FixedLines with { KeepWithNext = true };
        section.AddParagraph(string.Concat(Enumerable.Repeat(Sentence, 2))).Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.DoesNotContain("HEADING", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("HEADING", rendered.Text(1), StringComparison.Ordinal);
        Assert.Contains("quick brown fox", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void ShadingIsPaintedBehindTheParagraph()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Shaded.");
        paragraph.Format = paragraph.Format with
        {
            Shading = Shading.Solid(WordColor.FromRgb(0xFFFF00)),
        };

        using Rendered rendered = Rendered.Of(document);

        string content = System.Text.Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());
        Assert.Contains(" re", content, StringComparison.Ordinal);
        Assert.Contains("1 1 0 rg", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ABorderIsStrokedAroundTheParagraph()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Boxed.");
        paragraph.Format = paragraph.Format with
        {
            Borders = BorderSet.All(BorderLine.Single(Length.FromEighthPoints(8), WordColor.FromRgb(0xFF0000))),
        };

        using Rendered rendered = Rendered.Of(document);

        string content = System.Text.Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());
        Assert.Contains("1 0 0 RG", content, StringComparison.Ordinal);
        Assert.Contains("\nS\n", content, StringComparison.Ordinal);
    }
}
