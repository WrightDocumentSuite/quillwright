using Quillwright.Html;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// HTML becoming a document: the permissive parser, the element mapping that mirrors the
/// exporter's, inline CSS read for what Word can also say, and diagnostics naming the rest.
/// </summary>
public class HtmlImportTests
{
    [Fact]
    public void AFullPage_YieldsItsBodyAndItsTitle()
    {
        WordDocument document = HtmlImporter.Import(
            "<!DOCTYPE html><html><head><title>The Title</title><style>p{color:red}</style></head>" +
            "<body><h1>Top</h1><p>Body text.</p></body></html>").Document;

        Assert.Equal("The Title", document.Properties.Title);
        List<Paragraph> paragraphs = [.. document.Paragraphs];
        Assert.Equal(2, paragraphs.Count);
        Assert.Equal("Heading1", paragraphs[0].Format.StyleId);
        Assert.Equal("Top", paragraphs[0].Text);
        Assert.Equal("Body text.", paragraphs[1].Text);
    }

    [Fact]
    public void UnclosedParagraphs_CloseTheWayBrowsersCloseThem()
    {
        WordDocument document = HtmlImporter.Import("<p>first<p>second<div>third</div>").Document;

        Assert.Equal(["first", "second", "third"], document.Paragraphs.Select(static p => p.Text).ToArray());
    }

    [Fact]
    public void InlineElementsAndCss_BecomeRunFormatting()
    {
        WordDocument document = HtmlImporter.Import(
            "<p><b>b</b><em>i</em><s>s</s><u>u</u><sup>up</sup><code>m</code>" +
            "<span style=\"color:#c00000\">red</span>" +
            "<span style=\"font-weight:700;font-size:16pt\">big</span>" +
            "<mark>hl</mark></p>").Document;

        Paragraph paragraph = document.Paragraphs.Single();
        Assert.Equal("bisuupmredbighl", paragraph.Text);

        Assert.True(RunAt(paragraph, "b").Bold);
        Assert.True(RunAt(paragraph, "i").Italic);
        Assert.True(RunAt(paragraph, "s").Strike);
        Assert.Equal(UnderlineStyle.Single, RunAt(paragraph, "u").Underline);
        Assert.Equal(VerticalTextAlignment.Superscript, RunAt(paragraph, "up").VerticalAlignment);
        Assert.Equal("Consolas", RunAt(paragraph, "m").FontAscii);
        Assert.Equal(0xC00000u, RunAt(paragraph, "red").Color?.Rgb);
        Assert.True(RunAt(paragraph, "big").Bold);
        Assert.Equal(16, RunAt(paragraph, "big").Size?.Points);
        Assert.Equal(HighlightColor.Yellow, RunAt(paragraph, "hl").Highlight);
    }

    [Fact]
    public void WhitespaceCollapses_ExceptInsidePre()
    {
        WordDocument document = HtmlImporter.Import(
            "<p>one\n   two\t three</p><pre>line one\n  line two</pre>").Document;

        List<Paragraph> paragraphs = [.. document.Paragraphs];
        Assert.Equal("one two three", paragraphs[0].Text);
        Assert.Equal("CodeBlock", paragraphs[1].Format.StyleId);
        Assert.Equal("line one\n  line two", paragraphs[1].Text);
    }

    [Fact]
    public void LinksAnchorsAndBookmarks_Bind()
    {
        WordDocument document = HtmlImporter.Import(
            "<p><a href=\"https://example.org\" title=\"spec\">out</a> and " +
            "<a href=\"#target\">in</a> and <a id=\"target\">here</a></p>").Document;

        Paragraph paragraph = document.Paragraphs.Single();
        List<Hyperlink> links = [.. paragraph.Ranges.Select(static r => r.Range).OfType<Hyperlink>()];

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.org", links[0].Url);
        Assert.Equal("spec", links[0].Tooltip);
        Assert.Equal("target", links[1].Anchor);
        Assert.Contains(
            paragraph.Marks.Select(static m => m.Mark).OfType<BookmarkStart>(),
            static b => b.Name == "target");
    }

    [Fact]
    public void Lists_BecomeRealNumberingWithDepth()
    {
        WordDocument document = HtmlImporter.Import(
            "<ul><li>one</li><li>two<ul><li>nested</li></ul></li></ul><ol><li>first</li><li>second</li></ol>").Document;

        List<Paragraph> items = [.. document.Paragraphs];
        Assert.Equal(5, items.Count);
        Assert.Equal("ListParagraph", items[0].Format.StyleId);
        Assert.NotNull(items[0].Format.NumberingId);
        Assert.Equal(items[0].Format.NumberingId, items[2].Format.NumberingId);
        Assert.Equal(1, items[2].Format.NumberingLevel);
        Assert.NotEqual(items[0].Format.NumberingId, items[3].Format.NumberingId);
    }

