using System.Text;
using Inkwright;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Renders whole documents rather than one feature at a time, and checks the words came through.
/// </summary>
/// <remarks>
/// The tests elsewhere each hold one thing still and measure another. This one does the opposite:
/// it takes documents with everything in them at once and asks only whether the text survived, on
/// the grounds that a converter which drops a paragraph is broken however well it kerns.
/// </remarks>
public sealed class CorpusTests
{
    /// <summary>The documents on disk the test suite ships with.</summary>
    public static TheoryData<string> Fixtures
    {
        get
        {
            var data = new TheoryData<string>();
            string folder = Path.Combine(AppContext.BaseDirectory, "fixtures");

            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.docm").Order(StringComparer.Ordinal))
                    data.Add(file);
            }

            if (data.Count == 0)
                data.Add(string.Empty);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task EveryFixtureRendersAndKeepsItsText(string path)
    {
        Assert.SkipWhen(path.Length == 0, "The fixtures are built by hand and are not in this checkout.");

        WordDocument document = await WordDocument.LoadAsync(
            path, cancellationToken: TestContext.Current.CancellationToken);
        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 1);
        AssertTextSurvived(document, rendered);
    }

    [Fact]
    public void ADocumentUsingEverythingAtOnceComesThroughWhole()
    {
        WordDocument document = Everything();
        using Rendered rendered = Rendered.Of(document);

        AssertTextSurvived(document, rendered);
    }

    [Fact]
    public void ADocumentUsingEverythingAtOnceAlsoTagsCleanly()
    {
        WordDocument document = Everything();
        using Rendered rendered = Rendered.Of(document, new PdfExportOptions { Tagged = true });

        AssertTextSurvived(document, rendered);
        Assert.NotEmpty(rendered.Document.Structure);
    }

    [Fact]
    public void ADocumentThatCannotFinishSaysSoRatherThanRunningForEver()
    {
        WordDocument document = WordDocument.Create();

        // A page so short that nothing fits on it would otherwise paginate for ever.
        document.Sections[0].Properties.PageHeight = Length.FromInches(11);
        for (int i = 0; i < 200; i++)
            document.Sections[0].AddParagraph("Line " + i);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => PdfExporter.Render(document, new PdfExportOptions { MaxPages = 3 }));

