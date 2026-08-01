using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Tests;

public class ParagraphTests
{
    [Fact]
    public void AppendText_MergesRunsWithTheSameFormat()
    {
        var paragraph = new Paragraph();
        paragraph.AppendText("Hello, ");
        paragraph.AppendText("world");

        Assert.Equal("Hello, world", paragraph.Text);
        Assert.Single(paragraph.Runs);
    }

    [Fact]
    public void AppendText_KeepsRunsApartWhenFormatDiffers()
    {
        var paragraph = new Paragraph();
        paragraph.AppendText("plain ", RunFormat.Default);
        paragraph.AppendText("bold", RunFormat.Default with { Bold = true });

        Assert.Equal(2, paragraph.Runs.Count);
        Assert.Equal("bold", paragraph.Runs[1].Text);
        Assert.True(paragraph.Runs[1].Format.Bold);
    }

    [Fact]
    public void ReplaceText_SpansRunBoundaries()
    {
        var paragraph = new Paragraph();
        paragraph.AppendText("Hello ", RunFormat.Default);
        paragraph.AppendText("cruel ", RunFormat.Default with { Italic = true });
        paragraph.AppendText("world", RunFormat.Default);

        int index = paragraph.Text.IndexOf("cruel world", StringComparison.Ordinal);
        paragraph.ReplaceText(index, "cruel world".Length, "planet");

        Assert.Equal("Hello planet", paragraph.Text);
    }

    [Fact]
    public void ReplaceText_MovesObjectsAndMarks()
    {
        var paragraph = new Paragraph();
        paragraph.AppendText("start ");
        paragraph.AddMark(new BookmarkStart { Id = 1, Name = "here" });
        paragraph.AppendText("middle");
        paragraph.AppendBreak(BreakKind.Page);
        paragraph.AppendText(" end");

        paragraph.ReplaceText(0, "start ".Length, "");

        Assert.Equal("middle\n end", paragraph.Text);
        Assert.Equal(0, paragraph.Marks.Single().Offset);
        Assert.Equal("middle".Length, paragraph.Objects.Single().Offset);
    }

    [Fact]
    public void ReplaceText_KeepsAWrapperAroundTheReplacement()
    {
        var paragraph = new Paragraph();
        paragraph.AppendText("see ");
        int start = paragraph.TextLength;
        paragraph.AppendText("{{link}}");
        paragraph.AddRange(new Hyperlink { Url = "https://example.com" }, start, paragraph.TextLength - start);

        paragraph.ReplaceText(start, "{{link}}".Length, "our site");

        (int rangeStart, int rangeLength, InlineRange range) = paragraph.Ranges.Single();
        Assert.Equal("see our site", paragraph.Text);
        Assert.Equal(start, rangeStart);
        Assert.Equal("our site".Length, rangeLength);
        Assert.IsType<Hyperlink>(range);
    }

    [Fact]
    public void ApplyFormat_SplitsRunsAtTheEdgesOfTheRange()
    {
        var paragraph = new Paragraph("The quick brown fox");
        paragraph.ApplyFormat(4, 5, format => format with { Bold = true });

        Assert.Equal(3, paragraph.Runs.Count);
        Assert.Equal("quick", paragraph.Runs[1].Text);
        Assert.True(paragraph.Runs[1].Format.Bold);
        Assert.Null(paragraph.Runs[0].Format.Bold);
    }

    [Fact]
    public void GetText_DropsPlaceholdersOfObjectsWithoutText()
    {
        var paragraph = new Paragraph();
        paragraph.AppendText("before");
        paragraph.AppendObject(new CommentReference { Id = 1 });
        paragraph.AppendText("after");

        Assert.Equal("beforeafter", paragraph.GetText());
        Assert.Equal(12, paragraph.TextLength);
    }

    [Fact]
    public void Length_ConvertsBetweenTheUnitsOoxmlUses()
    {
        Assert.Equal(1440, Length.FromInches(1).Twips);
        Assert.Equal(24, Length.FromPoints(12).HalfPoints);
        Assert.Equal(914400, Length.FromInches(1).Emu);
        Assert.Equal(4, Length.FromPoints(0.5).EighthPoints);
        Assert.Equal(2.54, Length.FromInches(1).Centimeters, 3);
    }

    [Fact]
    public void RunFormat_MergeAppliesToggleSemantics()
    {
        RunFormat style = RunFormat.Default with { Bold = true };
        RunFormat direct = RunFormat.Default with { Bold = true };

        Assert.False(style.Merge(direct).Bold);
        Assert.True(style.Apply(direct).Bold);
    }
}
