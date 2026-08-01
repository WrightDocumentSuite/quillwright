using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Headers, footnotes and comments all live in the same run of characters as the body text,
/// distinguished only by where they fall in it. These tests check that each one comes back
/// as itself rather than as the story next to it.
/// </summary>
public class DocStoryTests
{
    [Fact]
    public void AFootnote_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("Body text"));
        document.AddFootnote(paragraph, "The note itself");

        WordDocument reopened = RoundTrip(document);

        Assert.Single(reopened.Footnotes);
        Assert.Contains("The note itself", reopened.Footnotes[0].GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFootnoteReference_StaysWhereItWas()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before");
        Add(document, paragraph);
        document.AddFootnote(paragraph, "note");
        paragraph.AppendText(" after");

        Paragraph reopened = First(RoundTrip(document));
        (int offset, InlineObject reference) = reopened.Objects.Single(static o => o.Object is NoteReference);

        Assert.Equal(6, offset);
        Assert.False(((NoteReference)reference).IsEndnote);
    }

    [Fact]
    public void SeveralFootnotes_KeepTheirOwnText()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("one two three"));
        document.AddFootnote(paragraph, "first note");
        document.AddFootnote(paragraph, "second note");
        document.AddFootnote(paragraph, "third note");

        WordDocument reopened = RoundTrip(document);

        Assert.Equal(3, reopened.Footnotes.Count);
        Assert.Contains("first note", reopened.Footnotes[0].GetText(), StringComparison.Ordinal);
        Assert.Contains("second note", reopened.Footnotes[1].GetText(), StringComparison.Ordinal);
        Assert.Contains("third note", reopened.Footnotes[2].GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteWithAMarkTheAuthorChose_IsNotReadBackAsNumbered()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("Body"));
        document.AddFootnote(paragraph, "starred");
        NoteReference reference = paragraph.Objects.Select(static o => o.Object).OfType<NoteReference>().Single();
        reference.CustomMark = true;

        WordDocument reopened = RoundTrip(document);
        NoteReference back = First(reopened).Objects.Select(static o => o.Object).OfType<NoteReference>().Single();

        Assert.True(back.CustomMark);
    }

    [Fact]
    public void ANumberedNote_StaysNumbered()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("Body"));
        document.AddFootnote(paragraph, "counted");

        NoteReference back = First(RoundTrip(document)).Objects.Select(static o => o.Object).OfType<NoteReference>().Single();

        Assert.False(back.CustomMark);
    }

    [Fact]
    public void AnEndnote_IsNotReadBackAsAFootnote()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("Body"));
        document.AddEndnote(paragraph, "at the end");

        WordDocument reopened = RoundTrip(document);

        Assert.Single(reopened.Endnotes);
        Assert.Contains("at the end", reopened.Endnotes[0].GetText(), StringComparison.Ordinal);
        Assert.Empty(reopened.Footnotes);
    }

    [Fact]
    public void FootnotesAndEndnotesTogether_StayApart()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("Body"));
        document.AddFootnote(paragraph, "bottom of page");
        document.AddEndnote(paragraph, "end of document");

        WordDocument reopened = RoundTrip(document);

        Assert.Contains("bottom of page", reopened.Footnotes[0].GetText(), StringComparison.Ordinal);
        Assert.Contains("end of document", reopened.Endnotes[0].GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AComment_SurvivesWithItsAuthor()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("Commented text"));
        document.AddComment(paragraph, 0, 9, "Needs work", "Ada Lovelace", "AL");

        WordDocument reopened = RoundTrip(document);

        Assert.Single(reopened.Comments);
        Assert.Contains("Needs work", reopened.Comments[0].GetText(), StringComparison.Ordinal);
        Assert.Equal("Ada Lovelace", reopened.Comments[0].Author);
    }

    [Fact]
    public void AHeaderAndAFooter_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("Body"));
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("Page header"));
        document.Sections[0].Footers.GetOrCreate().Blocks.Add(new Paragraph("Page footer"));

        Section section = RoundTrip(document).Sections[0];

        Assert.Equal("Page header", section.Headers.Default!.GetText().Trim());
        Assert.Equal("Page footer", section.Footers.Default!.GetText().Trim());
    }

    [Fact]
    public void FirstAndEvenPageHeaders_KeepTheirSlots()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("Body"));
        document.Sections[0].Headers.GetOrCreate(HeaderFooterKind.First).Blocks.Add(new Paragraph("First only"));
        document.Sections[0].Headers.GetOrCreate(HeaderFooterKind.Even).Blocks.Add(new Paragraph("Even only"));
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("The rest"));

        Section section = RoundTrip(document).Sections[0];

        Assert.Equal("First only", section.Headers.First!.GetText().Trim());
        Assert.Equal("Even only", section.Headers.Even!.GetText().Trim());
        Assert.Equal("The rest", section.Headers.Default!.GetText().Trim());
    }

    [Fact]
    public void EachSectionKeepsItsOwnHeader()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("First body"));
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("Header one"));

        var second = new Section();
        second.Blocks.Add(new Paragraph("Second body"));
        document.Sections.Add(second);
        second.Headers.GetOrCreate().Blocks.Add(new Paragraph("Header two"));

        WordDocument reopened = RoundTrip(document);

        Assert.Equal("Header one", reopened.Sections[0].Headers.Default!.GetText().Trim());
        Assert.Equal("Header two", reopened.Sections[1].Headers.Default!.GetText().Trim());
    }

    [Fact]
    public void AHeaderWithSeveralParagraphs_KeepsThemAll()
    {
        WordDocument document = WordDocument.Create();
        Add(document, new Paragraph("Body"));
        HeaderFooter header = document.Sections[0].Headers.GetOrCreate();
        header.Blocks.Add(new Paragraph("Line one"));
        header.Blocks.Add(new Paragraph("Line two"));

        HeaderFooter reopened = RoundTrip(document).Sections[0].Headers.Default!;

        Assert.Equal(2, reopened.Blocks.OfType<Paragraph>().Count());
        Assert.Equal("Line one", reopened.Blocks.OfType<Paragraph>().First().Text);
        Assert.Equal("Line two", reopened.Blocks.OfType<Paragraph>().Last().Text);
    }

    [Fact]
    public void ADocumentWithEveryStory_KeepsTheBodyTextIntact()
    {
        // Every story shares one run of characters, so a mistake in any of their lengths
        // shows up as the body text being cut short or running on.
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = Add(document, new Paragraph("The body of the document"));
        document.AddFootnote(paragraph, "a footnote");
        document.AddEndnote(paragraph, "an endnote");
        document.AddComment(paragraph, 0, 3, "a comment", "Reviewer");
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("a header"));

        WordDocument reopened = RoundTrip(document);

        // The references are characters of their own, so the body text is compared without
        // the placeholders that stand for them.
        string body = First(reopened).Text.Replace($"{InlineObject.Placeholder}", string.Empty, StringComparison.Ordinal);
        Assert.StartsWith("The body of the document", body, StringComparison.Ordinal);
        Assert.Contains("a footnote", reopened.Footnotes[0].GetText(), StringComparison.Ordinal);
        Assert.Contains("an endnote", reopened.Endnotes[0].GetText(), StringComparison.Ordinal);
        Assert.Contains("a comment", reopened.Comments[0].GetText(), StringComparison.Ordinal);
        Assert.Equal("a header", reopened.Sections[0].Headers.Default!.GetText().Trim());
    }

    private static WordDocument RoundTrip(WordDocument document) => DocReader.Load(DocWriter.Save(document));

    private static Paragraph Add(WordDocument document, Paragraph paragraph)
    {
        document.Sections[0].Blocks.Add(paragraph);
        return paragraph;
    }

    private static Paragraph First(WordDocument document) =>
        document.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();
}
