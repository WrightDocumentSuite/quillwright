using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// Markdown becoming a document: CommonMark blocks and inlines, the GitHub extensions, and the
/// same style names the exporter reads back — so what was imported exports to the Markdown it
/// came from, as far as the two formats overlap.
/// </summary>
public class MarkdownImportTests
{
    [Fact]
    public void HeadingsAndParagraphs_TakeTheHeadingStyles()
    {
        WordDocument document = MarkdownImporter.Import("# Title\n\nBody text.\n\n## Section\n\nMore.").Document;

        List<Paragraph> paragraphs = [.. document.Paragraphs];
        Assert.Equal("Heading1", paragraphs[0].Format.StyleId);
        Assert.Equal("Title", paragraphs[0].Text);
        Assert.Null(paragraphs[1].Format.StyleId);
        Assert.Equal("Heading2", paragraphs[2].Format.StyleId);
        Assert.Equal("Section", paragraphs[2].Text);
    }

    [Fact]
    public void ASetextUnderline_MakesTheHeading()
    {
        WordDocument document = MarkdownImporter.Import("Title\n=====\n\nSubtitle\n--------").Document;

        List<Paragraph> paragraphs = [.. document.Paragraphs];
        Assert.Equal("Heading1", paragraphs[0].Format.StyleId);
        Assert.Equal("Heading2", paragraphs[1].Format.StyleId);
    }

    [Fact]
    public void EmphasisNestsAndStrikes()
    {
        WordDocument document = MarkdownImporter.Import("plain **bold** *italic* ***both*** ~~gone~~").Document;

        Paragraph paragraph = document.Paragraphs.Single();
        Assert.Equal("plain bold italic both gone", paragraph.Text);

        Run bold = paragraph.Runs.Single(static r => r.Text == "bold");
        Assert.True(bold.Format.Bold);
        Assert.NotEqual(true, bold.Format.Italic);

        Run italic = paragraph.Runs.Single(static r => r.Text == "italic");
        Assert.True(italic.Format.Italic);

        Run both = paragraph.Runs.Single(static r => r.Text == "both");
        Assert.True(both.Format.Bold);
        Assert.True(both.Format.Italic);

        Run gone = paragraph.Runs.Single(static r => r.Text == "gone");
        Assert.True(gone.Format.Strike);
    }

    [Fact]
    public void ACodeSpan_IsMonospaceAndLiteral()
    {
        WordDocument document = MarkdownImporter.Import("Call `map(*x*)` now.").Document;

        Paragraph paragraph = document.Paragraphs.Single();
        Assert.Equal("Call map(*x*) now.", paragraph.Text);

        Run code = paragraph.Runs.Single(static r => r.Text == "map(*x*)");
        Assert.Equal("Consolas", code.Format.FontAscii);
    }

    [Fact]
    public void Links_BecomeHyperlinkRanges()
    {
        WordDocument document = MarkdownImporter.Import(
            "See [the spec](https://example.org/spec \"ISO 29500\") and <https://example.com> and [named][ref].\n\n[ref]: https://example.net").Document;

        Paragraph paragraph = document.Paragraphs.First();
        List<Hyperlink> links = [.. paragraph.Ranges.Select(static r => r.Range).OfType<Hyperlink>()];

        Assert.Equal(3, links.Count);
        Assert.Equal("https://example.org/spec", links[0].Url);
        Assert.Equal("ISO 29500", links[0].Tooltip);
        Assert.Equal("https://example.com", links[1].Url);
        Assert.Equal("https://example.net", links[2].Url);

        (int start, int length, _) = paragraph.Ranges.First(r => r.Range == links[0]);
        Assert.Equal("the spec", paragraph.Text.Substring(start, length));
    }

    [Fact]
    public void AnAnchorLink_BecomesAnAnchor()
    {
        WordDocument document = MarkdownImporter.Import("Back to [the top](#top).").Document;

        Hyperlink link = document.Paragraphs.Single().Ranges.Select(static r => r.Range).OfType<Hyperlink>().Single();
        Assert.Equal("top", link.Anchor);
        Assert.Null(link.Url);
    }

