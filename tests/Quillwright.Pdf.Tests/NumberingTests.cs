using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class NumberingTests
{
    private static Paragraph AddItem(Section section, string text, int listId, int level = 0)
    {
        Paragraph paragraph = section.AddParagraph(text);
        paragraph.Format = paragraph.Format with { NumberingId = listId, NumberingLevel = level };
        return paragraph;
    }

    /// <summary>
    /// The lines of a page, in order. The gap between a marker and its text is a tab jump rather
    /// than a character, so it leaves no trace here; where the marker lands is checked by
    /// <see cref="TheMarkerSitsInTheOutdentAndTheTextAtTheIndent"/>.
    /// </summary>
    private static IReadOnlyList<string> Lines(Rendered rendered, int page = 0) => rendered.Lines(page);

    [Fact]
    public void ANumberedListCountsUp()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();

        foreach (string text in new[] { "First", "Second", "Third" })
            AddItem(document.Sections[0], text, list);

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("1.First", lines[0]);
        Assert.Equal("2.Second", lines[1]);
        Assert.Equal("3.Third", lines[2]);
    }

    [Fact]
    public void TwoListsCountSeparately()
    {
        WordDocument document = WordDocument.Create();
        int first = document.Numbering.AddNumberedList();
        int second = document.Numbering.AddNumberedList();

        AddItem(document.Sections[0], "A", first);
        AddItem(document.Sections[0], "B", second);
        AddItem(document.Sections[0], "C", first);

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("1.A", lines[0]);
        Assert.Equal("1.B", lines[1]);
        Assert.Equal("2.C", lines[2]);
    }

    [Fact]
    public void ADeeperLevelStartsAgainUnderEachItemAbove()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        Section section = document.Sections[0];

        AddItem(section, "One", list);
        AddItem(section, "Under one", list, level: 1);
        AddItem(section, "Also under one", list, level: 1);
        AddItem(section, "Two", list);
        AddItem(section, "Under two", list, level: 1);

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("1.One", lines[0]);
        Assert.Equal("a)Under one", lines[1]);
        Assert.Equal("b)Also under one", lines[2]);
        Assert.Equal("2.Two", lines[3]);
        Assert.Equal("a)Under two", lines[4]);
    }

    [Fact]
    public void AnOutlineListShowsEveryLevelInItsMarker()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddList(ListTemplate.Outline);
        Section section = document.Sections[0];

        AddItem(section, "Scope", list);
        AddItem(section, "Purpose", list, level: 1);
        AddItem(section, "Detail", list, level: 2);

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("1.Scope", lines[0]);
        Assert.Equal("1.1.Purpose", lines[1]);
        Assert.Equal("1.1.1.Detail", lines[2]);
    }

    [Fact]
    public void AStartOverrideDecidesWhereTheListBegins()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        NumberingInstance instance = document.Numbering.Instances.First(candidate => candidate.Id == list);
        instance.Overrides.Add(new NumberingLevelOverride { Level = 0, StartOverride = 7 });

        AddItem(document.Sections[0], "Seven", list);
        AddItem(document.Sections[0], "Eight", list);

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("7.Seven", lines[0]);
        Assert.Equal("8.Eight", lines[1]);
    }

    [Fact]
    public void ALevelThatNeverRestartsCountsThrough()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        AbstractNumbering definition = document.Numbering.Definitions[0];
        definition.Levels[1].RestartAfter = 0;

        Section section = document.Sections[0];
        AddItem(section, "One", list);
        AddItem(section, "Under one", list, level: 1);
        AddItem(section, "Two", list);
        AddItem(section, "Under two", list, level: 1);

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = Lines(rendered);

        Assert.Equal("a)Under one", lines[1]);
        Assert.Equal("b)Under two", lines[3]);
    }

    [Fact]
    public void ABulletUsesTheSymbolFontWithoutInfectingTheText()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddBulletList();
        AddItem(document.Sections[0], "Milk", list);

        using Rendered rendered = Rendered.Of(document);

        // The marker comes from Symbol and the words from the body font, so two fonts appear.
        var fonts = rendered.Letters()
            .Where(letter => !letter.IsWhiteSpace)
            .GroupBy(letter => letter.FontName)
            .ToList();

        Assert.Equal(2, fonts.Count);
        Assert.Contains("Milk", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkerSitsInTheOutdentAndTheTextAtTheIndent()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        AddItem(document.Sections[0], "Item", list);

        using Rendered rendered = Rendered.Of(document);

        double margin = document.Sections[0].Properties.Margins.Left.Points;
        double marker = rendered.Letters().First(letter => letter.Text == "1").Origin.X;
        double text = rendered.Letters().First(letter => letter.Text == "I").Origin.X;

        // The template indents by 720 twips and hangs by 360, so the marker is half an inch in
        // from the margin less a quarter, and the text a full half inch in.
        Assert.Equal(margin + Length.FromTwips(360).Points, marker, 0.05);
        Assert.Equal(margin + Length.FromTwips(720).Points, text, 0.05);
    }

    [Fact]
    public void ASpaceSuffixPutsOneSpaceAfterTheMarker()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        document.Numbering.Definitions[0].Levels[0].Suffix = ListLevelSuffix.Space;
        AddItem(document.Sections[0], "Item", list);

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal("1. Item", Lines(rendered)[0]);
    }

    [Fact]
    public void AParagraphWithNoListGetsNoMarker()
    {
        WordDocument document = WordDocument.Create();
        document.Numbering.AddNumberedList();
        document.Sections[0].AddParagraph("Plain");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal("Plain", Lines(rendered)[0]);
    }

    [Fact]
    public void ALegalOutlinePrintsEveryLevelInDigits()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();
        AbstractNumbering definition = document.Numbering.Definitions[0];
        definition.Levels[1].IsLegal = true;
        definition.Levels[1].Text = "%1.%2";

        Section section = document.Sections[0];
        AddItem(section, "One", list);
        AddItem(section, "Under", list, level: 1);

        using Rendered rendered = Rendered.Of(document);

        // Level two counts in letters, but a legal level prints the whole reference in digits.
        Assert.Equal("1.1Under", Lines(rendered)[1]);
    }
}
