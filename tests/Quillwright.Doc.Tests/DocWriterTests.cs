using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// A document written by the writer and read back by the reader has been through every
/// structure the format has: the container, the header, the piece table, both kinds of
/// formatting page and both bin tables. What survives the trip is what actually works.
/// </summary>
public class DocWriterTests
{
    [Fact]
    public void AnEmptyDocument_IsAValidFile()
    {
        WordDocument reopened = RoundTrip(WordDocument.Create());

        Assert.Single(reopened.Sections);
        Assert.NotEmpty(reopened.Sections[0].Blocks);
    }

    [Fact]
    public void Text_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("The first paragraph."));
        Add(document, new Paragraph("The second one, with a tab\there."));

        WordDocument reopened = RoundTrip(document);
        List<Paragraph> paragraphs = [.. reopened.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.Equal("The first paragraph.", paragraphs[0].Text);
        Assert.Equal("The second one, with a tab\there.", paragraphs[1].Text);
    }

    [Fact]
    public void NonLatinText_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("Русский текст, ελληνικά, 日本語"));

        Assert.Equal("Русский текст, ελληνικά, 日本語", FirstParagraph(RoundTrip(document)).Text);
    }

    [Fact]
    public void CharacterFormatting_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("plain ");
        paragraph.AppendText("bold ", RunFormat.Default with { Bold = true });
        paragraph.AppendText("big", RunFormat.Default with { Size = Length.FromPoints(18) });
        Add(document, paragraph);

        Paragraph reopened = FirstParagraph(RoundTrip(document));
        List<Run> runs = [.. reopened.Runs];

        Assert.Equal("plain bold big", reopened.Text);
        Assert.Contains(runs, r => r.Format.Bold == true);
        Assert.Contains(runs, r => r.Format.Size == Length.FromPoints(18));
    }

    [Fact]
    public void ParagraphFormatting_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("Centred") { Format = ParagraphFormat.Default with { Alignment = ParagraphAlignment.Center } });
        Add(document, new Paragraph("Indented") { Format = ParagraphFormat.Default with { IndentLeft = Length.FromCentimeters(2) } });

        List<Paragraph> paragraphs = [.. RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.Equal(ParagraphAlignment.Center, paragraphs[0].Format.Alignment);
        Assert.Equal(Length.FromCentimeters(2), paragraphs[1].Format.IndentLeft);
    }

    [Fact]
    public void AFont_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("monospaced", RunFormat.Default with { FontAscii = "Consolas" });
        Add(document, paragraph);

        Paragraph reopened = FirstParagraph(RoundTrip(document));

        Assert.Equal("Consolas", reopened.Runs.First().Format.FontAscii);
    }

    [Fact]
    public void PageSetup_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.Orientation = PageOrientation.Landscape;
        document.Sections[0].Properties.PageWidth = Length.FromMillimeters(297);
        document.Sections[0].Properties.PageHeight = Length.FromMillimeters(210);
        Add(document, new Paragraph("Landscape"));

        SectionProperties properties = RoundTrip(document).Sections[0].Properties;

        Assert.Equal(PageOrientation.Landscape, properties.Orientation);
        Assert.Equal(Length.FromMillimeters(297).Twips, properties.PageWidth.Twips);
    }

    [Theory]
    [InlineData(ListNumberFormat.Decimal)]
    [InlineData(ListNumberFormat.UpperRoman)]
    [InlineData(ListNumberFormat.LowerLetter)]
    public void ThePageNumberScheme_SurvivesTheRoundTrip(ListNumberFormat scheme)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.PageNumbering.Format = scheme;
        document.Sections[0].Properties.PageNumbering.Start = 7;
        Add(document, new Paragraph("numbered pages"));

        PageNumbering numbering = RoundTrip(document).Sections[0].Properties.PageNumbering;

        Assert.Equal(scheme, numbering.Format);
        Assert.Equal(7, numbering.Start);
    }

    [Fact]
    public void TwoSections_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("First section"));
        var second = new Section();
        second.Blocks.Add(new Paragraph("Second section"));
        document.Sections.Add(second);

        WordDocument reopened = RoundTrip(document);

        Assert.Equal(2, reopened.Sections.Count);
        Assert.Contains("First section", reopened.Sections[0].GetText(), StringComparison.Ordinal);
        Assert.Contains("Second section", reopened.Sections[1].GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATable_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Add(document, NewTable(["a", "b"], ["c", "d"]));
        Add(document, new Paragraph("after"));

        Table table = RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Table>().Single();

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Cells.Count);
        Assert.Equal("a", table.Rows[0].Cells[0].GetText().Trim());
        Assert.Equal("d", table.Rows[1].Cells[1].GetText().Trim());
    }

    [Fact]
    public void ManyParagraphs_SpillAcrossFormattingPagesAndStillComeBack()
    {
        // A page of paragraph formatting holds at most twenty-nine entries, so this many
        // forces the writer to close pages and the bin table to name several of them.
        WordDocument document = WordDocument.Create();
        for (int i = 0; i < 500; i++)
            Add(document, new Paragraph($"Paragraph number {i}"));

        List<Paragraph> paragraphs = [.. RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.Equal(500, paragraphs.Count(static p => p.Text.StartsWith("Paragraph number", StringComparison.Ordinal)));
        Assert.Equal("Paragraph number 499", paragraphs[499].Text);
    }

    [Fact]
    public void ManyRuns_SpillAcrossFormattingPagesAndStillComeBack()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        for (int i = 0; i < 400; i++)
            paragraph.AppendText($"r{i} ", RunFormat.Default with { Size = Length.FromHalfPoints(20 + (i % 30)) });
        Add(document, paragraph);

        Paragraph reopened = FirstParagraph(RoundTrip(document));

        Assert.StartsWith("r0 r1 r2 ", reopened.Text, StringComparison.Ordinal);
        Assert.EndsWith("r399 ", reopened.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStyle_SurvivesTheRoundTripAsItsIdentifier()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("A heading") { Format = ParagraphFormat.Default with { StyleId = "Heading1" } });

        Paragraph reopened = FirstParagraph(RoundTrip(document));

        Assert.Equal("heading1", reopened.Format.StyleId, ignoreCase: true);
    }

    [Fact]
    public void TheFileOpensAsACompoundContainerWithTheExpectedStreams()
    {
        byte[] file = DocWriter.Save(WordDocument.Create());
        CompoundFile container = CompoundFile.Open(file);

        Assert.Contains("WordDocument", container.StreamNames);
        Assert.Contains("1Table", container.StreamNames);
    }

    private static WordDocument RoundTrip(WordDocument document) =>
        DocReader.Load(DocWriter.Save(document));

    private static Paragraph FirstParagraph(WordDocument document) =>
        document.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();

    private static void Add(WordDocument document, Block block) => document.Sections[0].Blocks.Add(block);

    private static Table NewTable(params string[][] rows)
    {
        var table = new Table();
        foreach (string[] cells in rows)
        {
            var row = new TableRow();
            foreach (string text in cells)
            {
                var cell = new TableCell();
                cell.Blocks.Add(new Paragraph(text));
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }
}
