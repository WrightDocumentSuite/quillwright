using Quillwright.Editing;
using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests.Markdown;

public class MarkdownContractTests
{
    [Fact]
    public void EmptyDocument_HasOneTrailingLf()
    {
        MarkdownDocument markdown = WordDocument.Create().ToMarkdown();

        Assert.Equal("\n", markdown.Text);
        Assert.Empty(markdown.Images);
        Assert.Empty(markdown.Diagnostics);
    }

    [Fact]
    public void Paragraphs_AreEscapedAndSeparatedDeterministically()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("# heading\n1. item\nplain * [x] &");
        document.Sections[0].AddParagraph("second");

        MarkdownDocument first = document.ToMarkdown();
        MarkdownDocument second = document.ToMarkdown();

        Assert.Equal("\\# heading  \n1\\. item  \nplain \\* \\[x\\] \\&\n\nsecond\n", first.Text);
        Assert.Equal(first.Text, second.Text);
        Assert.DoesNotContain('\r', first.Text);
    }

    [Fact]
    public void InlineFormatting_UsesMarkdownAndTargetedHtml()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("plain ");
        paragraph.AppendText("bold", RunFormat.Default with { Bold = true });
        paragraph.AppendText(" and ", RunFormat.Default);
        paragraph.AppendText("italic", RunFormat.Default with { Italic = true });
        paragraph.AppendText(" and ", RunFormat.Default);
        paragraph.AppendText("strike", RunFormat.Default with { Strike = true });
        paragraph.AppendText(" and ", RunFormat.Default);
        paragraph.AppendText("under", RunFormat.Default with { Underline = UnderlineStyle.Single });
        paragraph.AppendText(" and ", RunFormat.Default);
        paragraph.AppendText("up", RunFormat.Default with { VerticalAlignment = VerticalTextAlignment.Superscript });

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "plain **bold** and *italic* and ~~strike~~ and <ins>under</ins> and <sup>up</sup>\n",
            markdown.Text);
    }

    [Fact]
    public void ComplexField_EmitsCachedResultButNotInstruction()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Page ");
        paragraph.AppendField("PAGE \\* MERGEFORMAT", "7");

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("Page 7\n", markdown.Text);
        Assert.DoesNotContain("MERGEFORMAT", markdown.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RevisionMode_ProjectsAcceptedAndOriginalWithoutMutatingDocument()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("keep ");
        int inserted = paragraph.TextLength;
        paragraph.AppendText("added ");
        paragraph.AddRange(new Revision { Kind = RevisionKind.Inserted, Id = 1 }, inserted, 6);
        int deleted = paragraph.TextLength;
        paragraph.AppendText("removed");
        paragraph.AddRange(new Revision { Kind = RevisionKind.Deleted, Id = 2 }, deleted, 7);

        string before = paragraph.Text;
        int rangeCount = paragraph.Ranges.Count();
        MarkdownDocument accepted = document.ToMarkdown();
        MarkdownDocument original = document.ToMarkdown(new MarkdownExportOptions
        {
            RevisionMode = MarkdownRevisionMode.Original,
        });

        Assert.Equal("keep added \n", accepted.Text);
        Assert.Equal("keep removed\n", original.Text);
        Assert.Equal(before, paragraph.Text);
        Assert.Equal(rangeCount, paragraph.Ranges.Count());
        Assert.True(document.HasRevisions());
    }

    [Fact]
    public void Hyperlinks_ResolveBookmarksRegisteredLaterInDocumentOrder()
    {
        WordDocument document = WordDocument.Create();
        Paragraph link = document.Sections[0].AddParagraph("Jump");
        link.AddRange(new Hyperlink { Anchor = "Target name" }, 0, link.TextLength);
        Paragraph target = document.Sections[0].AddParagraph("Destination");
        target.AddMark(new BookmarkStart { Id = 4, Name = "Target name" }, 0);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "[Jump](#bookmarktarget-name)\n\n<a id=\"bookmarktarget-name\"></a>Destination\n",
            markdown.Text);
    }

    [Fact]
    public void ExecutableHyperlink_IsFlattenedAndDiagnosed()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("do not run");
        paragraph.AddRange(new Hyperlink { Url = "javascript:alert(1)" }, 0, paragraph.TextLength);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("do not run\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Kind == MarkdownExportWarningKind.UnsafeLinkSkipped);
    }

    [Fact]
    public void HiddenText_IsControlledByOptionWithoutAWarning()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("visible");
        paragraph.AppendText(" secret", RunFormat.Default with { Hidden = true });

        MarkdownDocument omitted = document.ToMarkdown();
        MarkdownDocument included = document.ToMarkdown(new MarkdownExportOptions { IncludeHiddenText = true });

        Assert.Equal("visible\n", omitted.Text);
        Assert.Equal("visible secret\n", included.Text);
        Assert.Empty(omitted.Diagnostics);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../media")]
    [InlineData("media/../outside")]
    [InlineData("C:/absolute")]
    [InlineData("/absolute")]
    [InlineData("media//images")]
    [InlineData("media./images")]
    [InlineData("CON")]
    [InlineData("assets/COM1.txt")]
    public void UnsafeMediaDirectory_IsRejectedBeforeRendering(string path)
    {
        WordDocument document = WordDocument.Create();

        Assert.Throws<ArgumentException>(() => document.ToMarkdown(new MarkdownExportOptions
        {
            MediaDirectoryName = path,
        }));
    }

    [Fact]
    public void UnknownOptionValues_AreRejected()
    {
        WordDocument document = WordDocument.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.ToMarkdown(new MarkdownExportOptions
        {
            Flavor = (MarkdownFlavor)byte.MaxValue,
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ToMarkdown(new MarkdownExportOptions
        {
            RevisionMode = (MarkdownRevisionMode)byte.MaxValue,
        }));
    }
}