    [Fact]
    public void BulletAndOrderedLists_GetRealNumbering()
    {
        WordDocument document = MarkdownImporter.Import(
            "- one\n- two\n  - nested\n\n1. first\n2. second").Document;

        List<Paragraph> items = [.. document.Paragraphs];
        Assert.Equal(5, items.Count);

        Assert.NotNull(items[0].Format.NumberingId);
        Assert.Equal(items[0].Format.NumberingId, items[1].Format.NumberingId);
        Assert.Equal(0, items[0].Format.NumberingLevel);
        Assert.Equal(1, items[2].Format.NumberingLevel);
        Assert.Equal(items[0].Format.NumberingId, items[2].Format.NumberingId);
        Assert.Equal("ListParagraph", items[0].Format.StyleId);

        Assert.NotNull(items[3].Format.NumberingId);
        Assert.NotEqual(items[0].Format.NumberingId, items[3].Format.NumberingId);
        Assert.Equal(items[3].Format.NumberingId, items[4].Format.NumberingId);
    }

    [Fact]
    public void TaskItems_ShowTheirBoxes()
    {
        WordDocument document = MarkdownImporter.Import("- [x] done\n- [ ] pending").Document;

        List<Paragraph> items = [.. document.Paragraphs];
        Assert.Equal("☒ done", items[0].Text);
        Assert.Equal("☐ pending", items[1].Text);
        Assert.NotNull(items[0].Format.NumberingId);
    }

    [Fact]
    public void AFencedBlock_KeepsItsLinesInACodeParagraph()
    {
        WordDocument document = MarkdownImporter.Import("```csharp\nvar x = 1;\n\nreturn x;\n```").Document;

        Paragraph code = document.Paragraphs.Single();
        Assert.Equal("CodeBlock", code.Format.StyleId);
        Assert.Equal("var x = 1;\n\nreturn x;", code.Text);
        Assert.All(code.Runs, static run => Assert.Equal("Consolas", run.Format.FontAscii));
    }

    [Fact]
    public void ABlockquote_TakesTheQuoteStyle()
    {
        WordDocument document = MarkdownImporter.Import("> Quoted words\n> over two lines.").Document;

        Paragraph quote = document.Paragraphs.Single();
        Assert.Equal("Quote", quote.Format.StyleId);
        Assert.Equal("Quoted words over two lines.", quote.Text);
    }

    [Fact]
    public void AThematicBreak_DrawsABottomBorder()
    {
        WordDocument document = MarkdownImporter.Import("above\n\n---\n\nbelow").Document;

        Paragraph rule = document.Paragraphs.ElementAt(1);
        Assert.Equal(BorderStyle.Single, rule.Format.Borders?.Bottom?.Style);
        Assert.Equal(string.Empty, rule.Text);
    }

    [Fact]
    public void ATable_BecomesARealTableWithAHeaderRow()
    {
        WordDocument document = MarkdownImporter.Import(
            "| Name | Qty | Price |\n|:-----|:---:|------:|\n| Bolt | 12  | 0.30  |\n| Nut \\| washer | 3 | 0.10 |").Document;

        Table table = document.Sections[0].Blocks.OfType<Table>().Single();
        Assert.Equal(3, table.Rows.Count);
        Assert.True(table.Rows[0].Format.IsHeader);
        Assert.Equal("TableGrid", table.Format.StyleId);

        Paragraph header = table.Rows[0].Cells[0].Blocks.Paragraphs.Single();
        Assert.Equal("Name", header.Text);
        Assert.True(header.Runs.Single().Format.Bold);

        Assert.Equal(ParagraphAlignment.Center, table.Rows[1].Cells[1].Blocks.Paragraphs.Single().Format.Alignment);
        Assert.Equal(ParagraphAlignment.Right, table.Rows[1].Cells[2].Blocks.Paragraphs.Single().Format.Alignment);
        Assert.Equal("Nut | washer", table.Rows[2].Cells[0].Blocks.Paragraphs.Single().Text);
    }

