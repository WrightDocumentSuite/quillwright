namespace Quillwright.Html;

using Quillwright.Diagnostics;

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
/// 2229 named character references of §13.5. Document types and processing instructions are
/// retained as nodes in the parsed document tree.
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
    /// <summary>Parses a document and hands back the root of the tree.</summary>
    /// <param name="html">The markup.</param>
    /// <param name="budget">Optional counters for a public import operation.</param>
    public static HtmlElement Parse(string html, DocumentLoadBudgetState? budget = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new HtmlTreeBuilder(html, budget).Build();
    }

    internal static HtmlElement ParseWithCancellation(
        string html,
        DocumentLoadBudgetState? budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        cancellationToken.ThrowIfCancellationRequested();
        return new HtmlTreeBuilder(html, budget, cancellationToken).Build();
    }

    /// <summary>Parses markup using an element as the HTML fragment context.</summary>
    /// <param name="html">The fragment markup.</param>
    /// <param name="contextElement">The context element's local name.</param>
    /// <param name="contextNamespace">The context element's namespace.</param>
    /// <param name="budget">Optional counters for a public import operation.</param>
    public static HtmlElement ParseFragment(
        string html,
        string contextElement,
        HtmlNamespace contextNamespace = HtmlNamespace.Html,
        DocumentLoadBudgetState? budget = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrEmpty(contextElement);

        string localName = contextNamespace == HtmlNamespace.Html
            ? AsciiLower(contextElement)
            : contextElement;
        var context = new HtmlElement(localName, contextNamespace);
        return new HtmlTreeBuilder(html, context, budget).Build();
    }

    internal static HtmlElement ParseFragmentWithCancellation(
        string html,
        string contextElement,
        HtmlNamespace contextNamespace = HtmlNamespace.Html,
        DocumentLoadBudgetState? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrEmpty(contextElement);
        cancellationToken.ThrowIfCancellationRequested();

        string localName = contextNamespace == HtmlNamespace.Html
            ? AsciiLower(contextElement)
            : contextElement;
        var context = new HtmlElement(localName, contextNamespace);
        return new HtmlTreeBuilder(html, context, budget, cancellationToken).Build();
    }

    private static string AsciiLower(string value)
    {
        char[]? folded = null;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character is not (>= 'A' and <= 'Z'))
                continue;

            folded ??= value.ToCharArray();
            folded[i] = (char)(character + 0x20);
        }

        return folded is null ? value : new string(folded);
    }
}
