using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Tests.Markdown;

public class MarkdownBlockTests
{
    [Fact]
    public void HeadingsQuotesAndEmptyParagraphs_HaveStableBlockStructure()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Report", "Heading1");
        document.Sections[0].AddParagraph();
        document.Sections[0].AddParagraph("First quote", "Quote");
        document.Sections[0].AddParagraph("Second quote", "Quote");
        document.Sections[0].AddParagraph();
        document.Sections[0].AddParagraph("Tail");

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("# Report\n\n> First quote\n>\n> Second quote\n\nTail\n", markdown.Text);
    }

    [Fact]
    public void CodeParagraphs_UseAFenceLongerThanTheirContents()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("var fence = ```;", "CodeBlock");
        document.Sections[0].AddParagraph("~~~", "CodeBlock");

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal("````\nvar fence = ```;\n~~~\n````\n", markdown.Text);
    }

    [Fact]
    public void BulletAndNestedOrderedLists_UseValidMarkdownIndentation()
    {
        WordDocument document = WordDocument.Create();
        int bullets = document.Numbering.AddBulletList();
        AddListParagraph(document, bullets, 0, "Alpha");
        AddListParagraph(document, bullets, 1, "Nested");
        AddListParagraph(document, bullets, 0, "Omega");
        document.Sections[0].AddParagraph("Between");
        int numbered = document.Numbering.AddNumberedList();
        AddListParagraph(document, numbered, 0, "First");
        AddListParagraph(document, numbered, 1, "Child");
        AddListParagraph(document, numbered, 0, "Second");

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "- Alpha\n    - Nested\n- Omega\n\nBetween\n\n1. First\n    1. Child\n2. Second\n",
            markdown.Text);
        Assert.Contains(markdown.Diagnostics,
            warning => warning.Subject == "list-format-lowerletter");
    }

    [Fact]
    public void OrderedList_UsesStartOverrideAndRestartAfterParent()
    {
        WordDocument document = WordDocument.Create();
        int id = document.Numbering.AddNumberedList();
        NumberingInstance instance = document.Numbering.Instances.Single(item => item.Id == id);
        instance.Overrides.Add(new NumberingLevelOverride { Level = 0, StartOverride = 4 });
        AddListParagraph(document, id, 0, "Parent A");
        AddListParagraph(document, id, 1, "Child A");
        AddListParagraph(document, id, 1, "Child B");
        AddListParagraph(document, id, 0, "Parent B");
        AddListParagraph(document, id, 1, "Child restarted");

        MarkdownDocument markdown = document.ToMarkdown();

        Assert.Equal(
            "4. Parent A\n    1. Child A\n    2. Child B\n5. Parent B\n    1. Child restarted\n",
            markdown.Text);
    }

    [Fact]
    public void DeletedParagraphMark_JoinsParagraphsOnlyInAcceptedView()
    {
        WordDocument document = WordDocument.Create();
        Paragraph first = document.Sections[0].AddParagraph("left ");
        first.MarkFormat = first.MarkFormat with { MarkRevisionXml = "<w:del w:id=\"1\"/>" };
        document.Sections[0].AddParagraph("right");

        MarkdownDocument accepted = document.ToMarkdown();
        MarkdownDocument original = document.ToMarkdown(new MarkdownExportOptions
        {
            RevisionMode = MarkdownRevisionMode.Original,
        });

        Assert.Equal("left right\n", accepted.Text);
        Assert.Equal("left \n\nright\n", original.Text);
    }

    [Fact]
    public void CommonMarkFlavor_DoesNotGenerateGfmStrikethrough()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendText(
            "removed", RunFormat.Default with { Strike = true });

        MarkdownDocument markdown = document.ToMarkdown(new MarkdownExportOptions
        {
            Flavor = MarkdownFlavor.CommonMark,
        });

        Assert.Equal("<del>removed</del>\n", markdown.Text);
        Assert.DoesNotContain("~~", markdown.Text, StringComparison.Ordinal);
    }

    private static void AddListParagraph(WordDocument document, int id, int level, string text)
    {
        Paragraph paragraph = document.Sections[0].AddParagraph(text);
        paragraph.Format = paragraph.Format with
        {
            NumberingId = id,
            NumberingLevel = level,
            StyleId = "ListParagraph",
        };
    }
}
