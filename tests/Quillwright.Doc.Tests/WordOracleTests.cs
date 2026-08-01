using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Opens files the writer produced in Word itself.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this project checks the writer against this library's own reader,
/// which shares its understanding of the format and so shares any misunderstanding. Word is
/// the only judge of whether a file is really valid: it repairs, or refuses, anything it
/// does not accept, and a document that opens without a repair prompt and reads back its own
/// text has been through the real parser.
/// </para>
/// <para>
/// These tests need Word installed and are opt-in through the <c>QUILLWRIGHT_WORD_ORACLE</c>
/// environment variable, because launching Word takes seconds per file and cannot run on a
/// build server. Automation is driven through late binding so that nothing here depends on
/// an interop assembly.
/// </para>
/// </remarks>
[Trait("Category", "word-oracle")]
[SupportedOSPlatform("windows")]
public class WordOracleTests
{
    private static bool Enabled => WordOracle.Enabled;

    [Fact]
    public void APlainDocument_OpensInWordWithItsTextIntact()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("The quick brown fox."));
        document.Sections[0].Blocks.Add(new Paragraph("Jumped over the lazy dog."));

        Assert.Contains("The quick brown fox.", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void AFormattedDocument_OpensInWord()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("plain ");
        paragraph.AppendText("bold ", RunFormat.Default with { Bold = true });
        paragraph.AppendText("coloured", RunFormat.Default with { Color = WordColor.FromRgb(0xC00000), Size = Length.FromPoints(16) });
        document.Sections[0].Blocks.Add(paragraph);
        document.Sections[0].Blocks.Add(new Paragraph("Centred") { Format = ParagraphFormat.Default with { Alignment = ParagraphAlignment.Center } });

        Assert.Contains("coloured", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithATable_OpensInWord()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        for (int row = 0; row < 2; row++)
        {
            var line = new TableRow();
            for (int column = 0; column < 3; column++)
            {
                var cell = new TableCell();
                cell.Blocks.Add(new Paragraph($"r{row}c{column}"));
                line.Cells.Add(cell);
            }

            table.Rows.Add(line);
        }

        document.Sections[0].Blocks.Add(table);
        document.Sections[0].Blocks.Add(new Paragraph("after the table"));

        Assert.Contains("r1c2", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithEveryStory_OpensInWord()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Body text with extras.");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddFootnote(paragraph, "a footnote");
        document.AddEndnote(paragraph, "an endnote");
        document.AddComment(paragraph, 0, 4, "a comment", "Reviewer", "R");
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("a header"));
        document.Sections[0].Footers.GetOrCreate().Blocks.Add(new Paragraph("a footer"));

        Assert.Contains("Body text with extras.", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithSectionsAndPageSetup_OpensInWord()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.Orientation = PageOrientation.Landscape;
        document.Sections[0].Blocks.Add(new Paragraph("Landscape section"));

        var portrait = new Section { Properties = { Orientation = PageOrientation.Portrait } };
        portrait.Blocks.Add(new Paragraph("Portrait section"));
        document.Sections.Add(portrait);

        Assert.Contains("Portrait section", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithAListAndAHyperlink_OpensInWord()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        document.Sections[0].Blocks.Add(new Paragraph("First item")
        {
            Format = ParagraphFormat.Default with { NumberingId = list, NumberingLevel = 0 },
        });

        var paragraph = new Paragraph("Visit the site");
        paragraph.AddRange(new Hyperlink { Url = "https://example.com/" }, 6, 8);
        document.Sections[0].Blocks.Add(paragraph);

        Assert.Contains("First item", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithAPicture_OpensInWord()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("Look: ");
        paragraph.AppendPicture(
            ImageData.FromBytes(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")),
            Length.FromCentimeters(2),
            Length.FromCentimeters(2));
        document.Sections[0].Blocks.Add(paragraph);

        Assert.Contains("Look:", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ACommentedRange_IsTheRangeWordHighlights()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        document.Sections[0].Blocks.Add(paragraph);
        document.AddComment(paragraph, 4, 5, "about this word", "Reviewer", "R");

        object scope = Inspect(document, opened => Get(Get(Invoke(Get(opened, "Comments")!, "Item", 1)!, "Scope")!, "Text")!);

        Assert.Equal("quick", (string)scope);
    }

    /// <summary>
    /// Word learns who answers whom, and when a comment was written, from the comment tree of
    /// <c>AtrdExtra</c> ([MS-DOC] 2.9.5) — the one structure in the binary format that says
    /// anything about a conversation.
    /// </summary>
    [Fact]
    public void AReplyAndItsDate_AreWhatWordShows()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The quick brown fox");
        document.Sections[0].Blocks.Add(paragraph);

        Comment question = document.AddComment(paragraph, 4, 5, "which one?", "Ada", "A");
        question.Date = new DateTimeOffset(2024, 3, 17, 9, 41, 0, TimeSpan.Zero);
        document.AddReply(question, "that one", "Grace", "G");

        object seen = Inspect(document, opened =>
        {
            object comments = Get(opened, "Comments")!;
            object first = Invoke(comments, "Item", 1)!;
            object reply = Invoke(comments, "Item", 2)!;
            object? parent = Get(reply, "Ancestor");
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Get(comments, "Count")}|{(parent is null ? 0 : Get(parent, "Index"))}|{Get(first, "Date"):yyyy-MM-dd HH:mm}");
        });

        Assert.Equal("2|1|2024-03-17 09:41", (string)seen);
    }

    [Fact]
    public void ADocumentWithOneHeader_DoesNotTurnOnFacingPagesInWord()
    {
        // The properties block calls this fFacingPages. Setting it on a document with a
        // single header is what leaves the even pages with none.
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("the only header"));

        Assert.Equal(0, Facing(document));
    }

    [Fact]
    public void ADocumentWithAnEvenPageHeader_TurnsOnFacingPagesInWord()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("odd pages"));
        document.Sections[0].Headers.GetOrCreate(HeaderFooterKind.Even).Blocks.Add(new Paragraph("even pages"));

        Assert.NotEqual(0, Facing(document));
    }

    [Fact]
    public void TabStopsAndBorders_OpenInWord()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("first\tsecond")
        {
            Format = ParagraphFormat.Default with
            {
                Tabs = new EquatableArray<TabStop>(
                [
                    new TabStop(Length.FromTwips(1440), TabAlignment.Center, TabLeader.Dot),
                    new TabStop(Length.FromTwips(2880), TabAlignment.Right),
                ]),
                Borders = new BorderSet { Top = BorderLine.Single(Length.FromEighthPoints(12), WordColor.Auto) },
                Shading = Shading.Solid(WordColor.FromRgb(0xFFFF00)),
            },
        });

        Assert.Contains("first", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ShadedTableCells_OpenInWord()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell
        {
            Format = TableCellFormat.Default with { Shading = Shading.Solid(WordColor.FromRgb(0x00FF00)) },
        };

        cell.Blocks.Add(new Paragraph("shaded"));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);
        document.Sections[0].Blocks.Add(new Paragraph("after"));

        Assert.Contains("shaded", OpenInWord(document), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocumentsTitle_IsTheOneWordShows()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Properties.Title = "Quarterly report";
        document.Properties.Creator = "Ada Lovelace";

        // The built-in properties are a parameterised property rather than a collection with
        // an Item method, so the index goes to the property itself.
        object title = Inspect(document, opened => Get(Indexed(opened, "BuiltInDocumentProperties", 1), "Value")!);

        Assert.Equal("Quarterly report", (string)title);
    }

    /// <summary>Whether Word believes the document wants different headers on even pages.</summary>
    private static int Facing(WordDocument document) =>
        Convert.ToInt32(Inspect(document, opened => Get(Get(opened, "PageSetup")!, "OddAndEvenPagesHeaderFooter")!), CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes the document, opens it in Word read-only and returns the text Word read. Word
    /// is asked not to prompt, so a file it would have offered to repair comes back empty or
    /// throws rather than hanging the test.
    /// </summary>
    private static string OpenInWord(WordDocument document) =>
        (string)Inspect(document, opened => Get(Get(opened, "Content")!, "Text")!);

    /// <summary>Writes the document, opens it in Word read-only and asks Word about it.</summary>
    private static object Inspect(WordDocument document, Func<object, object> ask)
    {
        Assert.SkipUnless(Enabled, "Set QUILLWRIGHT_WORD_ORACLE=1 and install Word to run the oracle tests.");

        string path = Path.Combine(Path.GetTempPath(), $"quillwright-oracle-{Guid.NewGuid():N}.doc");
        File.WriteAllBytes(path, DocWriter.Save(document));
        return WordOracle.Inspect(path, ask);
    }

    private static object Indexed(object target, string name, object index) => WordOracle.Indexed(target, name, index);

    private static object? Get(object target, string name) => WordOracle.Get(target, name);

    private static object? Invoke(object target, string name, params object[] arguments) =>
        WordOracle.Invoke(target, name, arguments);
}
