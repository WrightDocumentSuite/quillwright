namespace Quillwright.Html;

/// <summary>The "in body" insertion mode (WHATWG HTML §13.2.6.4.7), which most content goes through.</summary>
internal sealed partial class HtmlTreeBuilder
{
    /// <summary>The start tags that close an open paragraph and then insert an ordinary block.</summary>
    private static readonly string[] BlockStartTags =
    [
        "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
        "figcaption", "figure", "footer", "header", "hgroup", "main", "menu", "nav", "ol", "p", "search", "section",
        "summary", "ul",
    ];

    /// <summary>The end tags that close the block of the same name.</summary>
    private static readonly string[] BlockEndTags =
    [
        "address", "article", "aside", "blockquote", "button", "center", "details", "dialog", "dir", "div", "dl",
        "fieldset", "figcaption", "figure", "footer", "header", "hgroup", "listing", "main", "menu", "nav", "ol",
        "pre", "search", "section", "select", "summary", "ul",
    ];

    private static readonly string[] Headings = ["h1", "h2", "h3", "h4", "h5", "h6"];

    private void InBodyMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
                InBodyCharacters(token);
                return;

            case HtmlTokenKind.Comment:
                InsertComment(token);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag:
                InBodyStartTag(token);
                return;

            case HtmlTokenKind.EndTag:
                InBodyEndTag(token);
                return;

            case HtmlTokenKind.EndOfFile:
                if (_templateModes.Count > 0)
                {
                    InTemplateMode(token);
                    return;
                }

                StopParsing();
                return;

