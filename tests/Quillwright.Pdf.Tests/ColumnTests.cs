using System.Text.RegularExpressions;
using Inkwright.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Multi-column sections: text fills the first column before the second, column breaks move it
/// along, and the notes still get the full width of the page.
/// </summary>
/// <remarks>
/// On A4 with inch margins the body runs from 72 to 523.3 points. Two equal columns with the
/// default half-inch gap are 207.6 points wide each: the first spans 72–279.6, the second starts
/// at 315.6, and the middle of the gap is at 297.6.
/// </remarks>
public sealed class ColumnTests
{
    private const double GapLeft = 280.7;
    private const double GapRight = 314.6;
    private const double SecondColumn = 315.1;

    /// <summary>A paragraph whose lines are exactly twenty points tall, so a test can count them.</summary>
    private static ParagraphFormat FixedLines => ParagraphFormat.Default with
    {
        LineSpacingRule = LineSpacingRule.Exact,
        LineSpacing = Length.FromPoints(20),
    };

    private static WordDocument TwoColumns()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.Columns.Count = 2;
        return document;
    }

    [Fact]
    public void TextFillsTheFirstColumnBeforeTheSecond()
    {
        WordDocument document = TwoColumns();
        for (int i = 1; i <= 60; i++)
            document.Sections[0].AddParagraph($"Line {i}").Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        // Sixty lines of twenty points are 1200 points of text: far too much for one column of
        // 697.9, and exactly what two columns of a single page hold.
        Assert.Equal(1, rendered.PageCount);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X < GapLeft);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X >= SecondColumn);
    }

    [Fact]
    public void TextThatFitsTheFirstColumnNeverTouchesTheSecond()
    {
        WordDocument document = TwoColumns();
        for (int i = 1; i <= 20; i++)
            document.Sections[0].AddParagraph($"Line {i}").Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        Assert.All(rendered.Letters(), letter => Assert.True(letter.Origin.X < GapLeft));
    }

    [Fact]
    public void NoLineReachesIntoTheGapBetweenColumns()
    {
        WordDocument document = TwoColumns();
        for (int i = 0; i < 60; i++)
        {
            document.Sections[0]
                .AddParagraph("The quick brown fox jumps over the lazy dog by the river barrel.")
                .Format = FixedLines;
        }

        using Rendered rendered = Rendered.Of(document);

        foreach (PdfLetter letter in rendered.Letters())
        {
            bool inFirst = letter.Origin.X + letter.Width <= GapLeft;
            bool inSecond = letter.Origin.X >= GapRight;
            Assert.True(inFirst || inSecond, $"A glyph sits in the gap at {letter.Origin.X:0.0}.");
        }
    }

    [Fact]
    public void ASingleParagraphSplitsFromOneColumnIntoTheNext()
    {
        WordDocument document = TwoColumns();
        string words = string.Concat(Enumerable.Repeat("wander onward through the pages ", 55));
        document.Sections[0].AddParagraph(words.TrimEnd()).Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(1, rendered.PageCount);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X < GapLeft);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X >= SecondColumn);
    }

    [Fact]
    public void AColumnBreakMovesTextToTheTopOfTheNextColumn()
    {
        WordDocument document = TwoColumns();
        Paragraph paragraph = document.Sections[0].AddParagraph("Left");
        paragraph.AppendBreak(BreakKind.Column);
        paragraph.AppendText("Right");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        Assert.Equal(1, rendered.PageCount);
        PdfLetter l = letters.First(letter => letter.Text == "L");
        PdfLetter r = letters.First(letter => letter.Text == "R");
        Assert.True(l.Origin.X < GapLeft);
        Assert.True(r.Origin.X >= SecondColumn);

        // Both sit on the first baseline of their column, which is the same height.
        Assert.Equal(l.Origin.Y, r.Origin.Y, 1);
    }

    [Fact]
    public void AColumnBreakInTheLastColumnOpensANewPage()
    {
        WordDocument document = TwoColumns();
        Paragraph paragraph = document.Sections[0].AddParagraph("One");
        paragraph.AppendBreak(BreakKind.Column);
        paragraph.AppendText("Two");
        paragraph.AppendBreak(BreakKind.Column);
        paragraph.AppendText("Three");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("Three", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void AColumnBreakInAOneColumnSectionActsAsAPageBreak()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("First");
        paragraph.AppendBreak(BreakKind.Column);
        paragraph.AppendText("Second");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("Second", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void UnequalColumnsSitWhereTheSectionPutsThem()
    {
        WordDocument document = WordDocument.Create();
        ColumnLayout columns = document.Sections[0].Properties.Columns;
        columns.Count = 2;
        columns.EqualWidth = false;
        columns.Columns.Add(new TextColumn(Length.FromPoints(100), Length.FromPoints(30)));
        columns.Columns.Add(new TextColumn(Length.FromPoints(321), Length.Zero));

        Paragraph paragraph = document.Sections[0].AddParagraph("A");
        paragraph.AppendBreak(BreakKind.Column);
        paragraph.AppendText("B");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        Assert.Equal(72, letters.First(letter => letter.Text == "A").Origin.X, 0.5);
        Assert.Equal(202, letters.First(letter => letter.Text == "B").Origin.X, 0.5);
    }

    [Fact]
    public void AParagraphMovesWholeBetweenUnequalColumns()
    {
        WordDocument document = WordDocument.Create();
        ColumnLayout columns = document.Sections[0].Properties.Columns;
        columns.Count = 2;
        columns.EqualWidth = false;
        columns.Columns.Add(new TextColumn(Length.FromPoints(150), Length.FromPoints(30)));
        columns.Columns.Add(new TextColumn(Length.FromPoints(271), Length.Zero));

        // Thirty-three fixed lines leave room for one more, which is not enough for a paragraph
        // of three: measured against a column of another width, it may not split into it.
        for (int i = 0; i < 33; i++)
            document.Sections[0].AddParagraph("Filler").Format = FixedLines;

        Paragraph moved = document.Sections[0].AddParagraph("One");
        moved.Format = FixedLines;
        moved.AppendBreak(BreakKind.Line);
        moved.AppendText("Two");
        moved.AppendBreak(BreakKind.Line);
        moved.AppendText("Three");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        Assert.Equal(1, rendered.PageCount);
        foreach (string text in new[] { "O", "T" })
        {
            Assert.All(
                letters.Where(letter => letter.Text == text),
                letter => Assert.True(letter.Origin.X >= 250, "The paragraph should have moved whole."));
        }
    }

    [Fact]
    public void ASeparatorRuleRunsDownTheMiddleOfTheGap()
    {
        WordDocument document = TwoColumns();
        document.Sections[0].Properties.Columns.Separator = true;
        for (int i = 1; i <= 50; i++)
            document.Sections[0].AddParagraph($"Line {i}").Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        string content = System.Text.Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());
        var vertical = new Regex(@"(?<x>\d+(?:\.\d+)?) (?<top>\d+(?:\.\d+)?) m\n\k<x> (?<bottom>\d+(?:\.\d+)?) l");

        bool found = vertical.Matches(content).Any(match =>
            Math.Abs(double.Parse(match.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture) - 297.6) < 1
            && Math.Abs(
                double.Parse(match.Groups["top"].Value, System.Globalization.CultureInfo.InvariantCulture)
                - double.Parse(match.Groups["bottom"].Value, System.Globalization.CultureInfo.InvariantCulture)) > 100);

        Assert.True(found, "No vertical rule was stroked down the gap between the columns.");
    }

    [Fact]
    public void NoSeparatorIsDrawnUnlessTheSectionAsksForOne()
    {
        WordDocument document = TwoColumns();
        for (int i = 1; i <= 50; i++)
            document.Sections[0].AddParagraph($"Line {i}").Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        string content = System.Text.Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());
        Assert.DoesNotContain("\nS\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AFootnoteStillGetsTheFullWidthOfThePage()
    {
        WordDocument document = TwoColumns();
        Paragraph paragraph = document.Sections[0].AddParagraph("Left");
        paragraph.AppendBreak(BreakKind.Column);
        paragraph.AppendText("A claim.");
        document.AddFootnote(
            paragraph,
            "The evidence goes on at considerable length so that a single note line is far wider " +
            "than any one column of the page could ever hold on its own.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("far wider", string.Concat(rendered.Lines()), StringComparison.Ordinal);

        // The note wraps only at the page margin, so its lines run straight through the gap that
        // no body line may touch: glyphs in the gap can only be the note's.
        Assert.Contains(
            rendered.Letters(),
            letter => letter.Origin.X > GapLeft && letter.Origin.X < GapRight);
    }

    [Fact]
    public void AContinuousSectionWithTheSameColumnsSharesThePage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("One");
        Section second = document.Sections.Add(SectionStart.Continuous);
        second.AddParagraph("Two");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(1, rendered.PageCount);
        Assert.Contains("Two", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void AContinuousSectionThatChangesTheColumnsRestacksTheSamePage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("One");
        Section second = document.Sections.Add(SectionStart.Continuous);
        second.Properties.Columns.Count = 2;
        for (int i = 1; i <= 6; i++)
            second.AddParagraph($"Col {i}.");

        using Rendered rendered = Rendered.Of(document);

        // The page carries on: the one-column text above, the new band of columns below it.
        Assert.Equal(1, rendered.PageCount);
        Assert.Contains("One", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("Col 1.", rendered.Text(0), StringComparison.Ordinal);

        PdfLetter one = rendered.Letters().First(letter => letter.Text == "O");
        PdfLetter col = rendered.Letters().First(letter => letter.Text == "C");
        Assert.True(col.Origin.Y < one.Origin.Y, "the band of columns did not start below the text before it");
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "columns");
    }

    [Fact]
    public void AContinuousBreakBalancesTheColumnsBeforeIt()
    {
        WordDocument document = WordDocument.Create();
        Section columns = document.Sections[0];
        columns.Properties.Columns.Count = 2;
        for (int i = 1; i <= 10; i++)
            columns.AddParagraph($"Item {i} of ten.").Format = FixedLines;

        Section after = document.Sections.Add(SectionStart.Continuous);
        after.AddParagraph("Plain text after the balanced columns.");

        using Rendered rendered = Rendered.Of(document);

        // Ten short lines balance five and five, so both columns are used even though all ten
        // would have fitted down the first one.
        Assert.Equal(1, rendered.PageCount);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X < GapLeft && letter.Text == "I");
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X >= SecondColumn && letter.Text == "I");
        Assert.Contains("Plain text after the balanced columns.", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnsSandwichedBetweenPlainTextShareThePage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Intro above the columns.");

        Section columns = document.Sections.Add(SectionStart.Continuous);
        columns.Properties.Columns.Count = 2;
        for (int i = 1; i <= 8; i++)
            columns.AddParagraph($"Point {i}.");

        Section outro = document.Sections.Add(SectionStart.Continuous);
        outro.AddParagraph("Outro below the columns.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(1, rendered.PageCount);

        // Reading the page top down: the intro, then the band of columns, then the outro.
        IReadOnlyList<string> lines = rendered.Lines();
        int introRow = Index(lines, "Intro above");
        int pointRow = Index(lines, "Point 1.");
        int outroRow = Index(lines, "Outro below");

        Assert.True(introRow < pointRow, "the columns did not start below the intro");
        Assert.True(pointRow < outroRow, "the outro did not continue below the columns");

        // The eight points balance four and four across the two columns.
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X >= SecondColumn && letter.Text == "P");
    }

    private static int Index(IReadOnlyList<string> lines, string text)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(text, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    [Fact]
    public void ThreeColumnsFillLeftToRight()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.Columns.Count = 3;
        for (int i = 1; i <= 90; i++)
            document.Sections[0].AddParagraph($"L{i}").Format = FixedLines;

        using Rendered rendered = Rendered.Of(document);

        // Columns of 126.4 points start at 72, 234.4 and 396.9.
        Assert.Equal(1, rendered.PageCount);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X is >= 71 and < 199);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X is >= 234 and < 361);
        Assert.Contains(rendered.Letters(), letter => letter.Origin.X >= 396);
    }
}
