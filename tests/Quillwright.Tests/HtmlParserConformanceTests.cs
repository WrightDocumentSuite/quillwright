using System.Text;
using Quillwright.Html;

namespace Quillwright.Tests;

/// <summary>
/// The parser against the standard it implements (WHATWG HTML §13.2). Every expected tree
/// here is the one a browser produces for the same markup — checked against Chrome's own
/// parser through its DevTools protocol — so these are not this parser's opinion of the
/// standard being compared with itself.
/// </summary>
public class HtmlParserConformanceTests
{
    /// <summary>
    /// The example the standard opens its error-handling section with (§13.2.10.1): the
    /// adoption agency algorithm reopening an italic that was closed out of order.
    /// </summary>
    [Fact]
    public void MisnestedFormatting_IsUntangledAsTheStandardDescribes() => Assert.Equal(
        "<head></head><body><p>1<b>2<i>3</i></b><i>4</i>5</p></body>",
        Parse("<p>1<b>2<i>3</b>4</i>5</p>"));

    /// <summary>The standard's second example (§13.2.10.2), where the furthest block is a paragraph.</summary>
    [Fact]
    public void FormattingAcrossAParagraph_MovesTheParagraphOut() => Assert.Equal(
        "<head></head><body><b>1</b><p><b>2</b>3</p></body>",
        Parse("<b>1<p>2</b>3</p>"));

    [Fact]
    public void AnEmptyFormattingElementAcrossAParagraph_SplitsTheSameWay() => Assert.Equal(
        "<head></head><body><b></b><p><b>1</b>2</p></body>",
        Parse("<b><p>1</b>2</p>"));

    /// <summary>
    /// The standard's table example (§13.2.10.3): content that cannot live in a table is
    /// fostered out in front of it, and the formatting element is reopened after it.
    /// </summary>
    [Fact]
    public void ContentStrandedInATable_IsFosterParented() => Assert.Equal(
        "<head></head><body><b></b><b>bbb</b><table><tbody><tr><td>aaa</td></tr></tbody></table><b>ccc</b></body>",
        Parse("<table><b><tr><td>aaa</td></tr>bbb</table>ccc"));

    [Fact]
    public void AnUnclosedFormattingElement_SwallowsTheBlockThatFollows() => Assert.Equal(
        "<head></head><body><b>one<p>two</p></b></body>",
        Parse("<b>one<p>two</p>"));

    [Fact]
    public void ListItems_CloseEachOther() => Assert.Equal(
        "<head></head><body><ul><li>a</li><li>b</li></ul></body>",
        Parse("<ul><li>a<li>b</ul>"));

    [Fact]
    public void ATableWithoutItsBody_GetsOneImplied() => Assert.Equal(
        "<head></head><body><table><tbody><tr><td>x</td></tr></tbody></table></body>",
        Parse("<table><tr><td>x"));

    /// <summary>
    /// The example the standard gives for the <c>a</c> start tag rule: the outer link is
    /// closed by the inner one even across a table, and the two end up indirectly nested.
    /// </summary>
    [Fact]
    public void AnAnchorInsideAnAnchor_ClosesTheOuterOne() => Assert.Equal(
        "<head></head><body><a href=\"a\">a<a href=\"b\">b</a><table></table></a><a href=\"b\">x</a></body>",
        Parse("<a href=\"a\">a<table><a href=\"b\">b</table>x"));

    [Fact]
    public void AnUnclosedParagraphInsideADiv_ClosesWithIt() => Assert.Equal(
        "<head></head><body><div><p>a</p></div>b</body>",
        Parse("<div><p>a</div>b"));

    /// <summary>
    /// The standard's own illustration of the legacy semicolon rule (§13.2.5.78): outside an
    /// attribute a name without its semicolon still matches the longest name it can.
    /// </summary>
    [Fact]
    public void ASemicolonLessReference_MatchesTheLongestNameOutsideAnAttribute()
    {
        Assert.Equal("<head></head><body>I'm ¬it; I tell you</body>", Parse("I'm &notit; I tell you"));
        Assert.Equal("<head></head><body>I'm ∉ I tell you</body>", Parse("I'm &notin; I tell you"));
    }

    /// <summary>Inside an attribute the same markup is left alone, which is the other half of the rule.</summary>
    [Fact]
    public void ASemicolonLessReference_IsLeftAloneInsideAnAttribute() => Assert.Equal(
        "<head></head><body><a title=\"&notit;\"></a></body>",
        Parse("<a title=\"&notit;\"></a>"));

    /// <summary>
    /// Numeric references: the C1 range is remapped to the Windows-1252 characters authors
    /// meant, and an ampersand that names nothing stays an ampersand.
    /// </summary>
    [Fact]
    public void NumericReferences_AreRemappedAndAmbiguousOnesAreLeft() => Assert.Equal(
        "<head></head><body><p>•—&&x</p></body>",
        Parse("<p>&#149;&#x2014;&amp&ampx</p>"));

    [Fact]
    public void ATitleHoldsText_NotMarkup() => Assert.Equal(
        "<head><title>a<b>c</title></head><body><p>after</p></body>",
        Parse("<title>a<b>c</title><p>after"));

    /// <summary>
    /// <c>nobr</c> is the element the adoption agency algorithm was written for: a second one
    /// closes the first through the algorithm rather than nesting inside it.
    /// </summary>
    [Fact]
    public void NobrElements_CloseEachOtherThroughTheAgency() => Assert.Equal(
        "<head></head><body><p>a<b>b<nobr>c</nobr><nobr>d</nobr></b></p></body>",
        Parse("<p>a<b>b<nobr>c<nobr>d</p>"));

    [Fact]
    public void TheEntityTable_IsTheWholeOne()
    {
        Assert.Equal(2229, HtmlNamedCharacterReferences.Count);
        Assert.Equal("Æ", HtmlNamedCharacterReferences.Lookup("AElig;"));
        Assert.Equal("Æ", HtmlNamedCharacterReferences.Lookup("AElig"));

        // The 93 names that expand to two code points, one of which is a combining mark.
        Assert.Equal("∾̳", HtmlNamedCharacterReferences.Lookup("acE;"));
        Assert.Null(HtmlNamedCharacterReferences.Lookup("nosuchname;"));
    }

    /// <summary>Renders the parsed tree the way the browser oracle rendered it, for comparison.</summary>
    private static string Parse(string html)
    {
        HtmlElement document = HtmlParser.Parse(html);
        HtmlElement root = document.Children.OfType<HtmlElement>().First(element => element.Name == "html");
        var text = new StringBuilder();
        SerializeChildren(root, text);
        return text.ToString();
    }

    private static void SerializeChildren(HtmlElement element, StringBuilder text)
    {
        foreach (HtmlNode child in element.Children)
        {
            switch (child)
            {
                case HtmlText content:
                    text.Append(content.Value);
                    break;

                case HtmlComment comment:
                    text.Append("<!--").Append(comment.Data).Append("-->");
                    break;

                case HtmlElement nested:
                    text.Append('<').Append(nested.Name);
                    foreach (HtmlAttribute attribute in nested.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal))
                        text.Append(' ').Append(attribute.Name).Append("=\"").Append(attribute.Value).Append('"');

                    text.Append('>');
                    SerializeChildren(nested, text);
                    text.Append("</").Append(nested.Name).Append('>');
                    break;

                default:
                    break;
            }
        }
    }
}
