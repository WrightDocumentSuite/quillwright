using Quillwright.Diagnostics;

namespace Quillwright.Html;

/// <summary>
/// The tree construction stage of the HTML standard (WHATWG HTML §13.2.6): the insertion
/// modes, the stack of open elements with its scopes, the list of active formatting elements
/// with the adoption agency algorithm, and foster parenting.
/// </summary>
/// <remarks>
/// <para>
/// The parser runs with scripting disabled, which is what a document importer is: a
/// <c>noscript</c> element's content is markup and is parsed, exactly as in a browser with
/// scripting turned off, and a <c>script</c> element's content is text that is never run.
/// </para>
/// <para>
/// Parse errors are not reported. The standard defines them alongside the recovery each one
/// takes, and it is the recovery that decides what tree an author's markup produces; that is
/// what is implemented here, error for error.
/// </para>
/// </remarks>
internal sealed partial class HtmlTreeBuilder
{
    /// <summary>The insertion modes of §13.2.4.1.</summary>
    private enum Mode
    {
        Initial,
        BeforeHtml,
        BeforeHead,
        InHead,
        InHeadNoscript,
        AfterHead,
        InBody,
        Text,
        InTable,
        InTableText,
        InCaption,
        InColumnGroup,
        InTableBody,
        InRow,
        InCell,
        InTemplate,
        AfterBody,
        InFrameset,
        AfterFrameset,
        AfterAfterBody,
        AfterAfterFrameset,
    }

    /// <summary>The special category of §13.2.4.2, whose members end an open paragraph and stop a scope walk.</summary>
    private static readonly HashSet<string> Special = new(StringComparer.Ordinal)
    {
        "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote", "body", "br",
        "button", "caption", "center", "col", "colgroup", "dd", "details", "dir", "div", "dl", "dt", "embed",
        "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6",
        "head", "header", "hgroup", "hr", "html", "iframe", "img", "input", "keygen", "li", "link", "listing", "main",
        "marquee", "menu", "meta", "nav", "noembed", "noframes", "noscript", "object", "ol", "p", "param", "plaintext",
        "pre", "script", "search", "section", "select", "source", "style", "summary", "table", "tbody", "td",
        "template", "textarea", "tfoot", "th", "thead", "title", "tr", "track", "ul", "wbr", "xmp",
    };

    /// <summary>The formatting category of §13.2.4.2, whose members go on the list of active formatting elements.</summary>
    private static readonly string[] Formatting =
        ["a", "b", "big", "code", "em", "font", "i", "nobr", "s", "small", "strike", "strong", "tt", "u"];

    /// <summary>The element types that stop the ordinary scope walk.</summary>
    private static readonly string[] ScopeBoundary =
        ["applet", "caption", "html", "table", "td", "th", "marquee", "object", "select", "template"];

    /// <summary>What implied end tags close, unless the caller names an exception.</summary>
    private static readonly string[] ImpliedEndTags =
        ["dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc"];

    /// <summary>What thorough implied end tags close, which is the list above and the table parts.</summary>
    private static readonly string[] ThoroughImpliedEndTags =
        ["caption", "colgroup", "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc", "tbody", "td",
         "tfoot", "th", "thead", "tr"];

    private readonly HtmlTokenizer _tokenizer;
    private readonly DocumentLoadBudgetState? _budget;
    private readonly CancellationToken _cancellationToken;
    private readonly HtmlElement _document = new("#document");
    private readonly HtmlElement? _fragment;
    private readonly HtmlElement? _context;
    private readonly List<HtmlElement> _stack = [];
    private readonly List<HtmlElement?> _formatting = [];
    private readonly List<Mode> _templateModes = [];
    private readonly Dictionary<HtmlElement, HtmlToken> _tokensByElement = new(ReferenceEqualityComparer.Instance);
    private readonly List<HtmlToken> _pendingTableCharacters = [];

    private Mode _mode = Mode.Initial;
    private Mode _originalMode = Mode.Initial;
    private HtmlElement? _head;
    private HtmlElement? _form;
    private bool _framesetOk = true;
    private bool _fosterParenting;
    private bool _quirks;
    private bool _done;
    private bool _ignoreNextLineFeed;

