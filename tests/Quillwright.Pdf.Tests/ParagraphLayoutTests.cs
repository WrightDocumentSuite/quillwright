using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class ParagraphLayoutTests
{
    private const string Sentence =
        "The quick brown fox jumps over the lazy dog while the cooper mends the barrel by the river. ";

    private static WordDocument WithText(string text)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph(text);
        return document;
    }

    [Fact]
    public void TextReachesThePage()
    {
        using Rendered rendered = Rendered.Of(WithText("Hello from Quillwright."));

        Assert.Contains("Hello from Quillwright.", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void LongTextWrapsAtWordBoundaries()
    {
        using Rendered rendered = Rendered.Of(WithText(string.Concat(Enumerable.Repeat(Sentence, 3))));

        var lines = rendered.Letters()
            .GroupBy(letter => Math.Round(letter.Origin.Y, 2))
            .OrderByDescending(group => group.Key)
            .Select(group => string.Concat(group.Select(letter => letter.Text)))
            .ToList();

        Assert.Equal(3, lines.Count);
        Assert.Equal(
            string.Concat(Enumerable.Repeat(Sentence, 3)).TrimEnd(),
            string.Concat(lines).TrimEnd());

        // Wrapping happens between words, so no line starts or ends inside one.
        foreach (string line in lines.SkipLast(1))
            Assert.EndsWith(" ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void NoLineRunsPastTheRightMargin()
    {
        WordDocument document = WithText(string.Concat(Enumerable.Repeat(Sentence, 4)));
        SectionProperties properties = document.Sections[0].Properties;

        using Rendered rendered = Rendered.Of(document);

        double margin = properties.PageWidth.Points - properties.Margins.Right.Points;
        Assert.True(rendered.RightEdge() <= margin + 1, $"Text reached {rendered.RightEdge()}, margin is {margin}.");
    }

    [Fact]
    public void TextStartsAtTheLeftMargin()
    {
        WordDocument document = WithText("Flush left.");
        double margin = document.Sections[0].Properties.Margins.Left.Points;

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(margin, rendered.LeftEdge(), 1);
    }

    [Fact]
    public void AnIndentMovesTheTextRight()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Indented.");
        paragraph.Format = paragraph.Format with { IndentLeft = Length.FromCentimeters(2) };

        using Rendered rendered = Rendered.Of(document);

        double expected = document.Sections[0].Properties.Margins.Left.Points + Length.FromCentimeters(2).Points;
        Assert.Equal(expected, rendered.LeftEdge(), 1);
    }

    [Fact]
    public void AHangingIndentPullsTheFirstLineBack()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(string.Concat(Enumerable.Repeat(Sentence, 2)));
        paragraph.Format = paragraph.Format with
        {
            IndentLeft = Length.FromCentimeters(2),
            IndentHanging = Length.FromCentimeters(1),
        };

        using Rendered rendered = Rendered.Of(document);

        double firstLine = rendered.Letters().OrderByDescending(letter => letter.Origin.Y).First().Origin.X;
        Assert.True(firstLine < rendered.Letters().Max(letter => letter.Origin.X));

        double margin = document.Sections[0].Properties.Margins.Left.Points;
        Assert.Equal(margin + Length.FromCentimeters(1).Points, firstLine, 1);
    }

    [Fact]
    public void RightAlignmentPushesTheTextToTheRightMargin()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Right.");
        paragraph.Format = paragraph.Format with { Alignment = ParagraphAlignment.Right };

        using Rendered rendered = Rendered.Of(document);

        SectionProperties properties = document.Sections[0].Properties;
        double margin = properties.PageWidth.Points - properties.Margins.Right.Points;
        Assert.Equal(margin, rendered.RightEdge(), 1);
    }

    [Fact]
    public void CentringPutsEqualSpaceOnBothSides()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Centre.");
        paragraph.Format = paragraph.Format with { Alignment = ParagraphAlignment.Center };

        using Rendered rendered = Rendered.Of(document);

        SectionProperties properties = document.Sections[0].Properties;
        double left = rendered.LeftEdge() - properties.Margins.Left.Points;
        double right = properties.PageWidth.Points - properties.Margins.Right.Points - rendered.RightEdge();
        Assert.Equal(left, right, 1);
    }

    [Fact]
    public void JustificationStretchesEveryLineButTheLast()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(string.Concat(Enumerable.Repeat(Sentence, 3)));
        paragraph.Format = paragraph.Format with { Alignment = ParagraphAlignment.Justify };

        using Rendered rendered = Rendered.Of(document);

        SectionProperties properties = document.Sections[0].Properties;
        double margin = properties.PageWidth.Points - properties.Margins.Right.Points;

        var byLine = rendered.Letters()
            .Where(letter => !letter.IsWhiteSpace)
            .GroupBy(letter => Math.Round(letter.Origin.Y, 1))
            .OrderByDescending(group => group.Key)
            .ToList();

        foreach (var line in byLine.SkipLast(1))
            Assert.Equal(margin, line.Max(letter => letter.Origin.X + letter.Width), 1);

        Assert.True(byLine[^1].Max(letter => letter.Origin.X + letter.Width) < margin - 1);
    }

    [Fact]
    public void DoubleSpacingDoublesTheGapBetweenBaselines()
    {
        double Gap(LineSpacingRule rule, int value)
        {
            WordDocument document = WordDocument.Create();
            Paragraph paragraph = document.Sections[0].AddParagraph(string.Concat(Enumerable.Repeat(Sentence, 2)));
            paragraph.Format = paragraph.Format with
            {
                LineSpacingRule = rule,
                LineSpacing = Length.FromTwips(value),
            };

            using Rendered rendered = Rendered.Of(document);
            IReadOnlyList<double> baselines = rendered.Baselines();
            return baselines[0] - baselines[1];
        }

        double single = Gap(LineSpacingRule.Auto, 240);
        double doubled = Gap(LineSpacingRule.Auto, 480);

        Assert.Equal(single * 2, doubled, 1);
    }

    [Fact]
    public void ExactSpacingIsExact()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(string.Concat(Enumerable.Repeat(Sentence, 2)));
        paragraph.Format = paragraph.Format with
        {
            LineSpacingRule = LineSpacingRule.Exact,
            LineSpacing = Length.FromPoints(20),
        };

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<double> baselines = rendered.Baselines();

        Assert.Equal(20, baselines[0] - baselines[1], 1);
    }

    [Fact]
    public void SpaceBeforePushesTheParagraphDown()
    {
        double TopOfSecond(Length? before)
        {
            WordDocument document = WordDocument.Create();
            document.Sections[0].AddParagraph("First.");
            Paragraph second = document.Sections[0].AddParagraph("Second.");
            second.Format = second.Format with { SpacingBefore = before };

            using Rendered rendered = Rendered.Of(document);
            return rendered.Baselines()[1];
        }

        Assert.Equal(TopOfSecond(null) - 30, TopOfSecond(Length.FromPoints(30)), 1);
    }

    [Fact]
    public void ContextualSpacingDropsTheGapBetweenNeighboursOfOneStyle()
    {
        double Gap(bool contextual)
        {
            WordDocument document = WordDocument.Create();
            ParagraphFormat format = ParagraphFormat.Default with
            {
                StyleId = "Normal",
                SpacingBefore = Length.FromPoints(24),
                ContextualSpacing = contextual ? true : null,
            };

            document.Sections[0].AddParagraph("First.").Format = format;
            document.Sections[0].AddParagraph("Second.").Format = format;

            using Rendered rendered = Rendered.Of(document);
            IReadOnlyList<double> baselines = rendered.Baselines();
            return baselines[0] - baselines[1];
        }

        Assert.True(Gap(contextual: false) - Gap(contextual: true) > 20);
    }
}
