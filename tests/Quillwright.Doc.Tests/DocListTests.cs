using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// A numbered paragraph does not carry its numbering: it names an override, which names a
/// definition, which holds the levels. These tests follow that chain out to the file and
/// back.
/// </summary>
public class DocListTests
{
    [Fact]
    public void ANumberedParagraph_StillPointsAtAList()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        Add(document, Numbered("First item", list, 0));
        Add(document, Numbered("Second item", list, 0));

        List<Paragraph> paragraphs = [.. RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.NotNull(paragraphs[0].Format.NumberingId);
        Assert.Equal(paragraphs[0].Format.NumberingId, paragraphs[1].Format.NumberingId);
        Assert.Equal(0, paragraphs[0].Format.NumberingLevel);
    }

    [Fact]
    public void TheLevelOfAnItem_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddOutlineList();
        Add(document, Numbered("Top", list, 0));
        Add(document, Numbered("Under it", list, 1));
        Add(document, Numbered("Deeper still", list, 2));

        List<Paragraph> paragraphs = [.. RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.Equal(0, paragraphs[0].Format.NumberingLevel);
        Assert.Equal(1, paragraphs[1].Format.NumberingLevel);
        Assert.Equal(2, paragraphs[2].Format.NumberingLevel);
    }

    [Fact]
    public void TheDefinitionAParagraphPointsAt_ComesBackWithIt()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        Add(document, Numbered("An item", list, 0));

        WordDocument reopened = RoundTrip(document);
        Paragraph paragraph = reopened.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();

        Assert.NotEmpty(reopened.Numbering.Definitions);
        Assert.NotNull(reopened.Numbering.ResolveDefinition(paragraph.Format.NumberingId!.Value));
    }

    [Fact]
    public void TheNumberFormatOfALevel_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        AbstractNumbering definition = document.Numbering.ResolveDefinition(list)!;
        definition.Levels[0].Format = ListNumberFormat.UpperRoman;
        definition.Levels[0].Start = 4;
        Add(document, Numbered("Fourth", list, 0));

        WordDocument reopened = RoundTrip(document);
        Paragraph paragraph = reopened.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();
        NumberingLevel level = reopened.Numbering.ResolveLevel(paragraph.Format.NumberingId!.Value, 0)!;

        Assert.Equal(ListNumberFormat.UpperRoman, level.Format);
        Assert.Equal(4, level.Start);
    }

    [Fact]
    public void ABulletList_KeepsItsFormat()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddBulletList();
        Add(document, Numbered("A bullet", list, 0));

        WordDocument reopened = RoundTrip(document);
        Paragraph paragraph = reopened.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();

        Assert.Equal(ListNumberFormat.Bullet, reopened.Numbering.ResolveLevel(paragraph.Format.NumberingId!.Value, 0)!.Format);
    }

    [Fact]
    public void ALabelPattern_KeepsItsLevelPlaceholders()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddOutlineList();
        AbstractNumbering definition = document.Numbering.ResolveDefinition(list)!;
        definition.Levels[1].Text = "%1.%2)";
        Add(document, Numbered("Second level", list, 1));

        WordDocument reopened = RoundTrip(document);
        Paragraph paragraph = reopened.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();

        Assert.Equal("%1.%2)", reopened.Numbering.ResolveLevel(paragraph.Format.NumberingId!.Value, 1)!.Text);
    }

    [Fact]
    public void TwoListsSharingADefinition_StayTwoLists()
    {
        WordDocument document = WordDocument.Create();
        int first = document.Numbering.AddNumberedList();
        int second = document.Numbering.AddNumberedList();
        Add(document, Numbered("List one", first, 0));
        Add(document, Numbered("List two", second, 0));

        List<Paragraph> paragraphs = [.. RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>()];

        Assert.NotEqual(paragraphs[0].Format.NumberingId, paragraphs[1].Format.NumberingId);
    }

    private static WordDocument RoundTrip(WordDocument document) => DocReader.Load(DocWriter.Save(document));

    private static void Add(WordDocument document, Block block) => document.Sections[0].Blocks.Add(block);

    private static Paragraph Numbered(string text, int list, int level) =>
        new(text) { Format = ParagraphFormat.Default with { NumberingId = list, NumberingLevel = level } };
}
