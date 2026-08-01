using Inkwright.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Text flowing round floating pictures: beside them in the room they leave, past their bottom
/// when they leave none, and not at all when the picture says the text may ignore it.
/// </summary>
/// <remarks>
/// The stage is a default A4 page: content from 72 to 523.3 points, and a three-by-two
/// centimetre picture, which is 85 by 56.7 points. The pictures anchor to the top of the first
/// paragraph, so the wrapped band starts at the top of the page and its edges can be computed
/// by hand.
/// </remarks>
public sealed class WrapTests
{
    private const string Prose =
        "The quick brown fox jumps over the lazy dog while the cooper mends the barrel down by the river bank. ";

    /// <summary>The default clearance an anchor asks for at its sides: an eighth of an inch.</summary>
    private const double Clearance = 9;

    private static readonly double PageHeight = Length.FromMillimeters(297).Points;

    private static readonly double PictureWidth = Length.FromCentimeters(3).Points;

    private static readonly double PictureHeight = Length.FromCentimeters(2).Points;

    private static Picture Float(Paragraph paragraph, PictureAnchor anchor, double? width = null, double? height = null)
    {
        Picture picture = paragraph.AppendPicture(
            ImageData.FromBytes(Pixels.Png(60, 40)),
            width is { } w ? Length.FromPoints(w) : Length.FromCentimeters(3),
            height is { } h ? Length.FromPoints(h) : Length.FromCentimeters(2));

        picture.IsInline = false;
        picture.Anchor = anchor;
        return picture;
    }

    /// <summary>A diamond: the widest in the middle, a point at the top and at the bottom.</summary>
    private static PictureAnchor Tight(TextWrapping wrapping) => new()
    {
        Wrapping = wrapping,
        Polygon =
        [
            new PolygonPoint(10800, 0),
            new PolygonPoint(21600, 10800),
            new PolygonPoint(10800, 21600),
            new PolygonPoint(0, 10800),
        ],
    };

    /// <summary>The leftmost point text reaches between two heights of the page, measured down from its top.</summary>
    private static double LeftmostBetween(Rendered rendered, double from, double to)
    {
        return rendered.Letters()
            .Where(letter =>
            {
                double y = PageHeight - letter.Origin.Y;
                return y >= from && y <= to && letter.Text != " ";
            })
            .Min(letter => letter.Origin.X);
    }

