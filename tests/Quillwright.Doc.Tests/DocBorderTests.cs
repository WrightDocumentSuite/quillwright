using Quillwright.Doc.Writing;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Borders and backgrounds are the same two small structures wherever they appear. The older
/// border form can only name the sixteen colours of the palette, so a border keeps its shape
/// exactly and its colour approximately, while a background keeps both.
/// </summary>
public class DocBorderTests
{
    [Fact]
    public void AParagraphBorderBox_SurvivesTheRoundTrip()
    {
        var borders = new BorderSet
        {
            Top = BorderLine.Single(Length.FromEighthPoints(12), WordColor.Auto),
            Bottom = BorderLine.Single(Length.FromEighthPoints(6), WordColor.Auto),
        };

        BorderSet parsed = Assert.IsType<BorderSet>(RoundTrip(ParagraphFormat.Default with { Borders = borders }).Borders);

        Assert.Equal(12, parsed.Top!.Width.EighthPoints);
        Assert.Equal(6, parsed.Bottom!.Width.EighthPoints);
        Assert.Null(parsed.Left);
        Assert.Null(parsed.Right);
    }

    [Theory]
    [InlineData(BorderStyle.Single)]
    [InlineData(BorderStyle.Double)]
    [InlineData(BorderStyle.Dotted)]
    [InlineData(BorderStyle.Dashed)]
    [InlineData(BorderStyle.DotDash)]
    [InlineData(BorderStyle.Triple)]
    [InlineData(BorderStyle.Wave)]
    public void EveryBorderStyle_SurvivesTheRoundTrip(BorderStyle style)
    {
        var borders = new BorderSet { Top = new BorderLine { Style = style, Width = Length.FromEighthPoints(8) } };

        ParagraphFormat parsed = RoundTrip(ParagraphFormat.Default with { Borders = borders });

        Assert.Equal(style, parsed.Borders!.Top!.Style);
    }

    [Fact]
    public void ABorderColourFromThePalette_SurvivesTheRoundTrip()
    {
        var borders = new BorderSet
        {
            Left = BorderLine.Single(Length.FromEighthPoints(4), WordColor.FromRgb(0xFF0000)),
        };

        ParagraphFormat parsed = RoundTrip(ParagraphFormat.Default with { Borders = borders });

        Assert.Equal(WordColor.FromRgb(0xFF0000), parsed.Borders!.Left!.Color);
    }

    [Fact]
    public void ParagraphShading_KeepsItsFillAndPattern()
    {
        var shading = new Shading
        {
            Pattern = ShadingPattern.Clear,
            Fill = WordColor.FromRgb(0xFFFF00),
            Color = WordColor.FromRgb(0x0000FF),
        };

        Shading parsed = Assert.IsType<Shading>(RoundTrip(ParagraphFormat.Default with { Shading = shading }).Shading);

        Assert.Equal(WordColor.FromRgb(0xFFFF00), parsed.Fill);
        Assert.Equal(WordColor.FromRgb(0x0000FF), parsed.Color);
        Assert.Equal(ShadingPattern.Clear, parsed.Pattern);
    }

    [Fact]
    public void AShadingPattern_SurvivesTheRoundTrip()
    {
        var shading = new Shading { Pattern = ShadingPattern.DiagonalStripe, Fill = WordColor.FromRgb(0x00FF00) };

        Assert.Equal(ShadingPattern.DiagonalStripe, RoundTrip(ParagraphFormat.Default with { Shading = shading }).Shading!.Pattern);
    }

    [Fact]
    public void AParagraphWithNoBorders_WritesNone()
    {
        ParagraphFormat parsed = RoundTrip(ParagraphFormat.Default with { Alignment = ParagraphAlignment.Center });

        Assert.Null(parsed.Borders);
        Assert.Null(parsed.Shading);
    }

    [Fact]
    public void BordersAndShading_SurviveAWholeDocumentRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("boxed")
        {
            Format = ParagraphFormat.Default with
            {
                Borders = new BorderSet { Top = BorderLine.Single(Length.FromEighthPoints(8), WordColor.Auto) },
                Shading = Shading.Solid(WordColor.FromRgb(0xC0C0C0)),
            },
        });

        WordDocument reopened = DocReader.Load(DocWriter.Save(document));
        Paragraph paragraph = reopened.Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();

        Assert.Equal(8, paragraph.Format.Borders!.Top!.Width.EighthPoints);
        Assert.Equal(WordColor.FromRgb(0xC0C0C0), paragraph.Format.Shading!.Fill);
    }

    private static ParagraphFormat RoundTrip(ParagraphFormat format) =>
        SprmTranslator.ApplyParagraph(ParagraphFormat.Default, SprmBuilder.BuildParagraph(format), out _);
}
