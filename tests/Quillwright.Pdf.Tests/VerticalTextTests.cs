using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Text turned on its side: cells whose words run down the page, and text boxes whose words do.
/// The lines run along the height and stack across the width; what stacks past the width is cut
/// off and said.
/// </summary>
public sealed class VerticalTextTests
{
    [Fact]
    public void ATurnedCellMakesItsRowTallEnoughForItsWords()
    {
        WordDocument document = WordDocument.Create();
        Table table = document.Sections[0].AddTable(1, 2);
        table[0, 0].SetText("Tall vertical header");
        table[0, 0].Format = table[0, 0].Format with
        {
            TextDirection = TextDirection.TopToBottomRightToLeft,
        };
        table[0, 1].SetText("Beside");
        document.Sections[0].AddParagraph("After the table.");

        using Rendered rendered = Rendered.Of(document);

        // The row is as tall as the turned words are long, so the paragraph after the table
        // sits far below the words beside them.
        double beside = rendered.Letters().First(letter => letter.Text == "B").Origin.Y;
        double after = rendered.Letters().First(letter => letter.Text == "f").Origin.Y;

        Assert.True(beside - after > 80, $"the row is only {beside - after:0.0} points tall");
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "vertical-overflow");
    }

    [Fact]
    public void ATurnedCellReadingUpwardsRendersWithoutComplaint()
    {
        WordDocument document = WordDocument.Create();
        Table table = document.Sections[0].AddTable(1, 2);
        table[0, 0].SetText("Rising label");
        table[0, 0].Format = table[0, 0].Format with
        {
            TextDirection = TextDirection.BottomToTopLeftToRight,
        };
        table[0, 1].SetText("Plain");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(1, rendered.PageCount);
        Assert.Contains("Plain", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains(rendered.Letters(), letter => letter.Text == "R");
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "vertical-overflow");
    }

    [Fact]
    public void ATurnedBoxRunsItsWordsDownItsHeight()
    {
        var content = new TextBox();
        content.AddParagraph("A tall label runs down it");

        var shape = new Shape(["<wps/>", "</wps>"], content)
        {
            Width = Length.FromPoints(40),
            Height = Length.FromPoints(220),
            Direction = TextDirection.TopToBottomRightToLeft,
        };

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(shape);

        using Rendered rendered = Rendered.Of(document);

        // The words are far longer than the box is wide; only running down its height fits them.
        Assert.Contains(rendered.Letters(), letter => letter.Text == "A");
        Assert.Contains(rendered.Letters(), letter => letter.Text == "d");
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "vertical-overflow");
        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "shape-overflow");
    }

    [Fact]
    public void LinesThatStackPastTheBoxAreCutOffAndSaidSo()
    {
        var content = new TextBox();
        content.AddParagraph("One");
        content.AddParagraph("Two");
        content.AddParagraph("Three");

        var shape = new Shape(["<wps/>", "</wps>"], content)
        {
            Width = Length.FromPoints(32),
            Height = Length.FromPoints(200),
            Direction = TextDirection.TopToBottomRightToLeft,
        };

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(shape);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.LayoutApproximated
                && warning.Subject == "vertical-overflow");
    }

    [Fact]
    public void AVerticalSectionIsLaidOutPlainAndSaidPlainly()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.TextDirection = TextDirection.TopToBottomRightToLeft;
        document.Sections[0].AddParagraph("Ordinary after all.");

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Ordinary after all.", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.LayoutApproximated
                && warning.Subject == "vertical-section");
    }
}