    private static WordDocument WithFloat(PictureAnchor anchor, string? text = null)
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(
            text ?? string.Concat(Enumerable.Repeat(Prose, 12)).TrimEnd());
        Float(paragraph, anchor);
        return document;
    }

    /// <summary>
    /// Splits a page's letters at a height: the ones beside the picture's band, and the rest.
    /// </summary>
    private static (List<PdfLetter> Beside, List<PdfLetter> Past) SplitAt(Rendered rendered, double bandBottom)
    {
        double threshold = PageHeight - bandBottom;
        List<PdfLetter> beside = [];
        List<PdfLetter> past = [];

        foreach (PdfLetter letter in rendered.Letters())
            (letter.Origin.Y > threshold ? beside : past).Add(letter);

        return (beside, past);
    }

    [Fact]
    public void TextKeepsClearOfAPictureAtTheLeftMargin()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor()));

        (List<PdfLetter> beside, List<PdfLetter> past) = SplitAt(rendered, 72 + PictureHeight);
        double bandRight = 72 + PictureWidth + Clearance;

        Assert.NotEmpty(beside);
        Assert.NotEmpty(past);
        Assert.All(beside, letter => Assert.True(
            letter.Origin.X >= bandRight - 0.5,
            $"a letter at {letter.Origin.X:0.0} sits inside the picture's band"));

        // Below the picture the text comes back to the margin.
        Assert.Contains(past, letter => Math.Abs(letter.Origin.X - 72) < 0.5);
    }

    [Fact]
    public void TextStopsShortOfAPictureAtTheRightMargin()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Margin,
            HorizontalAlignment = AnchorAlignment.End,
        }));

        (List<PdfLetter> beside, List<PdfLetter> past) = SplitAt(rendered, 72 + PictureHeight);
        double bandLeft = Length.FromMillimeters(210).Points - 72 - PictureWidth - Clearance;

        // A trailing space is allowed to hang past the edge, exactly as it hangs past a margin.
        Assert.NotEmpty(beside);
        Assert.All(beside.Where(letter => letter.Text != " "), letter => Assert.True(
            letter.Origin.X + letter.Width <= bandLeft + 0.5,
            $"a letter ending at {letter.Origin.X + letter.Width:0.0} reaches into the picture's band"));

        // Below the picture the lines run past where the band ended.
        Assert.Contains(past, letter => letter.Origin.X + letter.Width > bandLeft + 1);
    }

    [Fact]
    public void TopAndBottomWrappingLeavesTheWholeBandEmpty()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor
        {
            OffsetY = Length.FromPoints(40),
            Wrapping = TextWrapping.TopAndBottom,
        }));

        // The picture spans forty to about ninety-seven points down the page; the text above it
        // and the text below it are separated by at least the picture's height.
        IReadOnlyList<double> baselines = rendered.Baselines();
        double widest = 0;
        for (int i = 1; i < baselines.Count; i++)
            widest = Math.Max(widest, baselines[i - 1] - baselines[i]);

        Assert.True(widest >= PictureHeight, $"the widest gap between baselines is only {widest:0.0}");
    }

    [Fact]
    public void APictureThatWantsNoWrappingGetsNone()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor { Wrapping = TextWrapping.None }));

        (List<PdfLetter> beside, _) = SplitAt(rendered, 72 + PictureHeight);

        Assert.Contains(beside, letter => letter.Origin.X < 100);
    }

    [Fact]
    public void TextTakesTheSideThePictureAllows()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor
        {
            HorizontalAlignment = AnchorAlignment.Center,
            Sides = WrapSides.Left,
        }));

        double contentWidth = Length.FromMillimeters(210).Points - 144;
        double pictureLeft = 72 + ((contentWidth - PictureWidth) / 2);
        (List<PdfLetter> beside, _) = SplitAt(rendered, 72 + PictureHeight);

        // The right side is wider, but the picture said left, so left it is.
        Assert.NotEmpty(beside);
        Assert.All(beside.Where(letter => letter.Text != " "), letter => Assert.True(
            letter.Origin.X + letter.Width <= pictureLeft - Clearance + 0.5,
            $"a letter ending at {letter.Origin.X + letter.Width:0.0} crossed to the forbidden side"));
    }

    [Fact]
    public void AParagraphAfterTheAnchorWrapsRoundTheSamePicture()
    {
        WordDocument document = WordDocument.Create();
        Paragraph anchor = document.Sections[0].AddParagraph("Anchor.");
        Float(anchor, new PictureAnchor());
        document.Sections[0].AddParagraph(string.Concat(Enumerable.Repeat(Prose, 10)).TrimEnd());

        using Rendered rendered = Rendered.Of(document);

        (List<PdfLetter> beside, List<PdfLetter> past) = SplitAt(rendered, 72 + PictureHeight);
        double bandRight = 72 + PictureWidth + Clearance;

        Assert.NotEmpty(beside);
        Assert.All(beside, letter => Assert.True(letter.Origin.X >= bandRight - 0.5));
        Assert.Contains(past, letter => Math.Abs(letter.Origin.X - 72) < 0.5);
    }

    [Fact]
    public void MeasuringRoundAPictureDoesNotCountTheListOn()
    {
        WordDocument document = WordDocument.Create();
        int list = document.Numbering.AddNumberedList();

        foreach (string text in new[] { "First", "Second", "Third" })
        {
            Paragraph paragraph = document.Sections[0].AddParagraph(text);
            paragraph.Format = paragraph.Format with { NumberingId = list, NumberingLevel = 0 };
            if (text == "First")
                Float(paragraph, new PictureAnchor());
        }

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = rendered.Lines();

        Assert.StartsWith("1.First", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("2.Second", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("3.Third", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void MeasuringRoundAPictureRegistersItsFootnoteOnce()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("A claim beside a picture.");
        Float(paragraph, new PictureAnchor());
        document.AddFootnote(paragraph, "The evidence.");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<string> lines = rendered.Lines();

        Assert.Equal("1 The evidence.", lines[^1]);
        Assert.Single(lines, line => line.Contains("The evidence.", StringComparison.Ordinal));
    }

    [Fact]
    public void TightWrappingWithoutAPolygonKeepsTheRectangleAndSaysSo()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor { Wrapping = TextWrapping.Tight }));

        (List<PdfLetter> beside, _) = SplitAt(rendered, 72 + PictureHeight);
        double bandRight = 72 + PictureWidth + Clearance;

        Assert.NotEmpty(beside);
        Assert.All(beside, letter => Assert.True(letter.Origin.X >= bandRight - 0.5));
        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.LayoutApproximated && warning.Subject == "wrap-tight");
    }

    [Fact]
    public void TightWrappingFollowsThePolygonRatherThanTheBox()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(
            string.Concat(Enumerable.Repeat(Prose, 14)).TrimEnd());
        Float(paragraph, Tight(TextWrapping.Tight), width: 100, height: 100);

        using Rendered rendered = Rendered.Of(document);

        // A diamond is narrow at its top and widest across its middle, so the first line beside
        // it starts well to the left of the lines that pass its waist.
        double atTheTip = LeftmostBetween(rendered, 72, 92);
        double atTheWaist = LeftmostBetween(rendered, 112, 132);

        Assert.True(
            atTheTip < atTheWaist - 20,
            $"the tip line starts at {atTheTip:0.0} and the waist line at {atTheWaist:0.0}; tight wrapping did not step");
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "wrap-tight");
    }

    [Fact]
    public void ThroughWrappingStaysOutsideTheInteriorAndSaysSo()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(
            string.Concat(Enumerable.Repeat(Prose, 14)).TrimEnd());
        Float(paragraph, Tight(TextWrapping.Through), width: 100, height: 100);

        using Rendered rendered = Rendered.Of(document);

        double atTheTip = LeftmostBetween(rendered, 72, 92);
        double atTheWaist = LeftmostBetween(rendered, 112, 132);

        Assert.True(atTheTip < atTheWaist - 20, "through wrapping did not follow the polygon");
        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.LayoutApproximated && warning.Subject == "wrap-through");
    }

    [Fact]
    public void TextRunsDownBothSidesOfAFloatThatAllowsIt()
    {
        // Sixty points in from the column edge: a narrow stretch on the left, a wide one on the
        // right, and the default wrapping allows text on both.
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor
        {
            OffsetX = Length.FromPoints(60),
        }));

        double threshold = PageHeight - (72 + PictureHeight);
        double bandLeft = 72 + 60 - Clearance;
        double bandRight = 72 + 60 + PictureWidth + Clearance;

        List<PdfLetter> beside = [.. rendered.Letters().Where(letter => letter.Origin.Y > threshold)];
        List<PdfLetter> left = [.. beside.Where(letter => letter.Origin.X + letter.Width <= bandLeft + 0.5)];
        List<PdfLetter> right = [.. beside.Where(letter => letter.Origin.X >= bandRight - 0.5)];

        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
        Assert.Equal(beside.Count, left.Count + right.Count);

        // The two sides are one line: they share their first baseline.
        Assert.Equal(
            left.Max(letter => letter.Origin.Y),
            right.Max(letter => letter.Origin.Y),
            1);
    }

    [Fact]
    public void AFloatThatAsksForTheLargestSideGetsOnlyThatSide()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor
        {
            OffsetX = Length.FromPoints(60),
            Sides = WrapSides.Largest,
        }));

        double threshold = PageHeight - (72 + PictureHeight);
        double bandLeft = 72 + 60 - Clearance;

        // The narrow left stretch stays empty; everything beside the picture sits to its right.
        Assert.DoesNotContain(
            rendered.Letters(),
            letter => letter.Origin.Y > threshold && letter.Origin.X + letter.Width <= bandLeft + 0.5);
    }

    [Fact]
    public void TheAnchorsOwnClearanceIsHonoured()
    {
        using Rendered rendered = Rendered.Of(WithFloat(new PictureAnchor
        {
            DistanceRight = Length.FromPoints(30),
        }));

        (List<PdfLetter> beside, _) = SplitAt(rendered, 72 + PictureHeight);

        Assert.NotEmpty(beside);
        Assert.All(beside, letter => Assert.True(letter.Origin.X >= 72 + PictureWidth + 30 - 0.5));
    }
}