    [Fact]
    public void ATable_ArrivesWithHeaderAndMerges()
    {
        WordDocument document = HtmlImporter.Import(
            "<table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody>" +
            "<tr><td colspan=\"2\">wide</td></tr>" +
            "<tr><td rowspan=\"2\">tall</td><td>beside</td></tr>" +
            "<tr><td>under</td></tr>" +
            "</tbody></table>").Document;

        Table table = document.Sections[0].Blocks.OfType<Table>().Single();
        Assert.Equal(4, table.Rows.Count);
        Assert.True(table.Rows[0].Format.IsHeader);
        Assert.True(table.Rows[0].Cells[0].Blocks.Paragraphs.Single().Runs.Single().Format.Bold);
        Assert.Equal(2, table.Rows[1].Cells[0].Format.GridSpan);
        Assert.Equal(VerticalMerge.Restart, table.Rows[2].Cells[0].Format.VerticalMerge);
        Assert.Equal(VerticalMerge.Continue, table.Rows[3].Cells[0].Format.VerticalMerge);
        Assert.Equal("under", table.Rows[3].Cells[1].GetText());
    }

    [Fact]
    public void ADataUriImage_IsEmbedded()
    {
        const string Png =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        HtmlImportResult result = HtmlImporter.Import(
            $"<p><img src=\"data:image/png;base64,{Png}\" alt=\"dot\" width=\"16\" height=\"8\"></p>");

        Assert.Single(result.Document.Media);
        Picture picture = result.Document.Paragraphs.Single().Objects.Select(static o => o.Object).OfType<Picture>().Single();
        Assert.Equal("dot", picture.Description);
        Assert.Equal(12, picture.Width.Points);
        Assert.Equal(6, picture.Height.Points);
    }

    [Fact]
    public void ScriptsStylesAndEntities_AreHandled()
    {
        HtmlImportResult result = HtmlImporter.Import(
            "<p>AT&amp;T &mdash; caf&eacute;? &#8212; &#x2014; yes&nbsp;sir</p><script>let x = '<p>not a paragraph</p>';</script>");

        Assert.Single(result.Document.Paragraphs);
        string text = result.Document.Paragraphs.Single().Text;
        Assert.StartsWith("AT&T — caf", text, StringComparison.Ordinal);
        Assert.Contains("— — yes sir", text, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static w => w.Kind == HtmlImportWarningKind.ContentSkipped && w.Subject == "script");
    }

    [Fact]
    public void WordHtmlJunk_IsSteppedOver()
    {
        WordDocument document = HtmlImporter.Import(
            "<!--[if !supportLists]--><p style=\"mso-line-height:normal\"><o:p>kept</o:p></p><!--[endif]-->").Document;

        Assert.Equal("kept", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task AnImportedPage_SavesValidAndReloads()
    {
        WordDocument document = HtmlImporter.Import(
            "<h1>Report</h1><p>Intro with <b>bold</b>, a <a href=\"https://example.org\">link</a> and <code>code</code>.</p>" +
            "<ul><li>item one</li><li>item two</li></ul>" +
            "<table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>" +
            "<blockquote><p>Quoted.</p></blockquote><pre>let x = 1</pre>").Document;

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a document imported from HTML");

        Assert.Contains("Report", reloaded.GetText(), StringComparison.Ordinal);
        Assert.Contains("item two", reloaded.GetText(), StringComparison.Ordinal);
        Assert.Single(reloaded.Sections[0].Blocks.OfType<Table>());
    }

    /// <summary>
    /// Export and import are inverses as far as the formats overlap: a page the exporter wrote
    /// imports back to the same constructs.
    /// </summary>
    [Fact]
    public void WhatWasExported_ImportsBackToItsOwnConstructs()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("Title", "Heading1");
        var paragraph = new Paragraph("Plain ");
        paragraph.AppendText("bold", RunFormat.Default with { Bold = true });
        paragraph.AppendText(" and ");
        paragraph.AppendText("code", RunFormat.Default with { FontAscii = "Consolas", FontHighAnsi = "Consolas" });
        original.Sections[0].Blocks.Add(paragraph);
        int list = original.Numbering.AddBulletList();
        Paragraph item = original.Sections[0].AddParagraph("bullet");
        item.Format = item.Format with { NumberingId = list, NumberingLevel = 0 };

        string html = original.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        WordDocument reimported = HtmlImporter.Import(html).Document;

        List<Paragraph> paragraphs = [.. reimported.Paragraphs];
        Assert.Equal("Heading1", paragraphs[0].Format.StyleId);
        Assert.Equal("Title", paragraphs[0].Text);
        Assert.Equal("Plain bold and code", paragraphs[1].Text);
        Assert.True(RunAt(paragraphs[1], "bold").Bold);
        Assert.Equal("Consolas", RunAt(paragraphs[1], "code").FontAscii);
        Assert.NotNull(paragraphs[2].Format.NumberingId);
    }

    private static RunFormat RunAt(Paragraph paragraph, string text)
    {
        int at = paragraph.Text.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{text}' is not in '{paragraph.Text}'.");
        return paragraph.FormatAtOffset(at);
    }
}