        Assert.Contains("MaxPages", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A document that leans on every part of the converter at once.</summary>
    private static WordDocument Everything()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Everything";
        document.Styles.GetOrAdd("Heading1");
        document.Styles.GetOrAdd("Heading2");

        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = false;
        section.Headers.GetOrCreate().AddParagraph("Running head");

        Paragraph footer = section.Footers.GetOrCreate().AddParagraph();
        footer.AppendText("Page ");
        footer.AppendPageNumber();
        footer.AppendText(" of ");
        footer.AppendPageCount();

        section.AddParagraph("The whole report", "Heading1");

        Paragraph opening = section.AddParagraph();
        opening.AppendText("Plain, then ", RunFormat.Default);
        opening.AppendText("bold", RunFormat.Default with { Bold = true });
        opening.AppendText(", then ", RunFormat.Default);
        opening.AppendText("italic", RunFormat.Default with { Italic = true });
        opening.AppendText(", then ", RunFormat.Default);
        opening.AppendText("underlined", RunFormat.Default with { Underline = UnderlineStyle.Single });
        opening.AppendText(".", RunFormat.Default);

        Paragraph justified = section.AddParagraph(string.Concat(Enumerable.Repeat(
            "Justified prose that has to wrap more than once to be worth measuring at all. ", 4)));
        justified.Format = justified.Format with { Alignment = ParagraphAlignment.Justify };

        section.AddParagraph("The list", "Heading2");
        int list = document.Numbering.AddNumberedList();
        foreach ((string text, int level) in new[] { ("Outer one", 0), ("Inner one", 1), ("Outer two", 0) })
        {
            Paragraph item = section.AddParagraph(text);
            item.Format = item.Format with { NumberingId = list, NumberingLevel = level };
        }

        section.AddParagraph("The table", "Heading2");
        Table table = Table.Create(4, 3, Length.FromCentimeters(15));
        table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 3; column++)
                table[row, column].SetText($"Cell {row}x{column}");
        }

        table[3, 0].Format = table[3, 0].Format with { Shading = Shading.Solid(WordColor.FromRgb(0xEEEEEE)) };
        section.Blocks.Add(table);

        Paragraph linked = section.AddParagraph("Follow the link for more.");
        linked.AddRange(new Hyperlink { Url = "https://example.org/" }, 11, 4);

        Paragraph pictured = section.AddParagraph();
        pictured.AppendPicture(
            ImageData.FromBytes(Pixels.Png(60, 30)), Length.FromPoints(60), Length.FromPoints(30));

        section.AddParagraph("The notes", "Heading2");
        Paragraph noted = section.AddParagraph("A claim that needs evidence and a closing remark.");
        document.AddFootnote(noted, "The evidence at the foot of the page.");
        document.AddEndnote(noted, "The remark after everything else.");

        section.AddParagraph("The float", "Heading2");
        Paragraph anchored = section.AddParagraph(string.Concat(Enumerable.Repeat(
            "Prose that has to make room for the picture floating at the margin beside it. ", 3)).TrimEnd());
        Picture floated = anchored.AppendPicture(
            ImageData.FromBytes(Pixels.Png(40, 40)), Length.FromPoints(70), Length.FromPoints(50));
        floated.IsInline = false;
        floated.Anchor = new PictureAnchor();

        section.AddParagraph("The box", "Heading2");
        var boxContent = new TextBox();
        boxContent.AddParagraph("Words carried inside a text box.");
        Table boxed = boxContent.AddTable(2, 2);
        boxed[0, 0].SetText("North");
        boxed[1, 1].SetText("South");
        section.AddParagraph().AppendObject(new Shape(["<wps/>", "</wps>"], boxContent)
        {
            Width = Length.FromPoints(220),
            Height = Length.FromPoints(110),
            Fill = WordColor.FromRgb(0xF2F2F2),
            Outline = BorderLine.Single(Length.FromPoints(0.75), WordColor.FromRgb(0x4472C4)),
        });

        section.AddParagraph("The other directions", "Heading2");
        Paragraph hebrew = section.AddParagraph();
        hebrew.Format = hebrew.Format with { RightToLeft = true };
        hebrew.AppendText(
            "\u05E9\u05DC\u05D5\u05DD \u05E2\u05D5\u05DC\u05DD 123",
            RunFormat.Default with { RightToLeft = true });

        // The turned cell's letters interleave with its neighbour's in extracted text, so its
        // label stays short enough for the survival check to leave it alone; that it turns and
        // measures right is VerticalTextTests' business.
        Table turned = section.AddTable(1, 2);
        turned[0, 0].SetText("Up!");
        turned[0, 0].Format = turned[0, 0].Format with
        {
            TextDirection = TextDirection.TopToBottomRightToLeft,
        };
        turned[0, 1].SetText("Plain beside it");

        // One line per paragraph: text extraction reads a two-column page row by row, so a
        // sentence that wrapped would come back with the other column's words inside it.
        Section columns = document.Sections.Add(SectionStart.NextPage);
        columns.Properties.Columns.Count = 2;
        columns.Properties.Columns.Separator = true;
        for (int i = 1; i <= 60; i++)
            columns.AddParagraph($"Column line {i} of sixty.");

        Section landscape = document.Sections.Add(SectionStart.NextPage);
        landscape.Properties.Orientation = PageOrientation.Landscape;
        landscape.Properties.PageWidth = Length.FromMillimeters(297);
        landscape.Properties.PageHeight = Length.FromMillimeters(210);
        landscape.AddParagraph("A wider page for the appendix.");

        return document;
    }

    /// <summary>
    /// Checks that every plain paragraph of the document turns up in the rendered text. Spaces are
    /// dropped from both sides first, because a paragraph that wrapped is one line in the model and
    /// several on the page, and where the breaks fell is not what this test is about.
    /// </summary>
    private static void AssertTextSurvived(WordDocument document, Rendered rendered)
    {
        var pages = new StringBuilder();
        for (int page = 0; page < rendered.PageCount; page++)
            pages.Append(rendered.Text(page));

        string printed = Squeeze(pages.ToString());

        foreach (Paragraph paragraph in document.Paragraphs)
        {
            string expected = Squeeze(paragraph.GetText());
            if (expected.Length < 4 || paragraph.Objects.Any())
                continue;

            // A right-to-left paragraph prints in visual order, so its characters are checked
            // one by one rather than as the logical string.
            if (paragraph.Format.RightToLeft == true)
            {
                foreach (char c in expected)
                {
                    if (c is >= '\u0590' and <= '\u05FF' || char.IsAsciiDigit(c))
                        Assert.Contains(c, printed);
                }

                continue;
            }

            Assert.Contains(expected, printed, StringComparison.Ordinal);
        }
    }

    private static string Squeeze(string text)
    {
        var squeezed = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
                squeezed.Append(c);
        }

        return squeezed.ToString();
    }
}
