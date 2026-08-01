using System.Diagnostics;
using System.Text;
using Quillwright.Html;

namespace Quillwright.Tests;

/// <summary>
/// That the parser stays linear on the shapes that tempt a parser into quadratic behaviour:
/// a long document, deep nesting, a wide table, and a run of formatting elements that the
/// active-formatting list has to reconstruct over and over.
/// </summary>
public class HtmlParserScaleTests
{
    [Fact]
    public void ALongDocument_ParsesInReasonableTime()
    {
        var html = new StringBuilder("<!DOCTYPE html><html><body>");
        for (int i = 0; i < 20_000; i++)
        {
            html.Append("<p class=\"c\">Paragraph ").Append(i)
                .Append(" with <b>bold</b> and <a href=\"https://example.org/")
                .Append(i).Append("\">a link</a> &amp; an entity.</p>");
        }

        html.Append("</body></html>");

        var stopwatch = Stopwatch.StartNew();
        HtmlElement document = HtmlParser.Parse(html.ToString());
        stopwatch.Stop();

        Assert.Equal(20_000, CountElements(document, "p"));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Parsing took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void DeeplyNestedMarkup_DoesNotRunAway()
    {
        const int Depth = 2_000;
        var html = new StringBuilder();
        for (int i = 0; i < Depth; i++)
            html.Append("<div>");

        html.Append("deep");
        for (int i = 0; i < Depth; i++)
            html.Append("</div>");

        HtmlElement document = HtmlParser.Parse(html.ToString());

        Assert.Equal(Depth, CountElements(document, "div"));
    }

    /// <summary>
    /// Unclosed formatting elements are the pathological case: each one stays on the list of
    /// active formatting elements and is reconstructed at every block that follows.
    /// </summary>
    [Fact]
    public void ManyUnclosedFormattingElements_StayBounded()
    {
        var html = new StringBuilder();
        for (int i = 0; i < 500; i++)
            html.Append("<b><i><em>text</em>");

        HtmlElement document = HtmlParser.Parse(html.ToString());

        Assert.True(CountElements(document, "b") >= 500);
    }

    [Fact]
    public void AWideTable_KeepsItsCells()
    {
        var html = new StringBuilder("<table>");
        for (int row = 0; row < 200; row++)
        {
            html.Append("<tr>");
            for (int cell = 0; cell < 20; cell++)
                html.Append("<td>").Append(row).Append(',').Append(cell);
        }

        html.Append("</table>");

        HtmlElement document = HtmlParser.Parse(html.ToString());

        Assert.Equal(200 * 20, CountElements(document, "td"));
    }

    private static int CountElements(HtmlElement element, string name)
    {
        int count = element.Is(name) ? 1 : 0;
        foreach (HtmlNode child in element.Children)
        {
            if (child is HtmlElement nested)
                count += CountElements(nested, name);
        }

        return count;
    }
}
