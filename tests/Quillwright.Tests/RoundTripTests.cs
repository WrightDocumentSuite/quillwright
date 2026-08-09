using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Quillwright.Tests;

public class RoundTripTests
{
    [Fact]
    public async Task EmptyDocument_IsValidAndReadable()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Hello, Quillwright.");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "an empty document");

        Assert.Single(reloaded.Sections);
        Assert.Equal("Hello, Quillwright.", reloaded.GetText());
    }

    [Fact]
    public async Task FormattedRuns_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("normal ");
        paragraph.AppendText("bold ", RunFormat.Default with { Bold = true });
        paragraph.AppendText("red", RunFormat.Default with
        {
            Color = WordColor.FromRgb(0xC00000),
            Size = Length.FromPoints(18),
            Underline = UnderlineStyle.Double,
        });

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "formatted runs");
        Paragraph result = reloaded.Paragraphs.Single();

        Assert.Equal("normal bold red", result.Text);
        Assert.Equal(3, result.Runs.Count);
        Assert.True(result.Runs[1].Format.Bold);
        Assert.Equal(WordColor.FromRgb(0xC00000), result.Runs[2].Format.Color);
        Assert.Equal(Length.FromPoints(18), result.Runs[2].Format.Size);
        Assert.Equal(UnderlineStyle.Double, result.Runs[2].Format.Underline);
    }

    [Fact]
    public async Task ParagraphFormatting_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Centred");
        paragraph.Format = paragraph.Format with
        {
            Alignment = ParagraphAlignment.Center,
            SpacingBefore = Length.FromPoints(12),
            IndentLeft = Length.FromCentimeters(1),
            Borders = BorderSet.All(BorderLine.Single(Length.FromEighthPoints(8), WordColor.FromRgb(0x2278D4))),
            Shading = Shading.Solid(WordColor.FromRgb(0xEEEEEE)),
            Tabs = new[] { new TabStop(Length.FromInches(3), TabAlignment.Center, TabLeader.Dot) },
        };

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "paragraph formatting");
        ParagraphFormat format = reloaded.Paragraphs.Single().Format;

        Assert.Equal(ParagraphAlignment.Center, format.Alignment);
        Assert.Equal(Length.FromPoints(12), format.SpacingBefore);
        Assert.Equal(Length.FromCentimeters(1), format.IndentLeft);
        Assert.Equal(BorderStyle.Single, format.Borders?.Top?.Style);
        Assert.Equal(WordColor.FromRgb(0xEEEEEE), format.Shading?.Fill);
        Assert.Equal(TabLeader.Dot, format.Tabs[0].Leader);
    }

    [Fact]
    public async Task Tables_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Table table = document.Sections[0].AddTable(2, 3);
        table[0, 0].SetText("A1");
        table[1, 2].SetText("C2");
        table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a table");
        Table result = reloaded.Sections[0].Tables.Single();

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(3, result.Rows[0].Cells.Count);
        Assert.Equal("A1", result[0, 0].GetText());
        Assert.Equal("C2", result[1, 2].GetText());
        Assert.True(result.Rows[0].Format.IsHeader);
    }

    [Fact]
    public async Task Sections_SplitAndRejoinAroundTheirBreaks()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("first section");
        Section second = document.Sections.Add(SectionStart.NextPage);
        second.Properties.Orientation = PageOrientation.Landscape;
        second.Properties.SetPaperSize(Length.FromMillimeters(210), Length.FromMillimeters(297));
        second.AddParagraph("second section");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "two sections");

        Assert.Equal(2, reloaded.Sections.Count);
        Assert.Single(reloaded.Sections[0].Blocks);
        Assert.True(reloaded.Sections[0].Blocks.Paragraphs.Single().IsSectionBreakCarrier);
        Assert.Equal("first section", reloaded.Sections[0].Blocks.Paragraphs.First().Text);
        Assert.Equal("second section", reloaded.Sections[1].Blocks.Paragraphs.First().Text);
        Assert.Equal(PageOrientation.Landscape, reloaded.Sections[1].Properties.Orientation);
        Assert.True(reloaded.Sections[1].Properties.PageWidth > reloaded.Sections[1].Properties.PageHeight);
    }

    [Fact]
    public async Task AnEmptySectionCarrierRemainsInTheEditableModel()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph();
        document.Sections.Add(SectionStart.NextPage).AddParagraph("second");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "empty section carrier");

        Paragraph carrier = reloaded.Sections[0].Blocks.Paragraphs.Single();
        Assert.True(carrier.IsEmpty);
        Assert.True(carrier.IsSectionBreakCarrier);
    }

    [Fact]
    public async Task SectionStartIsWrittenOnThePrecedingSectionProperties()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("first");
        document.Sections.Add(SectionStart.Continuous).AddParagraph("second");
        document.Sections.Add(SectionStart.OddPage).AddParagraph("third");

        using MemoryStream package = await DocumentFixture.SaveAsync(document);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (Stream stream = archive.GetEntry("word/document.xml")!.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            string xml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            MatchCollection types = Regex.Matches(xml, "<w:type w:val=\"([^\"]+)\"/>");
            Assert.Equal(new[] { "continuous", "oddPage" }, types.Select(match => match.Groups[1].Value));
        }

        package.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            new[] { SectionStart.NextPage, SectionStart.Continuous, SectionStart.OddPage },
            reloaded.Sections.Select(section => section.Properties.Start));
    }

    [Fact]
    public async Task HeadersAndFooters_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.Sections[0].Headers.GetOrCreate().AddParagraph("page header");
        document.Sections[0].Footers.GetOrCreate().AddParagraph("page footer");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "headers and footers");

        Assert.Equal("page header", reloaded.Sections[0].Headers.Default?.GetText());
        Assert.Equal("page footer", reloaded.Sections[0].Footers.Default?.GetText());
    }

    [Fact]
    public async Task GeneratedFixedLayoutTextBoxesAndLinesSurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Paragraph anchor = document.Sections[0].AddParagraph();
        var content = new TextBox();
        content.AddParagraph("editable fixed text");
        var placement = new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Page,
            VerticalFrom = AnchorOrigin.Page,
            OffsetX = Length.FromPoints(40),
            OffsetY = Length.FromPoints(60),
            Wrapping = TextWrapping.None,
        };
        anchor.AppendObject(Shape.CreateTextBox(
            Length.FromPoints(200), Length.FromPoints(20), content, placement));
        anchor.AppendObject(Shape.CreateLine(
            Length.FromPoints(200), Length.FromPoints(0.05),
            BorderLine.Single(Length.FromPoints(1), WordColor.Black), placement));

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "generated fixed layout drawings");
        Shape[] shapes = [.. reloaded.Paragraphs.SelectMany(paragraph => paragraph.Objects)
            .Select(anchored => anchored.Object).OfType<Shape>()];

        Assert.Equal(2, shapes.Length);
        Shape textBox = Assert.Single(shapes, shape => shape.GetText() == "editable fixed text");
        Assert.Equal(Length.Zero, textBox.InsetLeft);
        Assert.Equal(Length.Zero, textBox.InsetRight);
        Assert.Equal(Length.Zero, textBox.InsetTop);
        Assert.Equal(Length.Zero, textBox.InsetBottom);
        Assert.Contains(shapes, shape => shape.IsLine);
    }

    [Fact]
    public async Task HyperlinksBookmarksAndBreaks_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Go to ");
        int start = paragraph.TextLength;
        paragraph.AppendText("the site", RunFormat.Default with { StyleId = "Hyperlink" });
        paragraph.AddRange(new Hyperlink { Url = "https://example.com/a b" }, start, paragraph.TextLength - start);
        paragraph.AddMark(new BookmarkStart { Id = 1, Name = "anchor" }, 0);
        paragraph.AddMark(new BookmarkEnd { Id = 1 });
        paragraph.AppendBreak(BreakKind.Page);
        paragraph.AppendText("after the break");
        document.Styles.GetOrAdd("Hyperlink", StyleKind.Character);

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "links, bookmarks and breaks");
        Paragraph result = reloaded.Paragraphs.Single();

        Assert.Equal("https://example.com/a b", result.Ranges.Select(r => r.Range).OfType<Hyperlink>().Single().Url);
        Assert.Contains(result.Marks, m => m.Mark is BookmarkStart { Name: "anchor" });
        Assert.Contains(result.Objects, o => o.Object is Break { Kind: BreakKind.Page });
    }

    [Fact]
    public async Task NotesAndComments_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Body text here.");
        document.AddFootnote(paragraph, "A footnote.");
        document.AddComment(paragraph, 0, 4, "Check this", "Reviewer", "R");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "notes and comments");

        Assert.Contains(reloaded.Footnotes, note => note.GetText().Contains("A footnote."));
        Comment comment = Assert.Single(reloaded.Comments);
        Assert.Equal("Reviewer", comment.Author);
        Assert.Contains("Check this", comment.GetText());
    }

    [Fact]
    public async Task Numbering_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        int listId = document.Numbering.AddBulletList();
        foreach (string item in (string[])["one", "two", "three"])
        {
            Paragraph paragraph = document.Sections[0].AddParagraph(item);
            paragraph.Format = paragraph.Format with { NumberingId = listId, NumberingLevel = 0, StyleId = "ListParagraph" };
        }

        document.Styles.GetOrAdd("ListParagraph");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a bullet list");

        Assert.Equal(3, reloaded.Paragraphs.Count());
        Assert.All(reloaded.Paragraphs, p => Assert.Equal(listId, p.Format.NumberingId));
        Assert.Equal(9, reloaded.Numbering.Definitions.Single().Levels.Count);
        Assert.Equal(ListNumberFormat.Bullet, reloaded.Numbering.ResolveLevel(listId, 0)?.Format);
    }

    [Fact]
    public async Task Images_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        ImageData image = ImageData.FromBytes(TestImages.Png);
        document.Sections[0].AddParagraph().AppendPicture(image, Length.FromCentimeters(3), Length.FromCentimeters(2));

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "an inline picture");
        Picture picture = reloaded.Paragraphs.Single().Objects.Select(o => o.Object).OfType<Picture>().Single();

        Assert.InRange(picture.Width.Emu, Length.FromCentimeters(3).Emu - 1000, Length.FromCentimeters(3).Emu + 1000);
        Assert.Equal("image/png", picture.Image.ContentType);
        Assert.Equal(TestImages.Png.Length, picture.Image.Bytes.Length);
    }

    [Fact]
    public async Task DocumentProperties_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("x");
        document.Properties.Title = "Contract";
        document.Properties.Creator = "Quillwright";

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "core properties");

        Assert.Equal("Contract", reloaded.Properties.Title);
        Assert.Equal("Quillwright", reloaded.Properties.Creator);
    }
}
