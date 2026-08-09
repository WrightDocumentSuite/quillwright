using System.Text;
using Inkwright.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Text boxes on the page: the fill, the frame and the words inside, drawn from the geometry the
/// model read off the shape's markup. What the markup does not state is skipped and said.
/// </summary>
/// <remarks>
/// The shapes are built in memory with the geometry a reader would have recovered; that the
/// reader recovers it is proven by the model's own tests. The stage is a default A4 page with
/// inch margins, and a box leaves 7.2 points at its sides and 3.6 above and below, the insets
/// Word gives a text box that states none.
/// </remarks>
public sealed class ShapeTests
{
    private const double SideInset = 7.2;

    private static Shape Box(
        double width,
        double height,
        bool inline = true,
        PictureAnchor? anchor = null,
        WordColor? fill = null,
        BorderLine? outline = null,
        params string[] paragraphs)
    {
        var content = new TextBox();
        foreach (string text in paragraphs)
            content.AddParagraph(text);

        return new Shape(["<wps/>", "</wps>"], content)
        {
            Width = Length.FromPoints(width),
            Height = Length.FromPoints(height),
            IsInline = inline,
            Anchor = anchor,
            Fill = fill,
            Outline = outline,
        };
    }

    [Fact]
    public void TheWordsOfAnInlineBoxArePrinted()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Before ");
        paragraph.AppendObject(Box(200, 60, paragraphs: "Inside the box"));

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Before", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("Inside the box", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void AnInlineBoxTakesRoomOnItsLine()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("AB");
        paragraph.AppendObject(Box(150, 40, paragraphs: "x"));
        paragraph.AppendText("CD");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        PdfLetter a = letters.First(letter => letter.Text == "A");
        PdfLetter c = letters.First(letter => letter.Text == "C");

