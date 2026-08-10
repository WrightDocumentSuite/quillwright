using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Html;
using Quillwright.Model;
using Quillwright.Rendering;
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
    public void ImportedCssString_CannotCreateAnAttributeWhenExported()
    {
        WordDocument imported = HtmlImporter.Import(
            "<p><span style=\"font-family:'safe&quot; onmouseover=&quot;alert(1)'\">payload</span></p>").Document;

        string exported = imported.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        HtmlElement span = Descendants(HtmlParser.Parse(exported)).Single(static element => element.Is("span"));

        Assert.Equal("payload", imported.Paragraphs.Single().Text);
        Assert.DoesNotContain(" onmouseover=\"", exported, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("font-family:'safe&quot; onmouseover=&quot;alert(1)'", exported, StringComparison.Ordinal);
        Assert.Null(span.Attribute("onmouseover"));
        Assert.Single(span.Attributes);
    }

    [Theory]
    [InlineData("font-family:'Semi; Colon', serif", "Semi; Colon")]
    [InlineData("font-family:'Comma, Family', serif", "Comma, Family")]
    [InlineData("font-family:O\\27 Reilly, serif", "O'Reilly")]
    [InlineData("font-family:L\\FC beck", "Lübeck")]
    [InlineData("font-family:MiXeD Face", "MiXeD Face")]
    [InlineData("font-family:'A!important'", "A!important")]
    public void CssFontFamily_RespectsStringsListsAndEscapes(string style, string expected)
    {
        WordDocument imported = HtmlImporter.Import($"<p><span style=\"{style}\">x</span></p>").Document;

        Assert.Equal(expected, RunAt(imported.Paragraphs.Single(), "x").FontAscii);
    }

    [Theory]
    [InlineData("color:#ff0000 !important;color:#0000ff", 0xFF0000u)]
    [InlineData("color:#ff0000;color:#0000ff ! important", 0x0000FFu)]
    [InlineData("color:#ff0000 !/**/important;color:#0000ff", 0xFF0000u)]
    public void ImportantCssDeclaration_ControlsCascadeOrder(string style, uint expected)
    {
        WordDocument imported = HtmlImporter.Import($"<p><span style=\"{style}\">x</span></p>").Document;

        Assert.Equal(expected, RunAt(imported.Paragraphs.Single(), "x").Color?.Rgb);
    }

    [Fact]
    public void UnexpectedNewlineInCssString_DropsTheMalformedDeclaration()
    {
        WordDocument imported = HtmlImporter.Import(
            "<p><span style=\"font-family:'broken\ncolor:#ff0000;color:#0000ff\">x</span></p>").Document;
        RunFormat format = RunAt(imported.Paragraphs.Single(), "x");

        Assert.Null(format.FontAscii);
        Assert.Equal(0x0000FFu, format.Color?.Rgb);
    }

    [Fact]
    public void CssFontFamily_InheritDiffersFromAQuotedFamilyNamedInherit()
    {
        WordDocument imported = HtmlImporter.Import(
            "<p><span style=\"font-family:Parent Face\"><span style=\"font-family:inherit\">x</span>" +
            "<span style=\"font-family:'inherit'\">y</span></span></p>").Document;
        Paragraph paragraph = imported.Paragraphs.Single();

        Assert.Equal("Parent Face", RunAt(paragraph, "x").FontAscii);
        Assert.Equal("inherit", RunAt(paragraph, "y").FontAscii);
    }

    [Fact]
    public void InvalidCssFontFamily_DoesNotOverrideThePreviousDeclaration()
    {
        WordDocument imported = HtmlImporter.Import(
            "<p><span style=\"font-family:Arial;font-family:&quot;Lucida&quot; Grande;color:/*compat*/red\">x</span></p>").Document;
        RunFormat format = RunAt(imported.Paragraphs.Single(), "x");

        Assert.Equal("Arial", format.FontAscii);
        Assert.Equal(0xFF0000u, format.Color?.Rgb);
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
    public void NonBreakingAndUnicodeSpaces_DoNotCollapse()
    {
        WordDocument document = HtmlImporter.Import("<p>a&nbsp;b\u2003c\t d</p>").Document;

        Assert.Equal("a\u00A0b\u2003c d", document.Paragraphs.Single().Text);
    }

    [Fact]
    public void TemplateIsInertAndNoscriptIsVisibleWhenScriptingIsDisabled()
    {
        HtmlImportResult result = HtmlImporter.Import(
            "<body><template><p>hidden template</p></template>" +
            "<noscript><p>visible fallback</p></noscript><p>visible body</p></body>");

        Assert.Equal(["visible fallback", "visible body"], result.Document.Paragraphs.Select(static p => p.Text).ToArray());
        HtmlImportWarning warning = Assert.Single(result.Diagnostics);
        Assert.Equal(HtmlImportWarningKind.ContentSkipped, warning.Kind);
        Assert.Equal("template", warning.Subject);
    }

    [Fact]
    public void FragmentImport_UsesTheContextElementAndDoesNotInventADocumentBody()
    {
        WordDocument document = HtmlImporter.ImportFragment(
            "a<b>c</b>&amp;", contextElement: "textarea").Document;

        Assert.Equal("a<b>c</b>&", document.Paragraphs.Single().Text);
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
        Assert.Equal(items[0].Format.NumberingId, items[1].Format.NumberingId);
        Assert.NotEqual(items[0].Format.NumberingId, items[2].Format.NumberingId);
        Assert.Equal(1, items[2].Format.NumberingLevel);
        Assert.NotEqual(items[0].Format.NumberingId, items[3].Format.NumberingId);
    }

    [Fact]
    public void OrderedListStartAndType_BecomeTheNumberingLevel()
    {
        WordDocument document = HtmlImporter.Import(
            "<ol start=\"5\" type=\"I\"><li>five</li><li>six</li></ol>").Document;
        Paragraph[] items = [.. document.Paragraphs];
        int numberingId = Assert.IsType<int>(items[0].Format.NumberingId);
        NumberingLevel level = Assert.IsType<NumberingLevel>(document.Numbering.ResolveLevel(numberingId, 0));

        Assert.Equal(numberingId, items[1].Format.NumberingId);
        Assert.Equal(5, level.Start);
        Assert.Equal(ListNumberFormat.UpperRoman, level.Format);
        Assert.Equal([5, 6], ListValues(document));

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        Assert.Contains("<ol type=\"I\" start=\"5\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HtmlFileEncoding_MetaCharsetDecodesWindows1251()
    {
        Encoding windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        byte[] bytes = windows1251.GetBytes("<meta charset=windows-1251><p>Привет</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("Привет", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task HtmlFileEncoding_BomTakesPrecedenceOverMetaCharset()
    {
        byte[] source = Encoding.UTF8.GetBytes("<meta charset=windows-1251><p>Привет</p>");
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. source];

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("Привет", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task HtmlFileEncoding_HttpEquivDecodesWindows1251()
    {
        Encoding windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        byte[] bytes = windows1251.GetBytes(
            "<meta http-equiv=content-type content='text/html; charset=windows-1251'><p>Привет</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("Привет", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task HtmlFileEncoding_MetaAfterFirst1024BytesIsIgnored()
    {
        Encoding windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        byte[] bytes = windows1252.GetBytes(
            new string(' ', 1024) + "<meta charset=windows-1252><p>café</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("caf\uFFFD", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task HtmlFileEncoding_MetaInsideCommentIsIgnored()
    {
        Encoding windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        byte[] bytes = windows1252.GetBytes(
            "<!-- <meta charset=windows-1252> --><p>café</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("caf\uFFFD", document.Paragraphs.Single().Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HtmlFileEncoding_Utf16BomIsCertain(bool bigEndian)
    {
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("<p>Привет</p>")];

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("Привет", document.Paragraphs.Single().Text);
    }

    [Theory]
    [InlineData("<META CHARSET=WINDOWS-1251>")]
    [InlineData("<META CONTENT='text/html; CHARSET=WINDOWS-1251' HTTP-EQUIV=CONTENT-TYPE>")]
    public async Task HtmlFileEncoding_MetaPrescanIsAsciiInsensitiveAndAttributeOrderIndependent(string meta)
    {
        Encoding windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        byte[] bytes = windows1251.GetBytes(meta + "<p>Привет</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("Привет", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task HtmlFileEncoding_Utf16MetaLabelIsRemappedToUtf8()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<meta charset=utf-16le><p>Привет</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("Привет", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task HtmlFileEncoding_XUserDefinedMetaLabelIsRemappedToWindows1252()
    {
        Encoding windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        byte[] bytes = windows1252.GetBytes("<meta charset=x-user-defined><p>café</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("café", document.Paragraphs.Single().Text);
    }

    [Fact]
    public async Task HtmlFileEncoding_UnsupportedUtf7MetaLabelDoesNotOverrideUtf8Fallback()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<meta charset=utf-7><p>Привет</p>");

        WordDocument document = await ImportHtmlBytesAsync(bytes);

        Assert.Equal("Привет", document.Paragraphs.Single().Text);
    }

    [Fact]
    public void HtmlFileEncoding_ChunkedDecodePreservesSplitUtf8Sequences()
    {
        string expected = new string('a', (32 * 1024) - 1) + "😀é";
        byte[] bytes = Encoding.UTF8.GetBytes(expected);

        string decoded = HtmlEncoding.Decode(bytes, TestContext.Current.CancellationToken);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void HtmlFileEncoding_LargeDecodeHonorsCancellation()
    {
        byte[] bytes = GC.AllocateUninitializedArray<byte>(32 * 1024 * 1024);
        bytes.AsSpan().Fill((byte)'a');
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(1));

        Assert.ThrowsAny<OperationCanceledException>(() => HtmlEncoding.Decode(bytes, cancellation.Token));
    }

    [Fact]
    public async Task HtmlFileEncoding_DecodeEnforcesTheCharacterBudgetBeforeMaterialization()
    {
        const int CharacterLimit = 1_000;
        byte[] bytes = GC.AllocateUninitializedArray<byte>(1024 * 1024);
        bytes.AsSpan().Fill((byte)'a');
        string path = Path.Combine(
            Path.GetTempPath(), $"quillwright-html-decode-budget-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var options = new HtmlImportOptions
            {
                Budget = DocumentLoadBudget.Default with { MaxTextCharacters = CharacterLimit },
            };

            DocumentLoadLimitException exception = await Assert.ThrowsAsync<DocumentLoadLimitException>(() =>
                HtmlImporter.ImportFileAsync(path, options, TestContext.Current.CancellationToken));

            Assert.Equal(nameof(DocumentLoadBudget.MaxTextCharacters), exception.LimitName);
            Assert.Equal(CharacterLimit, exception.Limit);
            Assert.True(exception.Observed > CharacterLimit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ListItemValue_RestartsThisAndFollowingItems()
    {
        WordDocument document = HtmlImporter.Import(
            "<ol><li>one</li><li value=\"7\">seven</li><li>eight</li></ol>").Document;

        Assert.Equal([1, 7, 8], ListValues(document));

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        Assert.Contains("<li value=\"7\">seven</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"8\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ReversedList_PreservesDescendingOrdinals()
    {
        WordDocument document = HtmlImporter.Import(
            "<ol reversed><li>three</li><li>two</li><li>one</li></ol>").Document;

        Assert.Equal([3, 2, 1], ListValues(document));

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        Assert.Contains("<ol start=\"3\">", html, StringComparison.Ordinal);
        Assert.Contains("<li value=\"2\">two</li>", html, StringComparison.Ordinal);
        Assert.Contains("<li value=\"1\">one</li>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedNestedListKinds_KeepTheirOwnNumberingInstances()
    {
        WordDocument document = HtmlImporter.Import(
            "<ul><li>outer<ol type=\"a\"><li>inner</li></ol></li><li>end</li></ul>").Document;
        Paragraph[] items = [.. document.Paragraphs];
        int outerId = Assert.IsType<int>(items[0].Format.NumberingId);
        int innerId = Assert.IsType<int>(items[1].Format.NumberingId);

        Assert.Equal(outerId, items[2].Format.NumberingId);
        Assert.NotEqual(outerId, innerId);
        Assert.Equal(ListNumberFormat.Bullet, document.Numbering.ResolveLevel(outerId, 0)?.Format);
        Assert.Equal(ListNumberFormat.LowerLetter, document.Numbering.ResolveLevel(innerId, 1)?.Format);

        string html = document.ToHtml(new HtmlExportOptions { FullDocument = false }).Text;
        HtmlElement outer = Descendants(HtmlParser.Parse(html)).Single(static element => element.Is("ul"));
        HtmlElement firstItem = outer.Children.OfType<HtmlElement>().First(static element => element.Is("li"));
        HtmlElement nested = firstItem.Children.OfType<HtmlElement>().Single(static element => element.Is("ol"));
        Assert.Equal("a", nested.Attribute("type"));
    }

    [Fact]
    public void CssListStyleType_OverridesTheHtmlTypeHint()
    {
        WordDocument document = HtmlImporter.Import(
            "<ol type=\"A\" style=\"list-style-type:lower-roman\"><li>x</li></ol>").Document;
        Paragraph item = document.Paragraphs.Single();
        int numberingId = Assert.IsType<int>(item.Format.NumberingId);

        Assert.Equal(ListNumberFormat.LowerRoman, document.Numbering.ResolveLevel(numberingId, 0)?.Format);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASiblingDirectorySharingTheMediaPrefix_IsNotRead(bool percentEncoded)
    {
        string parent = Path.Combine(Path.GetTempPath(), "quillwright-html-import-" + Guid.NewGuid().ToString("N"));
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

            HtmlImportResult result = HtmlImporter.Import(
                $"<p><img src=\"{reference}\" alt=\"secret\"></p>",
                new HtmlImportOptions { MediaDirectory = media });

            Assert.Empty(result.Document.Media);
            Assert.Equal("secret", result.Document.Paragraphs.Single().Text);
            Assert.Empty(result.Document.Paragraphs.Single().Objects);
            HtmlImportWarning warning = Assert.Single(result.Diagnostics);
            Assert.Equal(HtmlImportWarningKind.ImageSkipped, warning.Kind);
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
        string parent = Path.Combine(Path.GetTempPath(), "quillwright-html-link-" + Guid.NewGuid().ToString("N"));
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

            HtmlImportResult result = HtmlImporter.Import(
                "<p><img src=\"escape/secret.png\" alt=\"secret\"></p>",
                new HtmlImportOptions { MediaDirectory = media });

            Assert.Empty(result.Document.Media);
            Assert.Equal("secret", result.Document.Paragraphs.Single().Text);
            Assert.Empty(result.Document.Paragraphs.Single().Objects);
            HtmlImportWarning warning = Assert.Single(result.Diagnostics);
            Assert.Equal(HtmlImportWarningKind.ImageSkipped, warning.Kind);
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
        string media = Path.Combine(Path.GetTempPath(), "quillwright-html-local-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(media, "nested folder"));
            File.WriteAllBytes(Path.Combine(media, "nested folder", "image.png"), TestImages.Png);

            HtmlImportResult result = HtmlImporter.Import(
                "<p><img src=\"nested%20folder/image.png\" alt=\"local\"></p>",
                new HtmlImportOptions { MediaDirectory = media });

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
        string media = Path.Combine(Path.GetTempPath(), "quillwright-html-absolute-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(media);
            string image = Path.Combine(media, "image.png");
            File.WriteAllBytes(image, TestImages.Png);
            string absolute = image.Replace('\\', '/');

            HtmlImportResult result = HtmlImporter.Import(
                $"<p><img src=\"{absolute}\" alt=\"local\"></p>",
                new HtmlImportOptions { MediaDirectory = media });

            Assert.Empty(result.Document.Media);
            Assert.Equal("local", result.Document.Paragraphs.Single().Text);
            Assert.Empty(result.Document.Paragraphs.Single().Objects);
            HtmlImportWarning warning = Assert.Single(result.Diagnostics);
            Assert.Equal(HtmlImportWarningKind.ImageSkipped, warning.Kind);
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
    public void ScriptsStylesAndEntities_AreHandled()
    {
        HtmlImportResult result = HtmlImporter.Import(
            "<p>AT&amp;T &mdash; caf&eacute;? &#8212; &#x2014; yes&nbsp;sir</p><script>let x = '<p>not a paragraph</p>';</script>");

        Assert.Single(result.Document.Paragraphs);
        string text = result.Document.Paragraphs.Single().Text;
        Assert.StartsWith("AT&T — caf", text, StringComparison.Ordinal);
        Assert.Contains("— — yes\u00A0sir", text, StringComparison.Ordinal);
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

    private static async Task<WordDocument> ImportHtmlBytesAsync(byte[] bytes)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "quillwright-html-encoding-" + Guid.NewGuid().ToString("N") + ".html");

        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            return (await HtmlImporter.ImportFileAsync(path)).Document;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static int[] ListValues(WordDocument document)
    {
        var counter = new NumberingCounter(document.Numbering);
        return [.. document.Paragraphs.Select(paragraph => counter.Next(paragraph.Format)!.Value.Value)];
    }

    private static IEnumerable<HtmlElement> Descendants(HtmlElement parent)
    {
        foreach (HtmlNode node in parent.Children)
        {
            if (node is not HtmlElement element)
                continue;

            yield return element;
            foreach (HtmlElement descendant in Descendants(element))
                yield return descendant;
        }
    }
}
