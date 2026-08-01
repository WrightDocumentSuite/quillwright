using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Tab stops are the one paragraph property stored as two lists in one operand: the stops a
/// paragraph adds, and the ones it refuses to inherit.
/// </summary>
public class DocTabStopTests
{
    [Fact]
    public void ATabStop_SurvivesTheRoundTrip()
    {
        ParagraphFormat parsed = RoundTrip(Tabs(new TabStop(Length.FromTwips(1440))));

        TabStop tab = Assert.Single(parsed.Tabs);
        Assert.Equal(Length.FromTwips(1440), tab.Position);
        Assert.Equal(TabAlignment.Left, tab.Alignment);
    }

    [Theory]
    [InlineData(TabAlignment.Left)]
    [InlineData(TabAlignment.Center)]
    [InlineData(TabAlignment.Right)]
    [InlineData(TabAlignment.Decimal)]
    [InlineData(TabAlignment.Bar)]
    public void EveryAlignment_SurvivesTheRoundTrip(TabAlignment alignment)
    {
        ParagraphFormat parsed = RoundTrip(Tabs(new TabStop(Length.FromTwips(720), alignment)));

        Assert.Equal(alignment, Assert.Single(parsed.Tabs).Alignment);
    }

    [Theory]
    [InlineData(TabLeader.None)]
    [InlineData(TabLeader.Dot)]
    [InlineData(TabLeader.Hyphen)]
    [InlineData(TabLeader.Underscore)]
    [InlineData(TabLeader.Heavy)]
    [InlineData(TabLeader.MiddleDot)]
    public void EveryLeader_SurvivesTheRoundTrip(TabLeader leader)
    {
        ParagraphFormat parsed = RoundTrip(Tabs(new TabStop(Length.FromTwips(2880), TabAlignment.Right, leader)));

        Assert.Equal(leader, Assert.Single(parsed.Tabs).Leader);
    }

    [Fact]
    public void SeveralStops_ComeBackInOrder()
    {
        ParagraphFormat parsed = RoundTrip(Tabs(
            new TabStop(Length.FromTwips(2880), TabAlignment.Right, TabLeader.Dot),
            new TabStop(Length.FromTwips(720)),
            new TabStop(Length.FromTwips(1440), TabAlignment.Center)));

        Assert.Equal([720, 1440, 2880], parsed.Tabs.Select(static tab => tab.Position.Twips));
        Assert.Equal(
            [TabAlignment.Left, TabAlignment.Center, TabAlignment.Right],
            parsed.Tabs.Select(static tab => tab.Alignment));
    }

    [Fact]
    public void AClearedStop_StaysCleared()
    {
        // A cleared stop is not a stop at all: it is a refusal to inherit one, and the format
        // keeps it in a separate list.
        ParagraphFormat parsed = RoundTrip(Tabs(
            new TabStop(Length.FromTwips(1000), TabAlignment.Clear),
            new TabStop(Length.FromTwips(2000), TabAlignment.Center)));

        Assert.Equal(TabAlignment.Clear, parsed.Tabs.First(static tab => tab.Position.Twips == 1000).Alignment);
        Assert.Equal(TabAlignment.Center, parsed.Tabs.First(static tab => tab.Position.Twips == 2000).Alignment);
    }

    [Fact]
    public void ANegativePosition_SurvivesTheRoundTrip()
    {
        ParagraphFormat parsed = RoundTrip(Tabs(new TabStop(Length.FromTwips(-360))));

        Assert.Equal(-360, Assert.Single(parsed.Tabs).Position.Twips);
    }

    [Fact]
    public void MoreStopsThanTheFormatHolds_AreCappedRatherThanCorrupting()
    {
        var many = new TabStop[100];
        for (int i = 0; i < many.Length; i++)
            many[i] = new TabStop(Length.FromTwips(100 * (i + 1)));

        ParagraphFormat parsed = RoundTrip(Tabs(many));

        Assert.InRange(parsed.Tabs.Count, 1, 64);
        Assert.Equal(100, parsed.Tabs[0].Position.Twips);
    }

    [Fact]
    public void TabStops_SurviveAWholeDocumentRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("first\tsecond")
        {
            Format = Tabs(new TabStop(Length.FromTwips(2160), TabAlignment.Right, TabLeader.Dot)),
        });

        WordDocument reopened = DocReader.Load(DocWriter.Save(document));
        Paragraph paragraph = reopened.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();

        TabStop tab = Assert.Single(paragraph.Format.Tabs);
        Assert.Equal(2160, tab.Position.Twips);
        Assert.Equal(TabAlignment.Right, tab.Alignment);
        Assert.Equal(TabLeader.Dot, tab.Leader);
    }

    private static ParagraphFormat Tabs(params TabStop[] stops) =>
        ParagraphFormat.Default with { Tabs = new EquatableArray<TabStop>(stops) };

    private static ParagraphFormat RoundTrip(ParagraphFormat format) =>
        SprmTranslator.ApplyParagraph(ParagraphFormat.Default, Writing.SprmBuilder.BuildParagraph(format), out _);
}