        Assert.True(c.Origin.X >= a.Origin.X + 150, "the text after the box was not moved past it");
    }

    [Fact]
    public void TheFillAndTheFrameAreDrawn()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(Box(
            120, 50,
            fill: WordColor.FromRgb(0xFF0000),
            outline: BorderLine.Single(Length.FromPoints(1), WordColor.FromRgb(0x0000FF)),
            paragraphs: "Filled"));

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Contains("1 0 0 rg", content, StringComparison.Ordinal);
        Assert.Contains("0 0 1 RG", content, StringComparison.Ordinal);
        Assert.Contains("\nS\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AStraightConnectorIsDrawnAsOneLineRatherThanAFlatBox()
    {
        WordDocument document = WordDocument.Create();
        var line = new Shape(["<wps:wsp/>"], new TextBox())
        {
            Width = Length.FromPoints(200),
            Height = Length.FromPoints(0.05),
            IsLine = true,
            Outline = BorderLine.Single(Length.FromPoints(2.25), WordColor.FromRgb(0x000000)),
        };
        document.Sections[0].AddParagraph().AppendObject(line);

        using Rendered rendered = Rendered.Of(document);
        string content = Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

        Assert.Equal(1, content.Split("\nS\n", StringSplitOptions.None).Length - 1);
        Assert.Contains("2.25 w", content, StringComparison.Ordinal);
        Assert.DoesNotContain(" re", content, StringComparison.Ordinal);
    }

    [Fact]
    public void AFloatingBoxSitsWhereItsAnchorSays()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Body text.");
        paragraph.AppendObject(Box(
            200, 60,
            inline: false,
            anchor: new PictureAnchor
            {
                HorizontalFrom = AnchorOrigin.Page,
                VerticalFrom = AnchorOrigin.Page,
                OffsetX = Length.FromPoints(100),
                OffsetY = Length.FromPoints(150),
                Wrapping = TextWrapping.None,
            },
            paragraphs: "Inside"));

        using Rendered rendered = Rendered.Of(document);

        PdfLetter first = rendered.Letters().First(letter => letter.Text == "I");
        Assert.Equal(100 + SideInset, first.Origin.X, 0.6);
    }

    [Fact]
    public void AGeneratedZeroInsetBoxUsesItsWholeFrame()
    {
        var content = new TextBox();
        content.AddParagraph("Exact");
        var anchor = new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Page,
            VerticalFrom = AnchorOrigin.Page,
            OffsetX = Length.FromPoints(40),
            OffsetY = Length.FromPoints(60),
            Wrapping = TextWrapping.None,
        };
        Shape shape = Shape.CreateTextBox(
            Length.FromPoints(100), Length.FromPoints(20), content, anchor);
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(shape);

        using Rendered rendered = Rendered.Of(document);

        PdfLetter first = rendered.Letters().First(letter => letter.Text == "E");
        Assert.Equal(40, first.Origin.X, 0.6);
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "shape-overflow");
    }

    [Fact]
    public void TextWrapsRoundAFloatingBox()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph(string.Concat(
            Enumerable.Repeat("The cooper mends the barrel down by the river bank while the fox looks on. ", 10)).TrimEnd());
        paragraph.AppendObject(Box(100, 60, inline: false, anchor: new PictureAnchor(), paragraphs: "Aside"));

        using Rendered rendered = Rendered.Of(document);

        // The box spans 72..172 with a 9 point clearance; beside it every glyph is either inside
        // the box or past the clearance, never in between.
        double pageHeight = Length.FromMillimeters(297).Points;
        double threshold = pageHeight - (72 + 60);

        foreach (PdfLetter letter in rendered.Letters().Where(letter => letter.Origin.Y > threshold))
        {
            bool insideBox = letter.Origin.X < 72 + 100 - SideInset + 0.5;
            bool pastBand = letter.Origin.X >= 72 + 100 + 9 - 0.5;
            Assert.True(insideBox || pastBand, $"a glyph at {letter.Origin.X:0.0} sits in the box's clearance");
        }

        Assert.Contains(rendered.Letters(), letter => letter.Origin.Y > threshold && letter.Origin.X > 181);
    }

    [Fact]
    public void WordsTallerThanTheBoxAreCutOffAndSaidSo()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(
            Box(150, 40, paragraphs: ["One", "Two", "Three", "Four", "Five"]));

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("One", rendered.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("Five", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.LayoutApproximated && warning.Subject == "shape-overflow");
    }

    [Fact]
    public void ATableInsideABoxIsDrawn()
    {
        var content = new TextBox();
        content.AddParagraph("Words above the table");
        Table table = content.AddTable(2, 2);
        table[0, 0].SetText("North");
        table[0, 1].SetText("East");
        table[1, 0].SetText("South");
        table[1, 1].SetText("West");

        var shape = new Shape(["<wps/>", "</wps>"], content)
        {
            Width = Length.FromPoints(260),
            Height = Length.FromPoints(140),
        };

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(shape);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Words above the table", rendered.Text(0), StringComparison.Ordinal);
        foreach (string cell in new[] { "North", "East", "South", "West" })
            Assert.Contains(cell, rendered.Text(0), StringComparison.Ordinal);

        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "shape-table");
    }

    [Fact]
    public void ATableTallerThanItsBoxIsCutOffAndSaidSo()
    {
        var content = new TextBox();
        Table table = content.AddTable(8, 1);
        for (int row = 0; row < 8; row++)
            table[row, 0].SetText($"Row {row + 1}");

        var shape = new Shape(["<wps/>", "</wps>"], content)
        {
            Width = Length.FromPoints(200),
            Height = Length.FromPoints(60),
        };

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(shape);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Row 1", rendered.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("Row 8", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.LayoutApproximated && warning.Subject == "shape-overflow");
    }

    [Fact]
    public void ABoxWhoseMarkupStatesNoSizeIsSkippedAndSaidSo()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Host text.");
        paragraph.AppendObject(Box(0, 0, paragraphs: "Lost"));

        using Rendered rendered = Rendered.Of(document);

        Assert.DoesNotContain("Lost", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.ContentSkipped && warning.Subject == "shapes");
    }

    [Fact]
    public void ABoxInsideAHeaderIsDrawnWithTheHeader()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Body.");
        Paragraph header = document.Sections[0].Headers.GetOrCreate(HeaderFooterKind.Default).AddParagraph();
        header.AppendObject(Box(200, 40, paragraphs: "Boxed header"));

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Boxed header", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void ABoxInsideABoxIsDrawnInsideIt()
    {
        var inner = Box(100, 30, paragraphs: "Deep");
        var content = new TextBox();
        content.AddParagraph("Shallow ").AppendObject(inner);

        var outer = new Shape(["<wps/>", "</wps>"], content)
        {
            Width = Length.FromPoints(300),
            Height = Length.FromPoints(120),
        };

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(outer);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Shallow", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("Deep", rendered.Text(0), StringComparison.Ordinal);
    }
}
