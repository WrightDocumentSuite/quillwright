using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class TableTests
{
    private const string Sentence =
        "The quick brown fox jumps over the lazy dog while the cooper mends the barrel by the river. ";

    private static Table AddGrid(Section section, int rows, int columns, Length? width = null)
    {
        Table table = Table.Create(rows, columns, width ?? Length.FromCentimeters(16));
        section.Blocks.Add(table);
        return table;
    }

    /// <summary>The x position of the first glyph of a word on a page.</summary>
    private static double StartOf(Rendered rendered, string word) =>
        rendered.Letters().First(letter => letter.Text == word[..1]).Origin.X;

    [Fact]
    public void ATableDrawsItsText()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 2, 2);
        table[0, 0].SetText("Alpha");
        table[0, 1].SetText("Beta");
        table[1, 0].SetText("Gamma");
        table[1, 1].SetText("Delta");

        using Rendered rendered = Rendered.Of(document);
        string text = rendered.Text();

        foreach (string word in new[] { "Alpha", "Beta", "Gamma", "Delta" })
            Assert.Contains(word, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnsSitSideBySideInGridOrder()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 1, 3, Length.FromCentimeters(12));
        table[0, 0].SetText("Left");
        table[0, 1].SetText("Middle");
        table[0, 2].SetText("Right");

        using Rendered rendered = Rendered.Of(document);

        double left = StartOf(rendered, "L");
        double middle = StartOf(rendered, "M");
        double right = StartOf(rendered, "R");

        Assert.True(left < middle && middle < right);

        // Three equal columns of twelve centimetres, so each starts a third of that further on.
        Assert.Equal(Length.FromCentimeters(12).Points / 3, middle - left, 0.5);
        Assert.Equal(Length.FromCentimeters(12).Points / 3, right - middle, 0.5);
    }

    [Fact]
    public void RowsStackDownThePage()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 2, 1);
        table[0, 0].SetText("Top");
        table[1, 0].SetText("Bottom");

        using Rendered rendered = Rendered.Of(document);

        double top = rendered.Letters().First(letter => letter.Text == "T").Origin.Y;
        double bottom = rendered.Letters().First(letter => letter.Text == "B").Origin.Y;

        Assert.True(top > bottom);
    }

    [Fact]
    public void ACellWiderThanItsTextStillWrapsWhatDoesNotFit()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 1, 2);
        table[0, 0].SetText(Sentence);
        table[0, 1].SetText("Short");

        using Rendered rendered = Rendered.Of(document);

        // The sentence does not fit in eight centimetres, so the cell grew taller than one line.
        Assert.True(rendered.Baselines().Count >= 2);
    }

    [Fact]
    public void AMergedCellSpansTheColumnsItSwallowed()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 2, 3);
        table.MergeCells(0, 0, 1, 3);
        table[0, 0].SetText(Sentence);
        table[1, 0].SetText("Q1");
        table[1, 1].SetText("Q2");
        table[1, 2].SetText("Q3");

        using Rendered rendered = Rendered.Of(document);

        // Across all three columns the sentence fits on one line; in one column it would not, so
        // the table is exactly two lines tall and the second holds the three cells side by side.
        Assert.Equal(2, rendered.Baselines().Count);

        double second = rendered.Baselines()[1];
        double[] quarters = [.. rendered.Letters()
            .Where(letter => letter.Text == "Q" && Math.Abs(letter.Origin.Y - second) < 0.01)
            .Select(letter => letter.Origin.X)
            .Order()];

        Assert.Equal(3, quarters.Length);
        Assert.True(quarters[1] - quarters[0] > 100 && quarters[2] - quarters[1] > 100);
    }

    [Fact]
    public void AVerticallyMergedCellIsDrawnOnceAndReachesDown()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 3, 2);
        table.MergeCells(0, 0, 3, 1);
        table[0, 0].SetText("Tall");
        table[0, 1].SetText("One");
        table[1, 1].SetText("Two");
        table[2, 1].SetText("Three");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(1, Occurrences(rendered.Text(), "Tall"));
        Assert.Contains("Three", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void CellShadingIsPaintedBehindTheCell()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 1, 1);
        table[0, 0].SetText("Shaded");
        table[0, 0].Format = table[0, 0].Format with { Shading = Shading.Solid(WordColor.FromRgb(0x00FFFF)) };

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Contains("0 1 1 rg", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGridIsStroked()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 2, 2);

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        // Four cells, four edges each: every rule of a bordered grid is drawn.
        Assert.True(Occurrences(content, "\nS\n") >= 8);
    }

    [Fact]
    public void TableAndConditionalCellStylesAreDrawn()
    {
        WordDocument document = WordDocument.Create();
        Style style = document.Styles.GetOrAdd("StyledGrid", StyleKind.Table);
        style.TableFormat = TableFormat.Default with
        {
            Borders = BorderSet.AllWithInside(
                BorderLine.Single(Length.FromEighthPoints(8), WordColor.FromRgb(0xFF0000))),
        };
        style.ConditionalFormats.Add(new ConditionalTableStyle
        {
            Region = TableStyleRegion.FirstRow,
            CellFormat = TableCellFormat.Default with { Shading = Shading.Solid(WordColor.FromRgb(0x00FFFF)) },
        });

        Table table = AddGrid(document.Sections[0], 2, 2);
        table.Format = TableFormat.Default with
        {
            StyleId = style.Id,
            StyleOptions = TableStyleOptions.FirstRow,
            Width = TableWidth.FromPercent(100),
        };

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Contains("1 0 0 RG", content, StringComparison.Ordinal);
        Assert.Contains("0 1 1 rg", content, StringComparison.Ordinal);
        Assert.True(Occurrences(content, "\nS\n") >= 8);
    }

    [Fact]
    public void AHeavierCellBorderBeatsTheTablesOwn()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 1, 1);
        table[0, 0].Format = table[0, 0].Format with
        {
            Borders = BorderSet.All(BorderLine.Single(Length.FromEighthPoints(24), WordColor.FromRgb(0xFF0000))),
        };

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Contains("1 0 0 RG", content, StringComparison.Ordinal);
        Assert.Contains("3 w", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableWithNoGridSharesTheRoomByContent()
    {
        WordDocument document = WordDocument.Create();
        Table table = document.Sections[0].AddTable(1, 2);
        table[0, 0].SetText("x");
        table[0, 1].SetText(Sentence);

        using Rendered rendered = Rendered.Of(document);

        // The narrow cell keeps to its content, so the wide one gets most of the room and the
        // sentence needs fewer lines than an even split would force.
        double sentenceStart = rendered.Letters()
            .Where(letter => letter.Text == "T")
            .Min(letter => letter.Origin.X);

        Assert.True(sentenceStart < 72 + 100, $"The second column started at {sentenceStart:0.#}.");
    }

    [Fact]
    public void RowsMoveToTheNextPageWhenTheyRunOut()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 60, 1);
        for (int i = 0; i < 60; i++)
            table[i, 0].SetText("Row " + i);

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 2);
        Assert.Contains("Row 59", rendered.Text(rendered.PageCount - 1), StringComparison.Ordinal);
    }

    [Fact]
    public void AHeaderRowIsRepeatedOnEveryPage()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 60, 1);
        table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };
        table[0, 0].SetText("HEADING ROW");

        for (int i = 1; i < 60; i++)
            table[i, 0].SetText("Row " + i);

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 2);
        for (int page = 0; page < rendered.PageCount; page++)
            Assert.Contains("HEADING ROW", rendered.Text(page), StringComparison.Ordinal);
    }

    [Fact]
    public void ATallRowIsBrokenAcrossThePageBoundary()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.AddParagraph("Before the table.");

        Table table = AddGrid(section, 1, 1);
        table[0, 0].SetText(string.Concat(Enumerable.Repeat(Sentence, 80)));

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 2);
        Assert.NotEmpty(rendered.Text(0));
        Assert.NotEmpty(rendered.Text(1));
    }

    [Fact]
    public void ARowThatMayNotSplitMovesWhole()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];

        // Thirty-three lines of twenty points leave forty points, less than the row needs.
        for (int i = 0; i < 33; i++)
        {
            section.AddParagraph("Filler " + i).Format = ParagraphFormat.Default with
            {
                LineSpacingRule = LineSpacingRule.Exact,
                LineSpacing = Length.FromPoints(20),
            };
        }

        Table table = AddGrid(section, 1, 1);
        table.Rows[0].Format = table.Rows[0].Format with { CannotSplit = true };
        table[0, 0].SetText(string.Concat(Enumerable.Repeat(Sentence, 6)));

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.DoesNotContain("quick brown fox", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void CellsAlignTheirContentVertically()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 1, 2);
        table[0, 0].SetText(string.Concat(Enumerable.Repeat(Sentence, 2)));
        table[0, 1].SetText("Bottom");
        table[0, 1].Format = table[0, 1].Format with { VerticalAlignment = VerticalCellAlignment.Bottom };

        using Rendered rendered = Rendered.Of(document);

        double first = rendered.Letters().First(letter => letter.Text == "T").Origin.Y;
        double aligned = rendered.Letters().First(letter => letter.Text == "B").Origin.Y;

        Assert.True(aligned < first - 10, "The bottom-aligned cell drew its text at the top.");
    }

    [Fact]
    public void ANestedTableIsDrawnInsideItsCell()
    {
        WordDocument document = WordDocument.Create();
        Table outer = AddGrid(document.Sections[0], 1, 1);
        outer[0, 0].Blocks.Clear();

        Table inner = Table.Create(1, 2, Length.FromCentimeters(10));
        inner[0, 0].SetText("Inner left");
        inner[0, 1].SetText("Inner right");
        outer[0, 0].Blocks.Add(inner);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Inner left", rendered.Text(), StringComparison.Ordinal);
        Assert.Contains("Inner right", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACentredTableSitsBetweenTheMargins()
    {
        WordDocument document = WordDocument.Create();
        Table table = AddGrid(document.Sections[0], 1, 1, Length.FromCentimeters(6));
        table.Format = table.Format with { Alignment = TableAlignment.Center };
        table[0, 0].SetText("Centred");

        using Rendered rendered = Rendered.Of(document);

        SectionProperties properties = document.Sections[0].Properties;
        double content = properties.PageWidth.Points - properties.Margins.Left.Points - properties.Margins.Right.Points;
        double expected = properties.Margins.Left.Points + ((content - Length.FromCentimeters(6).Points) / 2);

        Assert.Equal(expected + CellPaddingLeft, StartOf(rendered, "C"), 1);
    }

    /// <summary>What Word leaves between a cell's border and its text when nothing says otherwise.</summary>
    private const double CellPaddingLeft = 5.4;

    private static int Occurrences(string text, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
