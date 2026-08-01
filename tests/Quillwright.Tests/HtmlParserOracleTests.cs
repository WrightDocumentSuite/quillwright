using System.Text;
using Quillwright.Html;

namespace Quillwright.Tests;

/// <summary>
/// The parser against a browser, case by case. Every expected tree below came out of Chrome's
/// own HTML parser for the same input, read back through the DevTools protocol; the cases
/// cover the tokenizer's stranger states — script escaping, raw text, comments, character
/// references — and the tree builder's — tables and their foster parenting, formatting,
/// foreign content, templates, and the elements that close each other.
/// </summary>
public class HtmlParserOracleTests
{
    [InlineData("<!DOCTYPE html><html><body><p>x</p></body></html>", "<head></head><body><p>x</p></body>")]
    [InlineData("<script>var x = \"<p>not a tag</p>\";</script><p>after", "<head><script>var x = \"<p>not a tag</p>\";</script></head><body><p>after</p></body>")]
    [InlineData("<script><!-- <p>a</p> --></script>b", "<head><script><!-- <p>a</p> --></script></head><body>b</body>")]
    [InlineData("<script>a<!--<script>b</script>c</script>d", "<head><script>a<!--<script>b</script>c</script></head><body>d</body>")]
    [InlineData("<style>p { content: \"</p>\" }</style>x", "<head><style>p { content: \"</p>\" }</style></head><body>x</body>")]
    [InlineData("<textarea>\n<b>a</b></textarea>", "<head></head><body><textarea><b>a</b></textarea></body>")]
    [InlineData("<!-- a -- b --><p>x", "<head></head><body><p>x</p></body>")]
    [InlineData("<!--[if IE]>junk<![endif]--><p>x", "<head></head><body><p>x</p></body>")]
    [InlineData("<p>a<!-- c -->b", "<head></head><body><p>a<!-- c -->b</p></body>")]
    [InlineData("<div id=a class=b>x</div>", "<head></head><body><div class=\"b\" id=\"a\">x</div></body>")]
    [InlineData("<div id='a' data-x=1>y</div>", "<head></head><body><div data-x=\"1\" id=\"a\">y</div></body>")]
    [InlineData("<div a=\"1\" a=\"2\">z</div>", "<head></head><body><div a=\"1\">z</div></body>")]
    [InlineData("<img src=x alt=\"a\"b>", "<head></head><body><img alt=\"a\" b=\"\" src=\"x\"></img></body>")]
    [InlineData("<p/>a", "<head></head><body><p>a</p></body>")]
    [InlineData("<br/>", "<head></head><body><br></br></body>")]
    [InlineData("<table><td>x", "<head></head><body><table><tbody><tr><td>x</td></tr></tbody></table></body>")]
    [InlineData("<table><tbody><tr><td>a</td></tr></tbody><tr><td>b</table>", "<head></head><body><table><tbody><tr><td>a</td></tr></tbody><tbody><tr><td>b</td></tr></tbody></table></body>")]
    [InlineData("<table><caption>c<td>x", "<head></head><body><table><caption>c</caption><tbody><tr><td>x</td></tr></tbody></table></body>")]
    [InlineData("<table><colgroup><col><tr><td>a", "<head></head><body><table><colgroup><col></col></colgroup><tbody><tr><td>a</td></tr></tbody></table></body>")]
    [InlineData("<table>x<tr><td>y", "<head></head><body>x<table><tbody><tr><td>y</td></tr></tbody></table></body>")]
    [InlineData("<table> <tr><td>y", "<head></head><body><table> <tbody><tr><td>y</td></tr></tbody></table></body>")]
    [InlineData("<select><option>a<option>b</select>", "<head></head><body><select><option>a</option><option>b</option></select></body>")]
    [InlineData("<select><div>x</div></select>", "<head></head><body><select><div>x</div></select></body>")]
    [InlineData("<template><td>x</td></template>", "<head><template><td>x</td></template></head><body></body>")]
    [InlineData("<svg><circle/><path/></svg><p>after", "<head></head><body><svg><circle></circle><path></path></svg><p>after</p></body>")]
    [InlineData("<svg><foreignObject><p>in</p></foreignObject></svg>", "<head></head><body><svg><foreignObject><p>in</p></foreignObject></svg></body>")]
    [InlineData("<math><mi>x</mi></math><p>y", "<head></head><body><math><mi>x</mi></math><p>y</p></body>")]
    [InlineData("<svg><b>break</b></svg>", "<head></head><body><svg></svg><b>break</b></body>")]
    [InlineData("<form><input><form><input>", "<head></head><body><form><input></input><input></input></form></body>")]
    [InlineData("<ruby>a<rt>b</rt></ruby>", "<head></head><body><ruby>a<rt>b</rt></ruby></body>")]
    [InlineData("<dl><dt>a<dd>b<dt>c</dl>", "<head></head><body><dl><dt>a</dt><dd>b</dd><dt>c</dt></dl></body>")]
    [InlineData("<h1>a<h2>b</h2>", "<head></head><body><h1>a</h1><h2>b</h2></body>")]
    [InlineData("<button>a<button>b", "<head></head><body><button>a</button><button>b</button></body>")]
    [InlineData("<p>a</p></p>b", "<head></head><body><p>a</p><p></p>b</body>")]
    [InlineData("</br>x", "<head></head><body><br></br>x</body>")]
    [InlineData("<body><frameset><frame>", "<head></head><body></body>")]
    [InlineData("<plaintext><p>not a tag", "<head></head><body><plaintext><p>not a tag</plaintext></body>")]
    [InlineData("<xmp><b>raw</b></xmp>", "<head></head><body><xmp><b>raw</b></xmp></body>")]
    [InlineData("<iframe><p>raw</p></iframe>", "<head></head><body><iframe><p>raw</p></iframe></body>")]
    [InlineData("<noscript><p>shown</p></noscript>", "<head><noscript></noscript></head><body><p>shown</p></body>")]
    [InlineData("<noembed><p>raw</p></noembed>", "<head></head><body><noembed><p>raw</p></noembed></body>")]
    [InlineData("<a><b><a>", "<head></head><body><a><b></b></a><b><a></a></b></body>")]
    [InlineData("<font color=red><p>x</p></font>", "<head></head><body><font color=\"red\"><p>x</p></font></body>")]
    [InlineData("<i>a<b>b</i>c</b>d", "<head></head><body><i>a<b>b</b></i><b>c</b>d</body>")]
    [InlineData("<table><a>x<td>y", "<head></head><body><a>x</a><table><tbody><tr><td>y</td></tr></tbody></table></body>")]
    [InlineData("<span>&#0;&#xD800;&#x110000;</span>", "<head></head><body><span>\uFFFD\uFFFD\uFFFD</span></body>")]
    [InlineData("<p>&lt&gt&AMP&COPY</p>", "<head></head><body><p><>&©</p></body>")]
    [InlineData("<p a=&amp;b>x", "<head></head><body><p a=\"&b\">x</p></body>")]
    [InlineData("<?php echo 1; ?><p>x", "<head></head><body><p>x</p></body>")]
    [InlineData("<![CDATA[x]]><p>y", "<head></head><body><p>y</p></body>")]
    [InlineData("<hr><p>a", "<head></head><body><hr></hr><p>a</p></body>")]
    [InlineData("<pre>\nfirst</pre>", "<head></head><body><pre>first</pre></body>")]
    [InlineData("<li>a<li>b", "<head></head><body><li>a</li><li>b</li></body>")]
    [InlineData("<td>orphan", "<head></head><body>orphan</body>")]
    [InlineData("<caption>orphan", "<head></head><body>orphan</body>")]
    [InlineData("<b><i><u><s>deep</b>tail", "<head></head><body><b><i><u><s>deep</s></u></i></b><i><u><s>tail</s></u></i></body>")]
    [InlineData("<b><b><b><b><b><b><b><b><b>x</b></b></b></b></b></b></b></b></b>y", "<head></head><body><b><b><b><b><b><b><b><b><b>x</b></b></b></b></b></b></b></b></b>y</body>")]
    [InlineData("<table><tr><td><b>a</table>b", "<head></head><body><table><tbody><tr><td><b>a</b></td></tr></tbody></table>b</body>")]
    [InlineData("<table><tr><td>a</td></tr></table><table><tr><td>b", "<head></head><body><table><tbody><tr><td>a</td></tr></tbody></table><table><tbody><tr><td>b</td></tr></tbody></table></body>")]
    [InlineData("<table><tr><td><table><tr><td>inner</table>outer", "<head></head><body><table><tbody><tr><td><table><tbody><tr><td>inner</td></tr></tbody></table>outer</td></tr></tbody></table></body>")]
    [InlineData("<table><select><option>a</select>", "<head></head><body><select><option>a</option></select><table></table></body>")]
    [InlineData("<p><table><p>x</table>", "<head></head><body><p><p>x</p><table></table></p></body>")]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\"><p><table>", "<head></head><body><p><table></table></p></body>")]
    [InlineData("<!DOCTYPE html><p><table>", "<head></head><body><p></p><table></table></body>")]
    [InlineData("<html lang=en><html dir=rtl><p>x", "<head></head><body><p>x</p></body>")]
    [InlineData("<svg><clipPath><lineargradient/></clipPath></svg>", "<head></head><body><svg><clipPath><linearGradient></linearGradient></clipPath></svg></body>")]
    [InlineData("<svg><textpath></textpath></svg>", "<head></head><body><svg><textPath></textPath></svg></body>")]
    [InlineData("<div><svg><p>break</svg>after", "<head></head><body><div><svg></svg><p>breakafter</p></div></body>")]
    [InlineData("<b>a<table><tr><td><b>b</b></td></tr></table>c</b>", "<head></head><body><b>a<table><tbody><tr><td><b>b</b></td></tr></tbody></table>c</b></body>")]
    [InlineData("<form id=1><table><form id=2><input></table>", "<head></head><body><form id=\"1\"><input></input><table></table></form></body>")]
    [InlineData("<p>a<b>b</p>c", "<head></head><body><p>a<b>b</b></p><b>c</b></body>")]
    [InlineData("<h1>a</h2>b", "<head></head><body><h1>a</h1>b</body>")]
    [InlineData("<font size=1><b>x</font>y", "<head></head><body><font size=\"1\"><b>x</b></font><b>y</b></body>")]
    [InlineData("<span title=\"a&ampb&amp;c&amp=d\">x</span>", "<head></head><body><span title=\"a&ampb&c&amp=d\">x</span></body>")]
    [InlineData("<a href=\"?a=1&lt=2&notin=3\">x</a>", "<head></head><body><a href=\"?a=1&lt=2&notin=3\">x</a></body>")]
    [InlineData("<p>&#X41;&#65;&#x41&#65", "<head></head><body><p>AAAA</p></body>")]
    [InlineData("<div a=b/c=d>x</div>", "<head></head><body><div a=\"b/c=d\">x</div></body>")]
    [InlineData("<div /foo=bar>x</div>", "<head></head><body><div foo=\"bar\">x</div></body>")]
    [InlineData("<p<b>x", "<head></head><body><p<b>x</p<b></body>")]
    [InlineData("<3>x", "<head></head><body><3>x</body>")]
    [InlineData("</ >x", "<head></head><body>x</body>")]
    [InlineData("<!>x", "<head></head><body>x</body>")]
    [InlineData("<!-->x", "<head></head><body>x</body>")]
    [InlineData("<!--->x", "<head></head><body>x</body>")]
    [InlineData("<!---->x", "<head></head><body>x</body>")]
    [InlineData("<table><tr><td>a<tr><td>b", "<head></head><body><table><tbody><tr><td>a</td></tr><tr><td>b</td></tr></tbody></table></body>")]
    [InlineData("<table><thead><tr><td>h<tbody><tr><td>b", "<head></head><body><table><thead><tr><td>h</td></tr></thead><tbody><tr><td>b</td></tr></tbody></table></body>")]
    [InlineData("<td><tr><table>", "<head></head><body><table></table></body>")]
    [InlineData("<frameset><frame><noframes>x</noframes></frameset>", "<head></head><frameset><frame></frame><noframes>x</noframes></frameset>")]
    [InlineData("<body>a<frameset>b", "<head></head><body>ab</body>")]
    [InlineData("<optgroup><option>a<optgroup><option>b", "<head></head><body><optgroup><option>a</option><optgroup><option>b</option></optgroup></optgroup></body>")]
    [InlineData("<ruby><rb>a<rt>b<rtc>c", "<head></head><body><ruby><rb>a</rb><rt>b</rt><rtc>c</rtc></ruby></body>")]
    [InlineData("<nobr><nobr><nobr>x", "<head></head><body><nobr></nobr><nobr></nobr><nobr>x</nobr></body>")]
    [InlineData("<applet><b>a</applet>b", "<head></head><body><applet><b>a</b></applet>b</body>")]
    [InlineData("<marquee><b>a</marquee>b", "<head></head><body><marquee><b>a</b></marquee>b</body>")]
    [Theory]
    public void TheParsedTree_IsTheOneABrowserBuilds(string html, string expected) =>
        Assert.Equal(expected, Serialize(html));

    private static string Serialize(string html)
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