    internal HtmlTreeBuilder(string input, DocumentLoadBudgetState? budget = null)
        : this(input, budget, CancellationToken.None)
    {
    }

    internal HtmlTreeBuilder(string input, DocumentLoadBudgetState? budget, CancellationToken cancellationToken)
    {
        _budget = budget;
        _cancellationToken = cancellationToken;
        _budget?.AddMarkupNode(); // #document
        _tokenizer = new HtmlTokenizer(input, CanStartCdata, cancellationToken);
    }

    /// <summary>Creates the fragment parser with the supplied element as its context.</summary>
    internal HtmlTreeBuilder(string input, HtmlElement context, DocumentLoadBudgetState? budget = null)
        : this(input, context, budget, CancellationToken.None)
    {
    }

    internal HtmlTreeBuilder(
        string input,
        HtmlElement context,
        DocumentLoadBudgetState? budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _budget = budget;
        _cancellationToken = cancellationToken;
        _budget?.AddMarkupNode(2); // #document and #document-fragment
        _context = context;
        _fragment = new HtmlElement("#document-fragment");
        _tokenizer = new HtmlTokenizer(input, CanStartCdata, cancellationToken);

        if (context.Namespace == HtmlNamespace.Html)
        {
            switch (context.Name)
            {
                case "title" or "textarea":
                    _tokenizer.SwitchToRcdata();
                    break;

                case "style" or "xmp" or "iframe" or "noembed" or "noframes":
                    _tokenizer.SwitchToRawtext();
                    break;

                case "script":
                    _tokenizer.SwitchToScriptData();
                    break;

                // Scripting is disabled for imports, so noscript deliberately stays in Data.
                case "plaintext":
                    _tokenizer.SwitchToPlaintext();
                    break;
            }
        }

        var root = new HtmlElement("html");
        _budget?.AddMarkupNode();
        _budget?.EnsureMarkupDepth(1);
        _document.Append(root);
        _stack.Add(root);

        if (context.Is("template"))
            _templateModes.Add(Mode.InTemplate);

        ResetInsertionMode();

        for (HtmlElement? ancestor = context; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!ancestor.Is("form"))
                continue;

            _form = ancestor;
            break;
        }
    }

    /// <summary>Parses the whole input and hands back the document node.</summary>
    public HtmlElement Build()
    {
        while (!_done)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            HtmlToken token = _tokenizer.Next();
            Dispatch(token);
            if (token.Kind == HtmlTokenKind.EndOfFile)
                break;
        }

        return _fragment ?? _document;
    }

    /// <summary>The current node: the bottommost element of the stack of open elements.</summary>
    private HtmlElement? Current => _stack.Count > 0 ? _stack[^1] : null;

    /// <summary>
    /// The node whose namespace controls tokenizer decisions. It is the current node for a
    /// document parser; fragment parsing can substitute its context element here.
    /// </summary>
    private HtmlElement? AdjustedCurrent =>
        _context is not null && _stack.Count == 1 ? _context : Current;

    /// <summary>Whether this parser's fragment context is the named HTML element.</summary>
    private bool FragmentContextIs(string name) => _context?.Is(name) == true;

    private bool CanStartCdata() => AdjustedCurrent is { Namespace: not HtmlNamespace.Html };

    /// <summary>
    /// The tree construction dispatcher of §13.2.6: HTML rules unless the current node is
    /// foreign and the token is not one of the ones that break back out into HTML.
    /// </summary>
    private void Dispatch(HtmlToken token)
    {
        HtmlElement? current = AdjustedCurrent;
        bool html = current is null
            || current.Namespace == HtmlNamespace.Html
            || token.Kind == HtmlTokenKind.EndOfFile
            || (IsMathTextIntegrationPoint(current) && token.Kind == HtmlTokenKind.Character)
            || (IsMathTextIntegrationPoint(current) && token.Kind == HtmlTokenKind.StartTag &&
                token.TagName is not ("mglyph" or "malignmark"))
            || (current.Namespace == HtmlNamespace.MathML && current.Name == "annotation-xml" &&
                token.Kind == HtmlTokenKind.StartTag && token.TagName == "svg")
            || (IsHtmlIntegrationPoint(current) && token.Kind is HtmlTokenKind.StartTag or HtmlTokenKind.Character);

        if (html)
            ProcessInMode(_mode, token);
        else
            ForeignContent(token);
    }

    private static bool IsMathTextIntegrationPoint(HtmlElement element) =>
        element.Namespace == HtmlNamespace.MathML && element.Name is "mi" or "mo" or "mn" or "ms" or "mtext";

    private static bool IsHtmlIntegrationPoint(HtmlElement element)
    {
        if (element.Namespace == HtmlNamespace.Svg)
            return element.Name is "foreignObject" or "desc" or "title";

        if (element.Namespace != HtmlNamespace.MathML || element.Name != "annotation-xml")
            return false;

        string? encoding = element.Attribute("encoding");
        return encoding is not null &&
               (encoding.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
                encoding.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Inserting nodes (§13.2.6.1) ----

    /// <summary>
    /// The appropriate place for inserting a node: inside the current node, unless foster
    /// parenting sends it out of a table and in front of it.
    /// </summary>
    private (HtmlElement Parent, int Index) AppropriatePlace(HtmlElement? overrideTarget = null)
    {
        HtmlElement target = RootInsertionTarget(overrideTarget ?? Current ?? _document);

        if (_fosterParenting && target.IsAny("table", "tbody", "tfoot", "thead", "tr"))
        {
            HtmlElement? lastTemplate = LastOnStack("template");
            HtmlElement? lastTable = LastOnStack("table");

            if (lastTemplate is not null &&
                (lastTable is null || _stack.IndexOf(lastTemplate) > _stack.IndexOf(lastTable)))
            {
                return (lastTemplate, lastTemplate.Children.Count);
            }

            if (lastTable is null)
            {
                HtmlElement root = RootInsertionTarget(_stack[0]);
                return (root, root.Children.Count);
            }

            if (lastTable.Parent is { } parent)
                return (parent, parent.Children.IndexOf(lastTable));

            HtmlElement previous = _stack[_stack.IndexOf(lastTable) - 1];
            return (previous, previous.Children.Count);
        }

        return (target, target.Children.Count);
    }

    private HtmlElement RootInsertionTarget(HtmlElement target) =>
        _fragment is not null && _stack.Count > 0 && ReferenceEquals(target, _stack[0])
            ? _fragment
            : target;

    private HtmlElement? LastOnStack(string name)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (_stack[i].Is(name))
                return _stack[i];
        }

        return null;
    }

    /// <summary>Creates an element for a token, remembering the token so it can be recreated.</summary>
    private HtmlElement CreateFor(HtmlToken token, HtmlNamespace space = HtmlNamespace.Html)
    {
        _budget?.AddMarkupNode();
        var element = new HtmlElement(token.TagName, space) { Line = token.Line };
        element.AddAttributes(token.Attributes);
        _tokensByElement[element] = token.Clone();
        return element;
    }

    /// <summary>Inserts an HTML element for a token and pushes it onto the stack.</summary>
    private HtmlElement InsertElement(HtmlToken token, HtmlNamespace space = HtmlNamespace.Html)
    {
        _budget?.EnsureMarkupDepth(_stack.Count + 1);
        HtmlElement element = CreateFor(token, space);
        (HtmlElement parent, int index) = AppropriatePlace();
        parent.Insert(index, element);
        _stack.Add(element);
        return element;
    }

    /// <summary>Inserts an element for a tag the tree builder invents, such as an implied <c>tbody</c>.</summary>
    private HtmlElement InsertElement(string name, int line)
    {
        var token = new HtmlToken { Kind = HtmlTokenKind.StartTag, Line = line };
        token.Name.Append(name);
        return InsertElement(token);
    }

    /// <summary>Inserts characters, growing the text node before the insertion point when there is one.</summary>
    private void InsertText(string text)
    {
        if (text.Length == 0)
            return;

        (HtmlElement parent, int index) = AppropriatePlace();
        if (index > 0 && parent.Children[index - 1] is HtmlText previous)
        {
            previous.Append(text);
            return;
        }

        _budget?.AddMarkupNode();
        parent.Insert(index, new HtmlText(text));
    }

    private void InsertComment(HtmlToken token, HtmlElement? target = null)
    {
        _budget?.AddMarkupNode();
        var comment = new HtmlComment(token.Data.ToString()) { Line = token.Line };
        if (target is not null)
        {
            RootInsertionTarget(target).Append(comment);
            return;
        }

        (HtmlElement parent, int index) = AppropriatePlace();
        parent.Insert(index, comment);
    }

    private void InsertProcessingInstruction(HtmlToken token, HtmlElement? target = null)
    {
        _budget?.AddMarkupNode();
        var instruction = new HtmlProcessingInstruction(
            token.ProcessingInstructionTarget,
            token.Data.ToString())
        {
            Line = token.Line,
        };

        if (target is not null)
        {
            RootInsertionTarget(target).Append(instruction);
            return;
        }

        (HtmlElement parent, int index) = AppropriatePlace();
        parent.Insert(index, instruction);
    }

    private void InsertDocumentType(HtmlToken token)
    {
        _budget?.AddMarkupNode();
        _document.Append(new HtmlDocumentType(
            token.TagName,
            token.PublicIdentifier ?? string.Empty,
            token.SystemIdentifier ?? string.Empty)
        {
            Line = token.Line,
        });
    }

    // ---- The stack of open elements (§13.2.4.2) ----

    private bool InScope(string target, params ReadOnlySpan<string> extra)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            HtmlElement node = _stack[i];
            if (node.Is(target))
                return true;

            if (IsScopeBoundary(node, extra))
                return false;
        }

        return false;
    }

    private bool InScopeAny(ReadOnlySpan<string> targets, params ReadOnlySpan<string> extra)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            HtmlElement node = _stack[i];
            foreach (string target in targets)
            {
                if (node.Is(target))
                    return true;
            }

            if (IsScopeBoundary(node, extra))
                return false;
        }

        return false;
    }

    private static bool IsScopeBoundary(HtmlElement node, ReadOnlySpan<string> extra)
    {
        if (node.Namespace != HtmlNamespace.Html)
        {
            return IsMathTextIntegrationPoint(node) ||
                   (node.Namespace == HtmlNamespace.MathML && node.Name == "annotation-xml") ||
                   (node.Namespace == HtmlNamespace.Svg && node.Name is "foreignObject" or "desc" or "title");
        }

        foreach (string name in extra)
        {
            if (node.Is(name))
                return true;
        }

        return node.IsAny(ScopeBoundary);
    }

    private bool InListItemScope(string target) => InScope(target, "ol", "ul");

    private bool InButtonScope(string target) => InScope(target, "button");

    /// <summary>Table scope stops at far fewer elements than the ordinary one.</summary>
    private bool InTableScope(string target)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            HtmlElement node = _stack[i];
            if (node.Is(target))
                return true;

            if (node.IsAny("html", "table", "template"))
                return false;
        }

        return false;
    }

    private bool InTableScopeAny(params ReadOnlySpan<string> targets)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            HtmlElement node = _stack[i];
            foreach (string target in targets)
            {
                if (node.Is(target))
                    return true;
            }

            if (node.IsAny("html", "table", "template"))
                return false;
        }

        return false;
    }

    private bool StackHas(string name) => LastOnStack(name) is not null;

    private void PopUntilPopped(string name)
    {
        while (_stack.Count > 0)
        {
            HtmlElement popped = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            if (popped.Is(name))
                return;
        }
    }

    private void PopUntilPoppedAny(params ReadOnlySpan<string> names)
    {
        while (_stack.Count > 0)
        {
            HtmlElement popped = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            if (popped.IsAny(names))
                return;
        }
    }

    private void GenerateImpliedEndTags(string? except = null)
    {
        while (Current is { } node && node.IsAny(ImpliedEndTags) && !node.Is(except ?? "\0"))
            _stack.RemoveAt(_stack.Count - 1);
    }

    private void GenerateImpliedEndTagsThoroughly()
    {
        while (Current is { } node && node.IsAny(ThoroughImpliedEndTags))
            _stack.RemoveAt(_stack.Count - 1);
    }

    /// <summary>Closes an open paragraph, which is what "close a p element" means.</summary>
    private void CloseParagraph()
    {
        GenerateImpliedEndTags("p");
        PopUntilPopped("p");
    }

    // ---- The list of active formatting elements (§13.2.4.3) ----

    private void PushFormatting(HtmlElement element)
    {
        int sameCount = 0;
        for (int i = _formatting.Count - 1; i >= 0; i--)
        {
            if (_formatting[i] is not { } candidate)
                break;

            if (candidate.SameAs(element) && ++sameCount == 3)
            {
                _formatting.RemoveAt(i);
                break;
            }
        }

        _formatting.Add(element);
    }

    private void AddFormattingMarker() => _formatting.Add(null);

    private void ClearFormattingToMarker()
    {
        while (_formatting.Count > 0)
        {
            HtmlElement? entry = _formatting[^1];
            _formatting.RemoveAt(_formatting.Count - 1);
            if (entry is null)
                return;
        }
    }

    /// <summary>
    /// Reopens the formatting elements that are still active but no longer open, which is what
    /// makes <c>&lt;b&gt;one&lt;p&gt;two</c> put the second paragraph's text in bold too.
    /// </summary>
    private void ReconstructFormatting()
    {
        if (_formatting.Count == 0)
            return;

        int index = _formatting.Count - 1;
        if (_formatting[index] is not { } last || _stack.Contains(last))
            return;

        while (index > 0)
        {
            index--;
            if (_formatting[index] is not { } entry || _stack.Contains(entry))
            {
                index++;
                break;
            }
        }

        for (; index < _formatting.Count; index++)
        {
            HtmlElement entry = _formatting[index]!;
            HtmlToken token = _tokensByElement[entry];
            HtmlElement created = InsertElement(token);
            _formatting[index] = created;
        }
    }

    // ---- The adoption agency algorithm (§13.2.6.4.7) ----

    /// <summary>
    /// Untangles misnested formatting: the algorithm that makes <c>&lt;b&gt;1&lt;i&gt;2&lt;/b&gt;3&lt;/i&gt;</c>
    /// come out as a bold "1", a bold-italic "2" and an italic "3".
    /// </summary>
    /// <param name="token">The end tag being processed.</param>
    /// <returns>Whether the token was handled; when false, the caller falls back to any other end tag.</returns>
    private bool AdoptionAgency(HtmlToken token)
    {
        string subject = token.TagName;

        if (Current is { } current && current.Is(subject) && !_formatting.Contains(current))
        {
            _stack.RemoveAt(_stack.Count - 1);
            return true;
        }

        for (int outer = 0; outer < 8; outer++)
        {
            HtmlElement? formattingElement = null;
            for (int i = _formatting.Count - 1; i >= 0; i--)
            {
                if (_formatting[i] is not { } entry)
                    break;

                if (entry.Is(subject))
                {
                    formattingElement = entry;
                    break;
                }
            }

            if (formattingElement is null)
                return false;

            int formattingIndexOnStack = _stack.IndexOf(formattingElement);
            if (formattingIndexOnStack < 0)
            {
                _formatting.Remove(formattingElement);
                return true;
            }

            if (!InScope(formattingElement.Name))
                return true;

            HtmlElement? furthestBlock = null;
            for (int i = formattingIndexOnStack + 1; i < _stack.Count; i++)
            {
                if (IsSpecial(_stack[i]))
                {
                    furthestBlock = _stack[i];
                    break;
                }
            }

            if (furthestBlock is null)
            {
                while (_stack.Count > formattingIndexOnStack)
                    _stack.RemoveAt(_stack.Count - 1);

                _formatting.Remove(formattingElement);
                return true;
            }

            HtmlElement commonAncestor = _stack[formattingIndexOnStack - 1];
            int bookmark = _formatting.IndexOf(formattingElement);

            HtmlElement node = furthestBlock;
            HtmlElement lastNode = furthestBlock;
            int nodeIndex = _stack.IndexOf(node);

            for (int inner = 1; ; inner++)
            {
                nodeIndex--;
                if (nodeIndex < 0)
                    break;

                node = _stack[nodeIndex];
                if (node == formattingElement)
                    break;

                int formattingPosition = _formatting.IndexOf(node);
                if (inner > 3 && formattingPosition >= 0)
                {
                    _formatting.RemoveAt(formattingPosition);
                    if (formattingPosition < bookmark)
                        bookmark--;

                    formattingPosition = -1;
                }

                if (formattingPosition < 0)
                {
                    _stack.RemoveAt(nodeIndex);
                    continue;
                }

                HtmlElement replacement = CreateFor(_tokensByElement[node]);
                commonAncestor.Append(replacement);
                _formatting[formattingPosition] = replacement;
                _stack[nodeIndex] = replacement;
                node = replacement;

                if (lastNode == furthestBlock)
                    bookmark = _formatting.IndexOf(node) + 1;

                node.Append(lastNode);
                lastNode = node;
            }

            (HtmlElement parent, int index) = AppropriatePlace(commonAncestor);
            parent.Insert(index, lastNode);

            HtmlElement newElement = CreateFor(_tokensByElement[formattingElement]);
            foreach (HtmlNode child in furthestBlock.Children.ToArray())
                newElement.Append(child);

            furthestBlock.Append(newElement);

            _formatting.Remove(formattingElement);
            _formatting.Insert(Math.Clamp(bookmark, 0, _formatting.Count), newElement);

            int furthestIndex = _stack.IndexOf(furthestBlock);
            _stack.Remove(formattingElement);
            furthestIndex = _stack.IndexOf(furthestBlock);
            _stack.Insert(furthestIndex + 1, newElement);
        }

        return true;
    }

    private static bool IsSpecial(HtmlElement element)
    {
        if (element.Namespace == HtmlNamespace.MathML)
            return element.Name is "mi" or "mo" or "mn" or "ms" or "mtext" or "annotation-xml";

        if (element.Namespace == HtmlNamespace.Svg)
            return element.Name is "foreignObject" or "desc" or "title";

        return Special.Contains(element.Name);
    }

    // ---- Resetting the insertion mode (§13.2.4.1) ----

    private void ResetInsertionMode()
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            bool last = i == 0;
            HtmlElement node = last && _context is not null ? _context : _stack[i];

            if (node.IsAny("td", "th") && !last)
            {
                _mode = Mode.InCell;
                return;
            }

            if (node.Is("tr"))
            {
                _mode = Mode.InRow;
                return;
            }

            if (node.IsAny("tbody", "thead", "tfoot"))
            {
                _mode = Mode.InTableBody;
                return;
            }

            if (node.Is("caption"))
            {
                _mode = Mode.InCaption;
                return;
            }

            if (node.Is("colgroup"))
            {
                _mode = Mode.InColumnGroup;
                return;
            }

            if (node.Is("table"))
            {
                _mode = Mode.InTable;
                return;
            }

            if (node.Is("template"))
            {
                _mode = _templateModes.Count > 0 ? _templateModes[^1] : Mode.InBody;
                return;
            }

            if (node.Is("head") && !last)
            {
                _mode = Mode.InHead;
                return;
            }

            if (node.Is("body"))
            {
                _mode = Mode.InBody;
                return;
            }

            if (node.Is("frameset"))
            {
                _mode = Mode.InFrameset;
                return;
            }

            if (node.Is("html"))
            {
                _mode = _head is null ? Mode.BeforeHead : Mode.AfterHead;
                return;
            }

            if (last)
            {
                _mode = Mode.InBody;
                return;
            }
        }

        _mode = Mode.InBody;
    }

    private static bool IsWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    private static bool IsAllWhitespace(string text)
    {
        foreach (char c in text)
        {
            if (!IsWhitespace(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Splits a character run at its first non-whitespace character, for the modes that treat
    /// the two differently. The token keeps what follows, which the caller then reprocesses.
    /// </summary>
    private static string TakeLeadingWhitespace(HtmlToken token)
    {
        string data = token.Data.ToString();
        int i = 0;
        while (i < data.Length && IsWhitespace(data[i]))
            i++;

        token.Data.Remove(0, i);
        return data[..i];
    }
}
