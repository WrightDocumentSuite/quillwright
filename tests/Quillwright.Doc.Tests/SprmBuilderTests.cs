using Quillwright.Doc.Writing;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Every property the writer encodes has to come back out of the reader unchanged. Building
/// a format, parsing it straight back and comparing the records is the cheapest way to catch
/// an opcode that is one off from its neighbour — and in this format they routinely are.
/// </summary>
public class SprmBuilderTests
{
    private static readonly string[] Fonts = ["Times New Roman", "Calibri", "Consolas"];

    [Fact]
    public void CharacterToggles_SurviveTheRoundTrip()
    {
        var format = RunFormat.Default with
        {
            Bold = true,
            Italic = true,
            Strike = true,
            DoubleStrike = true,
            Outline = true,
            Shadow = true,
            Emboss = true,
            Imprint = true,
            SmallCaps = true,
            Caps = true,
            Hidden = true,
            WebHidden = true,
            NoProof = true,
        };

        RunFormat parsed = RoundTrip(format);

        Assert.True(parsed.Bold);
        Assert.True(parsed.Italic);
        Assert.True(parsed.Strike);
        Assert.True(parsed.DoubleStrike);
        Assert.True(parsed.Outline);
        Assert.True(parsed.Shadow);
        Assert.True(parsed.Emboss);
        Assert.True(parsed.Imprint);
        Assert.True(parsed.SmallCaps);
        Assert.True(parsed.Caps);
        Assert.True(parsed.Hidden);
        Assert.True(parsed.WebHidden);
        Assert.True(parsed.NoProof);
    }

    [Fact]
    public void TogglesTurnedOff_StayOff()
    {
        RunFormat parsed = RoundTrip(RunFormat.Default with { Bold = false, Italic = false, Caps = false });

        Assert.False(parsed.Bold);
        Assert.False(parsed.Italic);
        Assert.False(parsed.Caps);
    }

    [Fact]
    public void CharacterMeasurements_SurviveTheRoundTrip()
    {
        var format = RunFormat.Default with
        {
            Size = Length.FromPoints(13.5),
            SizeComplexScript = Length.FromPoints(11),
            Position = Length.FromPoints(-3),
            Kerning = Length.FromPoints(8),
            CharacterSpacing = Length.FromTwips(-15),
            Scale = 150,
        };

        RunFormat parsed = RoundTrip(format);

        Assert.Equal(Length.FromPoints(13.5), parsed.Size);
        Assert.Equal(Length.FromPoints(11), parsed.SizeComplexScript);
        Assert.Equal(Length.FromPoints(-3), parsed.Position);
        Assert.Equal(Length.FromPoints(8), parsed.Kerning);
        Assert.Equal(Length.FromTwips(-15), parsed.CharacterSpacing);
        Assert.Equal(150, parsed.Scale);
    }

    [Fact]
    public void ColourUnderlineHighlightAndScript_SurviveTheRoundTrip()
    {
        var format = RunFormat.Default with
        {
            Color = WordColor.FromRgb(0x2F5496),
            Underline = UnderlineStyle.Double,
            Highlight = HighlightColor.Yellow,
            VerticalAlignment = VerticalTextAlignment.Superscript,
        };

        RunFormat parsed = RoundTrip(format);

        Assert.Equal(WordColor.FromRgb(0x2F5496), parsed.Color);
        Assert.Equal(UnderlineStyle.Double, parsed.Underline);
        Assert.Equal(HighlightColor.Yellow, parsed.Highlight);
        Assert.Equal(VerticalTextAlignment.Superscript, parsed.VerticalAlignment);
    }

    [Fact]
    public void Fonts_ResolveThroughTheirIndex()
    {
        var format = RunFormat.Default with
        {
            FontAscii = "Consolas",
            FontEastAsia = "Calibri",
            FontComplexScript = "Times New Roman",
        };

        RunFormat parsed = RoundTrip(format);

        Assert.Equal("Consolas", parsed.FontAscii);
        Assert.Equal("Consolas", parsed.FontHighAnsi);
        Assert.Equal("Calibri", parsed.FontEastAsia);
        Assert.Equal("Times New Roman", parsed.FontComplexScript);
    }

    [Theory]
    [InlineData(ParagraphAlignment.Left)]
    [InlineData(ParagraphAlignment.Center)]
    [InlineData(ParagraphAlignment.Right)]
    [InlineData(ParagraphAlignment.Justify)]
    public void ParagraphAlignment_SurvivesTheRoundTrip(ParagraphAlignment alignment)
    {
        ParagraphFormat parsed = RoundTrip(ParagraphFormat.Default with { Alignment = alignment });
        Assert.Equal(alignment, parsed.Alignment);
    }

    [Fact]
    public void ParagraphGeometry_SurvivesTheRoundTrip()
    {
        var format = ParagraphFormat.Default with
        {
            IndentLeft = Length.FromCentimeters(2),
            IndentRight = Length.FromCentimeters(1),
            IndentHanging = Length.FromTwips(360),
            SpacingBefore = Length.FromPoints(12),
            SpacingAfter = Length.FromPoints(6),
            KeepWithNext = true,
            KeepLinesTogether = true,
            PageBreakBefore = true,
            WidowControl = true,
            ContextualSpacing = true,
            OutlineLevel = 2,
        };

        ParagraphFormat parsed = RoundTrip(format);

        Assert.Equal(Length.FromCentimeters(2), parsed.IndentLeft);
        Assert.Equal(Length.FromCentimeters(1), parsed.IndentRight);
        Assert.Equal(Length.FromTwips(360), parsed.IndentHanging);
        Assert.Null(parsed.IndentFirstLine);
        Assert.Equal(Length.FromPoints(12), parsed.SpacingBefore);
        Assert.Equal(Length.FromPoints(6), parsed.SpacingAfter);
        Assert.True(parsed.KeepWithNext);
        Assert.True(parsed.KeepLinesTogether);
        Assert.True(parsed.PageBreakBefore);
        Assert.True(parsed.WidowControl);
        Assert.True(parsed.ContextualSpacing);
        Assert.Equal(2, parsed.OutlineLevel);
    }

