namespace Quillwright.Html;

/// <summary>
/// The table insertion modes (WHATWG HTML §13.2.6.4.9 to §13.2.6.4.16) and the rules for
/// foreign content (§13.2.6.5).
/// </summary>
internal sealed partial class HtmlTreeBuilder
{
    private void InTableMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character when Current is { } current &&
                                              current.IsAny("table", "tbody", "template", "tfoot", "thead", "tr"):
                _pendingTableCharacters.Clear();
                _originalMode = _mode;
                _mode = Mode.InTableText;
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.Comment:
                InsertComment(token);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag:
                switch (token.TagName)
                {
                    case "caption":
                        ClearStackBackTo("table", "template", "html");
                        AddFormattingMarker();
                        InsertElement(token);
                        _mode = Mode.InCaption;
                        return;

                    case "colgroup":
                        ClearStackBackTo("table", "template", "html");
                        InsertElement(token);
                        _mode = Mode.InColumnGroup;
                        return;

                    case "col":
                        ClearStackBackTo("table", "template", "html");
                        InsertElement("colgroup", token.Line);
                        _mode = Mode.InColumnGroup;
                        ProcessInMode(_mode, token);
                        return;

                    case "tbody" or "tfoot" or "thead":
                        ClearStackBackTo("table", "template", "html");
                        InsertElement(token);
                        _mode = Mode.InTableBody;
                        return;

                    case "td" or "th" or "tr":
                        ClearStackBackTo("table", "template", "html");
                        InsertElement("tbody", token.Line);
                        _mode = Mode.InTableBody;
                        ProcessInMode(_mode, token);
                        return;

                    case "table":
                        if (!InTableScope("table"))
                            return;

                        PopUntilPopped("table");
                        ResetInsertionMode();
                        ProcessInMode(_mode, token);
                        return;

                    case "style" or "script" or "template":
                        InHeadMode(token);
                        return;

                    case "input":
                        if (!IsHiddenInput(token))
                            break;

                        InsertElement(token);
                        _stack.RemoveAt(_stack.Count - 1);
                        return;

                    case "form":
                        if (StackHas("template") || _form is not null)
                            return;

                        _form = InsertElement(token);
                        _stack.RemoveAt(_stack.Count - 1);
                        return;

                    default:
                        break;
                }

                break;

            case HtmlTokenKind.EndTag:
                switch (token.TagName)
                {
                    case "table":
                        if (!InTableScope("table"))
                            return;

                        PopUntilPopped("table");
                        ResetInsertionMode();
                        return;

                    case "body" or "caption" or "col" or "colgroup" or "html" or "tbody" or "td" or "tfoot"
                        or "th" or "thead" or "tr":
                        return;

                    case "template":
                        InHeadMode(token);
                        return;

                    default:
                        break;
                }

                break;

            case HtmlTokenKind.EndOfFile:
                InBodyMode(token);
                return;

