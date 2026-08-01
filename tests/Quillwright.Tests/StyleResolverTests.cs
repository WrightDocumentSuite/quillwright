using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

public class StyleResolverTests
{
    [Fact]
    public void Resolver_FollowsTheBasedOnChain()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("Base", StyleKind.Paragraph)
        {
            RunFormat = RunFormat.Default with { FontAscii = "Georgia", Size = Length.FromPoints(11) },
        });

        document.Styles.Add(new Style("Derived", StyleKind.Paragraph)
        {
            BasedOn = "Base",
            RunFormat = RunFormat.Default with { Size = Length.FromPoints(14) },
        });

        Paragraph paragraph = document.Sections[0].AddParagraph("text");
        paragraph.Format = paragraph.Format with { StyleId = "Derived" };

        RunFormat resolved = document.Resolver.ResolveRunFormat(paragraph.Runs[0]);

        Assert.Equal("Georgia", resolved.FontAscii);
        Assert.Equal(Length.FromPoints(14), resolved.Size);
    }

    /// <summary>
    /// Styles combine a toggle by exclusive-or, so two bold layers leave text unbold; direct
    /// formatting is the exception and means exactly what it says.
    /// </summary>
    [Fact]
    public void Resolver_ExclusiveOrsToggleStylesButNotDirectFormatting()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("BoldBody", StyleKind.Paragraph) { RunFormat = RunFormat.Default with { Bold = true } });
        document.Styles.Add(new Style("BoldRun", StyleKind.Character) { RunFormat = RunFormat.Default with { Bold = true } });

        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.Format = paragraph.Format with { StyleId = "BoldBody" };
        paragraph.AppendText("text", RunFormat.Default with { StyleId = "BoldRun" });

        Assert.False(document.Resolver.ResolveRunFormat(paragraph.Runs[0]).Bold);

        paragraph.Runs[0].SetFormat(RunFormat.Default with { StyleId = "BoldRun", Bold = true });
        Assert.True(document.Resolver.ResolveRunFormat(paragraph.Runs[0]).Bold);
    }

    [Fact]
    public void Resolver_LayersCharacterStyleOverParagraphStyle()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("Body", StyleKind.Paragraph)
        {
            RunFormat = RunFormat.Default with { Color = WordColor.FromRgb(0x333333), Italic = true },
        });

        document.Styles.Add(new Style("Accent", StyleKind.Character)
        {
            RunFormat = RunFormat.Default with { Color = WordColor.FromRgb(0xCC0000) },
        });

        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.Format = paragraph.Format with { StyleId = "Body" };
        paragraph.AppendText("accented", RunFormat.Default with { StyleId = "Accent" });

        RunFormat resolved = document.Resolver.ResolveRunFormat(paragraph.Runs[0]);

        Assert.Equal(WordColor.FromRgb(0xCC0000), resolved.Color);
        Assert.True(resolved.Italic);
    }

    [Fact]
    public void Resolver_AppliesTheNumberingLevelIndent()
    {
        WordDocument document = WordDocument.Create();
        int listId = document.Numbering.AddBulletList();
        Paragraph paragraph = document.Sections[0].AddParagraph("item");
        paragraph.Format = paragraph.Format with { NumberingId = listId, NumberingLevel = 1 };

        ParagraphFormat resolved = document.Resolver.ResolveParagraphFormat(paragraph);

        Assert.Equal(Length.FromTwips(1440), resolved.IndentLeft);
        Assert.Equal(Length.FromTwips(360), resolved.IndentHanging);
    }

    [Fact]
    public void Resolver_AppliesWordsCharacterIndentHierarchyRules()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("CharacterIndent", StyleKind.Paragraph)
        {
            ParagraphFormat = ParagraphFormat.Default with { IndentLeftCharacters = 250 },
        });
        document.Styles.Add(new Style("TwipOverride", StyleKind.Paragraph)
        {
            BasedOn = "CharacterIndent",
            ParagraphFormat = ParagraphFormat.Default with { IndentLeft = Length.FromTwips(720) },
        });
        Paragraph paragraph = document.Sections[0].AddParagraph("text");
        paragraph.Format = paragraph.Format with { StyleId = "TwipOverride" };

        ParagraphFormat inherited = document.Resolver.ResolveParagraphFormat(paragraph);
        Assert.Equal(250, inherited.IndentLeftCharacters);
        Assert.Equal(Length.FromTwips(720), inherited.IndentLeft);

        // [MS-OI29500] 2.1.44: a zero character-unit indent clears the related value from an
        // earlier hierarchy level, allowing the twip value to take effect.
        paragraph.Format = paragraph.Format with { IndentLeftCharacters = 0 };
        ParagraphFormat cleared = document.Resolver.ResolveParagraphFormat(paragraph);
        Assert.Null(cleared.IndentLeftCharacters);
        Assert.Equal(Length.FromTwips(720), cleared.IndentLeft);
    }

    [Fact]
    public void Resolver_AppliesTheHeaderRowOfATableStyle()
    {
        WordDocument document = WordDocument.Create();
        var style = new Style("Banded", StyleKind.Table)
        {
            RunFormat = RunFormat.Default with { FontAscii = "Verdana" },
        };

        style.ConditionalFormats.Add(new ConditionalTableStyle
        {
            Region = TableStyleRegion.FirstRow,
            RunFormat = RunFormat.Default with { Bold = true },
        });

        document.Styles.Add(style);

        Table table = document.Sections[0].AddTable(2, 2);
        table.Format = table.Format with { StyleId = "Banded", StyleOptions = TableStyleOptions.FirstRow };
        table[0, 0].SetText("head");
        table[1, 0].SetText("body");

        Paragraph header = table[0, 0].Blocks.Paragraphs.First();
        Paragraph body = table[1, 0].Blocks.Paragraphs.First();

        Assert.True(document.Resolver.ResolveRunFormat(header.Runs[0]).Bold);
        Assert.Null(document.Resolver.ResolveRunFormat(body.Runs[0]).Bold);
        Assert.Equal("Verdana", document.Resolver.ResolveRunFormat(body.Runs[0]).FontAscii);
    }

    [Fact]
    public void Resolver_CutsAStyleCycleInsteadOfHanging()
    {
        WordDocument document = WordDocument.Create();
        document.Styles.Add(new Style("A", StyleKind.Paragraph) { BasedOn = "B" });
        document.Styles.Add(new Style("B", StyleKind.Paragraph) { BasedOn = "A" });

        Paragraph paragraph = document.Sections[0].AddParagraph("text");
        paragraph.Format = paragraph.Format with { StyleId = "A" };

        Assert.NotNull(document.Resolver.ResolveParagraphFormat(paragraph));
    }

    /// <summary>
    /// §17.7.2 applies the numbering layer before the paragraph style, but the <c>w:numPr</c>
    /// naming the list is just as often in that style — which is how a numbered heading is
    /// built. The level's indents and the formatting of its marker have to arrive either way.
    /// </summary>
    [Fact]
    public void Resolver_AppliesTheNumberingLayerWhenTheStyleNamesTheList()
    {
        WordDocument document = WordDocument.Create();
        int listId = Numbered(document, Length.FromTwips(567), "Symbol");
        document.Styles.Add(new Style("NumberedHeading", StyleKind.Paragraph)
        {
            ParagraphFormat = ParagraphFormat.Default with { NumberingId = listId, NumberingLevel = 0 },
        });

        Paragraph paragraph = document.Sections[0].AddParagraph("clause");
        paragraph.Format = paragraph.Format with { StyleId = "NumberedHeading" };

        Assert.Equal(Length.FromTwips(567), document.Resolver.ResolveParagraphFormat(paragraph).IndentLeft);
        Assert.Equal("Symbol", document.Resolver.ResolveNumberingSymbolFormat(paragraph)?.FontAscii);
    }

    /// <summary>
    /// §17.9.21: an abstract numbering carrying <c>w:numStyleLink</c> declares no levels of
    /// its own — it names a numbering style, whose own <c>w:numId</c> leads to the definition
    /// that does. A paragraph pointed at the first one is still in a list.
    /// </summary>
    [Fact]
    public void Resolver_FollowsANumberingStyleLinkToTheRealDefinition()
    {
        WordDocument document = WordDocument.Create();
        int realList = Numbered(document, Length.FromTwips(360), "Courier New");
        document.Styles.Add(new Style("ListStyle", StyleKind.Numbering)
        {
            ParagraphFormat = ParagraphFormat.Default with { NumberingId = realList },
        });

        var deferring = new AbstractNumbering
        {
            Id = document.Numbering.Definitions.Max(d => d.Id) + 1,
            NumberingStyleLink = "ListStyle",
        };

        document.Numbering.Definitions.Add(deferring);
        var instance = new NumberingInstance
        {
            Id = document.Numbering.Instances.Max(i => i.Id) + 1,
            AbstractId = deferring.Id,
        };

        document.Numbering.Instances.Add(instance);

        NumberingLevel? level = document.Numbering.ResolveLevel(instance.Id, 0);

        Assert.NotNull(level);
        Assert.Equal(Length.FromTwips(360), level.ParagraphFormat.IndentLeft);
        Assert.Equal("Courier New", level.RunFormat.FontAscii);
    }

    /// <summary>A link that leads back to itself is abandoned rather than followed.</summary>
    [Fact]
    public void Resolver_CutsANumberingStyleLinkCycleInsteadOfHanging()
    {
        WordDocument document = WordDocument.Create();
        var definition = new AbstractNumbering { Id = 40, NumberingStyleLink = "Loop" };
        document.Numbering.Definitions.Add(definition);
        document.Numbering.Instances.Add(new NumberingInstance { Id = 40, AbstractId = 40 });
        document.Styles.Add(new Style("Loop", StyleKind.Numbering)
        {
            ParagraphFormat = ParagraphFormat.Default with { NumberingId = 40 },
        });

        Assert.Null(document.Numbering.ResolveLevel(40, 0));
    }

    /// <summary>Creates a one-level list whose level states an indent and a marker font.</summary>
    private static int Numbered(WordDocument document, Length indent, string markerFont)
    {
        int id = document.Numbering.AddNumberedList();
        NumberingLevel level = document.Numbering.ResolveDefinition(id)!.Levels[0];
        level.ParagraphFormat = level.ParagraphFormat with { IndentLeft = indent };
        level.RunFormat = level.RunFormat with { FontAscii = markerFont };
        return id;
    }
}
