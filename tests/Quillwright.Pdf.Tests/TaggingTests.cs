using Inkwright;
using Inkwright.Annotations;
using Inkwright.Cos;
using Inkwright.Layout;
using Inkwright.Tagging;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class TaggingTests
{
    private static readonly PdfExportOptions Tagged = new() { Tagged = true, Language = "en-GB" };

    /// <summary>Every structure tag in the tree, in reading order.</summary>
    private static List<string> Tags(PdfDocument pdf)
    {
        List<string> tags = [];
        Walk(pdf.Structure, tags);
        return tags;

        static void Walk(IReadOnlyList<PdfStructureNode> nodes, List<string> into)
        {
            foreach (PdfStructureNode node in nodes)
            {
                into.Add(node.Tag);
                Walk(node.Children, into);
            }
        }
    }

    [Fact]
    public void AnUntaggedExportHasNoStructureTree()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Plain");

        using Rendered rendered = Rendered.Of(document);

        Assert.Empty(rendered.Document.Structure);
    }

    [Fact]
    public void ParagraphsBecomeParagraphElements()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("One");
        document.Sections[0].AddParagraph("Two");

        using Rendered rendered = Rendered.Of(document, Tagged);
        List<string> tags = Tags(rendered.Document);

        Assert.Equal("Document", tags[0]);
        Assert.Equal(2, tags.Count(tag => tag == "P"));
    }

    [Fact]
    public void AHeadingBecomesAHeadingElement()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.GetOrAdd("Heading1");
        document.Styles.GetOrAdd("Heading2");
        document.Sections[0].AddParagraph("Chapter", "Heading1");
        document.Sections[0].AddParagraph("Section", "Heading2");
        document.Sections[0].AddParagraph("Body");

        using Rendered rendered = Rendered.Of(document, Tagged);
        List<string> tags = Tags(rendered.Document);

        Assert.Contains("H1", tags);
        Assert.Contains("H2", tags);
        Assert.Contains("P", tags);
    }

    [Fact]
    public void AnOutlineDeeperThanSixStopsAtTheSixthHeading()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Deep");
        paragraph.Format = paragraph.Format with { OutlineLevel = 8 };

        using Rendered rendered = Rendered.Of(document, Tagged);

        Assert.Contains("H6", Tags(rendered.Document));
    }

    [Fact]
    public void AListBecomesListItemsInsideOneList()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();

        foreach (string text in new[] { "First", "Second" })
        {
            Paragraph item = document.Sections[0].AddParagraph(text);
            item.Format = item.Format with { NumberingId = list };
        }

        using Rendered rendered = Rendered.Of(document, Tagged);
        List<string> tags = Tags(rendered.Document);

        Assert.Equal(1, tags.Count(tag => tag == "L"));
        Assert.Equal(2, tags.Count(tag => tag == "LI"));
        Assert.Equal(2, tags.Count(tag => tag == "LBody"));
    }

    [Fact]
    public void ADeeperListSitsInsideTheItemAboveIt()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        Section section = document.Sections[0];

        void Item(string text, int level)
        {
            Paragraph paragraph = section.AddParagraph(text);
            paragraph.Format = paragraph.Format with { NumberingId = list, NumberingLevel = level };
        }

        Item("Top", 0);
        Item("Nested", 1);
        Item("Top again", 0);

        using Rendered rendered = Rendered.Of(document, Tagged);
        List<string> tags = Tags(rendered.Document);

        Assert.Equal(2, tags.Count(tag => tag == "L"));
        Assert.Equal(3, tags.Count(tag => tag == "LI"));
    }

    [Fact]
    public void ATableBecomesRowsAndCells()
    {
        WordDocument document = WordDocument.Create();
        Table table = Table.Create(2, 2, Length.FromCentimeters(12));
        table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };
        table[0, 0].SetText("Name");
        table[0, 1].SetText("Value");
        table[1, 0].SetText("Speed");
        table[1, 1].SetText("Fast");
        document.Sections[0].Blocks.Add(table);

        using Rendered rendered = Rendered.Of(document, Tagged);
        List<string> tags = Tags(rendered.Document);

        Assert.Equal(1, tags.Count(tag => tag == "Table"));
        Assert.Equal(2, tags.Count(tag => tag == "TR"));
        Assert.Equal(2, tags.Count(tag => tag == "TH"));
        Assert.Equal(2, tags.Count(tag => tag == "TD"));
    }

    [Fact]
    public void APictureBecomesAFigureWithItsDescription()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        Picture picture = paragraph.AppendPicture(
            ImageData.FromBytes(Pixels.Png(20, 20)),
            Length.FromPoints(20),
            Length.FromPoints(20));

        picture.Description = "A blue square";

        using Rendered rendered = Rendered.Of(document, Tagged);

        Assert.Contains("Figure", Tags(rendered.Document));
    }

    [Fact]
    public void HeadersAreArtifactsRatherThanContent()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = false;
        section.Headers.GetOrCreate().AddParagraph("RUNNING HEAD");
        section.AddParagraph("Body");

        using Rendered rendered = Rendered.Of(document, Tagged);

        // The header is drawn but belongs to nobody, so no structure element claims its glyphs.
        Assert.Contains("RUNNING HEAD", rendered.Text(), StringComparison.Ordinal);
        Assert.Equal(1, Tags(rendered.Document).Count(tag => tag == "P"));
    }

    [Fact]
    public void TheLanguageAndTitleReachTheCatalog()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "A tagged document";
        document.Sections[0].AddParagraph("Body");

        using Rendered rendered = Rendered.Of(document, Tagged);

        Assert.Equal("A tagged document", rendered.Document.Info.Title);
        Assert.Equal("en-GB", rendered.Document.Catalog.GetTextString(Inkwright.Cos.PdfName.Get("Lang")));
    }

    [Fact]
    public void ATaggedDocumentPassesTheAccessibilityValidator()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "An accessible report";
        document.Styles.GetOrAdd("Heading1");
        Section section = document.Sections[0];

        section.AddParagraph("Quarterly report", "Heading1");
        section.AddParagraph("The quarter went as expected, all things considered.");

        int list = document.Numbering.AddNumberedList();
        foreach (string text in new[] { "Revenue rose", "Costs held" })
        {
            Paragraph item = section.AddParagraph(text);
            item.Format = item.Format with { NumberingId = list };
        }

        Table table = Table.Create(2, 2, Length.FromCentimeters(12));
        table.Rows[0].Format = table.Rows[0].Format with { IsHeader = true };
        table[0, 0].SetText("Line");
        table[0, 1].SetText("Amount");
        table[1, 0].SetText("Revenue");
        table[1, 1].SetText("1200");
        section.Blocks.Add(table);

        // Claiming conformance is the caller's decision, not the exporter's, so the test makes it
        // the way an application would: render, declare, save.
        using Rendered rendered = Rendered.Of(
            document,
            Tagged,
            pdf => PdfUaProfile.Declare(pdf, PdfUaConformance.Ua1, "An accessible report"));

        IReadOnlyList<PdfUaProblem> problems = PdfUaProfile.Validate(rendered.Document, PdfUaConformance.Ua1);
        IEnumerable<PdfUaProblem> violations = PdfUaProfile.Violations(problems);

        Assert.Empty(violations);
    }

    [Fact]
    public void ATaggedLinkOwnsItsTextAndAnnotationAndPassesUaValidation()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Accessible link";
        Paragraph paragraph = document.Sections[0].AddParagraph("Visit the site");
        paragraph.AddRange(new Hyperlink { Url = "https://example.org/" }, 6, 8);

        using Rendered rendered = Rendered.Of(
            document,
            Tagged,
            pdf => PdfUaProfile.Declare(pdf, PdfUaConformance.Ua1, "Accessible link"));

        PdfStructureNode link = Descendants(rendered.Document.Structure).Single(node => node.Tag == "Link");
        Assert.NotEmpty(link.Content);
        Assert.Equal(
            Assert.Single(rendered.Document.Pages[0].Annotations).Id,
            Assert.Single(link.Objects).Object);
        Assert.Empty(PdfUaProfile.Violations(
            PdfUaProfile.Validate(rendered.Document, PdfUaConformance.Ua1)));
    }

    [Fact]
    public void AWrappedTaggedLinkUsesOneAnnotationWithQuadPoints()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Accessible wrapped link";
        string text = string.Join(' ', Enumerable.Repeat(
            "A long linked phrase continues across the available line width", 5));
        Paragraph paragraph = document.Sections[0].AddParagraph(text);
        paragraph.AddRange(new Hyperlink { Url = "https://example.org/wrapped" }, 0, paragraph.TextLength);

        using Rendered rendered = Rendered.Of(
            document,
            Tagged,
            pdf => PdfUaProfile.Declare(pdf, PdfUaConformance.Ua1, "Accessible wrapped link"));

        Assert.True(rendered.Lines().Count > 1);
        PdfAnnotation annotation = Assert.Single(rendered.Document.Pages[0].Annotations);
        PdfArray quadrilaterals = Assert.IsType<PdfArray>(
            annotation.Dictionary.GetArray(PdfName.Get("QuadPoints")));
        Assert.True(quadrilaterals.Count >= 16);

        PdfStructureNode link = Descendants(rendered.Document.Structure).Single(node => node.Tag == "Link");
        Assert.True(link.Content.Count > 1);
        Assert.Equal(annotation.Id, Assert.Single(link.Objects).Object);
        Assert.Empty(PdfUaProfile.Violations(
            PdfUaProfile.Validate(rendered.Document, PdfUaConformance.Ua1)));
    }

    private static IEnumerable<PdfStructureNode> Descendants(IReadOnlyList<PdfStructureNode> nodes)
    {
        foreach (PdfStructureNode node in nodes)
        {
            yield return node;
            foreach (PdfStructureNode child in Descendants(node.Children))
                yield return child;
        }
    }
}