            default:
                break;
        }

        // Anything else: whatever it is does not belong in a table, so it is fostered out.
        _fosterParenting = true;
        InBodyMode(token);
        _fosterParenting = false;
    }

    private void ClearStackBackTo(params ReadOnlySpan<string> names)
    {
        while (Current is { } current && !current.IsAny(names))
            _stack.RemoveAt(_stack.Count - 1);
    }

    private void InTableTextMode(HtmlToken token)
    {
        if (token.Kind == HtmlTokenKind.Character)
        {
            string data = token.Data.ToString().Replace("\0", string.Empty, StringComparison.Ordinal);
            if (data.Length > 0)
            {
                var pending = new HtmlToken { Kind = HtmlTokenKind.Character, Line = token.Line };
                pending.Data.Append(data);
                _pendingTableCharacters.Add(pending);
            }

            return;
        }

        bool anyNonWhitespace = false;
        foreach (HtmlToken pending in _pendingTableCharacters)
        {
            if (!IsAllWhitespace(pending.Data.ToString()))
            {
                anyNonWhitespace = true;
                break;
            }
        }

        foreach (HtmlToken pending in _pendingTableCharacters)
        {
            if (anyNonWhitespace)
            {
                _fosterParenting = true;
                InBodyMode(pending);
                _fosterParenting = false;
            }
            else
            {
                InsertText(pending.Data.ToString());
            }
        }

        _pendingTableCharacters.Clear();
        _mode = _originalMode;
        ProcessInMode(_mode, token);
    }

    private void InCaptionMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.EndTag when token.TagName == "caption":
                if (!InTableScope("caption"))
                    return;

                GenerateImpliedEndTags();
                PopUntilPopped("caption");
                ClearFormattingToMarker();
                _mode = Mode.InTable;
                return;

            case HtmlTokenKind.StartTag when token.TagName
                is "caption" or "col" or "colgroup" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
            case HtmlTokenKind.EndTag when token.TagName == "table":
                if (!InTableScope("caption"))
                    return;

                GenerateImpliedEndTags();
                PopUntilPopped("caption");
                ClearFormattingToMarker();
                _mode = Mode.InTable;
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag when token.TagName
                is "body" or "col" or "colgroup" or "html" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                return;

            default:
                InBodyMode(token);
                return;
        }
    }

    private void InColumnGroupMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
            {
                string whitespace = TakeLeadingWhitespace(token);
                InsertText(whitespace);
                if (token.Data.Length == 0)
                    return;

                break;
            }

            case HtmlTokenKind.Comment:
                InsertComment(token);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag when token.TagName == "html":
                InBodyMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName == "col":
                InsertElement(token);
                _stack.RemoveAt(_stack.Count - 1);
                return;

            case HtmlTokenKind.EndTag when token.TagName == "colgroup":
                if (Current is not { } current || !current.Is("colgroup"))
                    return;

                _stack.RemoveAt(_stack.Count - 1);
                _mode = Mode.InTable;
                return;

            case HtmlTokenKind.EndTag when token.TagName == "col":
                return;

            case HtmlTokenKind.StartTag when token.TagName == "template":
            case HtmlTokenKind.EndTag when token.TagName == "template":
                InHeadMode(token);
                return;

            case HtmlTokenKind.EndOfFile:
                InBodyMode(token);
                return;

            default:
                break;
        }

        if (Current is not { } node || !node.Is("colgroup"))
            return;

        _stack.RemoveAt(_stack.Count - 1);
        _mode = Mode.InTable;
        ProcessInMode(_mode, token);
    }

    private void InTableBodyMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.StartTag when token.TagName == "tr":
                ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
                InsertElement(token);
                _mode = Mode.InRow;
                return;

            case HtmlTokenKind.StartTag when token.TagName is "th" or "td":
                ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
                InsertElement("tr", token.Line);
                _mode = Mode.InRow;
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag when token.TagName is "tbody" or "tfoot" or "thead":
                if (!InTableScope(token.TagName))
                    return;

                ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
                _stack.RemoveAt(_stack.Count - 1);
                _mode = Mode.InTable;
                return;

            case HtmlTokenKind.StartTag when token.TagName
                is "caption" or "col" or "colgroup" or "tbody" or "tfoot" or "thead":
            case HtmlTokenKind.EndTag when token.TagName == "table":
                if (!InTableScopeAny("tbody", "thead", "tfoot"))
                    return;

                ClearStackBackTo("tbody", "tfoot", "thead", "template", "html");
                _stack.RemoveAt(_stack.Count - 1);
                _mode = Mode.InTable;
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag when token.TagName
                is "body" or "caption" or "col" or "colgroup" or "html" or "td" or "th" or "tr":
                return;

            default:
                InTableMode(token);
                return;
        }
    }

    private void InRowMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.StartTag when token.TagName is "th" or "td":
                ClearStackBackTo("tr", "template", "html");
                InsertElement(token);
                _mode = Mode.InCell;
                AddFormattingMarker();
                return;

            case HtmlTokenKind.EndTag when token.TagName == "tr":
                if (!InTableScope("tr"))
                    return;

                ClearStackBackTo("tr", "template", "html");
                _stack.RemoveAt(_stack.Count - 1);
                _mode = Mode.InTableBody;
                return;

            case HtmlTokenKind.StartTag when token.TagName
                is "caption" or "col" or "colgroup" or "tbody" or "tfoot" or "thead" or "tr":
            case HtmlTokenKind.EndTag when token.TagName == "table":
                if (!InTableScope("tr"))
                    return;

                ClearStackBackTo("tr", "template", "html");
                _stack.RemoveAt(_stack.Count - 1);
                _mode = Mode.InTableBody;
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag when token.TagName is "tbody" or "tfoot" or "thead":
                if (!InTableScope(token.TagName) || !InTableScope("tr"))
                    return;

                ClearStackBackTo("tr", "template", "html");
                _stack.RemoveAt(_stack.Count - 1);
                _mode = Mode.InTableBody;
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag when token.TagName
                is "body" or "caption" or "col" or "colgroup" or "html" or "td" or "th":
                return;

            default:
                InTableMode(token);
                return;
        }
    }

    private void InCellMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.EndTag when token.TagName is "td" or "th":
                if (!InTableScope(token.TagName))
                    return;

                GenerateImpliedEndTags();
                PopUntilPopped(token.TagName);
                ClearFormattingToMarker();
                _mode = Mode.InRow;
                return;

            case HtmlTokenKind.StartTag when token.TagName
                is "caption" or "col" or "colgroup" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                if (!InTableScopeAny("td", "th"))
                    return;

                CloseCell();
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag when token.TagName is "body" or "caption" or "col" or "colgroup" or "html":
                return;

            case HtmlTokenKind.EndTag when token.TagName is "table" or "tbody" or "tfoot" or "thead" or "tr":
                if (!InTableScope(token.TagName))
                    return;

                CloseCell();
                ProcessInMode(_mode, token);
                return;

            default:
                InBodyMode(token);
                return;
        }
    }

    /// <summary>Closes the open cell, which the standard calls "close the cell".</summary>
    private void CloseCell()
    {
        GenerateImpliedEndTags();
        PopUntilPoppedAny("td", "th");
        ClearFormattingToMarker();
        _mode = Mode.InRow;
    }

    private void InTemplateMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
            case HtmlTokenKind.Comment:
            case HtmlTokenKind.Doctype:
                InBodyMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName
                is "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script" or "style"
                or "template" or "title":
            case HtmlTokenKind.EndTag when token.TagName == "template":
                InHeadMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName
                is "caption" or "colgroup" or "tbody" or "tfoot" or "thead":
                SwitchTemplateMode(Mode.InTable, token);
                return;

            case HtmlTokenKind.StartTag when token.TagName == "col":
                SwitchTemplateMode(Mode.InColumnGroup, token);
                return;

            case HtmlTokenKind.StartTag when token.TagName == "tr":
                SwitchTemplateMode(Mode.InTableBody, token);
                return;

            case HtmlTokenKind.StartTag when token.TagName is "td" or "th":
                SwitchTemplateMode(Mode.InRow, token);
                return;

            case HtmlTokenKind.StartTag:
                SwitchTemplateMode(Mode.InBody, token);
                return;

            case HtmlTokenKind.EndTag:
                return;

            case HtmlTokenKind.EndOfFile:
                if (!StackHas("template"))
                {
                    StopParsing();
                    return;
                }

                PopUntilPopped("template");
                ClearFormattingToMarker();
                if (_templateModes.Count > 0)
                    _templateModes.RemoveAt(_templateModes.Count - 1);

                ResetInsertionMode();
                ProcessInMode(_mode, token);
                return;

            default:
                return;
        }
    }

    private void SwitchTemplateMode(Mode mode, HtmlToken token)
    {
        if (_templateModes.Count > 0)
            _templateModes.RemoveAt(_templateModes.Count - 1);

        _templateModes.Add(mode);
        _mode = mode;
        ProcessInMode(_mode, token);
    }

    /// <summary>
    /// The rules for parsing tokens in foreign content (§13.2.6.5): SVG and MathML subtrees,
    /// and the tags that break back out of them into HTML.
    /// </summary>
    private void ForeignContent(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
            {
                string data = token.Data.ToString().Replace('\0', '�');
                InsertText(data);
                if (!IsAllWhitespace(data))
                    _framesetOk = false;

                return;
            }

            case HtmlTokenKind.Comment:
                InsertComment(token);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag when BreaksOutOfForeignContent(token):
                while (Current is { } node && node.Namespace != HtmlNamespace.Html &&
                       !IsMathTextIntegrationPoint(node) && !IsHtmlIntegrationPoint(node))
                {
                    _stack.RemoveAt(_stack.Count - 1);
                }

                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.StartTag:
            {
                HtmlNamespace space = Current?.Namespace ?? HtmlNamespace.Html;
                if (space == HtmlNamespace.Svg && SvgCase(token.TagName) is { } adjusted)
                {
                    token.Name.Clear();
                    token.Name.Append(adjusted);
                }

                InsertElement(token, space);
                if (token.SelfClosing)
                    _stack.RemoveAt(_stack.Count - 1);

                return;
            }

            case HtmlTokenKind.EndTag when token.TagName is "br" or "p":
                while (Current is { } node && node.Namespace != HtmlNamespace.Html &&
                       !IsMathTextIntegrationPoint(node) && !IsHtmlIntegrationPoint(node))
                {
                    _stack.RemoveAt(_stack.Count - 1);
                }

                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag:
            {
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    HtmlElement node = _stack[i];
                    if (i < _stack.Count - 1 && node.Namespace == HtmlNamespace.Html)
                    {
                        ProcessInMode(_mode, token);
                        return;
                    }

                    if (string.Equals(node.Name, token.TagName, StringComparison.OrdinalIgnoreCase))
                    {
                        while (_stack.Count > i)
                            _stack.RemoveAt(_stack.Count - 1);

                        return;
                    }
                }

                return;
            }

            default:
                ProcessInMode(_mode, token);
                return;
        }
    }

    /// <summary>The HTML start tags that end a foreign subtree wherever they appear in one.</summary>
    private static bool BreaksOutOfForeignContent(HtmlToken token)
    {
        switch (token.TagName)
        {
            case "b" or "big" or "blockquote" or "body" or "br" or "center" or "code" or "dd" or "div" or "dl"
                or "dt" or "em" or "embed" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "head" or "hr"
                or "i" or "img" or "li" or "listing" or "menu" or "meta" or "nobr" or "ol" or "p" or "pre"
                or "ruby" or "s" or "small" or "span" or "strong" or "strike" or "sub" or "sup" or "table"
                or "tt" or "u" or "ul" or "var":
                return true;

            case "font":
                foreach (HtmlAttribute attribute in token.Attributes)
                {
                    if (attribute.Name is "color" or "face" or "size")
                        return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>The SVG element names whose camel case the standard restores.</summary>
    private static string? SvgCase(string name) => name switch
    {
        "altglyph" => "altGlyph",
        "altglyphdef" => "altGlyphDef",
        "altglyphitem" => "altGlyphItem",
        "animatecolor" => "animateColor",
        "animatemotion" => "animateMotion",
        "animatetransform" => "animateTransform",
        "clippath" => "clipPath",
        "feblend" => "feBlend",
        "fecolormatrix" => "feColorMatrix",
        "fecomponenttransfer" => "feComponentTransfer",
        "fecomposite" => "feComposite",
        "feconvolvematrix" => "feConvolveMatrix",
        "fediffuselighting" => "feDiffuseLighting",
        "fedisplacementmap" => "feDisplacementMap",
        "fedistantlight" => "feDistantLight",
        "fedropshadow" => "feDropShadow",
        "feflood" => "feFlood",
        "fefunca" => "feFuncA",
        "fefuncb" => "feFuncB",
        "fefuncg" => "feFuncG",
        "fefuncr" => "feFuncR",
        "fegaussianblur" => "feGaussianBlur",
        "feimage" => "feImage",
        "femerge" => "feMerge",
        "femergenode" => "feMergeNode",
        "femorphology" => "feMorphology",
        "feoffset" => "feOffset",
        "fepointlight" => "fePointLight",
        "fespecularlighting" => "feSpecularLighting",
        "fespotlight" => "feSpotLight",
        "fetile" => "feTile",
        "feturbulence" => "feTurbulence",
        "foreignobject" => "foreignObject",
        "glyphref" => "glyphRef",
        "lineargradient" => "linearGradient",
        "radialgradient" => "radialGradient",
        "textpath" => "textPath",
        _ => null,
    };
}