    [Fact]
    public void HardBreaksBreak_AndSoftOnesFold()
    {
        WordDocument document = MarkdownImporter.Import("line one  \nline two\nline three").Document;

        Assert.Equal("line one\nline two line three", document.Paragraphs.Single().Text);
    }

    [Fact]
    public void EscapesAndEntities_AreLiteral()
    {
        WordDocument document = MarkdownImporter.Import("\\*not emphasis\\* &amp; ties &#8212; here").Document;

        Assert.Equal("*not emphasis* & ties — here", document.Paragraphs.Single().Text);
    }

    [Fact]
    public void RawHtml_IsKeptAsTextAndNamed()
    {
        MarkdownImportResult result = MarkdownImporter.Import("<div class=\"x\">kept</div>");

        Assert.Contains("<div class=\"x\">kept</div>", result.Document.Paragraphs.Single().Text, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, static w => w.Kind == MarkdownImportWarningKind.HtmlKeptAsText);
    }

    [Fact]
    public void FrontMatter_IsSkippedAndNamed()
    {
        MarkdownImportResult result = MarkdownImporter.Import("---\ntitle: x\n---\n\nBody.");

        Assert.Equal("Body.", result.Document.Paragraphs.Single().Text);
        Assert.Contains(result.Diagnostics, static w => w.Kind == MarkdownImportWarningKind.UnsupportedSyntax);
    }

    [Fact]
    public void AMissingImage_FallsBackToItsAltText()
    {
        MarkdownImportResult result = MarkdownImporter.Import(
            "![chart of Q1](missing.png)",
            new MarkdownImportOptions { MediaDirectory = Path.GetTempPath() });

        Assert.Equal("chart of Q1", result.Document.Paragraphs.Single().Text);
        Assert.Contains(result.Diagnostics, static w => w.Kind == MarkdownImportWarningKind.ImageSkipped);
    }

    [Fact]
    public void ADataUriImage_IsEmbedded()
    {
        // The smallest valid PNG: a 1x1 transparent pixel.
        const string Png =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        MarkdownImportResult result = MarkdownImporter.Import($"![dot](data:image/png;base64,{Png})");

        Assert.Single(result.Document.Media);
        Assert.True(result.Diagnostics.IsEmpty);
    }

    /// <summary>The imported document must be a valid package, not merely a plausible model.</summary>
    [Fact]
    public async Task AnImportedDocument_SavesValidAndReloads()
    {
        WordDocument document = MarkdownImporter.Import(
            "# Report\n\nIntro with **bold**, a [link](https://example.org) and `code`.\n\n" +
            "- item one\n- item two\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n> Quoted.\n\n```\nlet x = 1\n```").Document;

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a document imported from Markdown");

        Assert.Contains("Report", reloaded.GetText(), StringComparison.Ordinal);
        Assert.Contains("item two", reloaded.GetText(), StringComparison.Ordinal);
        Assert.Single(reloaded.Sections[0].Blocks.OfType<Table>());
    }

    /// <summary>
    /// Import and export are inverses as far as the formats overlap: what came from Markdown
    /// exports to the same constructs it was written with.
    /// </summary>
    [Fact]
    public void WhatWasImported_ExportsBackToItsOwnConstructs()
    {
        const string Source =
            "# Title\n\nSome **bold** and *italic* and `code`.\n\n- one\n- two\n\n> Quoted.";

        WordDocument document = MarkdownImporter.Import(Source).Document;
        string exported = document.ToMarkdown().Text;

        Assert.Contains("# Title", exported, StringComparison.Ordinal);
        Assert.Contains("**bold**", exported, StringComparison.Ordinal);
        Assert.Contains("*italic*", exported, StringComparison.Ordinal);
        Assert.Contains("`code`", exported, StringComparison.Ordinal);
        Assert.Contains("- one", exported, StringComparison.Ordinal);

        // The built-in Quote style is italic, and the exporter faithfully says so.
        Assert.Contains("> *Quoted.*", exported, StringComparison.Ordinal);
    }
}
