namespace Quillwright.Html;

/// <summary>The insertion modes of WHATWG HTML §13.2.6.4, one method each.</summary>
internal sealed partial class HtmlTreeBuilder
{
    private void ProcessInMode(Mode mode, HtmlToken token)
    {
        // A newline straight after <pre>, <listing> or <textarea> is an authoring
        // convenience the standard drops (§13.2.6.4.7).
        if (_ignoreNextLineFeed)
        {
            _ignoreNextLineFeed = false;
            if (token.Kind == HtmlTokenKind.Character && token.Data.Length > 0 && token.Data[0] == '\n')
            {
                token.Data.Remove(0, 1);
                if (token.Data.Length == 0)
                    return;
            }
        }

        switch (mode)
        {
            case Mode.Initial:
                InitialMode(token);
                break;
            case Mode.BeforeHtml:
                BeforeHtmlMode(token);
                break;
            case Mode.BeforeHead:
                BeforeHeadMode(token);
                break;
            case Mode.InHead:
                InHeadMode(token);
                break;
            case Mode.InHeadNoscript:
                InHeadNoscriptMode(token);
                break;
            case Mode.AfterHead:
                AfterHeadMode(token);
                break;
            case Mode.InBody:
                InBodyMode(token);
                break;
            case Mode.Text:
                TextMode(token);
                break;
            case Mode.InTable:
                InTableMode(token);
                break;
            case Mode.InTableText:
                InTableTextMode(token);
                break;
            case Mode.InCaption:
                InCaptionMode(token);
                break;
            case Mode.InColumnGroup:
                InColumnGroupMode(token);
                break;
            case Mode.InTableBody:
                InTableBodyMode(token);
                break;
            case Mode.InRow:
                InRowMode(token);
                break;
            case Mode.InCell:
                InCellMode(token);
                break;
            case Mode.InTemplate:
                InTemplateMode(token);
                break;
            case Mode.AfterBody:
                AfterBodyMode(token);
                break;
            case Mode.InFrameset:
                InFramesetMode(token);
                break;
            case Mode.AfterFrameset:
                AfterFramesetMode(token);
                break;
            case Mode.AfterAfterBody:
                AfterAfterBodyMode(token);
                break;
            case Mode.AfterAfterFrameset:
                AfterAfterFramesetMode(token);
                break;
            default:
                InBodyMode(token);
                break;
        }
    }

    private void StopParsing() => _done = true;

    private void InitialMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
                TakeLeadingWhitespace(token);
                if (token.Data.Length == 0)
                    return;

                break;

