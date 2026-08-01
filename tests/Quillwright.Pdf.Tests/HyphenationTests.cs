using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// How words break at line ends: soft hyphens that show only when taken, and automatic
/// hyphenation driven by the document's <c>w:autoHyphenation</c> and caller-supplied Liang
/// patterns.
/// </summary>
public sealed class HyphenationTests
{
    /// <summary>A pattern set whose one rule allows a break before every <c>na</c>.</summary>
    private const string BananaPatterns = "\\patterns{1na}";

    [Fact]
    public void TexPatterns_FindTheClassicBreak()
    {
        HyphenationPatterns patterns = HyphenationPatterns.Parse(BananaPatterns);

        // ba-nana: the later opportunity would leave only two characters, fewer than RightMin.
        Assert.Equal([2], patterns.Opportunities("banana"));
    }

    [Fact]
    public void AnExceptionList_WinsOverThePatterns()
    {
        HyphenationPatterns patterns = HyphenationPatterns.Parse("\\patterns{1na} \\hyphenation{ba-na-na}");

        Assert.Equal([2, 4], patterns.Opportunities("banana"));
    }

    [Fact]
    public void TheLineForm_ParsesLikeADictionaryFile()
    {
        HyphenationPatterns patterns = HyphenationPatterns.Parse("UTF-8\nLEFTHYPHENMIN 2\nRIGHTHYPHENMIN 3\n1na\n");

        Assert.Equal(2, patterns.LeftMin);
        Assert.Equal(3, patterns.RightMin);
        Assert.Equal([2], patterns.Opportunities("banana"));
    }

    [Fact]
    public void TheMargins_AreHonoured()
    {
        HyphenationPatterns patterns = HyphenationPatterns.Parse(BananaPatterns);
        patterns.LeftMin = 3;

        Assert.Empty(patterns.Opportunities("banana"));
    }

    [Fact]
    public void ASoftHyphenAwayFromTheBreak_NeverShows()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("co\u00ADoperation matters");

        using Rendered rendered = Rendered.Of(document);

        string text = rendered.Text();
        Assert.Contains("cooperation", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\u00AD', text);
        Assert.DoesNotContain('-', text);
    }

    [Fact]
    public void ASoftHyphenAtTheBreak_DrawsAHyphen()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("aaaaaaaa\u00ADbbbbbbbb");
        paragraph.Format = paragraph.Format with { IndentRight = Length.FromPoints(380) };

        using Rendered rendered = Rendered.Of(document);

        IReadOnlyList<string> lines = rendered.Lines();
        Assert.Equal(2, lines.Count);
        Assert.Equal("aaaaaaaa-", lines[0]);
        Assert.Equal("bbbbbbbb", lines[1]);
    }

    [Fact]
    public void AutomaticHyphenation_BreaksAWordWhereThePatternsAllow()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.AutoHyphenation = true;
        Paragraph paragraph = document.Sections[0].AddParagraph("bananabananabananabanana");
        paragraph.Format = paragraph.Format with { IndentRight = Length.FromPoints(400) };

        var options = new PdfExportOptions();
        options.HyphenationPatterns["en"] = HyphenationPatterns.Parse(BananaPatterns);

        using Rendered rendered = Rendered.Of(document, options);

        IReadOnlyList<string> lines = rendered.Lines();
        Assert.True(lines.Count > 1, "The word was expected to span lines.");
        foreach (string line in lines.SkipLast(1))
            Assert.EndsWith("-", line, StringComparison.Ordinal);

        string joined = string.Concat(lines.Select(line => line.TrimEnd('-')));
        Assert.Equal("bananabananabananabanana", joined);
    }

    [Fact]
    public void WithoutPatterns_TheUncoveredLanguageIsNamed()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.AutoHyphenation = true;
        Paragraph paragraph = document.Sections[0].AddParagraph("bananabananabananabanana");
        paragraph.Format = paragraph.Format with { IndentRight = Length.FromPoints(400) };

        using Rendered rendered = Rendered.Of(document);

        PdfExportWarning warning = Assert.Single(
            rendered.Diagnostics, w => w.Kind == PdfExportWarningKind.LayoutApproximated);
        Assert.Equal("en-US", warning.Subject);
        Assert.DoesNotContain(rendered.Lines(), line => line.EndsWith('-'));
    }

    [Fact]
    public void SuppressAutoHyphens_KeepsTheParagraphWhole()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.AutoHyphenation = true;
        Paragraph paragraph = document.Sections[0].AddParagraph("bananabananabananabanana");
        paragraph.Format = paragraph.Format with
        {
            IndentRight = Length.FromPoints(400),
            SuppressAutoHyphens = true,
        };

        var options = new PdfExportOptions();
        options.HyphenationPatterns["en"] = HyphenationPatterns.Parse(BananaPatterns);

        using Rendered rendered = Rendered.Of(document, options);

        Assert.DoesNotContain(rendered.Lines(), line => line.EndsWith('-'));
    }

    [Fact]
    public void DoNotHyphenateCaps_LeavesCapitalWordsWhole()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.AutoHyphenation = true;
        document.Settings.DoNotHyphenateCaps = true;
        Paragraph paragraph = document.Sections[0].AddParagraph("BANANABANANABANANABANANA");
        paragraph.Format = paragraph.Format with { IndentRight = Length.FromPoints(400) };

        var options = new PdfExportOptions();
        options.HyphenationPatterns["en"] = HyphenationPatterns.Parse(BananaPatterns);

        using Rendered rendered = Rendered.Of(document, options);

        Assert.DoesNotContain(rendered.Lines(), line => line.EndsWith('-'));
    }
}
