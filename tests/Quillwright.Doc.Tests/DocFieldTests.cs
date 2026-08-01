using Quillwright.Doc.Writing;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Fields, hyperlinks and bookmarks are all recorded as positions in the text rather than as
/// content, so what these tests check is that the positions still line up after the text has
/// been flattened and read back.
/// </summary>
public class DocFieldTests
{
    [Theory]
    [InlineData(" PAGE ", 0x21)]
    [InlineData("NUMPAGES", 0x1A)]
    [InlineData(" DATE \\@ \"dd.MM.yyyy\" ", 0x1F)]
    [InlineData(" TOC \\o \"1-3\" ", 0x0D)]
    [InlineData(" HYPERLINK \"https://example.com\" ", 0x58)]
    [InlineData(" MERGEFIELD Name ", 0x3B)]
    [InlineData(" SOMETHINGUNKNOWN ", 0x00)]
    public void AFieldKeyword_MapsToItsNumber(string instruction, byte expected) =>
        Assert.Equal(expected, FieldTable.TypeOf(instruction));

    [Fact]
    public void AFieldsThreeCharacters_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("Page ");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Begin });
        paragraph.AppendText(" PAGE ");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Separate });
        paragraph.AppendText("1");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.End });
        Add(document, paragraph);

        Paragraph reopened = First(RoundTrip(document));
        List<FieldCharKind> kinds = [.. reopened.Objects.Select(static o => o.Object).OfType<FieldCharacter>().Select(static f => f.Kind)];

        Assert.Equal([FieldCharKind.Begin, FieldCharKind.Separate, FieldCharKind.End], kinds);
        Assert.Contains(" PAGE ", reopened.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The binary format knows only the character form of a field, so a field written in a
    /// package as one element is spelled out as those characters on the way in.
    /// </summary>
    [Fact]
    public void ASimpleField_BecomesTheCharacterForm()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("by ");
        int start = paragraph.TextLength;
        paragraph.AppendText("someone");
        paragraph.AddRange(new SimpleField { Instruction = "AUTHOR" }, start, paragraph.TextLength - start);
        Add(document, paragraph);

        Paragraph reopened = First(RoundTrip(document));
        Field field = Assert.Single(reopened.Fields());

        Assert.False(field.IsSimple);
        Assert.Equal("AUTHOR", field.Name);
        Assert.Equal("someone", field.Result);
    }

    [Fact]
    public void AHyperlink_TravelsAsAFieldAndComesBackAsALink()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("Go to ");
        int start = paragraph.TextLength;
        paragraph.AppendText("the site");
        paragraph.AddRange(new Hyperlink { Url = "https://example.com/" }, start, paragraph.TextLength - start);
        Add(document, paragraph);

        Paragraph reopened = First(RoundTrip(document));
        (int offset, int length, InlineRange range) = reopened.Ranges.Single();

        Assert.Equal("Go to the site", reopened.Text);
        Assert.Equal("https://example.com/", ((Hyperlink)range).Url);
        Assert.Equal(6, offset);
        Assert.Equal(8, length);
    }

    [Fact]
    public void AHyperlinkToABookmark_KeepsItsAnchor()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("jump");
        paragraph.AddRange(new Hyperlink { Anchor = "chapter2", Tooltip = "go to chapter two" }, 0, 4);
        Add(document, paragraph);

        Paragraph reopened = First(RoundTrip(document));
        var link = (Hyperlink)reopened.Ranges.Single().Range;

        Assert.Equal("jump", reopened.Text);
        Assert.Equal("chapter2", link.Anchor);
        Assert.Equal("go to chapter two", link.Tooltip);
    }

    [Fact]
    public void AHyperlinkInsideAnotherField_IsStillRecognised()
    {
        // A table of contents entry is a hyperlink wrapped around a page reference, so the
        // inner field has to be stepped over rather than mistaken for the outer one's end.
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Begin });
        paragraph.AppendText(" TOC \\o \"1-3\" ");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Separate });
        int start = paragraph.TextLength;
        paragraph.AppendText("Chapter one");
        paragraph.AddRange(new Hyperlink { Anchor = "_Toc1" }, start, paragraph.TextLength - start);
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.End });
        Add(document, paragraph);

        Paragraph reopened = First(RoundTrip(document));

        Assert.Equal("_Toc1", ((Hyperlink)reopened.Ranges.Single().Range).Anchor);
        Assert.Equal(3, reopened.Objects.Count(static o => o.Object is FieldCharacter));
        Assert.Contains(" TOC \\o \"1-3\" ", reopened.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void HyperlinksCanBeTurnedOff()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("plain link");
        paragraph.AddRange(new Hyperlink { Url = "https://example.com/" }, 0, 10);
        Add(document, paragraph);

        byte[] file = DocWriter.Save(document, new DocWriteOptions { WriteHyperlinks = false });
        Paragraph reopened = First(DocReader.Load(file));

        Assert.Equal("plain link", reopened.Text);
        Assert.DoesNotContain("HYPERLINK", reopened.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABookmark_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("The marked words here");
        paragraph.AddMark(new BookmarkStart { Id = 1, Name = "marked" }, 4);
        paragraph.AddMark(new BookmarkEnd { Id = 1 }, 16);
        Add(document, paragraph);

        Paragraph reopened = First(RoundTrip(document));
        BookmarkStart bookmark = reopened.Marks.Select(static m => m.Mark).OfType<BookmarkStart>().Single();

        Assert.Equal("marked", bookmark.Name);
        Assert.Equal(4, reopened.Marks.Single(static m => m.Mark is BookmarkStart).Offset);
        Assert.Equal(16, reopened.Marks.Single(static m => m.Mark is BookmarkEnd).Offset);
    }

    [Fact]
    public void SeveralBookmarksIncludingOverlapping_KeepTheirNames()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("one two three four");
        paragraph.AddMark(new BookmarkStart { Id = 1, Name = "outer" }, 0);
        paragraph.AddMark(new BookmarkStart { Id = 2, Name = "inner" }, 4);
        paragraph.AddMark(new BookmarkEnd { Id = 1 }, 13);
        paragraph.AddMark(new BookmarkEnd { Id = 2 }, 7);
        Add(document, paragraph);

        Paragraph reopened = First(RoundTrip(document));
        List<string> names = [.. reopened.Marks.Select(static m => m.Mark).OfType<BookmarkStart>().Select(static b => b.Name)];

        Assert.Equal(2, names.Count);
        Assert.Contains("outer", names);
        Assert.Contains("inner", names);
    }

    [Fact]
    public void ABookmarkSpanningParagraphs_KeepsBothEnds()
    {
        WordDocument document = WordDocument.Create();
        var first = new Paragraph("first paragraph");
        var second = new Paragraph("second paragraph");
        first.AddMark(new BookmarkStart { Id = 1, Name = "across" }, 6);
        second.AddMark(new BookmarkEnd { Id = 1 }, 6);
        Add(document, first);
        Add(document, second);

        List<Paragraph> reopened = [.. RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.Contains(reopened[0].Marks, m => m.Mark is BookmarkStart { Name: "across" });
        Assert.Contains(reopened[1].Marks, static m => m.Mark is BookmarkEnd);
    }

    private static WordDocument RoundTrip(WordDocument document) => DocReader.Load(DocWriter.Save(document));

    private static void Add(WordDocument document, Block block) => document.Sections[0].Blocks.Add(block);

    private static Paragraph First(WordDocument document) =>
        document.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();
}
