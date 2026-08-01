using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

public class EditingTests
{
    [Fact]
    public void Replace_WorksAcrossRunBoundaries()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Dear ", RunFormat.Default);
        paragraph.AppendText("{{Cli", RunFormat.Default with { Bold = true });
        paragraph.AppendText("ent}}", RunFormat.Default with { Italic = true });
        paragraph.AppendText(", welcome.", RunFormat.Default);

        int replaced = document.Replace("{{Client}}", "Ромашка");

        Assert.Equal(1, replaced);
        Assert.Equal("Dear Ромашка, welcome.", paragraph.Text);
    }

    [Fact]
    public void Replace_KeepsTheHyperlinkAroundTheReplacedText()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Visit ");
        int start = paragraph.TextLength;
        paragraph.AppendText("{{Site}}");
        paragraph.AddRange(new Hyperlink { Url = "https://example.com" }, start, paragraph.TextLength - start);

        document.Replace("{{Site}}", "our site");

        (int rangeStart, int rangeLength, InlineRange range) = paragraph.Ranges.Single();
        Assert.Equal("Visit our site", paragraph.Text);
        Assert.Equal(start, rangeStart);
        Assert.Equal("our site".Length, rangeLength);
        Assert.IsType<Hyperlink>(range);
    }

    [Fact]
    public void Replace_ReachesHeadersAndFootnotes()
    {
        WordDocument document = WordDocument.Create();
        Paragraph body = document.Sections[0].AddParagraph("body {{X}}");
        document.Sections[0].Headers.GetOrCreate().AddParagraph("header {{X}}");
        document.AddFootnote(body, "note {{X}}");

        int replaced = document.Replace("{{X}}", "value");

        Assert.Equal(3, replaced);
        Assert.DoesNotContain("{{X}}", document.GetText(), StringComparison.Ordinal);
        Assert.Contains("header value", document.Sections[0].Headers.Default!.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Find_SupportsRegexAndCaseInsensitivity()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Order 42 and order 7.");

        TextMatch[] matches = [.. document.Find(@"order\s+(\d+)", new SearchOptions { IsRegex = true, MatchCase = false })];

        Assert.Equal(2, matches.Length);
        Assert.Equal("Order 42", matches[0].Value);
    }

    [Fact]
    public void Highlight_AppliesFormattingToMatchesOnly()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("alpha beta alpha");

        document.Highlight("alpha", format => format with { Bold = true });

        Assert.True(paragraph.Runs[0].Format.Bold);
        Assert.Null(paragraph.Runs[1].Format.Bold);
        Assert.True(paragraph.Runs[2].Format.Bold);
    }

    [Fact]
    public void Editor_BuildsADocumentThroughACursor()
    {
        WordDocument document = WordDocument.Create();
        var editor = new DocumentEditor(document);

        editor.WriteHeading("Report", 1)
            .WriteLine("Intro paragraph.")
            .WithFormat(format => format with { Bold = true })
            .WriteLine("Bold paragraph.")
            .ResetFormat();

        Table table = editor.InsertTable(2, 2);
        editor.MoveTo(table[0, 0]).Write("cell");
        editor.MoveToFooter().Write("page ").CurrentParagraph.AppendPageNumber();

        Assert.Equal("Heading1", document.Sections[0].Blocks.Paragraphs.First().Format.StyleId);
        Assert.True(document.Sections[0].Blocks.Paragraphs.ElementAt(2).Runs[0].Format.Bold);
        Assert.Equal("cell", table[0, 0].GetText());
        Assert.Contains("page", document.Sections[0].Footers.Default!.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fields_RoundTripAndExposeTheirInstruction()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Page ");
        paragraph.AppendPageNumber();
        paragraph.AppendText(" of ");
        paragraph.AppendPageCount();

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "fields");
        Field[] fields = [.. reloaded.Fields()];

        Assert.Equal(2, fields.Length);
        Assert.Equal("PAGE", fields[0].Name);
        Assert.Equal("NUMPAGES", fields[1].Name);
        Assert.Equal("1", fields[0].Result);

        fields[0].SetResult("7");
        Assert.Equal("7", reloaded.Fields().First().Result);
    }

    [Fact]
    public void AcceptAllRevisions_KeepsInsertionsAndDropsDeletions()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("keep ");
        int insertStart = paragraph.TextLength;
        paragraph.AppendText("added ");
        paragraph.AddRange(new Revision { Kind = RevisionKind.Inserted, Id = 1, Author = "A" }, insertStart, 6);
        int deleteStart = paragraph.TextLength;
        paragraph.AppendText("removed ");
        paragraph.AddRange(new Revision { Kind = RevisionKind.Deleted, Id = 2, Author = "A" }, deleteStart, 8);
        paragraph.AppendText("tail");

        Assert.True(document.HasRevisions());
        int resolved = document.AcceptAllRevisions();

        Assert.Equal(2, resolved);
        Assert.Equal("keep added tail", paragraph.Text);
        Assert.False(document.HasRevisions());
    }

    [Fact]
    public void RejectAllRevisions_DropsInsertionsAndKeepsDeletions()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("keep ");
        int insertStart = paragraph.TextLength;
        paragraph.AppendText("added ");
        paragraph.AddRange(new Revision { Kind = RevisionKind.Inserted, Id = 1 }, insertStart, 6);
        int deleteStart = paragraph.TextLength;
        paragraph.AppendText("removed");
        paragraph.AddRange(new Revision { Kind = RevisionKind.Deleted, Id = 2 }, deleteStart, 7);

        document.RejectAllRevisions();

        Assert.Equal("keep removed", paragraph.Text);
    }
}
