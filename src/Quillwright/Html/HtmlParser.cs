namespace Quillwright.Html;

/// <summary>
/// Parses HTML the way the standard says to (WHATWG HTML §13.2): the tokenizer of
/// <see cref="HtmlTokenizer"/> feeding the tree construction of <see cref="HtmlTreeBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every state of the tokenizer and every insertion mode of the tree builder is implemented,
/// along with the parts that make real-world markup come out the way a browser makes it come
/// out: the stack of open elements with its five scopes, the list of active formatting
/// elements with the Noah's Ark clause, the adoption agency algorithm for misnested
/// formatting, foster parenting for content stranded in a table, implied end tags, and the
/// 2229 named character references of §13.5.
/// </para>
/// <para>
/// The parser runs with scripting disabled — a document importer is not a browser — so a
/// <c>noscript</c> element's content is parsed as the markup it is. Parse errors are not
/// reported: the standard pairs each one with the recovery it requires, and it is the
/// recovery, faithfully performed, that decides what tree an author's markup produces.
/// </para>
/// </remarks>
internal static class HtmlParser
{
    /// <summary>Parses a document or a fragment and hands back the root of the tree.</summary>
    /// <param name="html">The markup.</param>
    public static HtmlElement Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new HtmlTreeBuilder(html).Build();
    }
}