    [Fact]
    public void AFirstLineIndent_IsNotMistakenForAHangingOne()
    {
        ParagraphFormat parsed = RoundTrip(ParagraphFormat.Default with { IndentFirstLine = Length.FromTwips(720) });

        Assert.Equal(Length.FromTwips(720), parsed.IndentFirstLine);
        Assert.Null(parsed.IndentHanging);
    }

    [Theory]
    [InlineData(LineSpacingRule.Auto)]
    [InlineData(LineSpacingRule.Exact)]
    [InlineData(LineSpacingRule.AtLeast)]
    public void LineSpacing_SurvivesTheRoundTripWithItsRule(LineSpacingRule rule)
    {
        var format = ParagraphFormat.Default with { LineSpacing = Length.FromTwips(360), LineSpacingRule = rule };

        ParagraphFormat parsed = RoundTrip(format);

        Assert.Equal(Length.FromTwips(360), parsed.LineSpacing);
        Assert.Equal(rule, parsed.LineSpacingRule);
    }

    [Fact]
    public void TableFlags_SurviveTheRoundTrip()
    {
        byte[] built = SprmBuilder.BuildParagraph(
            ParagraphFormat.Default,
            new DocParagraphFlags(InTable: true, IsRowEnd: true, TableDepth: 2));

        SprmTranslator.ApplyParagraph(ParagraphFormat.Default, built, out DocParagraphFlags flags);

        Assert.True(flags.InTable);
        Assert.True(flags.IsRowEnd);
        Assert.Equal(2, flags.TableDepth);
    }

    [Fact]
    public void SectionProperties_ProduceAWellFormedModifierList()
    {
        var properties = new SectionProperties
        {
            Orientation = PageOrientation.Landscape,
            PageWidth = Length.FromMillimeters(297),
            PageHeight = Length.FromMillimeters(210),
            Start = SectionStart.OddPage,
            DifferentFirstPage = true,
        };

        properties.Margins.Left = Length.FromCentimeters(3);
        properties.Columns.Count = 2;

        Dictionary<ushort, byte[]> written = Parse(SprmBuilder.BuildSection(properties));

        Assert.Equal(2, written[SprmCode.Orientation][0]);
        Assert.Equal(4, written[SprmCode.SectionBreak][0]);
        Assert.Equal(1, written[SprmCode.TitlePage][0]);
        Assert.Equal(properties.PageWidth.Twips, BitConverter.ToUInt16(written[SprmCode.PageWidth]));
        Assert.Equal(properties.PageHeight.Twips, BitConverter.ToUInt16(written[SprmCode.PageHeight]));
        Assert.Equal(Length.FromCentimeters(3).Twips, BitConverter.ToUInt16(written[SprmCode.MarginLeft]));
        Assert.Equal(1, BitConverter.ToUInt16(written[SprmCode.ColumnCount]));
    }

    [Fact]
    public void EveryModifierTheBuilderWrites_CanBeSteppedOverByTheReader()
    {
        // A wrong operand size would leave the reader mid-modifier and desynchronise
        // everything after it, so the whole list has to be consumed exactly.
        byte[] run = SprmBuilder.BuildRun(
            RunFormat.Default with { Bold = true, Size = Length.FromPoints(12), Color = WordColor.Black, FontAscii = "Calibri" },
            FontIndex);

        Assert.Equal(run.Length, Consumed(run));

        byte[] paragraph = SprmBuilder.BuildParagraph(
            ParagraphFormat.Default with { Alignment = ParagraphAlignment.Center, IndentLeft = Length.FromTwips(720) });

        Assert.Equal(paragraph.Length, Consumed(paragraph));
    }

    private static RunFormat RoundTrip(RunFormat format)
    {
        byte[] built = SprmBuilder.BuildRun(format, FontIndex);
        return SprmTranslator.ApplyRun(RunFormat.Default, built, FontTable());
    }

    private static ParagraphFormat RoundTrip(ParagraphFormat format)
    {
        byte[] built = SprmBuilder.BuildParagraph(format);
        return SprmTranslator.ApplyParagraph(ParagraphFormat.Default, built, out _);
    }

    private static int FontIndex(string name) => Array.IndexOf(Fonts, name);

    private static DocFontTable FontTable() => DocFontTable.Read(FontTableBuilder.Build(Fonts), 0, FontTableBuilder.Build(Fonts).Length);

    private static Dictionary<ushort, byte[]> Parse(byte[] properties)
    {
        var result = new Dictionary<ushort, byte[]>();
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
            result[sprm.Opcode] = sprm.Operand.ToArray();
        return result;
    }

    private static int Consumed(byte[] properties)
    {
        var reader = new SprmReader(properties);
        int total = 0;
        while (reader.TryRead(out Sprm sprm))
            total += 2 + sprm.Operand.Length + (sprm.Opcode >> 13 == 6 ? sprm.Opcode == 0xD608 ? 2 : 1 : 0);
        return total;
    }
}
