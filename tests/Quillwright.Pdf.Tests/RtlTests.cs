using Inkwright.Text;
using Quillwright.Model;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Right-to-left text: Hebrew read from the right, Latin and numbers keeping their own order
/// inside it, Arabic joined into its letterforms, and tables mirrored column for column.
/// </summary>
public sealed class RtlTests
{
    private static Paragraph Rtl(WordDocument document, string text, string font = "Arial")
    {
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.Format = paragraph.Format with { RightToLeft = true };
        paragraph.AppendText(text, RunFormat.Default with { FontAscii = font, RightToLeft = true });
        return paragraph;
    }

    private static bool IsHebrew(string text) => text.Length > 0 && text[0] is >= '\u0590' and <= '\u05FF';

    [Fact]
    public void HebrewReadsFromTheRight()
    {
        WordDocument document = WordDocument.Create();
        Rtl(document, "\u05E9\u05DC\u05D5\u05DD \u05E2\u05D5\u05DC\u05DD");

        using Rendered rendered = Rendered.Of(document);
        List<PdfLetter> hebrew = [.. rendered.Letters().Where(letter => IsHebrew(letter.Text))];

        Assert.NotEmpty(hebrew);

        // The first letter read — shin — is the rightmost letter drawn.
        PdfLetter rightmost = hebrew.OrderByDescending(letter => letter.Origin.X).First();
        Assert.Equal("\u05E9", rightmost.Text);
    }

    [Fact]
    public void ARightToLeftParagraphLeansOnTheRightMargin()
    {
        WordDocument document = WordDocument.Create();
        Rtl(document, "\u05E9\u05DC\u05D5\u05DD");

        using Rendered rendered = Rendered.Of(document);

        // A4 with inch margins puts the right edge of the text at 523.3 points.
        Assert.Equal(523.3, rendered.RightEdge(), 1.5);
    }

    [Fact]
    public void LatinInsideHebrewKeepsItsOwnOrder()
    {
        WordDocument document = WordDocument.Create();
        Rtl(document, "\u05E9\u05DC\u05D5\u05DD ABC \u05E2\u05D5\u05DC\u05DD");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        double a = letters.First(letter => letter.Text == "A").Origin.X;
        double b = letters.First(letter => letter.Text == "B").Origin.X;
        double c = letters.First(letter => letter.Text == "C").Origin.X;

        Assert.True(a < b && b < c, "the Latin island was not left-to-right inside the Hebrew");
    }

    [Fact]
    public void NumbersInsideHebrewReadForward()
    {
        WordDocument document = WordDocument.Create();
        Rtl(document, "\u05E2\u05DE\u05D5\u05D3 123 \u05DE\u05EA\u05D5\u05DA 456");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        double one = letters.First(letter => letter.Text == "1").Origin.X;
        double two = letters.First(letter => letter.Text == "2").Origin.X;
        double three = letters.First(letter => letter.Text == "3").Origin.X;

        Assert.True(one < two && two < three, "the digits did not read forward");

        // The second number sits to the left of the first: the sentence reads right to left.
        double four = letters.First(letter => letter.Text == "4").Origin.X;
        Assert.True(four < one, "the later number did not continue to the left");
    }

    [Fact]
    public void ArabicIsJoinedIntoItsLetterforms()
    {
        WordDocument document = WordDocument.Create();
        Rtl(document, "\u0627\u0644\u0633\u0644\u0627\u0645");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        // Every Arabic letter came out as a presentation form, and lam-alef fused into its
        // ligature — the final one, since seen joins onto the lam.
        Assert.Contains(letters, letter => letter.Text.Length == 1
            && letter.Text[0] is >= '\uFE70' and <= '\uFEFF');
        Assert.Contains(letters, letter => letter.Text == "\uFEFC");
        Assert.DoesNotContain(letters, letter => letter.Text.Length == 1
            && letter.Text[0] is >= '\u0621' and <= '\u064A');
    }

    [Fact]
    public void ARightToLeftTableMirrorsItsColumns()
    {
        WordDocument document = WordDocument.Create();
        Table table = document.Sections[0].AddTable(1, 2);
        table.Format = table.Format with { RightToLeft = true };
        table[0, 0].SetText("A");
        table[0, 1].SetText("B");

        using Rendered rendered = Rendered.Of(document);
        IReadOnlyList<PdfLetter> letters = rendered.Letters();

        double a = letters.First(letter => letter.Text == "A").Origin.X;
        double b = letters.First(letter => letter.Text == "B").Origin.X;

        Assert.True(a > b, "the first logical column was not drawn at the right edge");
    }

    [Fact]
    public void MirroredBracketsStillOpen()
    {
        WordDocument document = WordDocument.Create();
        Rtl(document, "\u05E9\u05DC\u05D5\u05DD (\u05E2\u05D5\u05DC\u05DD)");

        using Rendered rendered = Rendered.Of(document);

        // The parenthesis pair survives visually: both glyphs are on the page.
        Assert.Contains(rendered.Letters(), letter => letter.Text == "(");
        Assert.Contains(rendered.Letters(), letter => letter.Text == ")");
    }
}
