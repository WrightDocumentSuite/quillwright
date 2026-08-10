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
    public void ManyUnmatchedEmphasisClosers_RemainLiteral()
    {
        const int Count = 20_000;
        string markdown = string.Concat(Enumerable.Repeat("a* ", Count));

        Paragraph paragraph = MarkdownImporter.Import(markdown).Document.Paragraphs.Single();

        Assert.Equal(markdown.TrimEnd(), paragraph.GetText());
        Assert.All(paragraph.Runs, static run =>
        {
            Assert.NotEqual(true, run.Format.Bold);
            Assert.NotEqual(true, run.Format.Italic);
            Assert.NotEqual(true, run.Format.Strike);
        });
    }

    [Fact]
    public void NestedAndAdjacentDelimiterKinds_KeepTheirFormattingScopes()
    {
        Paragraph paragraph = MarkdownImporter.Import(
            "**bold *both* tail** _*adjacent*_ ~~strike **both-two** tail~~").Document.Paragraphs.Single();

        Run both = paragraph.Runs.Single(static run => run.Text == "both");
        Assert.True(both.Format.Bold);
        Assert.True(both.Format.Italic);

        Run adjacent = paragraph.Runs.Single(static run => run.Text == "adjacent");
        Assert.True(adjacent.Format.Italic);
        Assert.NotEqual(true, adjacent.Format.Bold);

        Run bothTwo = paragraph.Runs.Single(static run => run.Text == "both-two");
        Assert.True(bothTwo.Format.Bold);
        Assert.True(bothTwo.Format.Strike);
        Assert.NotEqual(true, bothTwo.Format.Italic);
    }

    [Fact]
    public void RuleOfThree_PreservesLiteralInteriorMarkers()
    {
        Paragraph paragraph = MarkdownImporter.Import("*foo**bar*").Document.Paragraphs.Single();

        Assert.Equal("foo**bar", paragraph.GetText());
        Assert.All(paragraph.Runs, static run =>
        {
            Assert.True(run.Format.Italic);
            Assert.NotEqual(true, run.Format.Bold);
        });
    }

    [Fact]
    public void RuleOfThree_KeepsStrongEmphasisInsideEmphasis()
    {
        Paragraph paragraph = MarkdownImporter.Import("*foo**bar**baz* *foo**qux***")
            .Document.Paragraphs.Single();

        Assert.Equal("foobarbaz fooqux", paragraph.GetText());
        foreach (Run run in paragraph.Runs.Where(static candidate => candidate.Text is "foo" or "baz"))
        {
            Assert.True(run.Format.Italic);
            Assert.NotEqual(true, run.Format.Bold);
        }
        Assert.Equal(3, paragraph.Runs.Count(static candidate => candidate.Text is "foo" or "baz"));

        foreach (string text in new[] { "bar", "qux" })
        {
            Run run = paragraph.Runs.Single(candidate => candidate.Text == text);
            Assert.True(run.Format.Italic);
            Assert.True(run.Format.Bold);
        }
    }

    [Fact]
    public void RuleOfThree_AppliesToUnderscoresButAllowsTwoTripleRuns()
    {
        Paragraph disallowed = MarkdownImporter.Import("_foo(__)bar_").Document.Paragraphs.Single();
        Paragraph triples = MarkdownImporter.Import("foo***bar***baz").Document.Paragraphs.Single();

        Assert.Equal("foo(__)bar", disallowed.GetText());
        Assert.All(disallowed.Runs, static run =>
        {
            Assert.True(run.Format.Italic);
            Assert.NotEqual(true, run.Format.Bold);
        });
        Run bar = triples.Runs.Single(static run => run.Text == "bar");
        Assert.True(bar.Format.Italic);
        Assert.True(bar.Format.Bold);
    }

    [Fact]
    public void RuleOfThree_SkipsInadmissibleOpenersWithoutQuadraticRescan()
    {
        const int Count = 10_000;
        string markdown = string.Concat(Enumerable.Repeat("**a ", Count)) +
                          string.Concat(Enumerable.Repeat("b*c* ", Count));

        Paragraph paragraph = MarkdownImporter.Import(markdown).Document.Paragraphs.Single();

        Assert.StartsWith("**a **a ", paragraph.GetText(), StringComparison.Ordinal);
        Assert.Equal(Count, paragraph.Runs.Count(static run => run.Text == "c" && run.Format.Italic == true));
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
    public void ManyUnterminatedAutolinkCandidates_RemainLiteral()
    {
        const int Count = 30_000;
        string markdown = string.Concat(Enumerable.Repeat("<a ", Count)) + "tail";

        MarkdownImportResult result = MarkdownImporter.Import(markdown);

        Paragraph paragraph = result.Document.Paragraphs.Single();
        Assert.Equal(markdown, paragraph.GetText());
        Assert.Empty(paragraph.Ranges.Select(static range => range.Range).OfType<Hyperlink>());
        Assert.True(result.Diagnostics.IsEmpty, result.Diagnostics.ToString());
    }

    [Fact]
    public void AShorterFenceInsideCode_DoesNotExposeALinkDefinition()
    {
        WordDocument document = MarkdownImporter.Import(
            "````\n```\n[inside]: https://example.org\n```\n````\n\n[inside]").Document;

        List<Paragraph> paragraphs = [.. document.Paragraphs];
        Assert.Contains("[inside]: https://example.org", paragraphs[0].GetText(), StringComparison.Ordinal);
        Assert.Equal("[inside]", paragraphs[1].GetText());
        Assert.Empty(paragraphs[1].Ranges.Select(static range => range.Range).OfType<Hyperlink>());
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
        WordDocument document = MarkdownImporter.Import(
            "\\*not emphasis\\* &amp; ties &#8212; here; invalid: &#0; &#xD800; &#x110000;").Document;

        Assert.Equal("*not emphasis* & ties — here; invalid: � � �", document.Paragraphs.Single().Text);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASiblingDirectorySharingTheMediaPrefix_IsNotRead(bool percentEncoded)
    {
        string parent = Path.Combine(Path.GetTempPath(), "quillwright-markdown-import-" + Guid.NewGuid().ToString("N"));
        string media = Path.Combine(parent, "media");
        string sibling = Path.Combine(parent, "media-private");

        try
        {
            Directory.CreateDirectory(media);
            Directory.CreateDirectory(sibling);
            string secret = Path.Combine(sibling, "secret.png");
            File.WriteAllBytes(secret, TestImages.Png);
            string relative = Path.GetRelativePath(media, secret).Replace('\\', '/');
            string reference = percentEncoded
                ? "%2e%2e%2fmedia-private%2fsecret.png"
                : relative;

            MarkdownImportResult result = MarkdownImporter.Import(
                $"![secret]({reference})",
                new MarkdownImportOptions { MediaDirectory = media });

            Assert.Empty(result.Document.Media);
            Assert.Equal("secret", result.Document.Paragraphs.Single().Text);
            Assert.Empty(result.Document.Paragraphs.Single().Objects);
            MarkdownImportWarning warning = Assert.Single(result.Diagnostics);
            Assert.Equal(MarkdownImportWarningKind.ImageSkipped, warning.Kind);
            Assert.Equal(reference, warning.Subject);
            Assert.Equal(1, warning.Line);
            Assert.Contains("traversal segment", warning.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ASymbolicLinkInsideTheMediaDirectory_IsNotFollowed()
    {
        string parent = Path.Combine(Path.GetTempPath(), "quillwright-markdown-link-" + Guid.NewGuid().ToString("N"));
        string media = Path.Combine(parent, "media");
        string outside = Path.Combine(parent, "outside");
        string link = Path.Combine(media, "escape");

        try
        {
            Directory.CreateDirectory(media);
            Directory.CreateDirectory(outside);
            File.WriteAllBytes(Path.Combine(outside, "secret.png"), TestImages.Png);

            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          PlatformNotSupportedException or NotSupportedException)
            {
                Assert.Skip($"This platform cannot create a directory symbolic link: {error.Message}");
                return;
            }

            MarkdownImportResult result = MarkdownImporter.Import(
                "![secret](escape/secret.png)",
                new MarkdownImportOptions { MediaDirectory = media });

            Assert.Empty(result.Document.Media);
            Assert.Equal("secret", result.Document.Paragraphs.Single().Text);
            Assert.Empty(result.Document.Paragraphs.Single().Objects);
            MarkdownImportWarning warning = Assert.Single(result.Diagnostics);
            Assert.Equal(MarkdownImportWarningKind.ImageSkipped, warning.Kind);
            Assert.Equal("escape/secret.png", warning.Subject);
            Assert.Equal(1, warning.Line);
            Assert.Contains("symbolic link", warning.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void APercentEncodedRelativeImageInsideTheMediaDirectory_IsEmbedded()
    {
        string media = Path.Combine(Path.GetTempPath(), "quillwright-markdown-local-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(media, "nested folder"));
            File.WriteAllBytes(Path.Combine(media, "nested folder", "image.png"), TestImages.Png);

            MarkdownImportResult result = MarkdownImporter.Import(
                "![local](nested%20folder/image.png)",
                new MarkdownImportOptions { MediaDirectory = media });

            Assert.Single(result.Document.Media);
            Assert.True(result.Diagnostics.IsEmpty);
            Assert.IsType<Picture>(Assert.Single(result.Document.Paragraphs.Single().Objects).Object);
        }
        finally
        {
            if (Directory.Exists(media))
                Directory.Delete(media, recursive: true);
        }
    }

    [Fact]
    public void AnAbsoluteImagePathInsideTheMediaDirectory_IsStillRejected()
    {
        string media = Path.Combine(Path.GetTempPath(), "quillwright-markdown-absolute-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(media);
            string image = Path.Combine(media, "image.png");
            File.WriteAllBytes(image, TestImages.Png);
            string absolute = image.Replace('\\', '/');

            MarkdownImportResult result = MarkdownImporter.Import(
                $"![local](<{absolute}>)",
                new MarkdownImportOptions { MediaDirectory = media });

            Assert.Empty(result.Document.Media);
            Assert.Equal("local", result.Document.Paragraphs.Single().Text);
            Assert.Empty(result.Document.Paragraphs.Single().Objects);
            MarkdownImportWarning warning = Assert.Single(result.Diagnostics);
            Assert.Equal(MarkdownImportWarningKind.ImageSkipped, warning.Kind);
            Assert.Equal(absolute, warning.Subject);
            Assert.Equal(1, warning.Line);
            Assert.Contains("rooted image path", warning.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(media))
                Directory.Delete(media, recursive: true);
        }
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