            case HtmlTokenKind.Comment:
                InsertComment(token, _document);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token, _document);
                return;

            case HtmlTokenKind.Doctype:
                InsertDocumentType(token);
                _quirks = token.ForceQuirks || !string.Equals(token.TagName, "html", StringComparison.Ordinal) ||
                          HtmlQuirks.ForcesQuirks(token.PublicIdentifier, token.SystemIdentifier);
                _mode = Mode.BeforeHtml;
                return;

            default:
                break;
        }

        _quirks = true;
        _mode = Mode.BeforeHtml;
        ProcessInMode(_mode, token);
    }

    private void BeforeHtmlMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.Comment:
                InsertComment(token, _document);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token, _document);
                return;

            case HtmlTokenKind.Character:
                TakeLeadingWhitespace(token);
                if (token.Data.Length == 0)
                    return;

                break;

            case HtmlTokenKind.StartTag when token.TagName == "html":
            {
                _budget?.EnsureMarkupDepth(1);
                HtmlElement html = CreateFor(token);
                _document.Append(html);
                _stack.Add(html);
                _mode = Mode.BeforeHead;
                return;
            }

            case HtmlTokenKind.EndTag when token.TagName is not ("head" or "body" or "html" or "br"):
                return;

            default:
                break;
        }

        _budget?.EnsureMarkupDepth(1);
        _budget?.AddMarkupNode();
        HtmlElement created = new("html") { Line = token.Line };
        _document.Append(created);
        _stack.Add(created);
        _mode = Mode.BeforeHead;
        ProcessInMode(_mode, token);
    }

    private void BeforeHeadMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
                TakeLeadingWhitespace(token);
                if (token.Data.Length == 0)
                    return;

                break;

            case HtmlTokenKind.Comment:
                InsertComment(token);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag when token.TagName == "html":
                InBodyMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName == "head":
                _head = InsertElement(token);
                _mode = Mode.InHead;
                return;

            case HtmlTokenKind.EndTag when token.TagName is not ("head" or "body" or "html" or "br"):
                return;

            default:
                break;
        }

        _head = InsertElement("head", token.Line);
        _mode = Mode.InHead;
        ProcessInMode(_mode, token);
    }

    private void InHeadMode(HtmlToken token)
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

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag:
                switch (token.TagName)
                {
                    case "html":
                        InBodyMode(token);
                        return;

                    case "base" or "basefont" or "bgsound" or "link" or "meta":
                        InsertElement(token);
                        _stack.RemoveAt(_stack.Count - 1);
                        return;

                    case "title":
                        ParseTextElement(token, rcdata: true);
                        return;

                    case "noframes" or "style":
                        ParseTextElement(token, rcdata: false);
                        return;

                    case "noscript":
                        // Scripting is disabled here, so a noscript element's content is
                        // markup and is parsed rather than kept as raw text.
                        InsertElement(token);
                        _mode = Mode.InHeadNoscript;
                        return;

                    case "script":
                        InsertElement(token);
                        _tokenizer.SwitchToScriptData();
                        _originalMode = _mode;
                        _mode = Mode.Text;
                        return;

                    case "template":
                        InsertElement(token);
                        AddFormattingMarker();
                        _framesetOk = false;
                        _mode = Mode.InTemplate;
                        _templateModes.Add(Mode.InTemplate);
                        return;

                    case "head":
                        return;

                    default:
                        break;
                }

                break;

            case HtmlTokenKind.EndTag:
                switch (token.TagName)
                {
                    case "head":
                        _stack.RemoveAt(_stack.Count - 1);
                        _mode = Mode.AfterHead;
                        return;

                    case "body" or "html" or "br":
                        break;

                    case "template":
                        if (!StackHas("template"))
                            return;

                        GenerateImpliedEndTagsThoroughly();
                        PopUntilPopped("template");
                        ClearFormattingToMarker();
                        if (_templateModes.Count > 0)
                            _templateModes.RemoveAt(_templateModes.Count - 1);

                        ResetInsertionMode();
                        return;

                    default:
                        return;
                }

                break;

            default:
                break;
        }

        _stack.RemoveAt(_stack.Count - 1);
        _mode = Mode.AfterHead;
        ProcessInMode(_mode, token);
    }

    /// <summary>The generic RCDATA and raw text element parsing algorithms (§13.2.6.2).</summary>
    private void ParseTextElement(HtmlToken token, bool rcdata)
    {
        InsertElement(token);
        if (rcdata)
            _tokenizer.SwitchToRcdata();
        else
            _tokenizer.SwitchToRawtext();

        _originalMode = _mode;
        _mode = Mode.Text;
    }

    private void InHeadNoscriptMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag when token.TagName == "html":
                InBodyMode(token);
                return;

            case HtmlTokenKind.EndTag when token.TagName == "noscript":
                _stack.RemoveAt(_stack.Count - 1);
                _mode = Mode.InHead;
                return;

            case HtmlTokenKind.Character:
            {
                string whitespace = TakeLeadingWhitespace(token);
                InsertText(whitespace);
                if (token.Data.Length == 0)
                    return;

                break;
            }

            case HtmlTokenKind.Comment:
            case HtmlTokenKind.ProcessingInstruction:
                InHeadMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName
                is "basefont" or "bgsound" or "link" or "meta" or "noframes" or "style":
                InHeadMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName is "head" or "noscript":
                return;

            case HtmlTokenKind.EndTag when token.TagName != "br":
                return;

            default:
                break;
        }

        _stack.RemoveAt(_stack.Count - 1);
        _mode = Mode.InHead;
        ProcessInMode(_mode, token);
    }

    private void AfterHeadMode(HtmlToken token)
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

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag:
                switch (token.TagName)
                {
                    case "html":
                        InBodyMode(token);
                        return;

                    case "body":
                        InsertElement(token);
                        _framesetOk = false;
                        _mode = Mode.InBody;
                        return;

                    case "frameset":
                        InsertElement(token);
                        _mode = Mode.InFrameset;
                        return;

                    case "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script"
                        or "style" or "template" or "title":
                    {
                        if (_head is { } head)
                        {
                            _stack.Add(head);
                            InHeadMode(token);
                            _stack.Remove(head);
                        }

                        return;
                    }

                    case "head":
                        return;

                    default:
                        break;
                }

                break;

            case HtmlTokenKind.EndTag:
                if (token.TagName == "template")
                {
                    InHeadMode(token);
                    return;
                }

                if (token.TagName is not ("body" or "html" or "br"))
                    return;

                break;

            default:
                break;
        }

        InsertElement("body", token.Line);
        _mode = Mode.InBody;
        ProcessInMode(_mode, token);
    }

    private void TextMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
                InsertText(token.Data.ToString());
                return;

            case HtmlTokenKind.EndOfFile:
                _stack.RemoveAt(_stack.Count - 1);
                _mode = _originalMode;
                ProcessInMode(_mode, token);
                return;

            case HtmlTokenKind.EndTag:
                _stack.RemoveAt(_stack.Count - 1);
                _mode = _originalMode;
                return;

            default:
                return;
        }
    }

    private void AfterBodyMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character when IsAllWhitespace(token.Data.ToString()):
                InBodyMode(token);
                return;

            case HtmlTokenKind.Comment:
                InsertComment(token, _stack.Count > 0 ? _stack[0] : _document);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token, _stack.Count > 0 ? _stack[0] : _document);
                return;

            case HtmlTokenKind.Doctype:
                return;

            case HtmlTokenKind.StartTag when token.TagName == "html":
                InBodyMode(token);
                return;

            case HtmlTokenKind.EndTag when token.TagName == "html":
                _mode = Mode.AfterAfterBody;
                return;

            case HtmlTokenKind.EndOfFile:
                StopParsing();
                return;

            default:
                _mode = Mode.InBody;
                ProcessInMode(_mode, token);
                return;
        }
    }

    private void AfterAfterBodyMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(token, _document);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token, _document);
                return;

            case HtmlTokenKind.Doctype:
            case HtmlTokenKind.StartTag when token.TagName == "html":
                InBodyMode(token);
                return;

            case HtmlTokenKind.Character when IsAllWhitespace(token.Data.ToString()):
                InBodyMode(token);
                return;

            case HtmlTokenKind.EndOfFile:
                StopParsing();
                return;

            default:
                _mode = Mode.InBody;
                ProcessInMode(_mode, token);
                return;
        }
    }

    private void InFramesetMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
                InsertText(new string([.. token.Data.ToString().Where(IsWhitespace)]));
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
                switch (token.TagName)
                {
                    case "html":
                        InBodyMode(token);
                        return;
                    case "frameset":
                        InsertElement(token);
                        return;
                    case "frame":
                        InsertElement(token);
                        _stack.RemoveAt(_stack.Count - 1);
                        return;
                    case "noframes":
                        InHeadMode(token);
                        return;
                    default:
                        return;
                }

            case HtmlTokenKind.EndTag when token.TagName == "frameset":
                if (Current is { } current && current.Is("html"))
                    return;

                _stack.RemoveAt(_stack.Count - 1);
                if (Current is { } now && !now.Is("frameset"))
                    _mode = Mode.AfterFrameset;

                return;

            case HtmlTokenKind.EndOfFile:
                StopParsing();
                return;

            default:
                return;
        }
    }

    private void AfterFramesetMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Character:
                InsertText(new string([.. token.Data.ToString().Where(IsWhitespace)]));
                return;

            case HtmlTokenKind.Comment:
                InsertComment(token);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName == "html":
                InBodyMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName == "noframes":
                InHeadMode(token);
                return;

            case HtmlTokenKind.EndTag when token.TagName == "html":
                _mode = Mode.AfterAfterFrameset;
                return;

            case HtmlTokenKind.EndOfFile:
                StopParsing();
                return;

            default:
                return;
        }
    }

    private void AfterAfterFramesetMode(HtmlToken token)
    {
        switch (token.Kind)
        {
            case HtmlTokenKind.Comment:
                InsertComment(token, _document);
                return;

            case HtmlTokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token, _document);
                return;

            case HtmlTokenKind.Doctype:
            case HtmlTokenKind.StartTag when token.TagName == "html":
                InBodyMode(token);
                return;

            case HtmlTokenKind.Character when IsAllWhitespace(token.Data.ToString()):
                InBodyMode(token);
                return;

            case HtmlTokenKind.StartTag when token.TagName == "noframes":
                InHeadMode(token);
                return;

            case HtmlTokenKind.EndOfFile:
                StopParsing();
                return;

            default:
                return;
        }
    }
}
