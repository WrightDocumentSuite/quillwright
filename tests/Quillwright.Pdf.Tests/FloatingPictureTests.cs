using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Quillwright.Model;
using Quillwright.Primitives;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Where a floating picture lands once the document has been through a file.
/// </summary>
/// <remarks>
/// Building the document in memory is not enough to prove anything here: the anchor is set by the
/// caller and would be believed whether or not the reader could recover it. These tests save the
/// document as <c>.docx</c> and open it again first, which is the path a real document takes and
/// the one where a position gets lost.
/// </remarks>
public sealed partial class FloatingPictureTests
{
    /// <summary>The transformation an image is painted under: width, height and where it sits.</summary>
    [GeneratedRegex(@"^(?<w>[-\d.]+) 0 0 (?<h>[-\d.]+) (?<x>[-\d.]+) (?<y>[-\d.]+) cm$", RegexOptions.Multiline)]
    private static partial Regex Placement();

    /// <summary>The width and height of a default A4 page, which the expectations are measured off.</summary>
    private static readonly Length PageWidth = Length.FromMillimeters(210);

    private static readonly Length PageHeight = Length.FromMillimeters(297);

    /// <summary>The default margin Word leaves on every side.</summary>
    private const double Margin = 72;

    private static async Task<(double X, double Y)> DrawnAtAsync(PictureAnchor? anchor, string text = "Text beside a picture.")
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(text);
        Picture picture = paragraph.AppendPicture(
            ImageData.FromBytes(Pixels.Png(60, 40)),
            Length.FromCentimeters(3),
            Length.FromCentimeters(2));

        if (anchor is not null)
        {
            picture.IsInline = false;
            picture.Anchor = anchor;
        }

        using var saved = new MemoryStream();
        await document.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        saved.Position = 0;

        WordDocument reopened = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);

        using Rendered rendered = Rendered.Of(reopened);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Match match = Placement().Match(content);
        Assert.True(match.Success, "no image was painted on the page");

        return (
            double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task APictureAnchoredToThePage_IsDrawnWhereTheAnchorSays()
    {
        (double x, double y) = await DrawnAtAsync(new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Page,
            VerticalFrom = AnchorOrigin.Page,
            OffsetX = Length.FromCentimeters(8),
            OffsetY = Length.FromCentimeters(5),
            Wrapping = TextWrapping.None,
        });

        // The picture is two centimetres tall, so its bottom edge sits five plus two centimetres
        // down from the top of the page — and PDF measures from the bottom.
        Assert.Equal(Length.FromCentimeters(8).Points, x, 0.5);
        Assert.Equal(PageHeight.Points - Length.FromCentimeters(7).Points, y, 0.5);
    }

    [Fact]
    public async Task MovingTheAnchorMovesThePicture()
    {
        static PictureAnchor At(double centimetres) => new()
        {
            HorizontalFrom = AnchorOrigin.Page,
            VerticalFrom = AnchorOrigin.Page,
            OffsetX = Length.FromCentimeters(centimetres),
            OffsetY = Length.FromCentimeters(4),
            Wrapping = TextWrapping.None,
        };

        (double near, _) = await DrawnAtAsync(At(2));
        (double far, _) = await DrawnAtAsync(At(9));

        Assert.Equal(Length.FromCentimeters(7).Points, far - near, 0.5);
    }

    [Fact]
    public async Task APictureAlignedToTheRightMargin_EndsAtIt()
    {
        (double x, _) = await DrawnAtAsync(new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Margin,
            HorizontalAlignment = AnchorAlignment.End,
            VerticalFrom = AnchorOrigin.Page,
            OffsetY = Length.FromCentimeters(4),
            Wrapping = TextWrapping.None,
        });

        // The right margin of a default A4 page, less the width of the picture.
        double right = PageWidth.Points - Margin;
        Assert.Equal(right - Length.FromCentimeters(3).Points, x, 0.5);
    }

    [Fact]
    public async Task APictureBehindTheText_IsPaintedBeforeIt()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Over the top.");
        Picture picture = paragraph.AppendPicture(
            ImageData.FromBytes(Pixels.Png(60, 40)),
            Length.FromCentimeters(6),
            Length.FromCentimeters(4));

        picture.IsInline = false;
        picture.Anchor = new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Page,
            VerticalFrom = AnchorOrigin.Page,
            Wrapping = TextWrapping.None,
            BehindText = true,
        };

        using var saved = new MemoryStream();
        await document.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        saved.Position = 0;

        WordDocument reopened = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);

        using Rendered rendered = Rendered.Of(reopened);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.True(
            content.IndexOf(" Do\n", StringComparison.Ordinal) < content.IndexOf("BT\n", StringComparison.Ordinal),
            "the picture was painted after the text it is supposed to sit behind");
    }

    /// <summary>A picture in the text flow is placed by the flow and by nothing else.</summary>
    [Fact]
    public async Task AnInlinePictureIsNotMovedByAnyOfThis()
    {
        (double alone, _) = await DrawnAtAsync(anchor: null, text: string.Empty);
        (double after, _) = await DrawnAtAsync(anchor: null);

        Assert.Equal(Margin, alone, 0.5);
        Assert.True(after > alone, "a picture after some words was not pushed along by them");
    }
}
