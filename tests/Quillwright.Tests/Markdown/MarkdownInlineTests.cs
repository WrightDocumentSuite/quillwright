using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests.Markdown;

public class MarkdownInlineTests
{
    [Fact]
    public void EquivalentAdjacentFormatting_IsCoalescedAcrossRunBoundaries()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("a", RunFormat.Default with { Bold = true, Color = WordColor.FromRgb(255, 0, 0) });
        paragraph.AppendText("b", RunFormat.Default with { Bold = true });

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("**ab**\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "text-color");
    }

    [Fact]
    public void EmphasisDelimiters_StayOutsideLeadingAndTrailingWhitespace()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendText(
            " bold ", RunFormat.Default with { Bold = true });

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(" **bold** \n", markdown.Text);
    }

    [Fact]
    public void InlineCode_ChoosesDelimiterLongerThanEmbeddedBackticks()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Use ", RunFormat.Default);
        paragraph.AppendText("a`b", RunFormat.Default with { FontAscii = "Consolas" });
        paragraph.AppendText(" now", RunFormat.Default);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("Use ``a`b`` now\n", markdown.Text);
    }

    [Fact]
    public void LinkLabelAndDestination_AreEscapedInTheirOwnContexts()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("site [x]");
        paragraph.AddRange(new Hyperlink { Url = "https://example.com/a (b)" }, 0, paragraph.TextLength);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("[site \\[x\\]](<https://example.com/a (b)>)\n", markdown.Text);
    }

    [Fact]
    public void OverlappingHyperlinks_KeepTheOutermostTargetAndWarnOnce()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("abcdef");
        paragraph.AddRange(new Hyperlink { Url = "https://outer.example" }, 0, 6);
        paragraph.AddRange(new Hyperlink { Url = "https://inner.example" }, 2, 2);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("[abcdef](https://outer.example)\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "overlapping-hyperlinks");
    }

    [Fact]
    public void ControlCharactersInLinkTargets_AreRejectedInsideHtmlFallbacksToo()
    {
        WordDocument document = WordDocument.Create();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        Paragraph paragraph = cell.AddParagraph("safe label");
        paragraph.AddRange(new Hyperlink { Url = "https://example.com/\nunsafe" }, 0, paragraph.TextLength);
        cell.AddParagraph("forces HTML");
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Sections[0].Blocks.Add(table);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Contains("safe label", markdown.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("href=", markdown.Text, StringComparison.Ordinal);
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "link-control-character");
    }

    [Fact]
    public void NestedComplexField_EmitsOnlyTheOuterCachedResult()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Before ");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Begin });
        paragraph.AppendText("OUTER instruction");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Begin });
        paragraph.AppendText("INNER instruction");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Separate });
        paragraph.AppendText("inner result");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.End });
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Separate });
        paragraph.AppendText("outer result");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.End });
        paragraph.AppendText(" after");

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("Before outer result after\n", markdown.Text);
    }

    [Fact]
    public void FieldWithoutCachedResult_IsSkippedWithDiagnostic()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Before");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.Begin });
        paragraph.AppendText("PAGE");
        paragraph.AppendObject(new FieldCharacter { Kind = FieldCharKind.End });
        paragraph.AppendText("After");

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("BeforeAfter\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "field-without-result");
    }

    [Fact]
    public void EmptySimpleField_IsSkippedWithTheSameDiagnosticContract()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AddRange(new SimpleField { Instruction = "PAGE" }, 0, 0);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "field-without-result");
    }

    [Fact]
    public void InlineControlAndRawWrappers_PreserveTextAndReportLostStructure()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("controlled raw");
        paragraph.AddRange(new InlineContentControl { Tag = "example" }, 0, 10);
        paragraph.AddRange(new RawRange("<w:smartTag>", "</w:smartTag>"), 11, 3);

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("controlled raw\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "inline-content-control");
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "raw-range");
    }

    [Fact]
    public void EveryTrackedRangeKind_FollowsAcceptedAndOriginalViews()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("KIDFT");
        paragraph.AddRange(new Revision { Kind = RevisionKind.Inserted, Id = 1 }, 1, 1);
        paragraph.AddRange(new Revision { Kind = RevisionKind.Deleted, Id = 2 }, 2, 1);
        paragraph.AddRange(new Revision { Kind = RevisionKind.MovedFrom, Id = 3 }, 3, 1);
        paragraph.AddRange(new Revision { Kind = RevisionKind.MovedTo, Id = 4 }, 4, 1);

        MarkdownDocument accepted = document.ToMarkdown();
        MarkdownDocument original = document.ToMarkdown(new MarkdownExportOptions
        {
            RevisionMode = MarkdownRevisionMode.Original,
        });

        Assert.Equal("KIT\n", accepted.Text);
        Assert.Equal("KDF\n", original.Text);
    }

    [Fact]
    public void AlternateContentIsUnwrappedAndRawInlineIsDiagnosed()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendObject(new AlternateContent(
            "<mc:AlternateContent><mc:Choice>",
            new SymbolCharacter { Character = 0x2713 },
            "</mc:Choice></mc:AlternateContent>"));
        paragraph.AppendObject(new RawInline("<w:unknown/>"));

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("✓\n", markdown.Text);
        Assert.Contains(markdown.Diagnostics, warning => warning.Subject == "raw-inline");
    }
}