            default:
                return;
        }
    }

    private void InBodyCharacters(HtmlToken token)
    {
        string data = token.Data.ToString();
        if (data.Contains('\0', StringComparison.Ordinal))
        {
            data = data.Replace("\0", string.Empty, StringComparison.Ordinal);
            if (data.Length == 0)
                return;
        }

        ReconstructFormatting();
        InsertText(data);
        if (!IsAllWhitespace(data))
            _framesetOk = false;
    }

    private void InBodyStartTag(HtmlToken token)
    {
        string name = token.TagName;
        switch (name)
        {
            case "html":
                if (StackHas("template"))
                    return;

                MergeAttributes(_stack[0], token);
                return;

            case "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script" or "style"
                or "template" or "title":
                InHeadMode(token);
                return;

            case "body":
                if (_stack.Count <= 1 || !_stack[1].Is("body") || StackHas("template"))
                    return;

                _framesetOk = false;
                MergeAttributes(_stack[1], token);
                return;

            case "frameset":
                if (_stack.Count <= 1 || !_stack[1].Is("body") || !_framesetOk)
                    return;

                _stack[1].Parent?.Children.Remove(_stack[1]);
                while (_stack.Count > 1)
                    _stack.RemoveAt(_stack.Count - 1);

                InsertElement(token);
                _mode = Mode.InFrameset;
                return;

            case "address" or "article" or "aside" or "blockquote" or "center" or "details" or "dialog" or "dir"
                or "div" or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "header" or "hgroup"
                or "main" or "menu" or "nav" or "ol" or "p" or "search" or "section" or "summary" or "ul":
                if (InButtonScope("p"))
                    CloseParagraph();

                InsertElement(token);
                return;

            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (InButtonScope("p"))
                    CloseParagraph();

                if (Current is { } heading && heading.IsAny(Headings))
                    _stack.RemoveAt(_stack.Count - 1);

                InsertElement(token);
                return;

            case "pre" or "listing":
                if (InButtonScope("p"))
                    CloseParagraph();

                InsertElement(token);
                _ignoreNextLineFeed = true;
                _framesetOk = false;
                return;

            case "form":
                if (_form is not null && !StackHas("template"))
                    return;

                if (InButtonScope("p"))
                    CloseParagraph();

                HtmlElement form = InsertElement(token);
                if (!StackHas("template"))
                    _form = form;

                return;

            case "li":
                InsertListItem(token, "li");
                return;

            case "dd" or "dt":
                InsertListItem(token, name);
                return;

            case "plaintext":
                if (InButtonScope("p"))
                    CloseParagraph();

                InsertElement(token);
                _tokenizer.SwitchToPlaintext();
                return;

            case "button":
                if (InScope("button"))
                {
                    GenerateImpliedEndTags();
                    PopUntilPopped("button");
                }

                ReconstructFormatting();
                InsertElement(token);
                _framesetOk = false;
                return;

            case "a":
            {
                for (int i = _formatting.Count - 1; i >= 0; i--)
                {
                    if (_formatting[i] is not { } entry)
                        break;

                    if (entry.Is("a"))
                    {
                        AdoptionAgency(token);
                        _formatting.Remove(entry);
                        _stack.Remove(entry);
                        break;
                    }
                }

                ReconstructFormatting();
                PushFormatting(InsertElement(token));
                return;
            }

            case "b" or "big" or "code" or "em" or "font" or "i" or "s" or "small" or "strike" or "strong"
                or "tt" or "u":
                ReconstructFormatting();
                PushFormatting(InsertElement(token));
                return;

            case "nobr":
                ReconstructFormatting();
                if (InScope("nobr"))
                {
                    AdoptionAgency(token);
                    ReconstructFormatting();
                }

                PushFormatting(InsertElement(token));
                return;

            case "applet" or "marquee" or "object":
                ReconstructFormatting();
                InsertElement(token);
                AddFormattingMarker();
                _framesetOk = false;
                return;

            case "table":
                if (!_quirks && InButtonScope("p"))
                    CloseParagraph();

                InsertElement(token);
                _framesetOk = false;
                _mode = Mode.InTable;
                return;

            case "area" or "br" or "embed" or "img" or "keygen" or "wbr":
                ReconstructFormatting();
                InsertElement(token);
                _stack.RemoveAt(_stack.Count - 1);
                _framesetOk = false;
                return;

            case "input":
                if (FragmentContextIs("select"))
                    return;

                if (InScope("select"))
                    PopUntilPopped("select");

                ReconstructFormatting();
                InsertElement(token);
                _stack.RemoveAt(_stack.Count - 1);
                if (!IsHiddenInput(token))
                    _framesetOk = false;

                return;

            case "param" or "source" or "track":
                InsertElement(token);
                _stack.RemoveAt(_stack.Count - 1);
                return;

            case "hr":
                if (InButtonScope("p"))
                    CloseParagraph();

                if (InScope("select"))
                    GenerateImpliedEndTags();

                InsertElement(token);
                _stack.RemoveAt(_stack.Count - 1);
                _framesetOk = false;
                return;

            case "image":
                // The standard's own words: "Change the token's tag name to img and reprocess
                // it. (Don't ask.)"
                token.Name.Clear();
                token.Name.Append("img");
                InBodyStartTag(token);
                return;

            case "textarea":
                InsertElement(token);
                _ignoreNextLineFeed = true;
                _tokenizer.SwitchToRcdata();
                _originalMode = _mode;
                _framesetOk = false;
                _mode = Mode.Text;
                return;

            case "xmp":
                if (InButtonScope("p"))
                    CloseParagraph();

                ReconstructFormatting();
                _framesetOk = false;
                ParseTextElement(token, rcdata: false);
                return;

            case "iframe":
                _framesetOk = false;
                ParseTextElement(token, rcdata: false);
                return;

            case "noembed":
                ParseTextElement(token, rcdata: false);
                return;

            case "noscript":
                // Scripting is disabled, so the content is parsed as ordinary markup.
                ReconstructFormatting();
                InsertElement(token);
                return;

            case "select":
                if (FragmentContextIs("select"))
                    return;

                if (InScope("select"))
                {
                    PopUntilPopped("select");
                    return;
                }

                ReconstructFormatting();
                InsertElement(token);
                _framesetOk = false;
                return;

            case "option":
                if (InScope("select"))
                    GenerateImpliedEndTags("optgroup");
                else if (Current is { } beforeOption && beforeOption.Is("option"))
                    _stack.RemoveAt(_stack.Count - 1);

                ReconstructFormatting();
                InsertElement(token);
                return;

            case "optgroup":
                if (InScope("select"))
                    GenerateImpliedEndTags();
                else if (Current is { } beforeGroup && beforeGroup.Is("option"))
                    _stack.RemoveAt(_stack.Count - 1);

                ReconstructFormatting();
                InsertElement(token);
                return;

            case "rb" or "rtc":
                if (InScope("ruby"))
                    GenerateImpliedEndTags();

                InsertElement(token);
                return;

            case "rp" or "rt":
                if (InScope("ruby"))
                    GenerateImpliedEndTags("rtc");

                InsertElement(token);
                return;

            case "math":
                ReconstructFormatting();
                InsertForeign(token, HtmlNamespace.MathML);
                return;

            case "svg":
                ReconstructFormatting();
                InsertForeign(token, HtmlNamespace.Svg);
                return;

            case "caption" or "col" or "colgroup" or "frame" or "head" or "tbody" or "td" or "tfoot" or "th"
                or "thead" or "tr":
                return;

            default:
                ReconstructFormatting();
                InsertElement(token);
                return;
        }
    }

    private void InsertForeign(HtmlToken token, HtmlNamespace space)
    {
        InsertElement(token, space);
        if (token.SelfClosing)
            _stack.RemoveAt(_stack.Count - 1);
    }

    private static bool IsHiddenInput(HtmlToken token)
    {
        foreach (HtmlAttribute attribute in token.Attributes)
        {
            if (attribute.Name == "type")
                return attribute.Value.Equals("hidden", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void MergeAttributes(HtmlElement element, HtmlToken token)
    {
        element.AddAttributes(token.Attributes);
    }

    /// <summary>
    /// The list-item rule of §13.2.6.4.7: an open item of the same kind is closed first,
    /// which is what makes <c>&lt;li&gt;one&lt;li&gt;two</c> two siblings rather than a nest.
    /// </summary>
    private void InsertListItem(HtmlToken token, string kind)
    {
        _framesetOk = false;

        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            HtmlElement node = _stack[i];

            if (kind == "li" && node.Is("li"))
            {
                GenerateImpliedEndTags("li");
                PopUntilPopped("li");
                break;
            }

            if (kind != "li" && node.IsAny("dd", "dt"))
            {
                string open = node.Name;
                GenerateImpliedEndTags(open);
                PopUntilPopped(open);
                break;
            }

            if (IsSpecial(node) && !node.IsAny("address", "div", "p"))
                break;
        }

        if (InButtonScope("p"))
            CloseParagraph();

        InsertElement(token);
    }

    private void InBodyEndTag(HtmlToken token)
    {
        string name = token.TagName;
        switch (name)
        {
            case "template":
                InHeadMode(token);
                return;

            case "body":
                if (!InScope("body"))
                    return;

                _mode = Mode.AfterBody;
                return;

            case "html":
                if (!InScope("body"))
                    return;

                _mode = Mode.AfterBody;
                ProcessInMode(_mode, token);
                return;

            case "address" or "article" or "aside" or "blockquote" or "button" or "center" or "details" or "dialog"
                or "dir" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "header"
                or "hgroup" or "listing" or "main" or "menu" or "nav" or "ol" or "pre" or "search" or "section"
                or "select" or "summary" or "ul":
                if (!InScope(name))
                    return;

                GenerateImpliedEndTags();
                PopUntilPopped(name);
                return;

            case "form":
                if (!StackHas("template"))
                {
                    HtmlElement? node = _form;
                    _form = null;
                    if (node is null || !_stack.Contains(node) || !InScope(node.Name))
                        return;

                    GenerateImpliedEndTags();
                    _stack.Remove(node);
                    return;
                }

                if (!InScope("form"))
                    return;

                GenerateImpliedEndTags();
                PopUntilPopped("form");
                return;

            case "p":
                if (!InButtonScope("p"))
                    InsertElement("p", token.Line);

                CloseParagraph();
                return;

            case "li":
                if (!InListItemScope("li"))
                    return;

                GenerateImpliedEndTags("li");
                PopUntilPopped("li");
                return;

            case "dd" or "dt":
                if (!InScope(name))
                    return;

                GenerateImpliedEndTags(name);
                PopUntilPopped(name);
                return;

            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (!InScopeAny(Headings))
                    return;

                GenerateImpliedEndTags();
                PopUntilPoppedAny(Headings);
                return;

            case "a" or "b" or "big" or "code" or "em" or "font" or "i" or "nobr" or "s" or "small" or "strike"
                or "strong" or "tt" or "u":
                if (!AdoptionAgency(token))
                    AnyOtherEndTag(token);

                return;

            case "applet" or "marquee" or "object":
                if (!InScope(name))
                    return;

                GenerateImpliedEndTags();
                PopUntilPopped(name);
                ClearFormattingToMarker();
                return;

            case "br":
                // An end tag here is a parse error the standard turns into a start tag.
                token.Kind = HtmlTokenKind.StartTag;
                token.Attributes.Clear();
                InBodyStartTag(token);
                return;

            default:
                AnyOtherEndTag(token);
                return;
        }
    }

    /// <summary>
    /// The "any other end tag" clause: close the nearest element of that name, unless
    /// something special stands in the way, in which case the tag is dropped.
    /// </summary>
    private void AnyOtherEndTag(HtmlToken token)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            HtmlElement node = _stack[i];
            if (node.Is(token.TagName))
            {
                GenerateImpliedEndTags(token.TagName);
                while (_stack.Count > i)
                    _stack.RemoveAt(_stack.Count - 1);

                return;
            }

            if (IsSpecial(node))
                return;
        }
    }
}
