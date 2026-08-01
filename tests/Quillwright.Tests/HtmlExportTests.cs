using Quillwright.Editing;
using Quillwright.Html;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

/// <summary>
/// The HTML export: semantic elements for what HTML can say, CSS for what only Word can name,
/// one self-contained page by default, and diagnostics naming every approximation.
/// </summary>
public class HtmlExportTests
{
    [Fact]
    public void AFullPage_CarriesItsSkeletonTitleAndLanguage()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Quarterly report";
        document.Sections[0].AddParagraph("Body.");

        string html = document.ToHtml().Text;

        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.Contains("<title>Quarterly report</title>", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"en-US\"", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>\n", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AFragment_IsJustTheBody()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Body only.");

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.DoesNotContain("<!DOCTYPE", html, StringComparison.Ordinal);
        Assert.Equal("<p>Body only.</p>\n", html);
    }

    [Fact]
    public void HeadingsAndAlignment_BecomeElementsAndCss()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Top", "Heading1");
        document.Sections[0].AddParagraph("Sub", "Heading3");
        Paragraph centred = document.Sections[0].AddParagraph("Middle");
        centred.Format = centred.Format with { Alignment = ParagraphAlignment.Center };

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.Contains("<h1>Top</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h3>Sub</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<p style=\"text-align:center\">Middle</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineFormatting_IsSemanticFirstAndCssSecond()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendText("b", RunFormat.Default with { Bold = true });
        paragraph.AppendText("i", RunFormat.Default with { Italic = true });
        paragraph.AppendText("s", RunFormat.Default with { Strike = true });
        paragraph.AppendText("u", RunFormat.Default with { Underline = UnderlineStyle.Single });
        paragraph.AppendText("up", RunFormat.Default with { VerticalAlignment = VerticalTextAlignment.Superscript });
        paragraph.AppendText("red", RunFormat.Default with { Color = WordColor.FromRgb(0xC00000) });
        paragraph.AppendText("hl", RunFormat.Default with { Highlight = HighlightColor.Yellow });
        paragraph.AppendText("mono", RunFormat.Default with { FontAscii = "Consolas", FontHighAnsi = "Consolas" });
        document.Sections[0].Blocks.Add(paragraph);

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.Contains("<strong>b</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>i</em>", html, StringComparison.Ordinal);
        Assert.Contains("<s>s</s>", html, StringComparison.Ordinal);
        Assert.Contains("<u>u</u>", html, StringComparison.Ordinal);
        Assert.Contains("<sup>up</sup>", html, StringComparison.Ordinal);
        Assert.Contains("<span style=\"color:#c00000\">red</span>", html, StringComparison.Ordinal);
        Assert.Contains("<mark style=\"background:#ffff00\">hl</mark>", html, StringComparison.Ordinal);
        Assert.Contains("<code>mono</code>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LinksAndAnchors_ResolveAndEscape()
    {
        WordDocument document = WordDocument.Create();
        Paragraph target = document.Sections[0].AddParagraph("Target here.");
        target.AddMark(new BookmarkStart { Id = 1, Name = "The Target" }, 0);
        target.AddMark(new BookmarkEnd { Id = 1 }, target.TextLength);

        Paragraph linked = document.Sections[0].AddParagraph("go & see");
        linked.AddRange(new Hyperlink { Url = "https://example.org/a?b=1&c=2", Tooltip = "spec" }, 0, 2);
        linked.AddRange(new Hyperlink { Anchor = "The Target" }, 5, 3);

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.Contains("<a href=\"https://example.org/a?b=1&amp;c=2\" title=\"spec\">go</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a id=\"bookmarkthe-target\"></a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#bookmarkthe-target\">see</a>", html, StringComparison.Ordinal);
        Assert.Contains("&amp; ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExecutableLink_IsRenderedAsTextAndNamed()
    {
        WordDocument document = WordDocument.Create();
        Paragraph linked = document.Sections[0].AddParagraph("click");
        linked.AddRange(new Hyperlink { Url = "javascript:alert(1)" }, 0, 5);

        Quillwright.Html.HtmlDocument html = document.ToHtml(new HtmlExportOptions { FullDocument = false });

        Assert.DoesNotContain("<a ", html.Text, StringComparison.Ordinal);
        Assert.Contains("click", html.Text, StringComparison.Ordinal);
        Assert.Contains(html.Diagnostics, static w => w.Kind == HtmlExportWarningKind.UnsafeLinkSkipped);
    }

    [Fact]
    public void Lists_NestAsRealListElements()
    {
        WordDocument document = WordDocument.Create();
        int bullets = document.Numbering.AddBulletList();
        AddItem(document, bullets, 0, "one");
        AddItem(document, bullets, 0, "two");
        AddItem(document, bullets, 1, "nested");

        int numbers = document.Numbering.AddNumberedList();
        AddItem(document, numbers, 0, "first");
        AddItem(document, numbers, 0, "second");

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("<li>one</li>", html, StringComparison.Ordinal);
        int outer = html.IndexOf("<li>two</li>", StringComparison.Ordinal);
        int nested = html.IndexOf("<li>nested</li>", StringComparison.Ordinal);
        Assert.True(outer >= 0 && nested > outer);
        Assert.Contains("<ol>", html, StringComparison.Ordinal);
        Assert.Contains("<li>first</li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ATable_KeepsItsHeaderAndItsMerges()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();

        var header = new TableRow();
        header.Format = header.Format with { IsHeader = true };
        header.AddCell("Name");
        header.AddCell("Value");
        table.Rows.Add(header);

        var merged = new TableRow();
        TableCell wide = merged.AddCell("spans two");
        wide.Format = wide.Format with { GridSpan = 2 };
        table.Rows.Add(merged);

        var top = new TableRow();
        TableCell tall = top.AddCell("tall");
        tall.Format = tall.Format with { VerticalMerge = VerticalMerge.Restart };
        top.AddCell("beside");
        table.Rows.Add(top);

        var bottom = new TableRow();
        TableCell continuation = bottom.AddCell(string.Empty);
        continuation.Format = continuation.Format with { VerticalMerge = VerticalMerge.Continue };
        bottom.AddCell("under");
        table.Rows.Add(bottom);

        document.Sections[0].Blocks.Add(table);

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.Contains("<thead>", html, StringComparison.Ordinal);
        Assert.Contains("<th>", html, StringComparison.Ordinal);
        Assert.Contains("colspan=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("rowspan=\"2\"", html, StringComparison.Ordinal);

        // Four body cells reach the page: the continuation cell is spanned over, not emitted.
        Assert.Equal(4, CountOf(html, "<td"));
    }

    [Fact]
    public void APicture_TravelsAsADataUriByDefault()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendPicture(Png());
        document.Sections[0].Blocks.Add(paragraph);

        Quillwright.Html.HtmlDocument html = document.ToHtml(new HtmlExportOptions { FullDocument = false });

        Assert.Contains("<img src=\"data:image/png;base64,", html.Text, StringComparison.Ordinal);
        Assert.Empty(html.Images);
    }

    [Fact]
    public void SidecarImages_AreCollectedAndReferenced()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendPicture(Png());
        document.Sections[0].Blocks.Add(paragraph);

        Quillwright.Html.HtmlDocument html = document.ToHtml(new HtmlExportOptions
        {
            FullDocument = false,
            Images = HtmlImageMode.Sidecar,
        });

        Assert.Contains("<img src=\"media/image1.png\"", html.Text, StringComparison.Ordinal);
        HtmlImage image = Assert.Single(html.Images);
        Assert.Equal("image1.png", image.FileName);
        Assert.Equal("image/png", image.ContentType);
    }

    [Fact]
    public void AFootnote_LinksBothWays()
    {
        WordDocument document = WordDocument.Create();
        Paragraph noted = document.Sections[0].AddParagraph("Noted.");
        document.AddFootnote(noted, "The small print.");

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;

        Assert.Contains("<sup id=\"fn-", html, StringComparison.Ordinal);
        Assert.Contains("class=\"footnotes\"", html, StringComparison.Ordinal);
        Assert.Contains("The small print.", html, StringComparison.Ordinal);
        Assert.Contains("↩", html, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkedRevisions_BecomeInsAndDel()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("The quick brown fox.");
        WordDocument revised = WordDocument.Create();
        revised.Sections[0].AddParagraph("The quick red fox.");

        WordDocument redline = DocumentComparer.Compare(original, revised).Document;

        string marked = redline.ToHtml(new HtmlExportOptions
        {
            FullDocument = false,
            RevisionMode = HtmlRevisionMode.Marked,
        }).Text;
        Assert.Contains("<del>", marked, StringComparison.Ordinal);
        Assert.Contains("<ins>", marked, StringComparison.Ordinal);

        string accepted = redline.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        Assert.Contains("red fox", accepted, StringComparison.Ordinal);
        Assert.DoesNotContain("brown", accepted, StringComparison.Ordinal);

        string originalView = redline.ToHtml(new HtmlExportOptions
        {
            FullDocument = false,
            RevisionMode = HtmlRevisionMode.Original,
        }).Text;
        Assert.Contains("brown fox", originalView, StringComparison.Ordinal);
        Assert.DoesNotContain("red fox", originalView, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExport_IsDeterministic()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Stable.", "Heading1");
        document.Sections[0].AddParagraph("Body with **markers** kept literal.");

        Assert.Equal(document.ToHtml().Text, document.ToHtml().Text);
    }

    private static void AddItem(WordDocument document, int listId, int level, string text)
    {
        Paragraph paragraph = document.Sections[0].AddParagraph(text);
        paragraph.Format = paragraph.Format with { NumberingId = listId, NumberingLevel = level };
    }

    private static ImageData Png()
    {
        const string Base64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        return ImageData.FromBytes(Convert.FromBase64String(Base64));
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
