using System.Text;
using Inkwright.Text;
using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class CharacterFormatTests
{
    private static Rendered Render(string text, RunFormat format)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendText(text, format);
        return Rendered.Of(document);
    }

    private static string Content(Rendered rendered) =>
        Encoding.Latin1.GetString(rendered.Document.Pages[0].GetContent());

    [Fact]
    public void BoldIsDrawnWithTheBoldFace()
    {
        using Rendered regular = Render("Weight", RunFormat.Default);
        using Rendered bold = Render("Weight", RunFormat.Default with { Bold = true });

        // The bold face of any real family advances wider than its regular, so a wider run is
        // proof the substitution chain found the bold file rather than reusing the regular one.
        Assert.True(
            bold.RightEdge() > regular.RightEdge() + 0.5,
            $"Bold measured {bold.RightEdge():0.##}, regular {regular.RightEdge():0.##}.");
    }

    [Fact]
    public void FontSizeReachesTheGlyphs()
    {
        using Rendered rendered = Render("Big", RunFormat.Default with { Size = Length.FromHalfPoints(48) });

        Assert.Equal(24, rendered.Letters()[0].FontSize, 1);
    }

    [Fact]
    public void ColourReachesTheContentStream()
    {
        using Rendered rendered = Render("Green", RunFormat.Default with { Color = WordColor.FromRgb(0x00FF00) });

        Assert.Contains("0 1 0 rg", Content(rendered), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnderlineIsStrokedUnderTheBaseline()
    {
        using Rendered plain = Render("Plain", RunFormat.Default);
        using Rendered underlined = Render("Plain", RunFormat.Default with { Underline = UnderlineStyle.Single });

        Assert.DoesNotContain("\nS\n", Content(plain), StringComparison.Ordinal);
        Assert.Contains("\nS\n", Content(underlined), StringComparison.Ordinal);
    }

    [Fact]
    public void ADoubleUnderlineIsTwoRules()
    {
        using Rendered rendered = Render("Double", RunFormat.Default with { Underline = UnderlineStyle.Double });

        Assert.Equal(2, Occurrences(Content(rendered), "\nS\n"));
    }

    [Fact]
    public void StrikethroughIsStrokedThroughTheText()
    {
        using Rendered rendered = Render("Struck", RunFormat.Default with { Strike = true });

        Assert.Contains("\nS\n", Content(rendered), StringComparison.Ordinal);
    }

    [Fact]
    public void HighlightingPaintsARectangleBehindTheText()
    {
        using Rendered rendered = Render("Marked", RunFormat.Default with { Highlight = HighlightColor.Yellow });

        string content = Content(rendered);
        Assert.Contains("1 1 0 rg", content, StringComparison.Ordinal);
        Assert.Contains("\nf\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ASuperscriptIsSmallerAndSitsHigher()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("x", RunFormat.Default);
        paragraph.AppendText("2", RunFormat.Default with { VerticalAlignment = VerticalTextAlignment.Superscript });

        using Rendered rendered = Rendered.Of(document);
        PdfLetter body = rendered.Letters().First(letter => letter.Text == "x");
        PdfLetter script = rendered.Letters().First(letter => letter.Text == "2");

        Assert.True(script.FontSize < body.FontSize);
        Assert.True(script.Origin.Y > body.Origin.Y);
    }

    [Fact]
    public void ASubscriptSitsLower()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("H", RunFormat.Default);
        paragraph.AppendText("2", RunFormat.Default with { VerticalAlignment = VerticalTextAlignment.Subscript });

        using Rendered rendered = Rendered.Of(document);
        PdfLetter body = rendered.Letters().First(letter => letter.Text == "H");
        PdfLetter script = rendered.Letters().First(letter => letter.Text == "2");

        Assert.True(script.Origin.Y < body.Origin.Y);
    }

    [Fact]
    public void CapsPrintsLowerCaseAsCapitals()
    {
        using Rendered rendered = Render("shout", RunFormat.Default with { Caps = true });

        Assert.Contains("SHOUT", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void SmallCapsDrawsLowerCaseSmallerButStillAsCapitals()
    {
        using Rendered rendered = Render("Ab", RunFormat.Default with { SmallCaps = true });

        Assert.Contains("AB", rendered.Text(), StringComparison.Ordinal);

        PdfLetter big = rendered.Letters().First(letter => letter.Text == "A");
        PdfLetter small = rendered.Letters().First(letter => letter.Text == "B");
        Assert.True(small.FontSize < big.FontSize);
    }

    [Fact]
    public void CharacterSpacingWidensTheRun()
    {
        double Width(Length? spacing)
        {
            using Rendered rendered = Render("iiiii", RunFormat.Default with { CharacterSpacing = spacing });
            return rendered.RightEdge() - rendered.LeftEdge();
        }

        Assert.True(Width(Length.FromPoints(2)) - Width(null) > 6);
    }

    [Fact]
    public void HiddenTextIsLeftOutUnlessAskedFor()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Shown ", RunFormat.Default);
        paragraph.AppendText("Secret", RunFormat.Default with { Hidden = true });

        using Rendered hidden = Rendered.Of(document);
        Assert.DoesNotContain("Secret", hidden.Text(), StringComparison.Ordinal);

        using Rendered shown = Rendered.Of(document, new PdfExportOptions { IncludeHiddenText = true });
        Assert.Contains("Secret", shown.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void TextDeletedUnderRevisionTrackingIsNotPrinted()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Kept Removed");

        using (document.TrackChanges("Reviewer"))
        {
            paragraph.RemoveText("Kept ".Length, "Removed".Length);
        }

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Kept", rendered.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("Removed", rendered.Text(), StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
